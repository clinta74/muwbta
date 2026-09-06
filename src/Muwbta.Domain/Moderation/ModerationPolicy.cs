namespace Muwbta.Domain.Moderation;

/// <summary>
/// What this server refuses to hear. One row, for the whole server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not on <see cref="Worlds.GameConfiguration"/>, where it used to live.</b> A configuration
/// says which world a new character wakes up in; a word list says what the people playing here may
/// not call each other. Those are not the same kind of fact, and storing them together meant
/// activating a different world silently changed the moderation policy - a dev server with two
/// configurations had two lists, and which one applied depended on which realm was loaded. Nobody
/// would choose that, and it was never decided; it followed from where the column happened to go.
/// </para>
/// <para>
/// <b>Not carried in a bundle either.</b> What a server refuses to hear belongs to whoever runs it
/// and to the people playing there, not to whoever authored the world. A list that travelled with
/// content would arrive from the realm's author, replacing a decision the operator made, and an
/// export would hand theirs to whoever they sent a realm to.
/// </para>
/// <para>
/// <b>A single row rather than settings in a file</b>, because a word list is the one piece of
/// configuration that has to change while people are logged in. Somebody finds a way around the
/// filter on a Friday night, and the answer cannot be a redeploy. The engine ships a default that
/// is seeded once; after that this row is the authority.
/// </para>
/// </remarks>
public sealed class ModerationPolicy
{
    /// <summary>
    /// The key of the only row there is. A fixed value rather than an identity column, so
    /// "read the policy" is a lookup that cannot return two answers or none by accident.
    /// </summary>
    public const string SingletonKey = "server";

    /// <summary>
    /// How long the list may be. Generous for a word list and short of anything that would make
    /// recompiling the filter on every edit worth thinking about.
    /// </summary>
    public const int MaxBlockedWordsLength = 4096;

    /// <summary>Always <see cref="SingletonKey"/>.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Words nobody may say here - one per line, or separated by commas or spaces. Empty, which is
    /// what a server with the shipped default removed has, means no filter at all. Compiled by
    /// <see cref="Worlds.WordFilter"/>, which says what matches and what deliberately does not.
    /// </summary>
    public string BlockedWords { get; set; } = string.Empty;
}
