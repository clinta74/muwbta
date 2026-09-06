using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Muwbta.Mcp;

/// <summary>
/// The write half of the tool surface (docs/PAT-AND-MCP.md §11, Phase C).
/// </summary>
/// <remarks>
/// Four tools. <c>set_exit</c> is separate because an exit is stated whole rather than patched -
/// a lock left out of the call is a lock removed - which is not what <c>upsert_content</c> means
/// by a field.
///
/// There was a fourth, <c>dig_room</c>, wrapping the walk-and-build endpoint. It went after the
/// first zone was drafted through these tools, which is the only evidence worth having: it failed
/// (DigThrottle paces one dig every two seconds, for a builder holding a movement key), the
/// fallback of upsert plus set_exit produced the same zone, and the fallback is the better path
/// anyway - it writes the title, the prose and the grid position in one call, where a dig leaves a
/// placeholder room that has to be written over. It cost a throttle, a second concept and a tool
/// slot, against saving one call and some arithmetic.
///
/// <c>update_canon</c> is the narrow exception to configurations being read-only here. The canon is
/// prose about the world rather than a deployment setting - changing it alters nothing a player
/// sees - and the first zone drafted through these tools found the canon contradicting the world it
/// describes. An author who can see that and not fix it is being made to file a bug about a text
/// file. It carries every other field of the configuration across untouched and cannot reach
/// <c>/activate</c> at all.
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
        A room wants editorX and editorY, its square on the builder's map: read the zone's existing
        rooms first and place new ones beside their neighbours, or they all land on the origin.
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
                "A configuration is not written through this tool. Which one is active decides what "
                + "the running server serves and what every new player is told, so the starting "
                + "room, the welcome message and activation itself belong to a person in the Setup "
                + "tab. Its canon is the exception - update_canon rewrites that and nothing else.");
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
        leaving a room in place over removing one somebody's exit points at.
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
                "A configuration is not written through this tool. Which one is active decides what "
                + "the running server serves and what every new player is told, so the starting "
                + "room, the welcome message and activation itself belong to a person in the Setup "
                + "tab. Its canon is the exception - update_canon rewrites that and nothing else.");
        }

        var path = ContentKinds.GetPath(kind, Require(key, "key"))
            ?? throw new McpException($"'{kind}' is not a kind. Known kinds: {ContentKinds.Known}.");

        await guard.EnsureWritableAsync(kind, [key], cancellationToken).ConfigureAwait(false);

        var body = await client
            .SendAsync(HttpMethod.Delete, path, null, cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(body) ? $"Deleted {kind} '{key}'." : body;
    }

    [McpServerTool(Name = "set_exit")]
    [Description("""
        Points one room's exit at another, or removes it when 'to' is omitted. The destination
        does not have to exist yet, so a zone can be linked before it is written. This states the
        whole exit: any lock or condition not named here is removed, which is how a locked door is
        unlocked. Exits do not pair themselves - call it twice, once each way, unless you mean a
        one-way passage.
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

    [McpServerTool(Name = "update_canon")]
    [Description("""
        Rewrites one configuration's canon - the text that says what is true in this world and how
        it is written. Nothing else about the configuration changes: not which world it serves, not
        the starting room, not the welcome message, and above all not whether it is active. Use it
        when the canon and the world have come apart, and say in your reply what you changed and
        why, because this is the one thing here that edits your own instructions.
        """)]
    public static async Task<string> UpdateCanonAsync(
        BuilderClient client,
        [Description("The configuration key, from list_content(kind: 'configuration').")]
        string configuration,
        [Description("The whole canon, in markdown. This replaces the previous text entirely.")]
        string canon,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var key = Require(configuration, "configuration");

        if (canon is null)
        {
            throw new McpException("'canon' is required. Pass the whole text; this is not a patch.");
        }

        // Read the row and write it back with one field changed. The endpoint is a whole-object
        // upsert, so sending a partial body would blank the starting room and the welcome message -
        // and a configuration whose starting room went missing is a server that cannot place a new
        // character. Everything but the canon is carried across untouched.
        var list = await client
            .GetAsync("/api/builder/configurations", cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(list);

        var row = document.RootElement.TryGetProperty("configurations", out var rows)
            ? rows.EnumerateArray().FirstOrDefault(
                c => string.Equals(c.GetProperty("key").GetString(), key, StringComparison.Ordinal))
            : default;

        if (row.ValueKind != JsonValueKind.Object)
        {
            throw new McpException(
                $"No configuration '{key}'. list_content(kind: 'configuration') has the keys.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            name = row.GetProperty("name").GetString(),
            description = Text(row, "description"),
            startingRoomKey = row.GetProperty("startingRoomKey").GetString(),
            welcomeMessage = Text(row, "welcomeMessage"),
            blockedWords = Text(row, "blockedWords"),
            canon,
            worldKeys = row.TryGetProperty("worldKeys", out var worlds)
                    && worlds.ValueKind == JsonValueKind.Array
                ? worlds.EnumerateArray().Select(w => w.GetString()).ToList()
                : null,
        });

        // Note the absence of any call to /activate. Whether this configuration is the one the
        // server serves is not a question this tool can reach.
        return await client.SendAsync(
            HttpMethod.Post,
            $"/api/builder/configurations/{Uri.EscapeDataString(key)}",
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? StringField(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new McpException($"'{name}' is required.")
            : value;
}
