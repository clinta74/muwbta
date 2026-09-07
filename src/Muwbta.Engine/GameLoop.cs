using System.Diagnostics;
using Muwbta.Domain.Entities;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Commands;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Quests;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Telemetry;
using Muwbta.Engine.Time;
using Muwbta.Engine.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Muwbta.Engine;

/// <summary>
/// The single thread that owns all mutable world state (PLAN.md §2.1).
///
/// Nothing else mutates <see cref="WorldState"/>. Every input arrives through
/// <see cref="GameGateway"/>, is drained here, and produces output events and save jobs.
/// That is what removes locks from the game logic entirely and makes ticks replayable.
/// </summary>
public sealed class GameLoop(
    GameGateway gateway,
    WorldState world,
    CommandRegistry commands,
    PlayerView view,
    WorldMutationApplier applier,
    LoopWorldEditor loopEditor,
    IAccountAdminQueue adminQueue,
    SystemGameClock clock,
    IWorldSource worldSource,
    ICharacterSaveQueue saveQueue,
    IItemSaveQueue itemSaveQueue,
    IAbilityRepository? abilityRepository,
    AbilityCache? abilityCache,
    IQuestRepository? questRepository,
    QuestCache? questCache,
    ICharacterQuestSaveQueue? questSaveQueue,
    IItemTemplateRepository? itemTemplateRepository,
    ItemTemplateCache? itemTemplateCache,
    IMobTemplateRepository? mobTemplateRepository,
    MobTemplateCache? mobTemplateCache,
    ISpawnerRepository? spawnerRepository,
    SpawnerCache? spawnerCache,
    SpawnerSystem? spawnerSystem,
    MobAiSystem? mobAiSystem,
    CombatSystem? combatSystem,
    WeatherSystem? weatherSystem,
    AbilitySystem? abilitySystem,
    ShutdownSchedule? shutdown,
    EngineMetrics? metrics,
    EngineOptions options,
    ILogger<GameLoop> logger) : BackgroundService
{
    /// <summary>Bounded per pulse so a command flood cannot starve scheduled systems.</summary>
    private const int MaxCommandsPerPulse = 512;

    private const int LinkDeadCheckPulses = 16;

    public WorldState World => world;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var data = await worldSource.LoadAsync(stoppingToken);
        world.Load(data.Worlds, data.Zones, data.Rooms);

        // Load ability cache for synchronous lookups during command handling
        if (abilityRepository != null && abilityCache != null)
        {
            await abilityCache.LoadAsync(abilityRepository, stoppingToken);
        }

        // Load quest cache for synchronous lookups during quest commands
        if (questRepository != null && questCache != null)
        {
            await questCache.LoadAsync(questRepository, stoppingToken);
        }

        // Load item template cache for synchronous lookups during quest rewards
        if (itemTemplateRepository != null && itemTemplateCache != null)
        {
            await itemTemplateCache.LoadAsync(itemTemplateRepository, stoppingToken);
        }

        // Load mob template cache for synchronous lookups during shop commands
        if (mobTemplateRepository != null && mobTemplateCache != null)
        {
            await mobTemplateCache.LoadAsync(mobTemplateRepository, stoppingToken);
        }

        // Load the spawner rules, so the sweep reads memory instead of the whole table every
        // 15 seconds. Kept live by the applier, same as the caches above.
        if (spawnerRepository != null && spawnerCache != null)
        {
            await spawnerCache.LoadAsync(spawnerRepository, stoppingToken);
        }

        EngineLog.LoopStarting(logger, world.RoomCount, GameTiming.PulseInterval.TotalMilliseconds);

        // Published once the world is loaded, so the first collection reports real counts rather
        // than the zeroes an empty pre-load world would give.
        metrics?.PublishGauges(() => world.PlayerCount, () => world.RoomCount);

        using var timer = new PeriodicTimer(GameTiming.PulseInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var start = Stopwatch.GetTimestamp();

                try
                {
                    Pulse();
                }
                catch (Exception ex)
                {
                    // The loop must survive anything a handler throws. A dead loop is a dead
                    // world for every connected player, so this catch is deliberately broad.
                    EngineLog.PulseFailed(logger, clock.CurrentPulse, ex);
                }

                var elapsed = Stopwatch.GetElapsedTime(start);
                var overBudget = elapsed > GameTiming.PulseBudget;

                // Recorded every pulse, not only the slow ones. The log answers "did that
                // happen"; only a distribution answers "is it getting worse", and a log of the
                // failures alone cannot tell one bad pulse from a p99 that has been creeping up
                // for a week (PLAN.md §11).
                metrics?.RecordPulse(elapsed.TotalMilliseconds, overBudget);

                if (overBudget)
                {
                    EngineLog.SlowPulse(
                        logger,
                        clock.CurrentPulse,
                        elapsed.TotalMilliseconds,
                        GameTiming.PulseBudget.TotalMilliseconds);
                }

                clock.Advance();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        ShutdownAllPlayers();
        gateway.Complete();
        EngineLog.LoopStopped(logger, clock.CurrentPulse);
    }

    private void Pulse()
    {
        DrainInbound();

        var pulse = clock.CurrentPulse;

        if (GameTiming.RunsOn(pulse, LinkDeadCheckPulses))
        {
            ExpireLinkDeadPlayers();
        }

        if (spawnerSystem != null && GameTiming.RunsOn(pulse, GameTiming.SpawnSweepPulses))
        {
            // Fire and forget - spawner runs on thread pool, never blocks the loop
            _ = spawnerSystem.RunAsync(world, pulse, CancellationToken.None);
        }

        if (mobAiSystem != null && GameTiming.RunsOn(pulse, GameTiming.MobAiPulses))
        {
            // Fire and forget - mob AI runs on thread pool for template lookups, but updates
            // are applied on the loop thread via SendText and RefreshRoom
            _ = mobAiSystem.RunAsync(world, CancellationToken.None);
        }

        if (abilitySystem != null)
        {
            // Run every pulse - checks for cast-time resolution and interruption
            abilitySystem.Tick(world);
        }

        // Combat runs every pulse and synchronously, on this thread: each combatant swings on
        // its own weapon's clock, and the world is single-writer. Casts resolve first, so a
        // spell that lands this pulse frees the caster's swings on the same one.
        combatSystem?.Tick(world, pulse);

        // Every pulse, because an effect ending is something a player watches for. On the regen
        // tick it was announced up to a minute late, by which time the fight it belonged to was
        // over. The sweep is over a table that is empty whenever nothing is buffed or bleeding.
        EffectExpirySystem.Tick(world, pulse);

        if (GameTiming.RunsOn(pulse, GameTiming.RegenPulses))
        {
            RegenSystem.Tick(world);

            // On the regen tick rather than every pulse: a dream is due once every five minutes,
            // so checking sixty times as often would be sixty times the work to find the same
            // answer. Regen already runs on the minute and already walks every player.
            DreamSystem.Tick(world, pulse);
        }

        // The sky. Reads the wall clock through the oracle and narrates only what changed, so a
        // world with nobody outdoors in it costs one function call and a dictionary compare.
        if (weatherSystem != null && GameTiming.RunsOn(pulse, GameTiming.WeatherPulses))
        {
            weatherSystem.Tick(world);
        }

        // Twice a minute, on its own cadence rather than the regen one. Hunger and thirst grow at
        // different rates and neither is a whole number of regen ticks, so they carry their own
        // accumulator and this only has to be finer than both.
        if (GameTiming.RunsOn(pulse, GameTiming.NeedsPulses))
        {
            NeedsSystem.Tick(world, pulse / GameTiming.NeedsPulses);
        }

        // Once a minute, and separately from regen despite sharing its cadence: this is the only
        // thing that ever clears the floor, and burying it inside a block named for recovery is
        // how a sweep gets removed by somebody tuning something else.
        if (GameTiming.RunsOn(pulse, GameTiming.GroundDecayPulses))
        {
            GroundDecaySystem.Tick(world, clock.UtcNow, view, itemSaveQueue);
        }

        // After everything that could have moved somebody, and before the per-player frames below.
        //
        // Rooms are marked dirty during the pulse rather than redrawn on the spot, so this is
        // where they are actually sent - once each, from the state the tick finished in. Twenty
        // people walking through the same room in one pulse marks it twenty times and sends it
        // once; the nineteen intermediate versions were superseded before anyone could have seen
        // them. See PlayerView.MarkRoomChanged.
        view.FlushChangedRooms(world);

        // One comparison per player per pulse, and a frame only when something actually moved.
        // Pushing unconditionally after combat would mean four frames a second per fighter.
        foreach (var actor in world.AllPlayers)
        {
            PlayerView.SendVitalsIfChanged(actor);
            PlayerView.SendPartyIfChanged(world, actor);
            PlayerView.SendAbilitiesIfLevelled(actor, world, abilityCache, pulse);
        }

        if (pulse > 0 && GameTiming.RunsOn(pulse, GameTiming.AutosavePulses))
        {
            Autosave();
        }

        // Last in the pulse, so the world it announces into is the one that just finished being
        // updated - and the final warning is not sent from a half-run tick.
        shutdown?.Tick(world);
    }

    private void DrainInbound()
    {
        var handled = 0;

        while (handled < MaxCommandsPerPulse && gateway.Reader.TryRead(out var message))
        {
            handled++;

            switch (message)
            {
                case EnterWorld enter:
                    HandleEnter(enter);
                    break;
                case PlayerCommand command:
                    HandleCommand(command);

                    // Timed here rather than inside the handler, which has half a dozen early
                    // returns - a refused verb still cost the queue wait and still tells you what
                    // the loop's latency looks like.
                    metrics?.RecordCommand(
                        Stopwatch.GetElapsedTime(command.AcceptedAt).TotalMilliseconds);
                    break;
                case LeaveWorld leave:
                    HandleLeave(leave);
                    break;
                case WorldMutation mutation:
                    HandleMutation(mutation);
                    break;
                case ReplaceWorld replace:
                    HandleReplaceWorld(replace);
                    break;
                case Notify notify:
                    world.FindBySession(notify.SessionId)?.SendSys(notify.Message, notify.Kind);
                    break;
                case SetActorRole role:
                    HandleSetActorRole(role);
                    break;
                case SetActorMute mute:
                    HandleSetActorMute(mute);
                    break;
                case EvictAccount evict:
                    HandleEvictAccount(evict);
                    break;
            }
        }
    }

    /// <summary>
    /// Applies a role change to every character this account has in the world (PLAN.md §7.7).
    /// </summary>
    private void HandleSetActorRole(SetActorRole message)
    {
        foreach (var actor in world.AllPlayers.Where(p => p.Character.AccountId == message.AccountId))
        {
            if (actor.Role == message.Role)
            {
                continue;
            }

            actor.Role = message.Role;

            // Told plainly, because it changes what their commands do. Silently revoking the
            // builder verbs would read as the game breaking.
            actor.SendSys(
                actor.IsBuilder
                    ? "You have been granted building privileges."
                    : "Your building privileges have been removed.",
                SysKinds.Warning);

            EngineLog.ActorRoleChanged(logger, actor.Name, message.Role.ToString());
        }
    }

    /// <summary>
    /// Applies a mute to every character this account has in the world (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// Told plainly, for the same reason a role change is: a player whose <c>say</c> silently
    /// stopped reaching anyone would conclude the game was broken and go looking for a bug.
    /// </remarks>
    private void HandleSetActorMute(SetActorMute message)
    {
        foreach (var actor in world.AllPlayers.Where(p => p.Character.AccountId == message.AccountId))
        {
            actor.MutedUntil = message.MutedUntil;

            actor.SendSys(
                actor.IsMuted(clock.UtcNow)
                    ? $"You have been muted until {message.MutedUntil:u}."
                    : "You may speak again.",
                SysKinds.Warning);
        }
    }

    /// <summary>
    /// Takes every character an account has out of the world (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// What makes a ban reach someone already connected. Cookie revalidation (§7.7) stops the next
    /// request, but an SSE stream is one long-lived request that was authorised before the ban
    /// existed — so without this a banned player stays in the world until they choose to leave.
    ///
    /// The list is copied first because <see cref="RemovePlayer"/> mutates the collection being
    /// enumerated, which matters here in a way it does not elsewhere: an account with two
    /// characters in the world is exactly the case this exists to cover.
    /// </remarks>
    private void HandleEvictAccount(EvictAccount message)
    {
        var evicted = world.AllPlayers
            .Where(p => p.Character.AccountId == message.AccountId)
            .ToList();

        foreach (var actor in evicted)
        {
            actor.SendSys(message.Message, SysKinds.Disconnect);

            world.TellOthersWhoCanSee(
                actor.RoomKey, actor, $"{actor.Name} is removed from the world.", "movement");

            RemovePlayer(actor, LeaveReason.Kicked);
        }
    }

    /// <summary>
    /// Applies a builder edit and answers the waiting HTTP request (PLAN.md §7.3).
    /// </summary>
    private void HandleMutation(WorldMutation message)
    {
        try
        {
            var result = applier.Apply(message.Change);

            if (result.Success)
            {
                EngineLog.MutationApplied(
                    logger, message.Change.EntityKind, message.Change.EntityKey, result.Applied.Count);
            }
            else
            {
                EngineLog.MutationRefused(
                    logger, message.Change.EntityKind, message.Change.EntityKey, result.Message ?? "");
            }

            message.Completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            // The applier is written not to throw, so reaching here is a bug rather than a
            // refusal. Fail the request instead of the loop, and make sure the builder's
            // request does not hang waiting for a completion that never arrives.
            EngineLog.MutationFailed(logger, message.Change.EntityKind, message.Change.EntityKey, ex);
            message.Completion.TrySetResult(
                MutationResult.Fail(MutationError.Invalid, "The edit could not be applied."));
        }
    }

    private void HandleReplaceWorld(ReplaceWorld message)
    {
        try
        {
            world.Load(message.Data.Worlds, message.Data.Zones, message.Data.Rooms);

            // A reload can remove the room someone is standing in. Same treatment as a
            // deleted room (PLAN.md §7.4): move them somewhere real, never leave them nowhere.
            foreach (var actor in world.AllPlayers.ToList())
            {
                if (world.FindRoom(actor.RoomKey) is null)
                {
                    actor.SendSys("The world shifted around you.", SysKinds.Warning);
                    world.Move(actor, options.StartingRoom);
                }

                view.SendRoom(world, actor, verbose: false);
            }

            EngineLog.WorldReloaded(logger, world.RoomCount);
            message.Completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.WorldReloadFailed(logger, ex);
            message.Completion.TrySetResult(false);
        }
    }

    // -----------------------------------------------------------------------
    // Message handling
    // -----------------------------------------------------------------------

    /// <summary>Said to every character on arrival, after the welcome. See where it is sent.</summary>
    internal const string StaffNotice =
        "Staff never ask for your password. Anyone who does is not staff.";

    private void HandleEnter(EnterWorld message)
    {
        var existing = world.FindByCharacter(message.Character.Id);

        if (existing is not null)
        {
            // Same character, new stream. Either a reconnect inside the grace window or a
            // second tab; both resolve to rebinding rather than cloning the character.
            var wasLinkDead = existing.IsLinkDead;

            existing.Output?.TryComplete();
            world.Rebind(existing, message.SessionId);
            existing.Output = message.Output;
            existing.LinkDeadSincePulse = 0;

            // Reload quests in case they changed while link-dead
            if (message.Quests.Count > 0)
            {
                world.LoadCharacterQuests(existing.Character.Id, message.Quests);
            }

            // Reload items in case inventory changed while link-dead
            if (message.Items.Count > 0)
            {
                world.LoadCharacterItems(existing.Character.Id, message.Items);
            }

            if (wasLinkDead)
            {
                existing.SendSys("Reconnected.", SysKinds.Info);
                EngineLog.PlayerReconnected(logger, existing.Name);
            }

            PlayerView.SendVitals(existing);
            PlayerView.SendParty(world, existing);
            // Resends the roster with live remaining cooldowns, which is what makes a reconnect
            // correct: the client counts down locally, so it has missed every cooldown event that
            // fired while it was away (PLAN.md §3.5).
            PlayerView.SendAbilities(existing, world, abilityCache, clock.CurrentPulse);
            view.SendRoom(world, existing, verbose: true);
            view.MarkRoomChanged(existing.RoomKey);
            return;
        }

        var character = message.Character;

        // The saved room may have been deleted by a builder while the player was away.
        if (!world.TryGetRoom(character.RoomKey, out _))
        {
            EngineLog.RelocatedFromMissingRoom(
                logger,
                character.Name,
                character.RoomKey.ToString(),
                options.StartingRoom.ToString());

            character.RoomKey = options.StartingRoom;
        }

        var actor = new PlayerActor
        {
            Character = character,
            Role = message.Role,
            MutedUntil = message.MutedUntil,
            SessionId = message.SessionId,
            Output = message.Output,
        };

        world.Add(actor);

        // Load character's quests from the message
        if (message.Quests.Count > 0)
        {
            world.LoadCharacterQuests(character.Id, message.Quests);
        }

        // Load character's inventory and equipped items from the message
        if (message.Items.Count > 0)
        {
            world.LoadCharacterItems(character.Id, message.Items);
        }

        actor.SendSys(GameConfiguration.Greet(options.WelcomeMessage, actor.Name), SysKinds.Info);

        // Every arrival, not a builder-editable message: the one sentence that makes the staff tag
        // mean something is the one that says what staff will never do. A player who has read it
        // once has the comparison to hand when "Admin" asks.
        actor.SendSys(StaffNotice, SysKinds.Info);

        PlayerView.SendVitals(actor);
        PlayerView.SendParty(world, actor);
        PlayerView.SendAbilities(actor, world, abilityCache, clock.CurrentPulse);
        view.SendRoom(world, actor, verbose: true);

        world.TellOthersWhoCanSee(actor.RoomKey, actor, $"{actor.Name} appears.", "movement");

        view.MarkRoomChanged(actor.RoomKey);
        EngineLog.PlayerEntered(logger, actor.Name, actor.RoomKey.ToString());
    }

    private void HandleCommand(PlayerCommand message)
    {
        var actor = world.FindBySession(message.SessionId);
        if (actor is null)
        {
            return;
        }

        var (verb, argument) = CommandRegistry.Split(message.Input);
        if (verb.Length == 0)
        {
            return;
        }

        var definition = commands.Find(verb);

        if (definition is null)
        {
            // Not a command. It may still be one of this character's abilities: skills are verbs
            // (PLAN.md §4.7), so `kick rat` has to reach Kick. Checked only after the table has
            // missed, so no existing command can ever be taken out from under someone.
            if (commands.FindAbilityVerb(actor.Character, verb, argument) is not { } ability)
            {
                actor.SendText($"'{verb}' is not something you can do. Try 'help'.", "bad");
                return;
            }

            // The typed verb is kept rather than rewritten to "cast": the handler needs to know
            // which way the player reached it, because a skill may be named but not cast.
            definition = ability.Definition;
            argument = ability.Argument;
        }

        var context = new CommandContext
        {
            Actor = actor,
            World = world,
            View = view,
            Editor = loopEditor,
            AdminQueue = adminQueue,
            ItemSaveQueue = itemSaveQueue,
            ItemTemplates = itemTemplateCache,
            MobTemplates = mobTemplateCache,
            Options = options,
            Shutdown = shutdown,
            Clock = clock,
            Quests = questCache,
            QuestSaveQueue = questSaveQueue,
            Abilities = abilityCache,
            Weather = weatherSystem,
            Verb = verb,
            Argument = argument,
        };

        try
        {
            definition.Handler(context);
        }
        catch (Exception ex)
        {
            // One player's bad command must not take down everyone else's world.
            EngineLog.CommandFailed(logger, message.Input, actor.Name, ex);
            actor.SendText("Something went wrong with that command.", "bad");
            return;
        }

        // Refresh any rooms marked for update by the command handler
        foreach (var roomKey in context.RoomsToRefresh)
        {
            view.MarkRoomChanged(roomKey);
        }

        // Other people first, so an admin who kicked someone still sees the result before their
        // own command can take them out of the world.
        foreach (var (characterId, removalReason) in context.RemovalsRequested)
        {
            if (world.FindByCharacter(characterId) is { } removed)
            {
                RemovePlayer(removed, removalReason);
            }
        }

        if (context.LeaveRequested is { } reason)
        {
            RemovePlayer(actor, reason);
        }
    }

    private void HandleLeave(LeaveWorld message)
    {
        var actor = world.FindBySession(message.SessionId);
        if (actor is null)
        {
            return;
        }

        if (message.Reason == LeaveReason.LinkDead)
        {
            // PLAN.md §3.6: the character stays in the world for the grace window and can
            // still be attacked. Classic MUD risk, and it makes a flaky connection survivable.
            actor.Output?.TryComplete();
            actor.Output = null;
            actor.LinkDeadSincePulse = clock.CurrentPulse;

            world.TellOthersWhoCanSee(
                actor.RoomKey, actor, $"{actor.Name} goes still, eyes unfocused.", "movement");

            view.MarkRoomChanged(actor.RoomKey);
            return;
        }

        RemovePlayer(actor, message.Reason);
    }

    // -----------------------------------------------------------------------
    // Scheduled systems
    // -----------------------------------------------------------------------

    private void ExpireLinkDeadPlayers()
    {
        var expired = world.AllPlayers
            .Where(p => p.IsLinkDead
                && clock.CurrentPulse - p.LinkDeadSincePulse >= options.LinkDeadGracePulses)
            .ToList();

        foreach (var actor in expired)
        {
            RemovePlayer(actor, LeaveReason.LinkDeadExpired);
        }
    }

    private void Autosave()
    {
        foreach (var actor in world.AllPlayers)
        {
            saveQueue.Enqueue(CharacterSnapshot.From(actor.Character, clock.UtcNow));
        }
    }

    private void ShutdownAllPlayers()
    {
        foreach (var actor in world.AllPlayers.ToList())
        {
            actor.SendSys("The world is closing. Your progress is saved.", SysKinds.Disconnect);
            RemovePlayer(actor, LeaveReason.Shutdown);
        }
    }

    private void RemovePlayer(PlayerActor actor, LeaveReason reason)
    {
        var room = actor.RoomKey;

        // Read before the removal, which is what drops them from the party (PLAN.md §5.3).
        var partyMates = world.Parties.Of(actor.CharacterId)?.Members
            .Where(id => id != actor.CharacterId)
            .ToList();

        saveQueue.Enqueue(CharacterSnapshot.From(actor.Character, clock.UtcNow));
        world.Remove(actor);

        foreach (var memberId in partyMates ?? [])
        {
            world.FindByCharacter(memberId)?.SendText($"{actor.Name} leaves the group.", "party");
        }
        actor.Output?.TryComplete();
        actor.Output = null;

        if (reason == LeaveReason.LinkDeadExpired)
        {
            foreach (var other in world.AwakeIn(room))
            {
                other.SendText($"{actor.Name} fades away.", "movement");
            }
        }

        view.MarkRoomChanged(room);
        EngineLog.PlayerLeft(logger, actor.Name, reason.ToString());
    }
}
