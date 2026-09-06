using System.Text.Json;
using System.Text.Json.Serialization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;

namespace Muwbta.Engine.Tests.Infrastructure;

/// <summary>
/// The abilities the game ships, read off disk for tests that cast a real one.
/// </summary>
/// <remarks>
/// <para>
/// <b>These used to come from <c>AbilityCatalogue</c>.</b> The set moved to
/// <c>shipped/abilities.json</c> and the catalogue is four examples, so the sixty-nine tests that
/// cast a named ability had to follow it. The alternative was a fixture set with convenient
/// numbers, which would have quietly given up the property those tests exist for: that a cast in
/// the harness spends the same cost, waits the same cooldown, and carries the same effect
/// parameters as a cast in the game.
/// </para>
/// <para>
/// <b>It reads the <c>abilities</c> array and nothing else</b>, with a local shape rather than
/// <c>BundleFormat</c> — this project has no business referencing the server. That is a second
/// reader of one corner of the format, which is a cost; what makes it acceptable is that the
/// corner is four scalar fields and a parameter bag, and a drift in it fails these tests loudly
/// rather than silently, since an ability that will not parse is one no test can cast.
/// </para>
/// </remarks>
internal static class ShippedAbilities
{
    private static readonly Lazy<IReadOnlyDictionary<string, Ability>> Loaded = new(Load);

    /// <summary>Every shipped ability, for tests that ask a question of the whole set.</summary>
    internal static IReadOnlyCollection<Ability> All => Loaded.Value.Values.ToList();

    /// <summary>The shipped ability with this key. Throws, loudly, when there is none.</summary>
    internal static Ability Get(string key) =>
        Loaded.Value.TryGetValue(key, out var ability)
            ? ability
            : throw new InvalidOperationException(
                $"No ability '{key}' in shipped/abilities.json. It carries "
                + $"{Loaded.Value.Count} abilities; a renamed key needs renaming here too.");

    private static IReadOnlyDictionary<string, Ability> Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Muwbta.slnx")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("No repository root above the test binary."),
            "shipped",
            "abilities.json");

        var bundle = JsonSerializer.Deserialize<Bundle>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"{path} did not parse.");

        return bundle.Abilities.ToDictionary(
            a => a.Key,
            a => new Ability
            {
                Key = a.Key,
                Path = a.Path,
                UnlockLevel = a.UnlockLevel,
                Name = a.Name,
                Description = a.Description,
                CostType = a.CostType,
                CostValue = a.CostValue,
                CooldownPulses = a.CooldownPulses,
                CooldownGroup = a.CooldownGroup,
                CastTimePulses = a.CastTimePulses,
                TargetingType = a.TargetingType,
                Effects = [.. a.Effects.Select(e =>
                    new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))],
            },
            StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Bundle(List<Entry> Abilities);

    private sealed record Entry(
        string Key,
        CharacterPath Path,
        int UnlockLevel,
        string Name,
        string Description,
        CostType CostType,
        int CostValue,
        long CooldownPulses,
        int? CooldownGroup,
        long? CastTimePulses,
        TargetingType TargetingType,
        List<Spec> Effects);

    private sealed record Spec(string Key, Dictionary<string, string> Params);
}
