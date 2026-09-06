namespace Muwbta.Domain.Worlds;

/// <summary>
/// A named set of the world-wide choices that are content rather than deployment (PLAN.md §4.16):
/// where a new character wakes up, and what the game says to them when they arrive.
/// </summary>
/// <remarks>
/// <para>
/// <b>Named and plural, so a server can hold more than one and swap between them.</b> Both of these
/// values used to live in code and configuration — the starting room in <c>EngineOptions</c> and an
/// <c>Engine__StartingRoom</c> environment variable, the greeting as a literal in <c>GameLoop</c>
/// that named a world by hand and so was wrong the moment the world changed. Neither is a thing an
/// operator should need a deploy or a container restart to alter, and neither is a thing that
/// belongs to one world: <c>aldenmoor-starter</c> and <c>the-reaches</c> are two complete answers to
/// "what does a new player meet", and being able to keep both and switch is the whole point.
/// </para>
/// <para>
/// <b>They travel in a <c>WorldBundle</c>, and <see cref="IsActive"/> deliberately does not.</b> The
/// definitions are content and belong beside the world they describe. Which one is live is a
/// property of *this* environment, like which room a character is standing in — and an import is a
/// merge that runs against a server with people on it, so a field riding in on a content file must
/// never silently repoint where every new character wakes up. Activation is its own explicit call,
/// and audited as its own act.
/// </para>
/// <para>
/// This is deliberately small. Anything genuinely about the deployment — connection strings, the
/// link-dead grace window, queue bounds — stays in configuration, because it belongs to the
/// environment rather than to the game.
/// </para>
/// </remarks>
public sealed class GameConfiguration
{
    public const int MaxKeyLength = 64;
    public const int MaxNameLength = 96;
    public const int MaxWelcomeLength = 512;

    /// <summary>Room for a few hundred words; a list longer than that is a policy, not a filter.</summary>
    public const int MaxBlockedWordsLength = 4096;

    /// <summary>
    /// A hard cap on <see cref="Canon"/>, well past anything a model window can read. The real
    /// limit is the assist's token budget, which the panel reports; this only stops a paste of
    /// the wrong file becoming a row.
    /// </summary>
    public const int MaxCanonLength = 262_144;

    /// <summary>What <see cref="WelcomeMessage"/> substitutes for the character's name.</summary>
    public const string NameToken = "{name}";

    /// <summary>The greeting used when a configuration leaves it blank, or when none is active.</summary>
    /// <remarks>
    /// Deliberately names no world. The literal it replaces said "Welcome to Aldenmoor" to every
    /// player in every world, and stayed wrong through an entire replacement world being designed —
    /// a default that names something specific is a default that goes stale.
    /// </remarks>
    public const string DefaultWelcomeMessage = "Welcome back, {name}.";

    /// <summary>Single lowercase segment, e.g. <c>the-reaches</c>.</summary>
    public required string Key { get; init; }

    /// <summary>What a builder sees in the list, e.g. "The Reaches".</summary>
    public required string Name { get; set; }

    /// <summary>What this configuration is for, in one or two sentences.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Where new characters start, and where anyone whose saved room no longer exists is placed on
    /// login (PLAN.md §7.4).
    /// </summary>
    /// <remarks>
    /// Text rather than a <see cref="RoomKey"/> column, and deliberately not a foreign key — the
    /// same choice <c>RoomExit.ToRoomKey</c> makes (§6). A configuration that cannot be saved until
    /// its room exists cannot be written *before* importing the world it points into, which is
    /// exactly the order an operator sets up a fresh server in.
    /// </remarks>
    public string StartingRoomKey { get; set; } = string.Empty;

    /// <summary>
    /// What a character is told on entering the game. <see cref="NameToken"/> is replaced with
    /// their name; a message with no token is sent as written.
    /// </summary>
    public string WelcomeMessage { get; set; } = DefaultWelcomeMessage;

    /// <summary>
    /// Words nobody may say here - one per line, or separated by commas or spaces. Empty, the
    /// default, means no filter at all. Compiled by <see cref="WordFilter"/>, which says what
    /// matches and what deliberately does not.
    /// </summary>
    /// <remarks>
    /// On the configuration rather than in appsettings so a builder can change it from the panel
    /// and have it take effect without a restart, the way the welcome message does. Whole words,
    /// case-insensitive; the same list refuses a character name that is exactly a listed word.
    /// <para>
    /// <b>It does not travel in a bundle</b> (format 18). What a server refuses to hear belongs to
    /// whoever runs it and to the people playing there, not to whoever authored the world - so an
    /// import leaves this alone and an export does not carry it. The engine ships a default that is
    /// seeded once into a configuration with none; after that this column is the authority.
    /// </para>
    /// </remarks>
    public string BlockedWords { get; set; } = string.Empty;

    /// <summary>
    /// What the builder assist is told about this world before every request: the canon, as
    /// markdown. Empty, the default, means the one compiled into the server from
    /// <c>docs/WORLD.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On the configuration, because the canon belongs to the world the server is running.</b>
    /// It was an embedded resource - present, identical everywhere, and the Reaches' forever. A
    /// second world built on this server would have inherited Ilvaro and the Regard, and the only
    /// way to tell the model otherwise was a rebuild. A configuration already decides which world
    /// a new character wakes up in, so it is the row that should decide what the assist knows;
    /// activating one swaps the canon with it, and editing it in the panel takes effect on the
    /// next request.
    /// </para>
    /// <para>
    /// Empty falls back to the embedded text rather than to nothing, so a deployment that never
    /// fills this in behaves exactly as it did. It travels in a full bundle and not in a scoped
    /// one: a realm's content file should stay reviewable prose, not carry forty kilobytes of
    /// the same canon six times over.
    /// </para>
    /// </remarks>
    public string Canon { get; set; } = string.Empty;

    /// <summary>
    /// The worlds this configuration is for. A world belongs to at most one configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What makes a configuration exportable as a whole.</b> A bundle used to be a zone, a
    /// world, or everything on the server. Everything is too much once a server holds two
    /// worlds that are not each other's, and a world is too little when a story spans five of
    /// them: the Reaches are five <c>World</c> rows and one configuration. Tagging the worlds
    /// here lets the export say "this configuration" and mean its worlds, the templates they
    /// need, and the configuration itself with its canon - which is the thing somebody moving a
    /// world to another server actually wants.
    /// </para>
    /// <para>
    /// One owner, enforced by the API rather than the database, because the question is asked
    /// across rows. Keys rather than a relation, for the reason <see cref="StartingRoomKey"/> is:
    /// a configuration is written before the worlds it names are imported.
    /// </para>
    /// </remarks>
    public List<string> WorldKeys { get; set; } = [];

    /// <summary>
    /// Whether this is the one the running server uses. Exactly one row may have it.
    /// </summary>
    /// <remarks>
    /// Environment state, not content: excluded from the bundle, and set only by an explicit
    /// activate call. See the type's remarks for why an import must not carry it.
    /// </remarks>
    public bool IsActive { get; set; }

    /// <summary>
    /// When this was last changed. There is deliberately no "by whom" beside it — every edit goes
    /// through <c>WorldEditor</c> and writes a <c>content_audit</c> row carrying the account, the
    /// before and the after, and a second copy of "who" here is one that could disagree with it.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True when this is a legal configuration key.</summary>
    /// <remarks>
    /// The same alphabet a world key uses, because these sit beside worlds in every list a builder
    /// reads and a key that sorts differently from its neighbours reads as a different kind of
    /// thing.
    /// </remarks>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= MaxKeyLength
        && key[0] is >= 'a' and <= 'z'
        && key[^1] != '-'
        && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary><paramref name="template"/> with the name token filled in.</summary>
    /// <remarks>
    /// Static and total: a null or blank template falls back to the default rather than greeting
    /// somebody with an empty line, because a builder who clears the box has almost certainly not
    /// decided that arriving in the world should be silent.
    /// </remarks>
    public static string Greet(string? template, string characterName) =>
        (string.IsNullOrWhiteSpace(template) ? DefaultWelcomeMessage : template)
            .Replace(NameToken, characterName, StringComparison.Ordinal);
}
