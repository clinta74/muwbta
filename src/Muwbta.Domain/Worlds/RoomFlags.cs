namespace Muwbta.Domain.Worlds;

public enum RoomFlagKind
{
    Boolean = 0,
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
public sealed record RoomFlag(
    string Key,
    RoomFlagKind Kind,
    bool Default,
    string Summary,
    string Phase);

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
        "indoors", "Sheltered from weather, once weather exists.", "later");

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

        return new FlagResolution(flag.Default, FlagSource.Default);
    }

    private static RoomFlag Register(string key, string summary, string phase)
    {
        // Every flag defaults to false today. That is not a coincidence to be tidied away:
        // §4.10 requires the default be the harmless value, and "off" is harmless for all of
        // them. A future flag that wants to default true has to justify itself here.
        var flag = new RoomFlag(key, RoomFlagKind.Boolean, Default: false, summary, phase);

        Registry.Add(key, flag);
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
