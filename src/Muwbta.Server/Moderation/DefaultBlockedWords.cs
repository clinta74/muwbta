using System.Reflection;

namespace Muwbta.Server.Moderation;

/// <summary>
/// The blocked-words list this build ships, read from <c>shipped/blocked-words.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A starting value, not a policy.</b> It is written into a configuration that has none and is
/// never consulted again: once a row has a list, the database is the authority and a builder edits
/// it from the panel without a restart. Editing this file changes what a *new* server starts with.
/// </para>
/// <para>
/// <b>Not carried in a bundle</b>, deliberately. What a server refuses to hear is a property of
/// who plays there, not of the world they play in, and a list that travelled with content would
/// arrive from whoever authored the realm - quietly replacing a decision the operator made, or
/// exporting theirs to somebody else.
/// </para>
/// <para>
/// Embedded rather than read from disk, for the reason the container gives: it publishes
/// <c>/app/publish</c> and there is no <c>shipped/</c> beside the server in the image. The realm
/// maps went the other way (<see cref="Game.MapSheets"/>) because a map is drawn from authored
/// rooms and a build carrying its own can describe a world the database no longer holds. This is
/// derived from nothing and can disagree with nothing.
/// </para>
/// </remarks>
public static class DefaultBlockedWords
{
    private const string ResourceName = "Muwbta.Server.BlockedWords.txt";

    private static readonly Lazy<string> Value = new(Read, isThreadSafe: true);

    /// <summary>
    /// The words, one per line, with the file's comments stripped. Empty if the resource is
    /// missing, which reads as "no filter" the same way an empty column does.
    /// </summary>
    public static string List => Value.Value;

    private static string Read()
    {
        using var stream = typeof(DefaultBlockedWords).GetTypeInfo().Assembly
            .GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);

        // Comments are stripped here rather than taught to WordFilter, which splits on spaces and
        // commas and would read a sentence of explanation as fourteen banned words. What reaches
        // the database is the list itself, because that is what the panel shows and edits.
        var words = reader.ReadToEnd()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

        return string.Join('\n', words);
    }
}
