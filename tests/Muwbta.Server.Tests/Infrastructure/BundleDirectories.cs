namespace Muwbta.Tests;

/// <summary>
/// The directories this repository ships importable bundles from.
/// </summary>
/// <remarks>
/// <para>
/// Two, and the split is the point. <c>content/</c> is the authored world - one world, authored
/// for this engine, on its way to a repository of its own (docs/CONTENT-SPLIT.md). <c>shipped/</c>
/// is what the engine carries whichever world is loaded, which today is the ability set: the four
/// Paths are the game system rather than anybody's realm, and a server with no abilities is not a
/// server missing content, it is a character who cannot cast what the level table already promised
/// them.
/// </para>
/// <para>
/// Here rather than inline in each test because the two that read every bundle have to agree about
/// what "every bundle" means. They disagreed for one commit, silently, in the safe direction - the
/// format check simply stopped covering the ability set when it moved.
/// </para>
/// </remarks>
internal static class BundleDirectories
{
    /// <summary>Every directory holding bundles, absolute, and only those that exist.</summary>
    /// <remarks>
    /// Missing directories are skipped rather than asserted on, because <c>content/</c> is
    /// expected to leave. Each caller asserts that it found bundles, which is the check that
    /// actually matters - a glob matching nothing is a test that always passes.
    /// </remarks>
    public static IEnumerable<string> All() =>
        new[] { "content", "shipped" }
            .Select(name => Path.Combine(RepoPath.Root(), name))
            .Where(Directory.Exists);
}
