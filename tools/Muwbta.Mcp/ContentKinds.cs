namespace Muwbta.Mcp;

/// <summary>
/// The one place a content kind turns into a path.
/// </summary>
/// <remarks>
/// Sixty REST endpoints are not sixty tools (docs/PAT-AND-MCP.md §11): the agent asks for a kind
/// and a key, and this table decides where that lives. Adding a kind is one row here rather than
/// a new tool, which is the property worth protecting - a tool list that grows with the domain is
/// the thing that makes an agent worse at using it.
/// </remarks>
public static class ContentKinds
{
    /// <summary>Collection endpoints, for kinds that have one that is not zone-scoped.</summary>
    private static readonly Dictionary<string, string> ListPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["configuration"] = "/api/builder/configurations",
        ["world"] = "/api/builder/worlds",
        ["zone"] = "/api/builder/zones",
        ["mob"] = "/api/builder/mob-templates",
        ["item"] = "/api/builder/item-templates",
        ["ability"] = "/api/builder/abilities",
        ["quest"] = "/api/builder/quests",
        ["spawner"] = "/api/builder/spawners",
    };

    /// <summary>Single-entity endpoints. Rooms live under their own group, not under their zone.</summary>
    private static readonly Dictionary<string, string> GetPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["world"] = "/api/builder/worlds",
        ["zone"] = "/api/builder/zones",
        ["room"] = "/api/builder/rooms",
        ["mob"] = "/api/builder/mob-templates",
        ["item"] = "/api/builder/item-templates",
        ["ability"] = "/api/builder/abilities",
        ["quest"] = "/api/builder/quests",
        ["spawner"] = "/api/builder/spawners",
    };

    public static string Known =>
        "configuration, world, zone, room, mob, item, ability, quest, spawner";

    /// <summary>
    /// The collection path for a kind, or null when the kind is listed some other way. Rooms are
    /// the one such kind: they are listed per zone, which is why <c>zone</c> is a required argument
    /// for them and meaningless for everything else.
    /// </summary>
    public static string? ListPath(string kind, string? world, string? zone)
    {
        if (string.Equals(kind, "room", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(zone)
                ? null
                : $"/api/builder/zones/{Uri.EscapeDataString(zone)}/rooms";
        }

        if (!ListPaths.TryGetValue(kind, out var path))
        {
            return null;
        }

        // Only two collections narrow, and they narrow by different things. Passing `zone` to
        // /worlds would be silently ignored by the server, which is worse than not sending it.
        return path + (kind.ToLowerInvariant() switch
        {
            "zone" => BuilderClient.Query(("world", world)),
            "spawner" => BuilderClient.Query(("zone", zone)),
            _ => string.Empty,
        });
    }

    public static string? GetPath(string kind, string key) =>
        GetPaths.TryGetValue(kind, out var path)
            ? $"{path}/{Uri.EscapeDataString(key)}"
            : null;

    /// <summary>Where a template is placed. Only mobs and items answer this.</summary>
    public static string? PlacementPath(string kind, string key) => kind.ToLowerInvariant() switch
    {
        "mob" => $"/api/builder/mob-templates/{Uri.EscapeDataString(key)}/placement",
        "item" => $"/api/builder/item-templates/{Uri.EscapeDataString(key)}/placement",
        _ => null,
    };
}
