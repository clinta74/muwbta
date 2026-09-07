using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Randomness;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Commands;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.Quests;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Telemetry;
using Muwbta.Engine.Time;
using Muwbta.Engine.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Muwbta.Engine;

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the game loop and everything it owns. The host must also register:
    /// - <see cref="IWorldSource"/> for loading world data (Phase 1)
    /// - <see cref="ICharacterSaveQueue"/> for character persistence (Phase 1)
    /// - <see cref="IMobTemplateRepository"/> for mob templates (Phase 3)
    /// - <see cref="IItemTemplateRepository"/> for item templates (Phase 3)
    /// - <see cref="ISpawnerRepository"/> for spawner configuration (Phase 3)
    /// </summary>
    public static IServiceCollection AddMuwbtaEngine(
        this IServiceCollection services,
        Action<EngineOptions>? configure = null)
    {
        var options = new EngineOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SystemGameClock>();
        services.AddSingleton<IGameClock>(sp => sp.GetRequiredService<SystemGameClock>());
        services.AddSingleton<IRandomSource>(sp => new SeededRandomSource(Random.Shared.Next()));

        // Singletons because the world is a single shared object owned by one thread.
        services.AddSingleton<WorldState>();
        services.AddSingleton<CommandRegistry>(sp =>
            new CommandRegistry(
                sp.GetService<AbilityCache>(),
                sp.GetService<IMobTemplateRepository>(),
                sp.GetService<IItemTemplateRepository>(),
                sp.GetService<MobSpawner>(),
                sp.GetService<ItemSpawner>(),
                sp.GetService<IGameClock>(),
                sp.GetService<EffectRegistry>()));
        services.AddSingleton<RoomLayoutService>();
        // Explicit rather than by convention, for the reason the Phase 4 block below gives: the
        // template cache is optional, and a container that quietly failed to supply it would leave
        // every dark room dark whatever anybody was carrying.
        services.AddSingleton<PlayerView>(sp => new PlayerView(
            sp.GetRequiredService<RoomLayoutService>(),
            sp.GetService<ItemTemplateCache>(),
            sp.GetService<WeatherSystem>()));
        // The sky. Takes only the clock, because the weather is arithmetic on it - what the
        // system owns is which line has already been said, not what the weather is.
        services.AddSingleton<WeatherSystem>();
        services.AddSingleton<WorldMutationApplier>();
        services.AddSingleton<LoopWorldEditor>();
        services.AddSingleton<GameGateway>();

        // Phase 3 systems (spawners, mob AI). Both take the caches so their sweeps read memory
        // instead of querying every tick - per-key template lookups for both, plus the whole
        // spawners table for the sweep; both fall back to the repository if a cache was never
        // loaded, so a container that omitted one costs throughput rather than behaviour.
        services.AddSingleton<MobSpawner>();
        services.AddSingleton<ItemSpawner>();
        services.AddSingleton<SpawnerSystem>(sp =>
            new SpawnerSystem(
                sp.GetRequiredService<ISpawnerRepository>(),
                sp.GetRequiredService<IMobTemplateRepository>(),
                sp.GetRequiredService<IItemTemplateRepository>(),
                sp.GetRequiredService<MobSpawner>(),
                sp.GetRequiredService<ItemSpawner>(),
                sp.GetRequiredService<ILogger<SpawnerSystem>>(),
                sp.GetService<PlayerView>(),
                sp.GetService<MobTemplateCache>(),
                sp.GetService<ItemTemplateCache>(),
                sp.GetService<SpawnerCache>()));
        services.AddSingleton<MobAiSystem>(sp =>
            new MobAiSystem(
                sp.GetRequiredService<IMobTemplateRepository>(),
                sp.GetRequiredService<IRandomSource>(),
                sp.GetRequiredService<IGameClock>(),
                sp.GetRequiredService<PlayerView>(),
                sp.GetService<MobTemplateCache>()));

        // Phase 4 systems (combat, progression). Constructed explicitly rather than by
        // convention: the template caches are optional parameters, and a container that quietly
        // failed to supply them would leave every weapon at the default speed and every mob
        // narrating "hit" - a balance-shaped bug with no exception to notice.
        services.AddSingleton<CombatSystem>(sp =>
            new CombatSystem(
                sp.GetRequiredService<EngineOptions>(),
                sp.GetService<PlayerView>(),
                sp.GetService<ItemTemplateCache>(),
                sp.GetService<MobTemplateCache>(),
                sp.GetService<ItemSpawner>(),
                sp.GetService<ILogger<CombatSystem>>(),
                // Without this a mob attack carrying an effect swings for its damage and applies
                // nothing - the silent half-feature this codebase keeps having to relearn.
                sp.GetService<EffectRegistry>(),
                // Read only to name what a level-up just granted. Absent, a player levels in
                // silence and finds their new ability by guessing.
                sp.GetService<AbilityCache>(),
                // Stamps the loot claim's expiry (LootClaim). Required rather than optional: the
                // fallback would be the ambient wall clock, which is right in production and
                // wrong in a test, where a claim has to be watched expiring rather than waited
                // out - and a test that silently waited out two real minutes would just hang.
                sp.GetRequiredService<IGameClock>()));

        // Phase 5 systems (abilities, quests). Explicit for the same reason as CombatSystem: the
        // mob templates are what tell an area effect which mobs are non-combatants, and a
        // container that quietly failed to supply them would let a stray Firestorm kill the
        // shopkeeper standing behind the wolves.
        services.AddSingleton<AbilityCache>();
        services.AddSingleton<AbilitySystem>(sp =>
            new AbilitySystem(
                sp.GetRequiredService<IGameClock>(),
                sp.GetService<AbilityCache>(),
                sp.GetService<EffectRegistry>(),
                sp.GetService<ILogger<AbilitySystem>>(),
                sp.GetService<MobTemplateCache>()));
        services.AddSingleton<QuestCache>();
        services.AddSingleton<ItemTemplateCache>();
        services.AddSingleton<MobTemplateCache>();
        services.AddSingleton<SpawnerCache>();

        // The countdown itself is always available, so `shutdown` can be scheduled and cancelled
        // wherever the engine runs. What is optional is IShutdownSignal - the thing that actually
        // stops the host - which the Server supplies. Without it a countdown announces and then
        // reaches zero having done nothing, which is the right behaviour for a test host.
        services.AddSingleton<ShutdownSchedule>(sp =>
            new ShutdownSchedule(
                sp.GetRequiredService<IGameClock>(),
                sp.GetService<IShutdownSignal>()));

        // Instruments only, no exporter (PLAN.md §8, Phase 6). Nothing leaves the process until a
        // deployment adds one pointed at EngineMetrics.MeterName, which needs no change here -
        // instrumentation belongs in the code, where it is sent does not.
        //
        // Resolved through IMeterFactory where the host provides one, so a test host gets its own
        // Meter rather than sharing a process-wide singleton with every other test host.
        services.AddSingleton<EngineMetrics>(sp =>
            new EngineMetrics(sp.GetService<System.Diagnostics.Metrics.IMeterFactory>()));

        // Game loop - wired once all systems are available
        services.AddHostedService<GameLoop>();

        return services;
    }
}
