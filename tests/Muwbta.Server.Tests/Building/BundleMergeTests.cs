using System.Text.Json;
using Muwbta.Server.Building;

namespace Muwbta.Server.Tests.Building;

/// <summary>
/// Several bundles as one, so a world imports in a single upload (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Saving five uploads is the smaller half. The importer's order is dependency order over the whole
/// bundle — worlds, zones, templates, rooms, <em>then</em> exits — so six files applied one at a
/// time set the first realm's exits before the next realm's rooms exist. Merged, every room exists
/// before any exit does, and a whole class of dangling-reference warning stops being reported at
/// all. That is asserted below against the real content, because it is the claim the merge is for.
/// </para>
/// <para>
/// It also gives the validator a whole-world view, which is how the Grask gate was found: reciprocity
/// needs both rooms in one bundle, so per file the four cross-realm gates were the one part of the
/// Reaches nothing could check.
/// </para>
/// </remarks>
public sealed class BundleMergeTests
{
    private static IReadOnlyList<BundleSource> AuthoredContent()
    {
        var sources = new List<BundleSource>();

        // content/ and shipped/ together, because that is what actually gets imported: the world
        // and the ability set it is played with. Merging one without the other would prove less
        // than the real command does.
        foreach (var path in BundleDirectories.All()
            .SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            Assert.True(BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error), error);
            sources.Add(new BundleSource(Path.GetRelativePath(RepoPath.Root(), path), bundle!));
        }

        Assert.NotEmpty(sources);
        return sources;
    }

    private static JsonElement NoFlags => JsonDocument.Parse("{}").RootElement;

    private static WorldBundle One(string worldKey) => new(
        BundleFormat.CurrentVersion,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        new BundleScope("world", worldKey),
        [new BundleWorld(worldKey, worldKey, "", 0, NoFlags, new Dictionary<string, decimal>())],
        [], [], [], [], [], [], [], []);

    // -----------------------------------------------------------------------
    // The rules
    // -----------------------------------------------------------------------

    private static BundleGameConfiguration Configuration(string key) =>
        new(key, key, "", "w.z.r", "Hello, {name}.");

    /// <summary>
    /// The canon document is written into the configuration it is for: cut at the marker,
    /// normalised, and into the one named or the only one there is.
    /// </summary>
    [Fact]
    public void A_canon_document_is_written_into_the_named_configuration()
    {
        var bundle = One("w") with { Configurations = [Configuration("the-reaches"), Configuration("other")] };
        const string document = "# The Reaches\r\n\r\nText.  \r\n\r\n<!-- canon:end -->\r\n## Notes\r\n";

        var (named, error) = BundleMerge.WithCanon(bundle, "the-reaches", document);

        Assert.Null(error);
        Assert.Equal("# The Reaches\n\nText.\n", named!.Configurations.Single(c => c.Key == "the-reaches").Canon);
        Assert.Null(named.Configurations.Single(c => c.Key == "other").Canon);

        var (_, ambiguous) = BundleMerge.WithCanon(bundle, null, document);
        Assert.Contains("say which", ambiguous, StringComparison.Ordinal);

        var (_, missing) = BundleMerge.WithCanon(bundle, "nosuch", document);
        Assert.Contains("no configuration 'nosuch'", missing, StringComparison.Ordinal);

        var (_, empty) = BundleMerge.WithCanon(bundle, "the-reaches", "<!-- canon:end -->\nnotes only");
        Assert.Contains("nothing above", empty, StringComparison.Ordinal);

        var (only, _) = BundleMerge.WithCanon(One("w") with { Configurations = [Configuration("solo")] }, null, document);
        Assert.Equal("# The Reaches\n\nText.\n", only!.Configurations.Single().Canon);
    }

    [Fact]
    public void Merging_nothing_is_refused_rather_than_producing_an_empty_world()
    {
        var merged = BundleMerge.Merge([]);

        Assert.False(merged.Ok);
        Assert.Contains(merged.Errors, e => e.Contains("no bundles", StringComparison.Ordinal));
    }

    [Fact]
    public void The_merged_bundle_is_scoped_to_everything()
    {
        var merged = BundleMerge.Merge([new BundleSource("a", One("a")), new BundleSource("b", One("b"))]);

        Assert.True(merged.Ok);
        Assert.Equal("all", merged.Bundle!.Scope.Kind);
        Assert.Null(merged.Bundle.Scope.Key);
        Assert.Equal(2, merged.Bundle.Worlds.Count);
    }

    /// <summary>
    /// An identical key in two files is one entity, not two. Today that is the <c>ossara</c> world
    /// row and the <c>epic-smith-vesh</c> mob template.
    /// </summary>
    [Fact]
    public void An_identical_key_in_two_files_is_carried_once()
    {
        var merged = BundleMerge.Merge([new BundleSource("a", One("shared")), new BundleSource("b", One("shared"))]);

        Assert.True(merged.Ok);
        Assert.Single(merged.Bundle!.Worlds);
    }

    /// <summary>
    /// And a key that <em>disagrees</em> is refused with both filenames, never resolved.
    /// </summary>
    /// <remarks>
    /// Last-one-wins would work today and would go on appearing to work right up until somebody
    /// retuned the shared smith in one file and not the other — which is the silent-drift failure
    /// this repository keeps being bitten by, so it is the one thing the merge will not do.
    /// </remarks>
    [Fact]
    public void A_key_that_differs_between_files_is_refused_naming_both()
    {
        var left = One("shared");
        var right = One("shared");
        right = right with { Worlds = [right.Worlds[0] with { Name = "Renamed" }] };

        var merged = BundleMerge.Merge(
            [new BundleSource("left.json", left), new BundleSource("right.json", right)]);

        Assert.False(merged.Ok);
        Assert.Null(merged.Bundle);

        var error = Assert.Single(merged.Errors);
        Assert.Contains("left.json", error, StringComparison.Ordinal);
        Assert.Contains("right.json", error, StringComparison.Ordinal);
        Assert.Contains("shared", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundles_of_different_versions_are_refused()
    {
        var stale = One("b") with { FormatVersion = BundleFormat.CurrentVersion - 1 };

        var merged = BundleMerge.Merge([new BundleSource("a.json", One("a")), new BundleSource("b.json", stale)]);

        Assert.False(merged.Ok);
        Assert.Contains(merged.Errors, e => e.Contains("formatVersion", StringComparison.Ordinal));
    }

    /// <summary>
    /// The newest input rather than the clock, which is what makes a re-merge reproducible.
    /// </summary>
    [Fact]
    public void The_export_timestamp_is_the_newest_input()
    {
        var older = One("a");
        var newer = One("b") with { ExportedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z") };

        var merged = BundleMerge.Merge([new BundleSource("a", older), new BundleSource("b", newer)]);

        Assert.Equal(newer.ExportedAt, merged.Bundle!.ExportedAt);
    }

    // -----------------------------------------------------------------------
    // Against the world as authored
    // -----------------------------------------------------------------------

    // A census of the authored world used to sit here - 5 worlds, 18 zones, 238 rooms, and four
    // more counts. It was deleted rather than updated again.
    //
    // It asserted nothing about merging. Every rule this file exists to prove is proven above on
    // two-line fixtures: that an identical key is carried once, that a conflicting one is refused
    // naming both files, that versions must agree, that the timestamp is the newest input. The
    // counts only restated how much content happened to exist on the day they were written, so
    // authoring a room failed the test, and the fix was always to edit the number - which teaches
    // people to change an assertion without reading it. It was bumped twice in one day before
    // anybody said so out loud.
    //
    // What it usefully checked - that the real content merges - is asserted below, as a property
    // rather than a number.

    /// <summary>
    /// <b>The whole point of merging.</b> Each file alone warns about references it cannot resolve,
    /// because a realm's gate names a room in the next realm; merged, there is nothing left to
    /// dangle. The count is asserted as zero of <em>everything</em>, errors and warnings both.
    /// </summary>
    [Fact]
    public void What_the_parts_warn_about_the_whole_does_not()
    {
        var sources = AuthoredContent();

        var apart = sources.Sum(s => BundleValidator.Validate(s.Bundle).Findings.Count);
        Assert.True(apart > 0, "the per-file runs used to warn; if they no longer do, this proves nothing");

        var merged = BundleMerge.Merge(sources);
        Assert.True(merged.Ok, string.Join("\n", merged.Errors));

        var whole = BundleValidator.Validate(merged.Bundle!);

        Assert.True(
            whole.Findings.Count == 0,
            $"the merged world should be clean, and reported:\n  "
            + string.Join("\n  ", whole.Findings.Select(f => $"{f.Level}: {f.Message}")));
    }

    /// <summary>
    /// The merged bundle survives being written and read back — which is the check a tool shuffling
    /// raw JSON cannot make, since well-formed JSON and "a bundle the endpoint will bind" are
    /// different claims.
    /// </summary>
    [Fact]
    public void The_merged_bundle_round_trips_through_the_format_the_endpoint_reads()
    {
        var merged = BundleMerge.Merge(AuthoredContent());
        Assert.True(merged.Ok);

        var json = BundleFormat.Write(merged.Bundle!);

        Assert.True(BundleFormat.TryRead(json, out var reread, out var error), error);
        Assert.Equal(merged.Bundle!.Rooms.Count, reread!.Rooms.Count);
        Assert.Equal(merged.Bundle.Spawners.Count, reread.Spawners.Count);

        // Mob attacks are authored in PascalCase and are the field that silently defaults under a
        // case-sensitive reader, so they are what a round-trip test has to look at.
        var attacks = reread.MobTemplates.SelectMany(m => m.Attacks).ToList();
        Assert.NotEmpty(attacks);
        Assert.Contains(attacks, a => a.Verb != Domain.Combat.AttackTiming.DefaultVerb);
        Assert.Contains(attacks, a => a.EffectKey is not null);
    }

    /// <summary>
    /// Re-merging unchanged content produces the same bytes, which is what makes it safe to leave
    /// the merged file out of git and rebuild it at import time.
    /// </summary>
    [Fact]
    public void Merging_twice_produces_the_same_bytes()
    {
        var sources = AuthoredContent();

        var first = BundleFormat.Write(BundleMerge.Merge(sources).Bundle!);
        var second = BundleFormat.Write(BundleMerge.Merge(sources).Bundle!);

        Assert.Equal(first, second);
    }
}
