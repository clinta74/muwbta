using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Muwbta.Mcp;

/// <summary>
/// The write half of the tool surface (docs/PAT-AND-MCP.md §11, Phase C).
/// </summary>
/// <remarks>
/// Four tools, and two of them exist only because layout has semantics the generic upsert cannot
/// express: digging a room places it beside another and links both ways, and setting an exit
/// states a whole exit rather than patching one. Everything else is <c>upsert_content</c>.
///
/// Every write passes <see cref="WorldGuard"/> first. The token's scope is the other guard and is
/// the server's; a BuilderRead token is refused there no matter what is registered here.
/// </remarks>
[McpServerToolType]
public static class WriteTools
{
    [McpServerTool(Name = "upsert_content")]
    [Description("""
        Creates or updates one piece of content. Kinds: world, zone, room, mob, item, ability,
        quest, spawner. Give the fields as an object shaped like what get_content returns for that
        kind - read one first if you are unsure. Omitted fields are left alone on an update.
        A spawner has no key of its own: omit key to create one, and pass its id to update it.
        Refuses to touch a world the running game is serving.
        """)]
    public static async Task<string> UpsertContentAsync(
        BuilderClient client,
        WorldGuard guard,
        [Description("One of: world, zone, room, mob, item, ability, quest, spawner.")]
        string kind,
        [Description("The fields to write, as an object.")]
        JsonElement fields,
        [Description("The content key. Omit only when creating a spawner.")]
        string? key = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(guard);

        // Not the guard's doing - there is simply no tool for it, and that is the decision. A
        // configuration says which world the game serves and what a new player is told; activating
        // one changes the running server for everybody on it. The error is explicit because
        // ContentKinds.Known lists 'configuration' (it can be read), and an agent told "not a
        // kind" about a kind the list names would reasonably try again.
        if (string.Equals(kind, "configuration", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                "Configurations are read-only here. Which configuration is active decides what the "
                + "running server serves and what every new player is told, so it is changed by a "
                + "person in the Setup tab and not by anything holding a token. Author the world; "
                + "let somebody activate it.");
        }

        if (fields.ValueKind != JsonValueKind.Object)
        {
            throw new McpException("'fields' must be an object of the content's fields.");
        }

        var json = fields.GetRawText();

        // The body can name a home the key does not - a room's zoneKey, a zone's worldKey - so
        // both are checked. Writing into a live world via the body would otherwise be a way past
        // the guard that nobody meant to leave open.
        await guard.EnsureWritableAsync(
            kind,
            [key, StringField(fields, "zoneKey"), StringField(fields, "worldKey")],
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(key))
        {
            var collection = ContentKinds.CreateCollectionPath(kind)
                ?? throw new McpException(
                    $"A {kind} needs a key. Only a spawner is created without one, because its id "
                    + "comes from the server.");

            return await client
                .SendAsync(HttpMethod.Post, collection, json, cancellationToken)
                .ConfigureAwait(false);
        }

        var path = ContentKinds.GetPath(kind, key)
            ?? throw new McpException($"'{kind}' is not a kind. Known kinds: {ContentKinds.Known}.");

        // Which verb this is depends on whether the thing is there, and the server is the only
        // authority on that: POST answers 409 for something that exists and PATCH answers 404 for
        // something that does not, so guessing costs a failed write and a confused agent. One GET
        // costs a round trip on an operation that is already rare.
        var exists = await client.ExistsAsync(path, cancellationToken).ConfigureAwait(false);

        return await client
            .SendAsync(exists ? HttpMethod.Patch : HttpMethod.Post, path, json, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "delete_content")]
    [Description("""
        Deletes one piece of content. This is not reversible from here and the world is live for
        anything already active, so prefer where_used first for a mob or an item, and prefer
        leaving a room in place over removing one somebody's exit points at. Refuses to touch a
        world the running game is serving.
        """)]
    public static async Task<string> DeleteContentAsync(
        BuilderClient client,
        WorldGuard guard,
        [Description("One of: world, zone, room, mob, item, ability, quest, spawner.")]
        string kind,
        [Description("The content key, or a spawner's id.")]
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(guard);

        // Not the guard's doing - there is simply no tool for it, and that is the decision. A
        // configuration says which world the game serves and what a new player is told; activating
        // one changes the running server for everybody on it. The error is explicit because
        // ContentKinds.Known lists 'configuration' (it can be read), and an agent told "not a
        // kind" about a kind the list names would reasonably try again.
        if (string.Equals(kind, "configuration", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                "Configurations are read-only here. Which configuration is active decides what the "
                + "running server serves and what every new player is told, so it is changed by a "
                + "person in the Setup tab and not by anything holding a token. Author the world; "
                + "let somebody activate it.");
        }

        var path = ContentKinds.GetPath(kind, Require(key, "key"))
            ?? throw new McpException($"'{kind}' is not a kind. Known kinds: {ContentKinds.Known}.");

        await guard.EnsureWritableAsync(kind, [key], cancellationToken).ConfigureAwait(false);

        var body = await client
            .SendAsync(HttpMethod.Delete, path, null, cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(body) ? $"Deleted {kind} '{key}'." : body;
    }

    [McpServerTool(Name = "dig_room")]
    [Description("""
        Carves a new room in a direction from an existing one and links them both ways. This is
        how a zone's layout is built: it places the new room beside its neighbour on the editor
        grid, which upsert_content would leave you to work out by hand. Directions: north, east,
        south, west, up, down. The new room arrives unnamed and undescribed - upsert_content gives
        it its prose. Refuses to touch a world the running game is serving.
        """)]
    public static async Task<string> DigRoomAsync(
        BuilderClient client,
        WorldGuard guard,
        [Description("The room to dig from, as world.zone.room.")] string from,
        [Description("north, east, south, west, up, or down.")] string direction,
        [Description("Key for the new room, as world.zone.room. The server picks one if omitted.")]
        string? newRoomKey = null,
        [Description("Whether the new room links back. True unless you mean a one-way passage.")]
        bool reciprocal = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(guard);

        var key = Require(from, "from");

        await guard.EnsureWritableAsync("room", [key, newRoomKey], cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            direction = Require(direction, "direction"),
            reciprocal,
            newRoomKey,
        });

        return await client.SendAsync(
            HttpMethod.Post,
            $"/api/builder/rooms/{Uri.EscapeDataString(key)}/dig",
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "set_exit")]
    [Description("""
        Points one room's exit at another, or removes it when 'to' is omitted. The destination
        does not have to exist yet. This states the whole exit: any lock or condition not named
        here is removed, which is how a locked door is unlocked. For a new room and its link in
        one step, dig_room is the tool. Refuses to touch a world the running game is serving.
        """)]
    public static async Task<string> SetExitAsync(
        BuilderClient client,
        WorldGuard guard,
        [Description("The room the exit leaves, as world.zone.room.")] string from,
        [Description("north, east, south, west, up, or down.")] string direction,
        [Description("Destination room key. Omit to remove the exit.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(guard);

        var key = Require(from, "from");
        var way = Require(direction, "direction");

        await guard.EnsureWritableAsync("room", [key, to], cancellationToken).ConfigureAwait(false);

        var path = $"/api/builder/rooms/{Uri.EscapeDataString(key)}/exits/{Uri.EscapeDataString(way)}";

        if (string.IsNullOrWhiteSpace(to))
        {
            var removed = await client
                .SendAsync(HttpMethod.Delete, path, null, cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(removed) ? $"Removed the {way} exit from '{key}'." : removed;
        }

        return await client.SendAsync(
            HttpMethod.Put,
            path,
            JsonSerializer.Serialize(new { to }),
            cancellationToken).ConfigureAwait(false);
    }

    private static string? StringField(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new McpException($"'{name}' is required.")
            : value;
}
