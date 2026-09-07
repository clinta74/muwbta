using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Muwbta.Mcp;

/// <summary>
/// The read half of the tool surface in docs/PAT-AND-MCP.md §11.
/// </summary>
/// <remarks>
/// Eight tools over roughly thirty read endpoints, on the argument in that section: an agent's
/// accuracy falls off as the tool list grows, and the REST surface is shaped for a React client
/// that already knows the domain. The kind-tagged pair (<c>list_content</c>, <c>get_content</c>)
/// carries most of it; the rest exist because they answer a question rather than fetch a row.
/// </remarks>
[McpServerToolType]
public static class BuilderTools
{
    [McpServerTool(Name = "list_content")]
    [Description("""
        Lists content of one kind. Kinds: configuration, world, zone, room, mob, item, ability,
        quest, spawner. 'room' requires zone. 'zone' may be narrowed by world, 'spawner' by zone;
        the other kinds ignore both. Returns the builder API's JSON unchanged, unless 'fields'
        names the properties to keep - a room carries its grid, its legend and every resolved flag,
        which is most of the payload and none of the answer when you are sweeping a zone.
        """)]
    public static async Task<string> ListContentAsync(
        BuilderClient client,
        [Description("One of: configuration, world, zone, room, mob, item, ability, quest, spawner.")]
        string kind,
        [Description("Zone key. Required for kind 'room'; narrows 'spawner'.")]
        string? zone = null,
        [Description("World key. Narrows kind 'zone'.")]
        string? world = null,
        [Description("Comma-separated properties to keep, e.g. 'key,title,flags'. Omit for everything.")]
        string? fields = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.Equals(kind, "room", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(zone))
        {
            throw new McpException(
                "Listing rooms needs a zone - there is no endpoint for every room on the server, "
                + "deliberately. Call list_content(kind: 'zone') first.");
        }

        var path = ContentKinds.ListPath(kind, world, zone)
            ?? throw new McpException($"'{kind}' is not a kind. Known kinds: {ContentKinds.Known}.");

        var json = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(fields) ? json : Project(json, fields);
    }

    /// <summary>
    /// Keeps only the named properties of each object in a JSON array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done here rather than asked of the server, deliberately. A projection parameter on the REST
    /// endpoints would be a second contract for the React client to know about and a second shape
    /// for the tests to cover, to save bytes on a wire that is loopback for the browser and only
    /// expensive for this one caller. The cost of reading it lands where the benefit does.
    /// </para>
    /// <para>
    /// Unknown names are ignored rather than refused: they cost the caller a property they did not
    /// get, and refusing would mean this file holding an opinion about the shape of a room -
    /// which is the thing <see cref="Combine"/> says it must not do.
    /// </para>
    /// <para>
    /// Anything that is not an array of objects comes back untouched. A projection is a request
    /// about a list, and quietly returning an empty object for a single row would be worse than
    /// ignoring the argument.
    /// </para>
    /// </remarks>
    internal static string Project(string json, string fields)
    {
        var keep = fields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (keep.Count == 0)
        {
            return json;
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return json;
        }

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    row.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();

                foreach (var property in row.EnumerateObject().Where(x => keep.Contains(x.Name)))
                {
                    property.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    [McpServerTool(Name = "find_rooms")]
    [Description("""
        Every room in a zone or a whole world, with how one flag resolves for it and where that
        value comes from - the room, its zone, its world, or the registry default. This is the
        question a content sweep asks: which rooms are not marked indoors, which are peaceful
        because their zone says so, which declare dark themselves. Give 'value' to keep only the
        rooms that resolve that way. A world is many calls to the server, so name a zone when you
        can.
        """)]
    public static async Task<string> FindRoomsAsync(
        BuilderClient client,
        [Description("The flag key, e.g. 'indoors'.")] string flag,
        [Description("Zone key. Either this or world.")] string? zone = null,
        [Description("World key. Sweeps every zone in it.")] string? world = null,
        [Description("Keep only rooms resolving this way. Omit for all of them.")] bool? value = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var name = Require(flag, "flag");

        if (string.IsNullOrWhiteSpace(zone) == string.IsNullOrWhiteSpace(world))
        {
            throw new McpException("Give exactly one of 'zone' or 'world'.");
        }

        var zones = string.IsNullOrWhiteSpace(zone)
            ? await ZoneKeysOfAsync(client, world!, cancellationToken).ConfigureAwait(false)
            : [zone!];

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            foreach (var key in zones)
            {
                var json = await client
                    .GetAsync($"/api/builder/zones/{Uri.EscapeDataString(key)}/rooms", cancellationToken)
                    .ConfigureAwait(false);

                using var rooms = JsonDocument.Parse(json);

                foreach (var room in rooms.RootElement.EnumerateArray())
                {
                    WriteRoomFlag(writer, room, name, value);
                }
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Writes one room's answer, or nothing when it does not match the filter.
    /// </summary>
    /// <remarks>
    /// Reads the <c>resolved</c> array the room list already carries rather than resolving the
    /// chain here. The server has done it correctly once; a second implementation on this side
    /// would be a copy of PLAN.md 4.10 that could drift from the one the game reads.
    /// </remarks>
    internal static void WriteRoomFlag(
        Utf8JsonWriter writer,
        JsonElement room,
        string flag,
        bool? wanted)
    {
        if (!room.TryGetProperty("resolved", out var resolved)
            || resolved.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in resolved.EnumerateArray())
        {
            if (!entry.TryGetProperty("key", out var key)
                || !string.Equals(key.GetString(), flag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = entry.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;

            if (wanted is { } only && only != value)
            {
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("key", room.TryGetProperty("key", out var k) ? k.GetString() : null);
            writer.WriteString("title", room.TryGetProperty("title", out var t) ? t.GetString() : null);
            writer.WriteBoolean(flag, value);
            writer.WriteString(
                "source",
                entry.TryGetProperty("source", out var src) ? src.GetString() : null);
            writer.WriteEndObject();
            return;
        }
    }

    /// <summary>The zone keys of one world, for a sweep that was given a world.</summary>
    private static async Task<List<string>> ZoneKeysOfAsync(
        BuilderClient client,
        string world,
        CancellationToken cancellationToken)
    {
        var json = await client
            .GetAsync(ContentKinds.ListPath("zone", world, null)!, cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray()
            .Select(z => z.TryGetProperty("key", out var k) ? k.GetString() : null)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)];
    }

    [McpServerTool(Name = "get_content")]
    [Description("""
        Fetches one piece of content by key. Kinds: world, zone, room, mob, item, ability, quest,
        spawner (whose key is its id). For a configuration's canon, read the muwbta://canon
        resource instead. Returns the builder API's JSON unchanged.
        """)]
    public static async Task<string> GetContentAsync(
        BuilderClient client,
        [Description("One of: world, zone, room, mob, item, ability, quest, spawner.")]
        string kind,
        [Description("The content key. For a spawner, its GUID.")]
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new McpException("A key is required.");
        }

        if (string.Equals(kind, "configuration", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                "A configuration has no single-entity endpoint. list_content(kind: 'configuration') "
                + "returns them all, and the muwbta://canon resource has the active one's canon.");
        }

        var path = ContentKinds.GetPath(kind, key)
            ?? throw new McpException($"'{kind}' is not a kind. Known kinds: {ContentKinds.Known}.");

        return await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "validate_zone")]
    [Description("""
        Everything structurally wrong with a zone, in one answer: dangling exits, unknown flags,
        rooms with no description, ragged grids, inherited PvP; the rooms still flagged unfinished;
        and the quest graph, with any cycles, unreachable quests, and missing prerequisites. This
        is a structural check only. It says nothing about whether the prose fits the world's canon,
        so it cannot tell you the writing is good, only that the wiring holds.
        """)]
    public static async Task<string> ValidateZoneAsync(
        BuilderClient client,
        [Description("Zone key.")] string zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Three endpoints, one question. A builder asking "what is wrong here" wants the hand-set
        // unfinished flags and the quest graph's own findings alongside the computed warnings, and
        // three tools would mean an agent that reliably calls one of them. The storyline belongs
        // here rather than beside the spawn preview it used to sit with: cycles, unreachable and
        // missingPrerequisites are the same kind of finding as a dangling exit, one layer up.
        var key = Uri.EscapeDataString(Require(zone, "zone"));

        var validation = await client
            .GetAsync($"/api/builder/zones/{key}/validate", cancellationToken)
            .ConfigureAwait(false);

        var unfinished = await client
            .GetAsync($"/api/builder/zones/{key}/unfinished", cancellationToken)
            .ConfigureAwait(false);

        var storyline = await client
            .GetAsync($"/api/builder/zones/{key}/storyline", cancellationToken)
            .ConfigureAwait(false);

        return Combine(("validation", validation), ("unfinished", unfinished), ("storyline", storyline));
    }

    [McpServerTool(Name = "spawn_preview")]
    [Description("""
        What a zone's spawns will actually be worth once its world and zone multipliers are
        applied: each template's base and resolved health, xp and gold, and the level it fights
        at. This is the balance view, not a map - for the layout of a zone, list_content(kind:
        'room', zone: ...) returns every room with its editor coordinates and exits.
        """)]
    public static async Task<string> SpawnPreviewAsync(
        BuilderClient client,
        [Description("Zone key.")] string zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var key = Uri.EscapeDataString(Require(zone, "zone"));

        return await client
            .GetAsync($"/api/builder/zones/{key}/preview", cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "check_quest")]
    [Description("""
        Whether a quest can actually be finished: that its giver is reachable, its objectives are
        placed, and its steps can be reached in order. Run it after writing a quest chain - a
        chain that reads perfectly and cannot be completed is the failure this catches.
        """)]
    public static async Task<string> CheckQuestAsync(
        BuilderClient client,
        [Description("Quest key.")] string quest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var key = Uri.EscapeDataString(Require(quest, "quest"));

        return await client
            .GetAsync($"/api/builder/quests/{key}/reachability", cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "where_used")]
    [Description("""
        Where a mob or item template is placed in the world - which spawners carry it and which
        rooms they sit in. Ask before retuning or deleting one: a template used in four zones is
        a different edit from one used nowhere.
        """)]
    public static async Task<string> WhereUsedAsync(
        BuilderClient client,
        [Description("Either 'mob' or 'item'.")] string kind,
        [Description("The template key.")] string key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var path = ContentKinds.PlacementPath(kind, Require(key, "key"))
            ?? throw new McpException(
                $"Only mobs and items are placed, so '{kind}' has no placement. Pass 'mob' or 'item'.");

        return await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "export_bundle")]
    [Description("""
        Exports a world or a zone as the bundle JSON the builder's import accepts. This is how you
        show your work: write the bundle to a file, diff it, and let a person read the change
        before anything reaches the world. Narrow it to one zone unless you mean the whole world -
        a world bundle runs to tens of thousands of tokens.
        """)]
    public static async Task<string> ExportBundleAsync(
        BuilderClient client,
        [Description("World key. Omit if exporting a zone.")] string? world = null,
        [Description("Zone key. Wins over world.")] string? zone = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(world) && string.IsNullOrWhiteSpace(zone))
        {
            throw new McpException("Name a world or a zone. Exporting everything is not a thing this does.");
        }

        var query = BuilderClient.Query(("world", world), ("zone", zone));

        return await client.GetAsync("/api/builder/export" + query, cancellationToken).ConfigureAwait(false);
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new McpException($"'{name}' is required.")
            : value;

    /// <summary>
    /// Joins two JSON documents under named properties without reparsing either into a model.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than through a DTO for the reason in <see cref="BuilderClient"/>:
    /// this project holds no opinion about the shape of a validation warning, and the moment it
    /// does it acquires a version to keep in step with the server.
    /// </remarks>
    private static string Combine(params (string Name, string Json)[] parts)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            foreach (var (name, json) in parts)
            {
                writer.WritePropertyName(name);

                using var document = JsonDocument.Parse(json);
                document.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
