using System.Text.Json;
using System.Text.RegularExpressions;
using Muwbta.Server.Building;

namespace Muwbta.Server.Tests.Building;

/// <summary>
/// The authored content agrees with the build that has to read it (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>The format version is the import path's one hard refusal, and it has been wrong twice.</b>
/// Not in the code — every C# reference already goes to <see cref="WorldBundle.CurrentFormatVersion"/>
/// — but at the edges, where the number has to be restated as data or as prose: the six content
/// files declare it, <c>content/README.md</c> names it, and <c>tools/check-bundle.py</c> keeps a
/// copy. The README said <b>9</b> for two bumps, underneath a sentence warning the reader that it
/// had been wrong before. A comment asking to be kept in step is not a mechanism; this is.
/// </para>
/// <para>
/// <b>Reading through the real record is the half no external checker can do.</b> A script reading
/// raw JSON can confirm a key is present and cannot confirm the server would bind it — and the
/// converters are exactly where that goes wrong, since <c>"paths": ["Warden"]</c> needs
/// <c>JsonStringEnumConverter</c> and throws without it. So these deserialize through
/// <see cref="BundleFormat.SerializerOptions"/>, which is the same set the request pipeline
/// installs.
/// </para>
/// </remarks>
public sealed class BundleFormatTests
{
    /// <summary>
    /// Every authored bundle. Fails loudly when there are none, because a glob that silently
    /// matches nothing turns this whole file into a test that always passes.
    /// </summary>
    public static TheoryData<string> ContentFiles()
    {
        // Both directories this repository ships bundles from: content/ is the authored world,
        // shipped/ is what the engine carries whichever world is loaded. A file in either one is
        // imported by the same endpoint and has to declare the same version.
        var data = new TheoryData<string>();

        foreach (var path in BundleDirectories.All()
            .SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)))
        {
            data.Add(Path.GetRelativePath(RepoPath.Root(), path));
        }

        Assert.NotEmpty(data);
        return data;
    }

    [Theory]
    [MemberData(nameof(ContentFiles))]
    public void An_authored_bundle_declares_the_version_this_build_reads(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(RepoPath.Root(), relativePath));

        Assert.True(
            BundleFormat.TryRead(json, out var bundle, out var error),
            $"{relativePath} is not a bundle this build can read: {error}");

        Assert.True(
            BundleFormat.IsCurrent(bundle!),
            $"{relativePath}: {BundleFormat.VersionRefusal(bundle!.FormatVersion)} "
            + "Re-export it, or bump it deliberately with the changelog entry that explains why.");
    }

    /// <summary>
    /// And binds — not merely parses. Every one of these counts was zero or threw at some point
    /// while this was being written, each for a different converter.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContentFiles))]
    public void An_authored_bundle_binds_through_the_converters_the_endpoint_uses(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(RepoPath.Root(), relativePath));

        Assert.True(BundleFormat.TryRead(json, out var bundle, out var error), error);

        // A spawner id is content: re-minting one doubles a zone's population, so Guid.Empty here
        // would mean the field did not bind rather than that the author left it out.
        Assert.DoesNotContain(bundle!.Spawners, spawner => spawner.Id == Guid.Empty);

        // Mob attacks are authored in PascalCase, so a case-sensitive reader binds none of the four
        // properties and hands back the record's defaults - a generic "hit" every eight pulses with
        // any effect on it gone. That is what a bare camelCase options object did here, silently,
        // which is why SerializerOptions is seeded from JsonSerializerDefaults.Web.
        foreach (var attack in bundle.MobTemplates.SelectMany(m => m.Attacks))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(attack.Verb),
                $"{relativePath}: an attack bound no verb, so the authored attacks are not being read");
        }

        var verbs = bundle.MobTemplates.SelectMany(m => m.Attacks).Select(a => a.Verb).Distinct().ToList();

        // Every mob in the Reaches has an authored verb, so a bundle where they are all "hit" is a
        // bundle whose attacks silently defaulted rather than one written by a lazy author.
        if (verbs.Count > 0)
        {
            Assert.True(
                verbs.Count > 1 || verbs[0] != Domain.Combat.AttackTiming.DefaultVerb,
                $"{relativePath}: every attack came back as the default verb, which means none of "
                + "them bound. See BundleFormat.SerializerOptions.");
        }

        // Enum-valued fields the general converter is responsible for. Named individually because
        // "it deserialised" is true of a record whose every enum silently defaulted.
        Assert.All(bundle.Spawners, spawner => Assert.True(Enum.IsDefined(spawner.TemplateKind)));
        Assert.All(
            bundle.ItemTemplates.Where(item => item.Paths is not null).SelectMany(item => item.Paths!),
            path => Assert.True(Enum.IsDefined(path)));
        Assert.All(
            bundle.ItemTemplates.Where(item => item.Slots is not null).SelectMany(item => item.Slots!),
            slot => Assert.True(Enum.IsDefined(slot)));
    }

    /// <summary>
    /// A bundle from the future is refused rather than half-applied, and says so in the one place
    /// the wording lives.
    /// </summary>
    [Fact]
    public void A_bundle_of_another_version_is_not_current_and_the_refusal_names_both_numbers()
    {
        var stale = new WorldBundle(
            BundleFormat.CurrentVersion - 1,
            DateTimeOffset.UtcNow,
            new BundleScope("all", null),
            [], [], [], [], [], [], [], [], []);

        Assert.False(BundleFormat.IsCurrent(stale));

        var refusal = BundleFormat.VersionRefusal(stale.FormatVersion);
        Assert.Contains((BundleFormat.CurrentVersion - 1).ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains(BundleFormat.CurrentVersion.ToString(), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading a bundle and judging what it claims to be are separate questions, and a malformed
    /// document must not surface as a version complaint.
    /// </summary>
    [Fact]
    public void Something_that_is_not_a_bundle_reports_where_it_went_wrong()
    {
        Assert.False(BundleFormat.TryRead("{\"formatVersion\": \"eleven\"}", out var bundle, out var error));
        Assert.Null(bundle);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// The pipeline and the file reader install the same converters, which is the whole claim
    /// <see cref="BundleFormat"/> exists to make.
    /// </summary>
    /// <remarks>
    /// Asserted by type rather than by reading <c>Program.cs</c>, because the endpoint's set is
    /// built from <see cref="BundleFormat.Converters"/> now — this is what fails if somebody adds
    /// a converter to one of the two by hand later.
    /// </remarks>
    [Fact]
    public void The_file_reader_carries_every_converter_the_pipeline_installs()
    {
        var expected = BundleFormat.Converters().Select(c => c.GetType()).ToList();
        var actual = BundleFormat.SerializerOptions.Converters.Select(c => c.GetType()).ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        Assert.Equal(JsonNamingPolicy.CamelCase, BundleFormat.SerializerOptions.PropertyNamingPolicy);
    }

    // -----------------------------------------------------------------------
    // The copies that live outside C#
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>shipped/README.md</c> tells an author which version to write. It said 9 through two bumps.
    /// </summary>
    [Fact]
    public void The_content_readme_names_the_version_this_build_reads()
    {
        var readme = File.ReadAllText(Path.Combine(RepoPath.Root(), "shipped", "README.md"));

        var stated = Regex.Match(readme, @"these files are at \*\*(\d+)\*\*");

        Assert.True(
            stated.Success,
            "shipped/README.md no longer states the format version in the shape this reads. "
            + "Restore the sentence or delete this test deliberately - silently losing the check "
            + "is how it was wrong for two bumps.");

        Assert.Equal(BundleFormat.CurrentVersion.ToString(), stated.Groups[1].Value);
    }

    /// <summary>
    /// No tool restates the version any more, and none should start.
    /// </summary>
    /// <remarks>
    /// This replaced a guard on <c>tools/check-bundle.py</c>'s hand-kept <c>FORMAT_VERSION = 11</c>.
    /// That copy existed only because the checker was in another language; the C# shims reference
    /// <see cref="BundleFormat.CurrentVersion"/> and cannot drift. What is worth keeping is the
    /// rule that produced the copy in the first place — so this fails if a new tool appears
    /// carrying its own number.
    /// </remarks>
    [Fact]
    public void No_tool_keeps_its_own_copy_of_the_version()
    {
        var tools = Path.Combine(RepoPath.Root(), "tools");

        // Anything that looks like a version constant assigned a bare integer. Deliberately loose:
        // this is meant to catch a shape, not a spelling.
        var suspicious = new Regex(
            @"(FORMAT_VERSION|FormatVersion|formatVersion)\s*[:=]\s*\d+",
            RegexOptions.IgnoreCase);

        var offenders = Directory
            .EnumerateFiles(tools, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => suspicious.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(RepoPath.Root(), f))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "a tool is carrying its own copy of the bundle format version, which is how content and "
            + "code drifted apart twice. Reference BundleFormat.CurrentVersion instead:\n  "
            + string.Join("\n  ", offenders));
    }
}
