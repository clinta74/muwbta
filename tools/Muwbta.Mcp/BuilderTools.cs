using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Muwbta.Mcp;

/// <summary>
/// The read half of the tool surface in docs/PAT-AND-MCP.md §11.
/// </summary>
/// <remarks>
/// Seven tools over roughly thirty read endpoints, on the argument in that section: an agent's
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
        the other kinds ignore both. Returns the builder API's JSON unchanged.
        """)]
    public static async Task<string> ListContentAsync(
        BuilderClient client,
        [Description("One of: configuration, world, zone, room, mob, item, ability, quest, spawner.")]
        string kind,
        [Description("Zone key. Required for kind 'room'; narrows 'spawner'.")]
        string? zone = null,
        [Description("World key. Narrows kind 'zone'.")]
        string? world = null,
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

        return await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
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
        Reports what is wrong with a zone: dangling exits, unknown flags, rooms with no
        description, ragged grids, inherited PvP - plus the rooms still flagged unfinished. This is
        a structural check only. It says nothing about whether the prose fits the world's canon,
        so it cannot tell you the writing is good, only that the wiring holds.
        """)]
    public static async Task<string> ValidateZoneAsync(
        BuilderClient client,
        [Description("Zone key.")] string zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Merged because they are one question. A builder asking "what is wrong here" wants the
        // hand-set unfinished flags alongside the computed warnings, and two tools would mean an
        // agent that reliably calls one of them.
        var key = Uri.EscapeDataString(Require(zone, "zone"));

        var validation = await client
            .GetAsync($"/api/builder/zones/{key}/validate", cancellationToken)
            .ConfigureAwait(false);

        var unfinished = await client
            .GetAsync($"/api/builder/zones/{key}/unfinished", cancellationToken)
            .ConfigureAwait(false);

        return Combine(("validation", validation), ("unfinished", unfinished));
    }

    [McpServerTool(Name = "zone_map")]
    [Description("""
        The shape of a zone: its room grid and exits as the editor draws them, plus the storyline
        graph of which quests lead to which. Use this before digging, so new rooms land beside
        the right ones rather than on top of them.
        """)]
    public static async Task<string> ZoneMapAsync(
        BuilderClient client,
        [Description("Zone key.")] string zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var key = Uri.EscapeDataString(Require(zone, "zone"));

        var preview = await client
            .GetAsync($"/api/builder/zones/{key}/preview", cancellationToken)
            .ConfigureAwait(false);

        var storyline = await client
            .GetAsync($"/api/builder/zones/{key}/storyline", cancellationToken)
            .ConfigureAwait(false);

        return Combine(("preview", preview), ("storyline", storyline));
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
