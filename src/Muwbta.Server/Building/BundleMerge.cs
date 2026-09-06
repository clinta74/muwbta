using Muwbta.Server.Assist;
using System.Text.Json;

namespace Muwbta.Server.Building;

/// <summary>The merged bundle, or the reasons there is not one.</summary>
public sealed record BundleMergeResult(WorldBundle? Bundle, IReadOnlyList<string> Errors)
{
    public bool Ok => Bundle is not null && Errors.Count == 0;
}

/// <summary>One named bundle on its way into a merge.</summary>
public sealed record BundleSource(string Name, WorldBundle Bundle);

/// <summary>
/// Several <see cref="WorldBundle"/> files as one, so a whole world imports in a single upload.
/// </summary>
/// <remarks>
/// <para>
/// <b>Saving five uploads is the smaller half.</b> The importer's order is dependency order over
/// the whole bundle — worlds, zones, templates, rooms, <em>then</em> exits. Importing six files one
/// at a time applies the first realm's exits before the next realm's rooms exist, which is why the
/// content README used to name a realm order and warn about reading past dangling exits. Merged,
/// every room exists before any exit is set and that class of warning goes away.
/// </para>
/// <para>
/// It also gives <see cref="BundleValidator"/> a whole-world view. Reciprocity and connectivity can
/// only judge an edge whose target is in the same bundle, so per file the four cross-realm gates
/// are the one part of the Reaches those passes cannot see at all. Run against the merge they found
/// one immediately: the Grask gate left <c>west</c> and came back <c>north</c>.
/// </para>
/// <para>
/// <b>Conflicts are refused, never resolved.</b> A key carried by two files with two different
/// bodies is an error naming both. Today the only repeated keys are the <c>ossara</c> world row and
/// the <c>epic-smith-vesh</c> mob template, and every copy is identical — so last-one-wins would
/// work now and would go on appearing to work right up until somebody retuned the smith in one file
/// and not the other.
/// </para>
/// <para>
/// <b>It does not make the import atomic.</b> One entity is still one loop round trip and one
/// transaction. What merging changes is that the intermediate states are all valid, and that one
/// dry run covers the whole world instead of six.
/// </para>
/// <para>
/// Output is deterministic: every collection is sorted by identity and <c>ExportedAt</c> is the
/// newest input rather than the clock, so re-merging unchanged content produces an identical file.
/// That is what makes it safe to leave the result out of git — a committed merged file would be a
/// second source of truth, six times the same words, drifting invisibly inside a diff nobody reads.
/// </para>
/// </remarks>
public static class BundleMerge
{
    /// <summary>
    /// The bundle with <paramref name="markdown"/>'s canon written into one configuration - the
    /// one named, or the only one there is (PLAN.md §4.16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How the Reaches' canon gets from <c>docs/WORLD.md</c> to the server.</b> The document is
    /// the reviewed, version-controlled source; the configuration row is what the assist reads;
    /// and the merged bundle is the one file that travels between them. Writing the canon in at
    /// merge time keeps the per-realm content files free of a forty-kilobyte string nobody could
    /// review, and keeps the server free of an embedded copy that was the Reaches' whether or not
    /// the server was.
    /// </para>
    /// <para>
    /// Cut and normalised exactly as the assist would (<see cref="Canon.Resolve"/>), so the bundle
    /// carries what the model is told and the row's estimate is the model's cost.
    /// </para>
    /// </remarks>
    /// <returns>The bundle, or an error a person can act on.</returns>
    public static (WorldBundle? Bundle, string? Error) WithCanon(
        WorldBundle bundle,
        string? configurationKey,
        string markdown)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var canon = Canon.Resolve(markdown);

        if (canon.Length == 0)
        {
            return (null, $"the canon document has nothing above its '{Canon.EndMarker}' marker, or is empty");
        }

        var candidates = configurationKey is null
            ? bundle.Configurations
            : [.. bundle.Configurations.Where(c => string.Equals(c.Key, configurationKey, StringComparison.Ordinal))];

        if (candidates.Count != 1)
        {
            return (null, configurationKey is null
                ? $"the bundle carries {bundle.Configurations.Count} configurations; say which one the canon is for"
                : $"the bundle carries no configuration '{configurationKey}'");
        }

        var target = candidates[0];

        return (bundle with
        {
            Configurations = [.. bundle.Configurations.Select(c => ReferenceEquals(c, target) ? c with { Canon = canon } : c)],
        }, null);
    }

    public static BundleMergeResult Merge(IReadOnlyList<BundleSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var errors = new List<string>();

        if (sources.Count == 0)
        {
            return new BundleMergeResult(null, ["no bundles were given"]);
        }

        // A single hard refusal, mirroring the import path's own: bundles of two shapes cannot be
        // meaningfully combined, since the merged file can only claim one version and would be
        // lying about half its contents.
        var versions = sources
            .GroupBy(s => s.Bundle.FormatVersion)
            .OrderBy(g => g.Key)
            .ToList();

        if (versions.Count > 1)
        {
            errors.Add(
                "these bundles are not all the same formatVersion: "
                + string.Join("; ", versions.Select(g =>
                    $"{g.Key} in {string.Join(", ", g.Select(s => s.Name))}")));
        }

        var worlds = Collect(sources, b => b.Worlds, w => w.Key, "worlds", errors);
        var zones = Collect(sources, b => b.Zones, z => z.Key, "zones", errors);
        var items = Collect(sources, b => b.ItemTemplates, i => i.Key, "itemTemplates", errors);
        var mobs = Collect(sources, b => b.MobTemplates, m => m.Key, "mobTemplates", errors);
        var abilities = Collect(sources, b => b.Abilities, a => a.Key, "abilities", errors);
        var rooms = Collect(sources, b => b.Rooms, r => r.Key, "rooms", errors);
        var spawners = Collect(sources, b => b.Spawners, s => s.Id.ToString(), "spawners", errors);
        var quests = Collect(sources, b => b.Quests, q => q.Key, "quests", errors);
        var configurations = Collect(sources, b => b.Configurations, c => c.Key, "configurations", errors);
        var maps = Collect(sources, b => b.Maps, m => m.WorldKey, "maps", errors);

        if (errors.Count > 0)
        {
            return new BundleMergeResult(null, errors);
        }

        return new BundleMergeResult(
            new WorldBundle(
                sources[0].Bundle.FormatVersion,
                sources.Max(s => s.Bundle.ExportedAt),
                new BundleScope("all", null),
                worlds, zones, rooms, items, mobs, abilities, spawners, quests, configurations, maps),
            []);
    }

    /// <summary>
    /// One collection, deduplicated by identity, with a disagreement reported rather than resolved.
    /// </summary>
    /// <remarks>
    /// Sorted by identity, not by the order the files were read in. Every collection is applied
    /// wholesale before the one that depends on it, so ordering within one carries no meaning — and
    /// sorting is what makes a re-merge byte-identical.
    /// </remarks>
    private static List<T> Collect<T>(
        IReadOnlyList<BundleSource> sources,
        Func<WorldBundle, IReadOnlyList<T>> select,
        Func<T, string> identityOf,
        string label,
        List<string> errors)
    {
        var held = new Dictionary<string, (T Entity, string Body, string Source)>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (var entity in select(source.Bundle) ?? [])
            {
                var identity = identityOf(entity);
                var body = JsonSerializer.Serialize(entity, BundleFormat.SerializerOptions);

                if (!held.TryGetValue(identity, out var existing))
                {
                    held[identity] = (entity, body, source.Name);
                }
                else if (!string.Equals(existing.Body, body, StringComparison.Ordinal))
                {
                    // Both sides named, because "which file wins" is the question the reader is
                    // about to ask and the whole reason this is not resolved silently.
                    errors.Add(
                        $"{label} '{identity}' differs between {existing.Source} and {source.Name}. "
                        + "Resolve it in the content, because there is no right answer to pick here.");
                }
            }
        }

        return [.. held
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value.Entity)];
    }
}
