namespace Muwbta.Domain.Worlds;

public enum RoomFlagKind
{
    Boolean = 0,

    /// <summary>
    /// One of a fixed set of words, named by <see cref="RoomFlag.Choices"/>.
    /// </summary>
    /// <remarks>
    /// <b>Enumerated rather than free text, and that is the whole point of the kind.</b> A flag
    /// whose value is anything a builder types is a flag where <c>"alpne"</c> resolves to the
    /// default and nothing says so - which is the exact failure §4.10 built the registry to
    /// prevent, reappearing one layer down. The choices are declared here, the API refuses
    /// anything else, and the builder renders a list rather than a text box.
    /// </remarks>
    Text = 1,
}

/// <summary>One entry in the registry: what a flag is called, what it defaults to, what it means.</summary>
/// <param name="Key">The jsonb key. Lowercase camelCase, stable forever once shipped.</param>
/// <param name="Default">
/// Must be the harmless value. An unflagged room, a mistyped key, and a value of the wrong
/// kind all resolve to this, so a flag whose default is dangerous makes forgetting it dangerous
/// (PLAN.md §4.10).
/// </param>
/// <param name="Summary">Shown as the checkbox label in the builder - the UI renders from here.</param>
/// <param name="Phase">Which phase actually enforces it. Documentation, not behaviour.</param>
/// <param name="Choices">
/// For <see cref="RoomFlagKind.Text"/>, every value this flag may take, the default first. Empty
/// for a boolean, whose choices are not worth writing down.
/// </param>
public sealed record RoomFlag(
    string Key,
    RoomFlagKind Kind,
    FlagValue Default,
    string Summary,
    string Phase,
    IReadOnlyList<string> Choices)
{
    /// <summary>The default as a boolean - false for anything that is not a boolean flag.</summary>
    /// <remarks>
    /// Kept because almost every caller asks a boolean question and should not have to unwrap a
    /// union to do it. A text flag answering "false" here is correct rather than lossy: the
    /// question "is this flag set" is not one a climate has an answer to.
    /// </remarks>
    public bool DefaultBoolean => Default.TryAsBoolean(out var value) && value;

    /// <summary>The default as text - empty for anything that is not a text flag.</summary>
    public string DefaultText => Default.TryAsText(out var value) ? value : string.Empty;

    public bool Accepts(string? value) =>
        value is not null && Choices.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// The single source of truth for what room flags exist (PLAN.md §4.10).
/// </summary>
/// <remarks>
/// Adding a flag is one Register call plus the code that reads it. Nothing in the database and
/// nothing in the builder UI: the room editor renders its checkboxes from <see cref="All"/>, so
/// a newly registered flag appears with its summary as the label.
///
/// Read flags through these fields rather than string literals - <c>RoomFlags.Pvp</c> makes a
/// typo a compile error, where <c>"pvpp"</c> would be a flag that is silently always off.
/// </remarks>
public static class RoomFlags
{
    private static readonly Dictionary<string, RoomFlag> Registry = new(StringComparer.Ordinal);
    private static readonly List<RoomFlag> Ordered = [];

    public static readonly RoomFlag Pvp = Register(
        "pvp", "Players may attack one another here.", "Phase 4");

    public static readonly RoomFlag Peaceful = Register(
        "peaceful",
        "No combat at all, mobs included. Overrides pvp. The only rooms sleep is allowed in.",
        "Phase 4");

    public static readonly RoomFlag Respawn = Register(
        "respawn", "A valid bind point: characters may set their respawn here.", "Phase 4");

    public static readonly RoomFlag NoMob = Register(
        "noMob", "Wandering mobs will not path in.", "Phase 3");

    public static readonly RoomFlag NoRecall = Register(
        "noRecall", "Recall and teleport out are refused.", "Phase 5");

    public static readonly RoomFlag Dark = Register(
        "dark", "Description withheld without a light source.", "Phase 5");

    public static readonly RoomFlag Indoors = Register(
        "indoors",
        "Sheltered: no weather, and no notice of the day turning.",
        "Weather");

    /// <summary>
    /// What kind of sky a place has - the one thing that makes one realm's weather differ from
    /// another's (docs/WEATHER.md §1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first text flag, and the reason the kind exists. It inherits like every other flag, so
    /// a world declares itself once and an alpine zone inside it overrides; the alternative was a
    /// column on <c>worlds</c>, which cannot express the mountain zone without a second column on
    /// <c>zones</c> and a third somewhere for the room.
    /// </para>
    /// <para>
    /// <c>temperate</c> is the default because it is the harmless answer: a world nobody has
    /// thought about gets four ordinary seasons rather than no weather or a desert.
    /// <c>subterranean</c> is the interesting one - it produces no weather at all, which is how a
    /// realm with no sky says so without every room in it carrying <c>indoors</c>.
    /// </para>
    /// </remarks>
    public static readonly RoomFlag Climate = RegisterText(
        "climate",
        "What kind of sky this place has. Decides the weather's character, not whether it shows.",
        "Weather",
        ["temperate", "coastal", "arid", "alpine", "subterranean", "blighted"]);

    public static readonly RoomFlag Unfinished = Register(
        "unfinished", "Still a stub. Appears in the zone build to-do list.", "Phase 2");

    /// <summary>Registration order, which is the order the builder renders them in.</summary>
    public static IReadOnlyList<RoomFlag> All => Ordered;

    public static RoomFlag? Find(string? key) =>
        key is null ? null : Registry.GetValueOrDefault(key);

    public static bool IsKnown(string? key) => key is not null && Registry.ContainsKey(key);

    /// <summary>
    /// Resolves a flag down the hierarchy: room, then zone, then world, then the registry
    /// default (PLAN.md §4.10). Overriding, not composing - the nearest level that declares
    /// the flag wins, so an arena zone sets <c>pvp</c> once instead of on forty rooms.
    /// </summary>
    /// <remarks>
    /// Takes flag sets rather than entities so it stays a pure function of four inputs, and so
    /// the Engine can call it with a zone and world it looked up itself. Any level may be null,
    /// which is what happens while a room is being built and its zone is not loaded yet.
    /// </remarks>
    public static FlagResolution Resolve(RoomFlag flag, FlagSet? room, FlagSet? zone, FlagSet? world)
    {
        ArgumentNullException.ThrowIfNull(flag);

        if (room?.BooleanOrNull(flag.Key) is { } fromRoom)
        {
            return new FlagResolution(fromRoom, FlagSource.Room);
        }

        if (zone?.BooleanOrNull(flag.Key) is { } fromZone)
        {
            return new FlagResolution(fromZone, FlagSource.Zone);
        }

        if (world?.BooleanOrNull(flag.Key) is { } fromWorld)
        {
            return new FlagResolution(fromWorld, FlagSource.World);
        }

        return new FlagResolution(flag.DefaultBoolean, FlagSource.Default);
    }

    /// <summary>
    /// The same chain for a text flag: room, then zone, then world, then the default.
    /// </summary>
    /// <remarks>
    /// A sibling rather than a widening of <see cref="Resolve"/>. Every caller of that one asks a
    /// yes-or-no question and gets a <c>bool</c>; making them all unwrap a union to keep asking it
    /// would be churn in service of a symmetry nobody wanted. The two really are different
    /// questions, and a value stored under the wrong kind falls through here exactly as it does
    /// there - a <c>climate</c> of <c>true</c> is not a climate, so it is not an answer.
    /// </remarks>
    public static TextResolution ResolveText(RoomFlag flag, FlagSet? room, FlagSet? zone, FlagSet? world)
    {
        ArgumentNullException.ThrowIfNull(flag);

        if (Accepted(flag, room?.TextOrNull(flag.Key)) is { } fromRoom)
        {
            return new TextResolution(fromRoom, FlagSource.Room);
        }

        if (Accepted(flag, zone?.TextOrNull(flag.Key)) is { } fromZone)
        {
            return new TextResolution(fromZone, FlagSource.Zone);
        }

        if (Accepted(flag, world?.TextOrNull(flag.Key)) is { } fromWorld)
        {
            return new TextResolution(fromWorld, FlagSource.World);
        }

        return new TextResolution(flag.DefaultText, FlagSource.Default);
    }

    /// <summary>
    /// The value if the registry knows it, or null so resolution falls through.
    /// </summary>
    /// <remarks>
    /// The API refuses an unknown choice on the way in, so reaching this with one means a value
    /// written by a newer binary, by a hand-edited bundle, or by a choice that has since been
    /// retired. Falling through to the level above - and eventually to the harmless default - is
    /// the same answer §4.10 gives a mistyped key, for the same reason.
    /// </remarks>
    private static string? Accepted(RoomFlag flag, string? value) =>
        flag.Accepts(value) ? value : null;

    private static RoomFlag Register(string key, string summary, string phase)
    {
        // Every boolean flag defaults to false. That is not a coincidence to be tidied away:
        // §4.10 requires the default be the harmless value, and "off" is harmless for all of
        // them. A future flag that wants to default true has to justify itself here.
        return Add(new RoomFlag(
            key, RoomFlagKind.Boolean, FlagValue.Of(false), summary, phase, []));
    }

    /// <summary>Registers a text flag. The first choice is the default, and must be harmless.</summary>
    private static RoomFlag RegisterText(
        string key,
        string summary,
        string phase,
        string[] choices)
    {
        ArgumentOutOfRangeException.ThrowIfZero(choices.Length);

        return Add(new RoomFlag(
            key, RoomFlagKind.Text, FlagValue.Of(choices[0]), summary, phase, choices));
    }

    private static RoomFlag Add(RoomFlag flag)
    {
        Registry.Add(flag.Key, flag);
        Ordered.Add(flag);
        return flag;
    }
}

public enum FlagSource
{
    Room = 0,
    Zone = 1,
    World = 2,
    Default = 3,
}

/// <summary>
/// A resolved flag and where the value came from. The builder shows inherited values greyed
/// out with their source, so a room that is PvP because of its zone never looks unflagged.
/// </summary>
public readonly record struct FlagResolution(bool Value, FlagSource Source)
{
    public bool IsInherited => Source is FlagSource.Zone or FlagSource.World;
}

/// <summary>A resolved text flag and where the value came from.</summary>
public readonly record struct TextResolution(string Value, FlagSource Source)
{
    public bool IsInherited => Source is FlagSource.Zone or FlagSource.World;
}
