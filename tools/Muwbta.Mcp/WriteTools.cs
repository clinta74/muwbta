using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Muwbta.Mcp;

/// <summary>
/// The write half of the tool surface (docs/PAT-AND-MCP.md §11, Phase C).
/// </summary>
/// <remarks>
/// Five tools. <c>set_exit</c> is separate because an exit is stated whole rather than patched -
/// a lock left out of the call is a lock removed - which is not what <c>upsert_content</c> means
/// by a field.
///
/// <c>set_flag</c> is separate for the opposite reason, and it is the one here that prevents a
/// defect rather than saving a call. A flag map sent through <c>upsert_content</c> <em>replaces</em>
/// the whole set (<c>SaveRoomRequest.Flags</c>), so setting <c>indoors</c> on a room that already
/// declares <c>dark</c> silently drops the dark. The builder's own editor never had that problem
/// because it has only ever used the single-flag route; this exposes the same route, and carries
/// the third state - <c>null</c> to remove the key so the level above decides - which a whole-map
/// write cannot express at all.
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
/// <c>set_starting_kit</c> is the second, and narrow the same way: it writes what a new character
/// is handed and carries everything else across. Unlike the canon it does change what a player
/// meets - the next character made gets the kit - which is why it names any kit item that is not
/// no-drop, since a kit that can be sold or handed on makes character creation a way to mint things.
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
                + "tab. Its canon and its starting kit are the exceptions - update_canon and "
                + "set_starting_kit rewrite those and nothing else.");
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
                + "tab. Its canon and its starting kit are the exceptions - update_canon and "
                + "set_starting_kit rewrite those and nothing else.");
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

    [McpServerTool(Name = "set_flag")]
    [Description("""
        Sets one room, zone or world flag, and touches nothing else. Three states: a value is a
        decision made at this level, and omitting it removes the key so the level above decides -
        which is not the same as false. Prefer this over upsert_content for flags: a flag map sent
        that way replaces the entire set, so setting one flag on something that already declares
        another silently drops it.
        Most flags are yes-or-no and take true or false: pvp, peaceful, respawn, noMob, noRecall,
        dark, indoors, unfinished. One takes a word - climate, which is one of temperate, coastal,
        arid, alpine, subterranean or blighted, and decides what kind of weather a realm has.
        Read the flag registry if you are unsure; a value of the wrong kind is refused.
        """)]
    public static async Task<string> SetFlagAsync(
        BuilderClient client,
        WorldGuard guard,
        [Description("One of: room, zone, world.")] string kind,
        [Description("The room, zone or world key.")] string key,
        [Description("The flag key, e.g. 'indoors'.")] string flag,
        [Description("true or false for a yes-or-no flag, a word for one that takes one, or omit to inherit.")]
        JsonElement? value = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(guard);

        var target = Require(key, "key");
        var name = Require(flag, "flag");

        // Named for the three scopes that carry flags rather than taken from ContentKinds, because
        // the other kinds have none: telling an agent "'mob' is not a kind" would be false, and
        // "a mob has no flags" is the answer to what it actually asked.
        var group = kind?.ToLowerInvariant() switch
        {
            "room" => "rooms",
            "zone" => "zones",
            "world" => "worlds",
            _ => throw new McpException(
                $"'{kind}' has no flags. Flags live on rooms, zones and worlds, and resolve in "
                + "that order (PLAN.md 4.10)."),
        };

        await guard.EnsureWritableAsync(kind!, [target], cancellationToken).ConfigureAwait(false);

        return await client.SendAsync(
            HttpMethod.Put,
            $"/api/builder/{group}/{Uri.EscapeDataString(target)}/flags/{Uri.EscapeDataString(name)}",
            JsonSerializer.Serialize(new { value }),
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
        // character. Everything but the canon is carried across untouched; the starting kit is
        // left out, and a missing kit means "keep it".
        var row = await ConfigurationRowAsync(client, key, cancellationToken).ConfigureAwait(false);

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

    [McpServerTool(Name = "set_starting_kit")]
    [Description("""
        Sets what a new character is handed when they are made under one configuration: item
        template keys and how many of each. Replaces the whole kit - an empty list hands out
        nothing. Nothing else about the configuration changes, and characters that already exist
        are given nothing. Kit items should be no-drop, or making a character becomes a way to mint
        things to sell or give away; the reply names any that are not.
        """)]
    public static async Task<string> SetStartingKitAsync(
        BuilderClient client,
        [Description("The configuration key, from list_content(kind: 'configuration').")]
        string configuration,
        [Description("The kit: an array of { itemKey, count }, count defaulting to 1. A bare string is one of that item.")]
        JsonElement items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var key = Require(configuration, "configuration");

        if (items.ValueKind != JsonValueKind.Array)
        {
            throw new McpException("'items' must be an array of { itemKey, count }.");
        }

        var kit = new List<(string ItemKey, int Count)>();

        foreach (var entry in items.EnumerateArray())
        {
            var itemKey = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object when entry.TryGetProperty("itemKey", out var named) => named.GetString(),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(itemKey))
            {
                throw new McpException("Every kit entry needs an itemKey.");
            }

            var count = entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("count", out var counted)
                && counted.TryGetInt32(out var n)
                    ? n
                    : 1;

            kit.Add((itemKey.Trim(), count));
        }

        // The same whole-object round trip update_canon makes, for the same reason. The canon is
        // left out, which the endpoint reads as "keep it".
        var row = await ConfigurationRowAsync(client, key, cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            name = row.GetProperty("name").GetString(),
            description = Text(row, "description"),
            startingRoomKey = row.GetProperty("startingRoomKey").GetString(),
            welcomeMessage = Text(row, "welcomeMessage"),
            worldKeys = row.TryGetProperty("worldKeys", out var worlds)
                    && worlds.ValueKind == JsonValueKind.Array
                ? worlds.EnumerateArray().Select(w => w.GetString()).ToList()
                : null,
            startingKit = kit.Select(e => new { itemKey = e.ItemKey, count = e.Count }).ToList(),
        });

        // The server refuses an unknown item, a duplicate or a silly count, so anything wrong with
        // the kit itself surfaces here as its error rather than being checked twice.
        var saved = await client.SendAsync(
            HttpMethod.Post,
            $"/api/builder/configurations/{Uri.EscapeDataString(key)}",
            payload,
            cancellationToken).ConfigureAwait(false);

        // Said after the save rather than refused before it: a kit item that can be dropped is a
        // choice a world may make, and the author should hear about it rather than be stopped.
        var templates = await client
            .GetAsync("/api/builder/item-templates", cancellationToken)
            .ConfigureAwait(false);

        using var catalogue = JsonDocument.Parse(templates);

        var wanted = kit.Select(e => e.ItemKey).ToHashSet(StringComparer.Ordinal);
        var droppable = catalogue.RootElement.ValueKind == JsonValueKind.Array
            ? catalogue.RootElement.EnumerateArray()
                .Where(t => wanted.Contains(t.GetProperty("key").GetString() ?? string.Empty)
                    && !(t.TryGetProperty("isNoDrop", out var noDrop) && noDrop.ValueKind == JsonValueKind.True))
                .Select(t => t.GetProperty("key").GetString())
                .ToList()
            : [];

        return droppable.Count == 0
            ? saved
            : saved + "\n\nNot no-drop, so a new character can sell these or give them away: "
                + string.Join(", ", droppable);
    }

    /// <summary>One configuration row as the list endpoint returns it, or a refusal naming the key.</summary>
    private static async Task<JsonElement> ConfigurationRowAsync(
        BuilderClient client, string key, CancellationToken cancellationToken)
    {
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

        return row.Clone();
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
