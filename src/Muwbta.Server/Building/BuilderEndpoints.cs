using System.Text.Json;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Microsoft.Extensions.Options;
using Muwbta.Server.Assist;
using Muwbta.Engine.Mutations;
using Muwbta.Persistence;
using Muwbta.Server.Auth;
using Muwbta.Server.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

using Muwbta.Server.Game;

namespace Muwbta.Server.Building;

/// <summary>
/// The builder API (PLAN.md §7.3). Reads come from the database via <see cref="BuilderQueries"/>;
/// writes go through <see cref="WorldEditor"/>, which is the only path that touches the world.
/// </summary>
public static class BuilderEndpoints
{
    public static void MapBuilderEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Throttled per account, and loosely: opening the builder issues a burst of reads for the
        // zone tree, and the population here is a trusted role whose failure mode is mess rather
        // than breach — the same reasoning DigThrottle already carries for `dig` alone.
        var group = routes.MapGroup("/api/builder")
            .RequireAuthorization(Policies.Builder)
            .RequireRateLimiting(RateLimiting.Builder);

        // The flag registry drives the room editor's checkboxes, so a newly registered flag
        // reaches the UI with no client change at all (PLAN.md §4.10).
        group.MapGet("/room-flags", () => Results.Ok(
            RoomFlags.All.Select(f => new RoomFlagResponse(f.Key, f.Default, f.Summary, f.Phase))));

        // The terrain vocabulary, same idea as the flag registry above: adding a kind reaches the
        // room editor's dropdown with no client change.
        group.MapGet("/terrain-kinds", () => Results.Ok(
            TerrainGenerator.Kinds.Select(k => new { k.Key, k.Summary })));

        // Draws a room's map. A pure function of the kind and the room key - no database, no
        // write - so it is a GET, and the client saves the result through the same room PATCH a
        // brush stroke uses. WorldEditor stays the only path into the world.
        //
        // Seeded by the room key rather than by chance, which is what makes asking twice give the
        // same answer and what keeps a regenerated zone's diff readable (WORLD.md §10.1).
        group.MapGet("/rooms/{key}/terrain/{kind}", (string key, string kind) =>
            TerrainGenerator.Find(kind) is null
                ? Results.NotFound($"There is no terrain kind '{kind}'.")
                : Results.Ok(TerrainGenerator.Generate(kind, key)));

        // What this build will accept, so the browser can say so before an upload rather than after
        // a refusal. The format version is the only hard refusal in the import path, and until this
        // existed the sole way to discover a server had not been updated yet was to send it a
        // bundle and read the 400 - which is a slow way to learn something the server knew all along.
        group.MapGet("/bundle-format", () => Results.Ok(
            new BundleFormatResponse(BundleFormat.CurrentVersion)));

        // Named starter configurations (§4.16). Content, not deployment: an operator should not
        // need a container restart to move where new characters wake up, and a server can hold
        // several complete answers and swap between them.
        group.MapGet("/configurations", ListConfigurationsAsync);
        group.MapPost("/configurations/{key}", UpsertConfigurationAsync);
        group.MapDelete("/configurations/{key}", DeleteConfigurationAsync);
        group.MapPost("/configurations/{key}/activate", ActivateConfigurationAsync);
        group.MapGet("/configurations/{key}/canon", ConfigurationCanonAsync);

        group.MapGet("/worlds", ListWorldsAsync);
        group.MapGet("/worlds/{key}", GetWorldAsync);
        group.MapPost("/worlds/{key}", CreateWorldAsync);
        group.MapPatch("/worlds/{key}", UpdateWorldAsync);
        group.MapPut("/worlds/{key}/flags/{flag}", SetWorldFlagAsync);
        group.MapDelete("/worlds/{key}", DeleteWorldAsync);

        group.MapGet("/zones", ListZonesAsync);
        group.MapGet("/zones/{key}", GetZoneAsync);
        group.MapPost("/zones/{key}", CreateZoneAsync);
        group.MapPatch("/zones/{key}", UpdateZoneAsync);
        group.MapPut("/zones/{key}/flags/{flag}", SetZoneFlagAsync);
        group.MapDelete("/zones/{key}", DeleteZoneAsync);

        group.MapGet("/zones/{key}/rooms", ListRoomsAsync);
        group.MapGet("/zones/{key}/validate", ValidateZoneAsync);
        group.MapGet("/zones/{key}/unfinished", UnfinishedAsync);
        group.MapGet("/zones/{key}/preview", PreviewAsync);
        group.MapPost("/zones/{key}/respawn", RespawnZoneAsync);

        group.MapGet("/audit", AuditAsync);

        group.MapGet("/export", ExportAsync);
        group.MapPost("/import", ImportAsync);

        // Live edit feed: a second builder's saved change lands here so open panels refresh.
        group.MapGet("/stream", StreamChangesAsync);

        group.MapGet("/mob-templates", ListMobTemplatesAsync);
        group.MapGet("/mob-templates/{key}", GetMobTemplateAsync);
        group.MapGet("/mob-templates/{key}/placement", MobPlacementAsync);
        group.MapPost("/mob-templates/{key}", CreateMobTemplateAsync);
        group.MapPatch("/mob-templates/{key}", UpdateMobTemplateAsync);
        group.MapDelete("/mob-templates/{key}", DeleteMobTemplateAsync);

        group.MapGet("/abilities", ListAbilitiesAsync);
        group.MapGet("/abilities/{key}", GetAbilityAsync);
        group.MapPost("/abilities/{key}", CreateAbilityAsync);
        group.MapPatch("/abilities/{key}", UpdateAbilityAsync);
        group.MapDelete("/abilities/{key}", DeleteAbilityAsync);

        group.MapGet("/item-templates", ListItemTemplatesAsync);
        group.MapGet("/item-templates/{key}", GetItemTemplateAsync);
        group.MapGet("/item-templates/{key}/placement", ItemPlacementAsync);
        group.MapPost("/item-templates/{key}", CreateItemTemplateAsync);
        group.MapPatch("/item-templates/{key}", UpdateItemTemplateAsync);
        group.MapDelete("/item-templates/{key}", DeleteItemTemplateAsync);

        group.MapGet("/spawners", ListSpawnersAsync);
        group.MapGet("/spawners/{id}", GetSpawnerAsync);
        group.MapPost("/spawners", CreateSpawnerAsync);
        group.MapPatch("/spawners/{id}", UpdateSpawnerAsync);
        group.MapDelete("/spawners/{id}", DeleteSpawnerAsync);

        group.MapGet("/quests", ListQuestsAsync);
        group.MapGet("/quests/{key}", GetQuestAsync);
        group.MapPost("/quests/{key}", CreateQuestAsync);
        group.MapPatch("/quests/{key}", UpdateQuestAsync);
        group.MapDelete("/quests/{key}", DeleteQuestAsync);
        group.MapGet("/quests/{key}/reachability", QuestReachabilityAsync);
        group.MapGet("/zones/{zoneKey}/storyline", StorylineGraphAsync);

        routes.MapBuilderRoomEndpoints();
    }

    // -----------------------------------------------------------------------
    // Change feed
    // -----------------------------------------------------------------------

    /// <summary>
    /// A long-lived SSE stream of persisted edits (PLAN §2). Modelled on the game stream, minus
    /// the per-character session: any authorised builder may listen and every subscriber sees
    /// every edit. Auth is the cookie, since EventSource cannot set headers.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    private static async Task StreamChangesAsync(
        HttpContext http,
        BuilderChangeFeed feed,
        CancellationToken ct)
    {
        var response = http.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Covers nginx (header) and Kestrel (DisableBuffering) - otherwise events sit buffered
        // and the stream looks dead.
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = feed.Subscribe(out var reader);

        try
        {
            await response.WriteAsync("retry: 3000\n\n", ct);
            await response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                // Wake at least every heartbeat so an idle stream stays alive through proxies.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(HeartbeatInterval);

                bool hasData;
                try
                {
                    hasData = await reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await response.WriteAsync(": ping\n\n", ct);
                    await response.Body.FlushAsync(ct);
                    continue;
                }

                if (!hasData)
                {
                    break;
                }

                while (reader.TryRead(out var change))
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        kind = change.Kind,
                        key = change.Key,
                        action = change.Action,
                        byAccountId = change.ByAccountId,
                    });
                    await response.WriteAsync($"event: entity-changed\ndata: {json}\n\n", ct);
                }

                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away. Expected; the subscription is disposed by the using above.
        }
    }

    // -----------------------------------------------------------------------
    // Worlds
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Named starter configurations (PLAN.md §4.16)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every configuration, with which one is live and whether its starting room actually exists.
    /// </summary>
    /// <remarks>
    /// Reports <c>startingRoomExists</c> rather than hiding or refusing a configuration that points
    /// nowhere. Writing one before importing the world it names is the ordinary order of operations
    /// on a fresh server, so a dangling value is a warning in the panel and not an error - the same
    /// reading §7.4 takes of every other dangling reference.
    /// </remarks>
    private static async Task<IResult> ListConfigurationsAsync(
        MuwbtaDbContext db,
        EngineOptions options,
        IOptions<AssistOptions> assist,
        CancellationToken ct)
    {
        var stored = await db.GameConfigurations.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var rooms = await RoomsThatExistAsync(db, stored.Select(c => c.StartingRoomKey), ct);

        var rows = stored
            .Select(c => new GameConfigurationResponse(
                c.Key,
                c.Name,
                c.Description,
                c.StartingRoomKey,
                c.WelcomeMessage,
                c.BlockedWords,
                c.IsActive,
                rooms.Contains(c.StartingRoomKey),
                c.UpdatedAt,
                c.Canon,
                Canon.EstimateTokens(Canon.Resolve(c.Canon)),
                [.. c.WorldKeys]))
            .ToList();

        // What the loop is actually obeying, which is not always what the rows say: a database
        // with no active row leaves EngineOptions on its configured fallback, and a panel that
        // showed an empty list would imply the server had no starting room at all.
        return Results.Ok(new GameConfigurationList(
            rows,
            options.StartingRoom.ToString(),
            options.WelcomeMessage,
            assist.Value.CanonTokenBudget,
            Canon.CharsPerToken));
    }

    /// <summary>The stored canon as markdown, for tools and for saving a copy. 404 when the key is unknown.</summary>
    private static async Task<IResult> ConfigurationCanonAsync(
        string key,
        MuwbtaDbContext db,
        CancellationToken ct)
    {
        var canon = await db.GameConfigurations.AsNoTracking()
            .Where(c => c.Key == key)
            .Select(c => c.Canon)
            .FirstOrDefaultAsync(ct);

        return canon is null
            ? Results.NotFound(new { error = $"No configuration '{key}'." })
            : Results.Text(Canon.Resolve(canon), "text/markdown; charset=utf-8");
    }


    private static async Task<IResult> UpsertConfigurationAsync(
        string key,
        GameConfigurationRequest request,
        WorldEditor editor,
        MuwbtaDbContext db,
        IServiceProvider services,
        HttpContext http,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Results.BadRequest(new { error = "A configuration body is required." });
        }

        if (!GameConfiguration.IsValidKey(key))
        {
            return Results.BadRequest(new
            {
                error = $"'{key}' is not a configuration key "
                    + "(lowercase letters, digits and inner hyphens, e.g. the-reaches).",
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "A configuration needs a name." });
        }

        if (!RoomKey.TryParse(request.StartingRoomKey, out var startingRoom))
        {
            return Results.BadRequest(new
            {
                error = $"'{request.StartingRoomKey}' is not a room key "
                    + "(three dot-separated segments, e.g. ossara.gatetown.the-gate-yard).",
            });
        }

        var welcome = request.WelcomeMessage ?? string.Empty;

        if (welcome.Length > GameConfiguration.MaxWelcomeLength)
        {
            return Results.BadRequest(new
            {
                error = $"The welcome message is limited to {GameConfiguration.MaxWelcomeLength} characters.",
            });
        }

        var blockedWords = request.BlockedWords ?? string.Empty;

        if (blockedWords.Length > GameConfiguration.MaxBlockedWordsLength)
        {
            return Results.BadRequest(new
            {
                error = $"The word list is limited to {GameConfiguration.MaxBlockedWordsLength} characters.",
            });
        }

        // Null leaves the stored canon alone; anything else, empty included, replaces it. Stored
        // as typed, and normalised on the way to the model (Canon.Resolve), so what a builder
        // reads back is what they wrote.
        if (request.Canon is { Length: > GameConfiguration.MaxCanonLength })
        {
            return Results.BadRequest(new
            {
                error = $"The canon is limited to {GameConfiguration.MaxCanonLength:N0} characters, and the "
                    + "model reads far fewer than that. Cut it down in the panel.",
            });
        }

        // Null leaves the stored list alone. Otherwise every key must be a world key and must not
        // already be another configuration's: one owner, so "export this configuration" has one
        // answer. Checked in memory - there are a handful of rows - rather than through an array
        // query the provider may or may not translate.
        List<string>? worldKeys = null;

        if (request.WorldKeys is not null)
        {
            worldKeys = [.. request.WorldKeys
                .Select(k => k?.Trim() ?? string.Empty)
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.Ordinal)];

            if (worldKeys.FirstOrDefault(k => !GameConfiguration.IsValidKey(k)) is { } badWorld)
            {
                return Results.BadRequest(new
                {
                    error = $"'{badWorld}' is not a world key (lowercase letters, digits and inner hyphens).",
                });
            }

            var others = await db.GameConfigurations.AsNoTracking()
                .Where(c => c.Key != key)
                .ToListAsync(ct);

            foreach (var other in others)
            {
                if (other.WorldKeys.Intersect(worldKeys, StringComparer.Ordinal).FirstOrDefault() is { } taken)
                {
                    return Results.BadRequest(new
                    {
                        error = $"'{taken}' already belongs to configuration '{other.Key}'. "
                            + "A world belongs to one configuration; remove it there first.",
                    });
                }
            }
        }

        // Whether this edit also moves the running loop. The applier has no database, so the
        // question is answered here and carried on the mutation.
        var live = await db.GameConfigurations.AsNoTracking()
            .AnyAsync(c => c.Key == key && c.IsActive, ct);

        http.TryGetAccountId(out var accountId);

        var outcome = await editor.ApplyAsync(
            new UpsertGameConfiguration(
                key, request.Name, request.Description ?? string.Empty,
                request.StartingRoomKey, welcome, blockedWords, request.Canon, worldKeys, live),
            accountId,
            ct);

        if (!outcome.Ok)
        {
            return Results.BadRequest(new { error = Describe(outcome) });
        }

        // The assist reads the live configuration's canon as the leading substring of every
        // prompt, and the model caches prompts by that prefix - so an edit to it has to be put in
        // front of the model again before somebody drafts, or the first draft pays minutes for it.
        // Asked rather than told: the call does nothing unless the text actually moved, and most
        // saves move a welcome message instead.
        if (live)
        {
            RewarmAssist(services);
        }

        var exists = await db.Rooms.AsNoTracking().AnyAsync(r => r.Key == startingRoom, ct);

        // Read back rather than echoed, because a null canon or world list in the request meant
        // "keep it".
        var stored = await db.GameConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);
        var canon = stored?.Canon ?? string.Empty;

        return Results.Ok(new GameConfigurationResponse(
            key, request.Name, request.Description ?? string.Empty,
            request.StartingRoomKey, welcome, blockedWords, live, exists, DateTimeOffset.UtcNow,
            canon, Canon.EstimateTokens(Canon.Resolve(canon)), [.. stored?.WorldKeys ?? []]));
    }

    /// <remarks>
    /// The live one is refused. Deleting the configuration the server is currently obeying would
    /// leave the loop pointing at values with no row behind them - fine until the next restart, and
    /// then silently back to the compiled fallback, which is the sort of failure that surfaces
    /// weeks later as "why do new characters start in Millbrook".
    /// </remarks>
    private static async Task<IResult> DeleteConfigurationAsync(
        string key,
        WorldEditor editor,
        MuwbtaDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        var entity = await db.GameConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);

        if (entity is null)
        {
            return Results.NotFound(new { error = $"No configuration '{key}'." });
        }

        if (entity.IsActive)
        {
            return Results.BadRequest(new
            {
                error = "This is the active configuration. Activate another one first.",
            });
        }

        http.TryGetAccountId(out var accountId);

        var outcome = await editor.ApplyAsync(new DeleteGameConfiguration(key), accountId, ct);

        return outcome.Ok
            ? Results.NoContent()
            : Results.BadRequest(new { error = Describe(outcome) });
    }

    /// <summary>
    /// Makes one configuration live, taking effect for the next character to enter the game.
    /// </summary>
    /// <remarks>
    /// Its own endpoint rather than a field on the upsert, because editing what a configuration
    /// says and choosing which one the server obeys are different decisions with very different
    /// blast radii - and because an import that could set it would repoint a running server as a
    /// side effect of loading content.
    /// </remarks>
    private static async Task<IResult> ActivateConfigurationAsync(
        string key,
        WorldEditor editor,
        MuwbtaDbContext db,
        IServiceProvider services,
        HttpContext http,
        CancellationToken ct)
    {
        var entity = await db.GameConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);

        if (entity is null)
        {
            return Results.NotFound(new { error = $"No configuration '{key}'." });
        }

        http.TryGetAccountId(out var accountId);

        var outcome = await editor.ApplyAsync(
            new ActivateGameConfiguration(
                key, entity.StartingRoomKey, entity.WelcomeMessage, entity.BlockedWords, entity.Canon),
            accountId,
            ct);

        if (!outcome.Ok)
        {
            return Results.BadRequest(new { error = Describe(outcome) });
        }

        // Activation swaps the canon along with the starting room, so the same re-warm applies -
        // and a swap between two configurations that carry the same canon still costs nothing.
        RewarmAssist(services);

        var exists = RoomKey.TryParse(entity.StartingRoomKey, out var parsed)
            && await db.Rooms.AsNoTracking().AnyAsync(r => r.Key == parsed, ct);

        return Results.Ok(new GameConfigurationResponse(
            entity.Key, entity.Name, entity.Description, entity.StartingRoomKey,
            entity.WelcomeMessage, entity.BlockedWords, IsActive: true, exists, DateTimeOffset.UtcNow,
            entity.Canon, Canon.EstimateTokens(Canon.Resolve(entity.Canon)), [.. entity.WorldKeys]));
    }

    /// <summary>
    /// Asks the assist to put the live canon in front of the model again, if it has moved.
    /// </summary>
    /// <remarks>
    /// Resolved rather than injected, because the assist is registered only when it is configured
    /// (<c>Program.cs</c>) and a server without a model behind it must still be able to save a
    /// configuration. <see cref="AssistWarmUp.CanonChanged"/> decides whether there is anything to
    /// do; this only knows where to ask.
    /// </remarks>
    private static void RewarmAssist(IServiceProvider services) =>
        services.GetService<AssistWarmUp>()?.CanonChanged();

    /// <summary>
    /// Why an edit did not stick, told apart properly.
    /// </summary>
    /// <remarks>
    /// <c>Refused</c> and <c>NotSaved</c> are different failures with the same falsy <c>Ok</c>, and
    /// collapsing them cost real time: a persistence error surfaced as the word "Refused." with no
    /// message, which reads as a validation rule nobody could find. It was an audit column too
    /// narrow for the entity kind - a thing the server knew exactly and declined to say.
    /// </remarks>
    private static string Describe(EditOutcome outcome) => outcome.Status switch
    {
        EditStatus.NotSaved =>
            "Applied to the world but could not be persisted; it was rolled back. Check the server log.",
        _ => outcome.Result.Message ?? "Refused.",
    };

    /// <summary>Which of these room keys this environment actually has.</summary>
    private static async Task<HashSet<string>> RoomsThatExistAsync(
        MuwbtaDbContext db,
        IEnumerable<string> keys,
        CancellationToken ct)
    {
        var parsed = keys
            .Select(k => RoomKey.TryParse(k, out var room) ? room : (RoomKey?)null)
            .OfType<RoomKey>()
            .Distinct()
            .ToList();

        if (parsed.Count == 0)
        {
            return [];
        }

        var found = await db.Rooms.AsNoTracking()
            .Where(r => parsed.Contains(r.Key))
            .Select(r => r.Key)
            .ToListAsync(ct);

        return [.. found.Select(k => k.ToString())];
    }

    private static async Task<IResult> ListWorldsAsync(BuilderQueries queries, CancellationToken ct) =>
        Results.Ok(await queries.WorldsAsync(ct));

    private static async Task<IResult> GetWorldAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.WorldAsync(key, ct) is { } world ? Results.Ok(world) : Results.NotFound();

    private static async Task<IResult> CreateWorldAsync(
        string key,
        SaveWorldRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("A world key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.WorldAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"World '{key}' already exists." });
        }

        var change = new UpsertWorld(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.SortOrder ?? 0,
            ToFlagSet(request.Flags),
            request.Multipliers ?? new Multipliers());

        return await SaveAsync(editor, change, http, ct, () => queries.WorldAsync(key, ct));
    }

    private static async Task<IResult> UpdateWorldAsync(
        string key,
        SaveWorldRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.WorldAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var change = new UpsertWorld(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.SortOrder ?? existing.SortOrder,
            // A null Flags means "leave them alone"; an empty object means "clear them". The
            // distinction matters because PATCH bodies are routinely partial.
            request.Flags is null ? ToFlagSet(existing.Flags) : ToFlagSet(request.Flags),
            request.Multipliers ?? existing.Multipliers);

        return await SaveAsync(editor, change, http, ct, () => queries.WorldAsync(key, ct));
    }

    /// <summary>
    /// Sets one flag on one world, leaving its siblings alone.
    /// </summary>
    /// <remarks>
    /// The room editor has had this since §4.10; the scopes above it were still doing a
    /// read-modify-PATCH of the whole flag map, which loses a concurrent builder's edit to a
    /// different flag - and loses it silently, since the second write is a perfectly valid map.
    /// The blast radius is largest here, so the narrow primitive matters most here.
    ///
    /// A null value clears the key rather than setting it false, which is the third state the
    /// builder's control offers: let the level above decide.
    /// </remarks>
    private static async Task<IResult> SetWorldFlagAsync(
        string key,
        string flag,
        SetFlagRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(
            editor,
            new SetWorldFlag(key, flag, request.Value),
            http,
            ct,
            () => queries.WorldAsync(key, ct));

    private static async Task<IResult> DeleteWorldAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteWorld(key), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Zones
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListZonesAsync(
        string? world,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.ZonesAsync(world, ct));

    private static async Task<IResult> GetZoneAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.ZoneAsync(key, ct) is { } zone ? Results.Ok(zone) : Results.NotFound();

    private static async Task<IResult> CreateZoneAsync(
        string key,
        SaveZoneRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        var parts = key.Split('.');
        if (parts.Length != 2 || !parts.All(IsKeySegment))
        {
            return Invalid("A zone key must be 'world.zone'.");
        }

        if (await queries.ZoneAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Zone '{key}' already exists." });
        }

        var worldKey = Trim(request.WorldKey) ?? parts[0];

        var change = new UpsertZone(
            key,
            worldKey,
            Trim(request.Name) ?? parts[1],
            request.Description ?? string.Empty,
            request.MinLevel ?? 1,
            request.MaxLevel ?? 50,
            ToFlagSet(request.Flags),
            request.Multipliers ?? new Multipliers());

        return await SaveAsync(editor, change, http, ct, () => queries.ZoneAsync(key, ct));
    }

    private static async Task<IResult> UpdateZoneAsync(
        string key,
        SaveZoneRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.ZoneAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var change = new UpsertZone(
            key,
            existing.WorldKey,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.MinLevel ?? existing.MinLevel,
            request.MaxLevel ?? existing.MaxLevel,
            request.Flags is null ? ToFlagSet(existing.Flags) : ToFlagSet(request.Flags),
            request.Multipliers ?? existing.Multipliers);

        return await SaveAsync(editor, change, http, ct, () => queries.ZoneAsync(key, ct));
    }

    /// <inheritdoc cref="SetWorldFlagAsync"/>
    private static async Task<IResult> SetZoneFlagAsync(
        string key,
        string flag,
        SetFlagRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(
            editor,
            new SetZoneFlag(key, flag, request.Value),
            http,
            ct,
            () => queries.ZoneAsync(key, ct));

    private static async Task<IResult> DeleteZoneAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteZone(key), http, ct, () => Task.FromResult<object?>(null));

    private static async Task<IResult> ListRoomsAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.RoomsAsync(key, ct));

    private static async Task<IResult> ValidateZoneAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.ValidateAsync(key, ct));

    private static async Task<IResult> UnfinishedAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.UnfinishedAsync(key, ct));

    private static async Task<IResult> PreviewAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.PreviewAsync(key, ct) is { } preview
            ? Results.Ok(preview)
            : Results.NotFound();

    /// <summary>
    /// Despawns and refills this zone's mob spawners, so a multiplier edit is visible in the
    /// world at once rather than at the next respawn (PLAN.md §7.5).
    /// </summary>
    /// <remarks>
    /// Not routed through <see cref="SaveAsync{T}"/>, which every other write here uses, because
    /// there is nothing to save: no primitive comes back, so no row is written, no audit entry is
    /// made, and there is nothing to reload and hand to the caller. What did happen is a pair of
    /// counts, and they come off the mutation result rather than out of a second read - only the
    /// loop can see live mobs, and asking it twice would be asking about a world that had moved on.
    /// </remarks>
    private static async Task<IResult> RespawnZoneAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        http.TryGetAccountId(out var accountId);

        var outcome = await editor.ApplyAsync(new RespawnZone(key), accountId, ct);

        if (outcome.Status != EditStatus.Saved)
        {
            return Refused(outcome.Result);
        }

        var tally = outcome.Result.Respawned ?? new RespawnTally(0, 0);
        return Results.Ok(new RespawnZoneResponse(key, tally.Despawned, tally.Spawned));
    }

    private static async Task<IResult> AuditAsync(
        string? kind,
        string? key,
        int? limit,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.AuditAsync(kind, key, limit ?? 50, ct));

    // -----------------------------------------------------------------------
    // Export and import (PLAN.md §6, Phase 6)
    // -----------------------------------------------------------------------

    /// <summary>
    /// The authored world as one JSON document: everything, or one world, or one zone.
    /// </summary>
    /// <remarks>
    /// Sent as an attachment with a dated filename, because the thing a builder does with this is
    /// save it - and a bundle that arrives as an untitled browser tab is one nobody keeps.
    /// </remarks>
    private static async Task<IResult> ExportAsync(
        string? world,
        string? zone,
        string? only,
        string? configuration,
        WorldExporter exporter,
        HttpContext http,
        CancellationToken ct)
    {
        // Abilities on their own, which is the return leg of tuning one: they are content, they
        // live in content/abilities.json, and a retune made in the editor has to be able to get
        // back to the file. Wins over world and zone rather than combining with them - an ability
        // belongs to a Path and not to a place, so there is nothing for a zone to narrow.
        if (string.Equals(only, WorldExporter.AbilitiesScope, StringComparison.OrdinalIgnoreCase))
        {
            return Download(http, await exporter.ExportAbilitiesAsync(ct), WorldExporter.AbilitiesScope);
        }

        if (!string.IsNullOrWhiteSpace(only))
        {
            return Results.BadRequest(new
            {
                error = $"'{only}' is not something this can export on its own. Try 'abilities'.",
            });
        }

        // A whole configuration: its worlds, and itself. Wins over world and zone because it is
        // the larger claim, and a request naming both has said which it means.
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            return await exporter.ExportConfigurationAsync(configuration, ct) is { } owned
                ? Download(http, owned, configuration)
                : Results.NotFound(new { error = $"No configuration '{configuration}'." });
        }

        if (await exporter.ExportAsync(world, zone, ct) is not { } bundle)
        {
            return Results.NotFound(new
            {
                error = string.IsNullOrWhiteSpace(zone)
                    ? $"No world '{world}'."
                    : $"No zone '{zone}'.",
            });
        }

        return Download(http, bundle, bundle.Scope.Key ?? "world");
    }

    /// <summary>
    /// A bundle as a named attachment, because the thing a builder does with one is save it.
    /// </summary>
    private static IResult Download(HttpContext http, WorldBundle bundle, string name)
    {
        http.Response.Headers.ContentDisposition =
            $"attachment; filename=\"{name}-{bundle.ExportedAt:yyyy-MM-dd}.json\"";

        return Results.Ok(bundle);
    }

    /// <summary>
    /// Applies a bundle to this environment. <c>?dryRun=true</c> reports what would happen and
    /// changes nothing.
    /// </summary>
    /// <remarks>
    /// The format version is the one hard refusal here. Everything else - a dangling exit, a
    /// quest whose giver is somewhere else - comes back as an advisory warning, because those are
    /// states the world already tolerates (§7.4) and refusing them would make importing one zone
    /// of several impossible.
    /// </remarks>
    private static async Task<IResult> ImportAsync(
        WorldBundle? bundle,
        bool? dryRun,
        WorldImporter importer,
        MapSheets sheets,
        MuwbtaDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        if (bundle is null)
        {
            return Invalid("An import needs a bundle.");
        }

        if (!BundleFormat.IsCurrent(bundle))
        {
            // Worded in one place, so a builder who trips this at the command line and again at
            // the endpoint reads the same sentence rather than two accounts of the same refusal.
            return Invalid(BundleFormat.VersionRefusal(bundle.FormatVersion));
        }

        http.TryGetAccountId(out var accountId);

        var report = await importer.ImportAsync(bundle, accountId, dryRun ?? false, ct);

        // Re-read the sheets whenever an import could have written one. Unlike the canon - which
        // is deliberately not re-warmed here, because an import says what a configuration means
        // rather than which one is live - a map has no equivalent of activation. It is served the
        // moment its rooms are, or it is a map of the world before this import.
        if (!(dryRun ?? false) && bundle.Maps.Count > 0)
        {
            var loaded = await db.WorldMaps.AsNoTracking()
                .Select(m => new { m.WorldKey, m.Svg })
                .ToListAsync(ct);

            sheets.Load(loaded.Select(m => (m.WorldKey, m.Svg)));
        }

        // A partial import is not a success, and reporting one as 200 is how a half-applied zone
        // gets noticed a week later. The report is the body either way, since which entities
        // failed is the whole answer.
        return report.Ok
            ? Results.Ok(report)
            : Results.Json(report, statusCode: StatusCodes.Status207MultiStatus);
    }

    // -----------------------------------------------------------------------
    // Mob Templates
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListMobTemplatesAsync(
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.MobTemplatesAsync(ct));

    private static async Task<IResult> GetMobTemplateAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.MobTemplateAsync(key, ct) is { } template ? Results.Ok(template) : Results.NotFound();

    /// <summary>
    /// Where this template actually exists in the world (PLAN.md §7.9).
    /// </summary>
    /// <remarks>
    /// Two routes rather than one with a kind parameter, because a mob key and an item key are
    /// different namespaces that routinely collide - <c>torch</c> is a plausible member of both -
    /// and a single route would need the caller to say which, which is what the path already says.
    /// </remarks>
    private static async Task<IResult> MobPlacementAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.PlacementAsync(TemplateKind.Mob, key, ct) is { } placement
            ? Results.Ok(placement)
            : Results.NotFound();

    /// <inheritdoc cref="MobPlacementAsync"/>
    private static async Task<IResult> ItemPlacementAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.PlacementAsync(TemplateKind.Item, key, ct) is { } placement
            ? Results.Ok(placement)
            : Results.NotFound();

    private static async Task<IResult> CreateMobTemplateAsync(
        string key,
        SaveMobTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("A mob template key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.MobTemplateAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Mob template '{key}' already exists." });
        }

        if (ValidateAttacks(request.Attacks) is { } refusal)
        {
            return refusal;
        }

        var change = new UpsertMobTemplate(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.Icon ?? "m",
            request.Level ?? 1,
            request.WanderIntervalPulses ?? 24,
            request.BaseStats ?? new Dictionary<string, object>(),
            request.BaseXp ?? 0,
            request.BaseGold ?? 0,
            request.Behavior ?? new Dictionary<string, object>(),
            request.Loot ?? new List<Dictionary<string, object>>(),
            NormalizeAttacks(request.Attacks) ?? new List<MobAttack>());

        return await SaveAsync(editor, change, http, ct, () => queries.MobTemplateAsync(key, ct));
    }

    private static async Task<IResult> UpdateMobTemplateAsync(
        string key,
        SaveMobTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.MobTemplateAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        if (ValidateAttacks(request.Attacks) is { } refusal)
        {
            return refusal;
        }

        var change = new UpsertMobTemplate(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.Icon ?? existing.Icon,
            request.Level ?? existing.Level,
            request.WanderIntervalPulses ?? existing.WanderIntervalPulses,
            request.BaseStats ?? existing.BaseStats,
            request.BaseXp ?? existing.BaseXp,
            request.BaseGold ?? existing.BaseGold,
            request.Behavior ?? existing.Behavior,
            request.Loot ?? existing.Loot,
            NormalizeAttacks(request.Attacks) ?? existing.Attacks);

        return await SaveAsync(editor, change, http, ct, () => queries.MobTemplateAsync(key, ct));
    }

    private static async Task<IResult> DeleteMobTemplateAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteMobTemplate(key), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Abilities
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListAbilitiesAsync(
        BuilderQueries queries,
        EffectRegistry effects,
        CancellationToken ct) =>
        Results.Ok(await queries.AbilitiesAsync(effects, ct));

    private static async Task<IResult> GetAbilityAsync(
        string key,
        BuilderQueries queries,
        EffectRegistry effects,
        CancellationToken ct) =>
        await queries.AbilityAsync(key, effects, ct) is { } ability
            ? Results.Ok(ability)
            : Results.NotFound();

    /// <summary>
    /// Refuses anything <see cref="AbilityValidator"/> calls an error, and reports the rest.
    /// </summary>
    /// <remarks>
    /// The one place in the builder API that refuses on content grounds rather than on shape.
    /// Everything else here follows §7.4 and lets the world be temporarily broken, because a
    /// dangling exit is visible the moment somebody walks into it. A broken ability is not: it
    /// costs its resource, starts its cooldown, and does nothing, so the mistake surfaces as a
    /// player thinking a spell is weak. The same argument already made mob attack effect keys a
    /// refusal rather than a warning.
    /// </remarks>
    private static IResult? RefuseInvalid(Domain.Abilities.Ability candidate, EffectRegistry effects)
    {
        var errors = AbilityValidator.ValidateOne(candidate, effects)
            .Where(p => p.Severity == AbilityProblemSeverity.Error)
            .Select(p => p.Message)
            .ToList();

        return errors.Count == 0 ? null : Invalid(string.Join(" ", errors));
    }

    private static async Task<IResult> CreateAbilityAsync(
        string key,
        SaveAbilityRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        EffectRegistry effects,
        HttpContext http,
        CancellationToken ct)
    {
        // Dotted, unlike a template key: an ability key is "<path>.<name>" and the validator
        // enforces that the prefix matches the Path that learns it.
        if (!IsAbilityKey(key))
        {
            return Invalid("An ability key looks like 'warden.shield-bash'.");
        }

        if (await queries.AbilityAsync(key, effects, ct) is not null)
        {
            return Results.Conflict(new { error = $"Ability '{key}' already exists." });
        }

        if (request.Path is not { } path)
        {
            return Invalid("An ability needs a Path.");
        }

        if (request.Effects is not { Count: > 0 } effectList)
        {
            return Invalid("An ability needs at least one effect.");
        }

        var candidate = new Domain.Abilities.Ability
        {
            Key = key,
            Path = path,
            UnlockLevel = request.UnlockLevel ?? 1,
            Name = Trim(request.Name) ?? key,
            Description = request.Description ?? string.Empty,
            CostType = request.CostType ?? Domain.Abilities.CostType.Stamina,
            CostValue = request.CostValue ?? 10,
            CooldownPulses = request.CooldownPulses ?? 24,
            CooldownGroup = request.CooldownGroup,
            CastTimePulses = request.CastTimePulses,
            TargetingType = request.TargetingType ?? Domain.Abilities.TargetingType.SingleTarget,
            Effects = effectList,
        };

        if (RefuseInvalid(candidate, effects) is { } refusal)
        {
            return refusal;
        }

        return await SaveAsync(editor, ChangeFor(candidate), http, ct,
            () => queries.AbilityAsync(key, effects, ct));
    }

    private static async Task<IResult> UpdateAbilityAsync(
        string key,
        SaveAbilityRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        EffectRegistry effects,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.AbilityAsync(key, effects, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var candidate = new Domain.Abilities.Ability
        {
            Key = key,
            Path = request.Path ?? existing.Path,
            UnlockLevel = request.UnlockLevel ?? existing.UnlockLevel,
            Name = Trim(request.Name) ?? existing.Name,
            Description = request.Description ?? existing.Description,
            CostType = request.CostType ?? existing.CostType,
            CostValue = request.CostValue ?? existing.CostValue,
            CooldownPulses = request.CooldownPulses ?? existing.CooldownPulses,

            // Uncoalesced for the same reason the cast time below is: null *means* "shares no
            // timer", so `?? existing` would leave a builder no way to take an ability off one.
            CooldownGroup = request.CooldownGroup,

            // Deliberately not coalesced against what is stored. A null cast time *means*
            // instant, so `?? existing` would make an ability that is being made instant keep
            // its old cast bar, with no way to clear one from the editor at all.
            CastTimePulses = request.CastTimePulses,

            TargetingType = request.TargetingType ?? existing.TargetingType,
            Effects = request.Effects ?? [.. existing.Effects],
        };

        if (RefuseInvalid(candidate, effects) is { } refusal)
        {
            return refusal;
        }

        return await SaveAsync(editor, ChangeFor(candidate), http, ct,
            () => queries.AbilityAsync(key, effects, ct));
    }

    private static async Task<IResult> DeleteAbilityAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteAbility(key), http, ct, () => Task.FromResult<object?>(null));

    private static UpsertAbility ChangeFor(Domain.Abilities.Ability a) =>
        new(a.Key, a.Path, a.UnlockLevel, a.Name, a.Description, a.CostType, a.CostValue,
            a.CooldownPulses, a.CooldownGroup, a.CastTimePulses, a.TargetingType,
            [.. a.Effects.Select(e =>
                new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))]);

    /// <summary>
    /// An ability key is two key-segments joined by a dot: <c>warden.shield-bash</c>.
    /// </summary>
    private static bool IsAbilityKey(string key)
    {
        var parts = key.Split('.');
        return parts.Length == 2 && parts.All(IsKeySegment);
    }

    // -----------------------------------------------------------------------
    // Item Templates
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListItemTemplatesAsync(
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.ItemTemplatesAsync(ct));

    private static async Task<IResult> GetItemTemplateAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.ItemTemplateAsync(key, ct) is { } template ? Results.Ok(template) : Results.NotFound();

    private static async Task<IResult> CreateItemTemplateAsync(
        string key,
        SaveItemTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("An item template key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.ItemTemplateAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Item template '{key}' already exists." });
        }

        if (ValidateWeapon(request.AttackDelayPulses, request.AttackVerb) is { } refusal)
        {
            return refusal;
        }

        if (ValidateSlots(request.Slots, request.IsTwoHanded) is { } badSlots)
        {
            return badSlots;
        }

        var change = new UpsertItemTemplate(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.Icon ?? "$",
            [.. SlotRules.Normalize(request.Slots)],
            request.IsTwoHanded ?? false,
            request.Weight ?? 1,
            request.BaseValue ?? 0,
            request.BaseStats ?? new Dictionary<string, object>(),
            request.AttackDelayPulses,
            Trim(request.AttackVerb),
            request.IsQuestItem ?? false,
            request.IsLore ?? false,
            request.IsNoDrop ?? false,
            request.IsLightSource ?? false,
            request.FoodValue,
            request.DrinkValue,
            request.Paths ?? []);

        return await SaveAsync(editor, change, http, ct, () => queries.ItemTemplateAsync(key, ct));
    }

    private static async Task<IResult> UpdateItemTemplateAsync(
        string key,
        SaveItemTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.ItemTemplateAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        if (ValidateWeapon(request.AttackDelayPulses, request.AttackVerb) is { } refusal)
        {
            return refusal;
        }

        // The merged values, not the request's. A PATCH that sends only `twoHanded: true` against
        // a row whose slots are [Chest] is incoherent even though neither half arrived wrong.
        if (ValidateSlots(
                request.Slots ?? existing.Slots,
                request.IsTwoHanded ?? existing.IsTwoHanded) is { } badSlots)
        {
            return badSlots;
        }

        var change = new UpsertItemTemplate(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.Icon ?? existing.Icon,
            [.. SlotRules.Normalize(request.Slots ?? existing.Slots)],
            request.IsTwoHanded ?? existing.IsTwoHanded,
            request.Weight ?? existing.Weight,
            request.BaseValue ?? existing.BaseValue,
            request.BaseStats ?? existing.BaseStats,
            request.AttackDelayPulses ?? existing.AttackDelayPulses,
            Trim(request.AttackVerb) ?? existing.AttackVerb,
            request.IsQuestItem ?? existing.IsQuestItem,
            request.IsLore ?? existing.IsLore,
            request.IsNoDrop ?? existing.IsNoDrop,
            request.IsLightSource ?? existing.IsLightSource,
            request.FoodValue ?? existing.FoodValue,
            request.DrinkValue ?? existing.DrinkValue,
            request.Paths ?? [.. existing.Paths]);

        return await SaveAsync(editor, change, http, ct, () => queries.ItemTemplateAsync(key, ct));
    }

    private static async Task<IResult> DeleteItemTemplateAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteItemTemplate(key), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Spawners
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListSpawnersAsync(
        string? zone,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.SpawnersAsync(zone, ct));

    private static async Task<IResult> GetSpawnerAsync(
        Guid id,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.SpawnerAsync(id, ct) is { } spawner ? Results.Ok(spawner) : Results.NotFound();

    private static async Task<IResult> CreateSpawnerAsync(
        SaveSpawnerRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ZoneKey))
        {
            return Invalid("Zone key is required.");
        }

        if (await queries.ZoneAsync(request.ZoneKey, ct) is null)
        {
            return Invalid($"No zone '{request.ZoneKey}'.");
        }

        if (string.IsNullOrWhiteSpace(request.TemplateKey))
        {
            return Invalid("Template key is required.");
        }

        // Absent means "follow the template", which is the default a fresh spawner should have.
        if (!WanderMode.TryParse(request.Wander ?? WanderMode.Template, out var wanders))
        {
            return Invalid($"Unknown wander mode '{request.Wander}'.");
        }

        // Absent means "let the zone decide", likewise.
        if (!SpawnLevel.TryParse(request.Level ?? SpawnLevel.Zone, out var fightsAt))
        {
            return Invalid(BadLevel(request.Level));
        }

        var kind = request.TemplateKind ?? TemplateKind.Mob;
        if (RefuseLevelOnItem(kind, fightsAt) is { } refusal)
        {
            return refusal;
        }

        var (modifier, refusedModifier) = ResolveModifier(request.NameModifier, existing: null, kind);
        if (refusedModifier is not null)
        {
            return refusedModifier;
        }

        if (await RefuseUnmodifiableAsync(queries, request.TemplateKey, modifier, ct) is { } named)
        {
            return named;
        }

        var id = Guid.CreateVersion7();
        var change = new UpsertSpawner(
            id,
            request.ZoneKey,
            request.TemplateKey,
            kind,
            request.RoomKeys ?? new List<string>(),
            request.TargetCount ?? 1,
            request.RespawnSeconds ?? 60,
            wanders,
            fightsAt,
            modifier);

        return await SaveAsync(editor, change, http, ct, () => queries.SpawnerAsync(id, ct));
    }

    private static async Task<IResult> UpdateSpawnerAsync(
        Guid id,
        SaveSpawnerRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.SpawnerAsync(id, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var zoneKey = request.ZoneKey ?? existing.ZoneKey;
        if (await queries.ZoneAsync(zoneKey, ct) is null)
        {
            return Invalid($"No zone '{zoneKey}'.");
        }

        // An absent Wander leaves the stored answer alone, exactly as an absent TargetCount does.
        // This is why the wire carries a word: the stored value is itself nullable, so a nullable
        // bool here could not tell "leave it" from "follow the template" (see WanderMode).
        if (!WanderMode.TryParse(request.Wander ?? existing.Wander, out var wanders))
        {
            return Invalid($"Unknown wander mode '{request.Wander}'.");
        }

        if (!SpawnLevel.TryParse(request.Level ?? existing.Level, out var fightsAt))
        {
            return Invalid(BadLevel(request.Level));
        }

        // Checked against the *resulting* kind, not the stored one: flipping a mob spawner to Item
        // while it carries a pin would otherwise leave a value that means nothing, and comes back
        // to life the day somebody flips it back.
        var kind = request.TemplateKind ?? existing.TemplateKind;
        if (RefuseLevelOnItem(kind, fightsAt) is { } refusal)
        {
            return refusal;
        }

        // Same PATCH rule as the level, and checked against the resulting kind for the same
        // reason: a modifier left on a spawner flipped to Item would come back to life with it.
        var (modifier, refusedModifier) = ResolveModifier(request.NameModifier, existing.NameModifier, kind);
        if (refusedModifier is not null)
        {
            return refusedModifier;
        }

        var templateKey = request.TemplateKey ?? existing.TemplateKey;
        if (await RefuseUnmodifiableAsync(queries, templateKey, modifier, ct) is { } named)
        {
            return named;
        }

        var change = new UpsertSpawner(
            id,
            zoneKey,
            templateKey,
            kind,
            request.RoomKeys ?? existing.RoomKeys,
            request.TargetCount ?? existing.TargetCount,
            request.RespawnSeconds ?? existing.RespawnSeconds,
            wanders,
            fightsAt,
            modifier);

        return await SaveAsync(editor, change, http, ct, () => queries.SpawnerAsync(id, ct));
    }

    /// <summary>
    /// Why a pinned level was not accepted. Named rather than inlined because both handlers refuse
    /// it identically, and because the message has to teach the two ways it can be wrong.
    /// </summary>
    private static string BadLevel(string? level) =>
        $"'{level}' is not a level. Use a whole number of 1 or more, or '{SpawnLevel.Zone}' to let "
        + "the zone decide.";

    /// <summary>
    /// An item has no level, so an item spawner cannot pin one.
    /// </summary>
    /// <remarks>
    /// The spawn path already ignores the field for items, so this is not about the runtime. It is
    /// about a stored value that means nothing today and becomes live the moment somebody changes
    /// the kind to Mob — at which point the mob fights at a level nobody remembers choosing.
    ///
    /// A hard refusal rather than a §7.4 advisory: those cover content that is *incomplete*, and
    /// this is a request that is malformed, alongside the endpoint's other refusals for a missing
    /// zone key or an unknown wander mode.
    /// </remarks>
    private static IResult? RefuseLevelOnItem(TemplateKind kind, int? fightsAt) =>
        kind == TemplateKind.Item && fightsAt is not null
            ? Invalid("An item has no level, so an item spawner cannot pin one.")
            : null;

    /// <summary>
    /// The name modifier a save results in, or why it was refused (PLAN.md §4.8).
    /// </summary>
    /// <remarks>
    /// Null on the wire leaves the stored word alone; an empty string clears it; anything else is
    /// trimmed and judged by <see cref="Muwbta.Domain.Inhabitants.MobNaming.Problem"/>. An item
    /// spawner cannot carry one at all, for the reason <see cref="RefuseLevelOnItem"/> gives.
    /// </remarks>
    private static (string? Modifier, IResult? Refusal) ResolveModifier(
        string? requested, string? existing, TemplateKind kind)
    {
        var modifier = requested is null
            ? existing
            : string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();

        if (modifier is null)
        {
            return (null, null);
        }

        if (kind == TemplateKind.Item)
        {
            return (null, Invalid("An item keeps its own name, so an item spawner cannot carry a name modifier."));
        }

        if (Muwbta.Domain.Inhabitants.MobNaming.Problem(modifier) is { } problem)
        {
            return (null, Invalid($"The name modifier '{modifier}' {problem}."));
        }

        return (modifier, null);
    }

    /// <summary>
    /// Refuses a modifier on a template whose name is a person's. A template the builder has not
    /// written yet is allowed through — the spawner goes dormant until it exists (§7.4), and the
    /// bundle validator repeats this check with both halves in hand.
    /// </summary>
    private static async Task<IResult?> RefuseUnmodifiableAsync(
        BuilderQueries queries, string templateKey, string? modifier, CancellationToken ct)
    {
        if (modifier is null || await queries.MobTemplateAsync(templateKey, ct) is not { } template)
        {
            return null;
        }

        return Muwbta.Domain.Inhabitants.MobNaming.CanModify(template.Name)
            ? null
            : Invalid($"'{template.Name}' is a named character and cannot take a name modifier.");
    }

    private static async Task<IResult> DeleteSpawnerAsync(
        Guid id,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteSpawner(id), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Quests (Phase 5.2b)
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListQuestsAsync(
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.QuestsAsync(ct));

    private static async Task<IResult> GetQuestAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.QuestAsync(key, ct) is { } quest ? Results.Ok(quest) : Results.NotFound();

    private static async Task<IResult> CreateQuestAsync(
        string key,
        SaveQuestRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("A quest key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.QuestAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Quest '{key}' already exists." });
        }

        if (string.IsNullOrWhiteSpace(request.ZoneKey))
        {
            return Invalid("Quest zone is required.");
        }

        if (string.IsNullOrWhiteSpace(request.GiverMobKey))
        {
            return Invalid("Quest giver mob is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TurninMobKey))
        {
            return Invalid("Quest turnin mob is required.");
        }

        var change = new UpsertQuest(
            key,
            request.ZoneKey,
            Trim(request.Name) ?? key,
            request.Summary ?? string.Empty,
            request.Description ?? string.Empty,
            request.GiverMobKey,
            request.TurninMobKey,
            request.RequiredItemKey,
            request.RequiredCount ?? 1,
            request.RewardXp ?? 0,
            request.RewardGold ?? 0,
            request.RewardItemKey,
            request.RewardItemCount ?? 1,
            request.RewardFlagKey,
            request.PrerequisiteQuestKeys ?? [],
            request.IsRepeatable ?? false,
            request.AutoStart ?? false,
            request.Paths ?? [],
            request.Dialogue ?? [],
            request.SortOrder ?? 0);

        return await SaveAsync(editor, change, http, ct, () => queries.QuestAsync(key, ct));
    }

    private static async Task<IResult> UpdateQuestAsync(
        string key,
        SaveQuestRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.QuestAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var change = new UpsertQuest(
            key,
            request.ZoneKey ?? existing.ZoneKey,
            Trim(request.Name) ?? existing.Name,
            request.Summary ?? existing.Summary,
            request.Description ?? existing.Description,
            request.GiverMobKey ?? existing.GiverMobKey,
            request.TurninMobKey ?? existing.TurninMobKey,
            request.RequiredItemKey ?? existing.RequiredItemKey,
            request.RequiredCount ?? existing.RequiredCount,
            request.RewardXp ?? existing.RewardXp,
            request.RewardGold ?? existing.RewardGold,
            request.RewardItemKey ?? existing.RewardItemKey,
            request.RewardItemCount ?? existing.RewardItemCount,
            request.RewardFlagKey ?? existing.RewardFlagKey,
            request.PrerequisiteQuestKeys ?? existing.PrerequisiteQuestKeys,
            request.IsRepeatable ?? existing.IsRepeatable,
            request.AutoStart ?? existing.AutoStart,
            request.Paths ?? [.. existing.Paths],
            request.Dialogue ?? existing.Dialogue,
            request.SortOrder ?? existing.SortOrder);

        return await SaveAsync(editor, change, http, ct, () => queries.QuestAsync(key, ct));
    }

    private static async Task<IResult> DeleteQuestAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteQuest(key), http, ct, () => Task.FromResult<object?>(null));

    /// <summary>
    /// Returns reachability status for quest items: required and reward items.
    /// An item is reachable if it has at least one source (mob drop or spawner).
    /// </summary>
    private static async Task<IResult> QuestReachabilityAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct)
    {
        var quest = await queries.QuestAsync(key, ct);
        if (quest is null)
        {
            return Results.NotFound();
        }

        var mobs = await queries.MobTemplatesAsync(ct);
        var spawners = await queries.SpawnersAsync(null, ct);
        var quests = await queries.QuestsAsync(ct);
        var warnings = new List<ReachabilityWarning>();

        var spawnedMobs = spawners
            .Where(s => s.TemplateKind == TemplateKind.Mob)
            .Select(s => s.TemplateKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A quest nobody can start or hand in is as broken as one whose item does not drop, and
        // fails just as quietly - the NPC simply never appears.
        CheckMob(quest.GiverMobKey, "giver", "offer this quest");
        CheckMob(quest.TurninMobKey, "turnin", "accept the turn-in");

        if (!string.IsNullOrEmpty(quest.RequiredItemKey))
        {
            await CheckItemAsync(quest.RequiredItemKey, "required");
        }

        if (!string.IsNullOrEmpty(quest.RewardItemKey))
        {
            await CheckItemAsync(quest.RewardItemKey, "reward");
        }

        return Results.Ok(new QuestReachability(key, warnings));

        void CheckMob(string mobKey, string role, string action)
        {
            if (string.IsNullOrEmpty(mobKey))
            {
                return;
            }

            if (!mobs.Any(m => string.Equals(m.Key, mobKey, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new ReachabilityWarning(
                    $"missing-{role}-mob",
                    $"No mob template '{mobKey}', so nothing can {action}.",
                    MobKey: mobKey));
                return;
            }

            if (!spawnedMobs.Contains(mobKey))
            {
                warnings.Add(new ReachabilityWarning(
                    $"unspawned-{role}-mob",
                    $"'{mobKey}' exists but no spawner places it, so it will never {action}.",
                    MobKey: mobKey));
            }
        }

        // §10: prove the item has at least one source rather than assuming it. An unobtainable
        // quest item is the classic silent quest bug - the editor reads fine and the player just
        // wanders - so this walks loot tables, spawners, and other quests' rewards.
        async Task CheckItemAsync(string itemKey, string role)
        {
            if (await queries.ItemTemplateAsync(itemKey, ct) is null)
            {
                warnings.Add(new ReachabilityWarning(
                    $"missing-{role}-item",
                    $"No item template '{itemKey}'.",
                    ItemKey: itemKey));
                return;
            }

            // A reward is granted directly by the turn-in, so it needs no world source.
            if (role == "reward")
            {
                return;
            }

            var droppers = mobs
                .Where(m => DropsItem(m, itemKey))
                .Select(m => m.Key)
                .ToList();

            var spawnsIt = spawners.Any(s =>
                s.TemplateKind == TemplateKind.Item
                && string.Equals(s.TemplateKey, itemKey, StringComparison.OrdinalIgnoreCase));

            var rewardedBy = quests.Any(q =>
                !string.Equals(q.Key, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(q.RewardItemKey, itemKey, StringComparison.OrdinalIgnoreCase));

            if (droppers.Count == 0 && !spawnsIt && !rewardedBy)
            {
                warnings.Add(new ReachabilityWarning(
                    $"unreachable-{role}-item",
                    $"Nothing drops, spawns, or rewards '{itemKey}', so the quest cannot be finished.",
                    ItemKey: itemKey));
                return;
            }

            // Loot on a mob no spawner places is loot nobody can reach.
            if (!spawnsIt && !rewardedBy && droppers.All(m => !spawnedMobs.Contains(m)))
            {
                warnings.Add(new ReachabilityWarning(
                    $"unspawned-{role}-item-source",
                    $"'{itemKey}' only drops from {string.Join(", ", droppers)}, which no spawner places.",
                    ItemKey: itemKey));
            }
        }
    }

    /// <summary>
    /// Whether a mob's loot table lists an item with a non-zero chance. Mirrors how CombatSystem
    /// reads the same jsonb: an "itemTemplateKey" and a "chance" per entry.
    /// </summary>
    /// <summary>
    /// Whether this mob's table is a source for the item, including the rule that a zero chance is
    /// an entry which can never fire. <see cref="LootTable"/> holds the reading, because the
    /// placement panel asks the same question of the same bag and two readers of one jsonb shape
    /// is how they come to disagree.
    /// </summary>
    private static bool DropsItem(MobTemplateResponse mob, string itemKey) =>
        LootTable.Drops(mob.Loot, itemKey);

    /// <summary>
    /// The quest graph for a zone: nodes are quests, edges are prerequisites, plus the two ways
    /// a chain can be broken - a cycle (§7.4: every quest in it is unstartable) and a quest whose
    /// prerequisites can never all be met.
    /// </summary>
    /// <remarks>
    /// Resolution spans every zone even though only this zone's quests are drawn. Prerequisites
    /// are plain keys and chains legitimately cross zones, so scoping the lookup to one zone
    /// dropped those edges and then reported the dependent quest as unreachable - a warning about
    /// a chain that was in fact fine.
    /// </remarks>
    private static async Task<IResult> StorylineGraphAsync(
        string zoneKey,
        BuilderQueries queries,
        CancellationToken ct)
    {
        var all = (await queries.QuestsAsync(ct)).ToDictionary(q => q.Key, StringComparer.Ordinal);
        var inZone = all.Values
            .Where(q => string.Equals(q.ZoneKey, zoneKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(q => q.SortOrder)
            .ToList();

        // Prerequisites outside the zone are drawn too, marked external, so a cross-zone chain
        // is visible rather than looking like a quest with no way in.
        var shown = new HashSet<string>(inZone.Select(q => q.Key), StringComparer.Ordinal);
        foreach (var quest in inZone)
        {
            foreach (var prereq in quest.PrerequisiteQuestKeys.Where(all.ContainsKey))
            {
                shown.Add(prereq);
            }
        }

        var nodes = shown
            .Select(k => all[k])
            .Select(q => new
            {
                key = q.Key,
                name = q.Name,
                zoneKey = q.ZoneKey,
                external = !string.Equals(q.ZoneKey, zoneKey, StringComparison.OrdinalIgnoreCase),
            })
            .ToList();

        var edges = inZone
            .SelectMany(q => q.PrerequisiteQuestKeys
                .Where(all.ContainsKey)
                .Select(p => new { from = p, to = q.Key }))
            .ToList();

        // A prerequisite naming a quest that does not exist can never be satisfied, which is a
        // different failure from a cycle and worth saying so.
        var missingPrerequisites = inZone
            .SelectMany(q => q.PrerequisiteQuestKeys
                .Where(p => !all.ContainsKey(p))
                .Select(p => new { quest = q.Key, missing = p }))
            .ToList();

        var onCycle = FindCycleMembers(all);

        // Reachable = every prerequisite is itself reachable. Runs over the whole graph so an
        // external prerequisite resolves properly, then reports only this zone's quests.
        var reachable = new HashSet<string>(
            all.Values.Where(q => q.PrerequisiteQuestKeys.Count == 0).Select(q => q.Key),
            StringComparer.Ordinal);

        bool changed;
        do
        {
            changed = false;
            foreach (var quest in all.Values)
            {
                if (reachable.Contains(quest.Key))
                {
                    continue;
                }

                if (quest.PrerequisiteQuestKeys.All(reachable.Contains))
                {
                    reachable.Add(quest.Key);
                    changed = true;
                }
            }
        }
        while (changed);

        return Results.Ok(new
        {
            zoneKey,
            nodes,
            edges,
            cycles = inZone.Where(q => onCycle.Contains(q.Key)).Select(q => q.Key).ToList(),
            unreachable = inZone.Where(q => !reachable.Contains(q.Key)).Select(q => q.Key).ToList(),
            missingPrerequisites,
        });
    }

    /// <summary>
    /// Every quest that sits on a prerequisite cycle, found in one pass.
    /// </summary>
    /// <remarks>
    /// The previous version shared one visited set across restarts while only clearing its
    /// recursion stack on the non-cycle path, so once any cycle was found the stack stayed dirty
    /// and later quests were reported as cyclic when they were not. Here the colour map is what
    /// carries across roots, and the stack is always unwound, so each node is classified once.
    /// </remarks>
    private static HashSet<string> FindCycleMembers(Dictionary<string, QuestResponse> all)
    {
        const int InProgress = 1;
        const int Done = 2;

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var onCycle = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        void Visit(string key)
        {
            if (!all.TryGetValue(key, out var quest))
            {
                return;
            }

            if (state.TryGetValue(key, out var colour))
            {
                if (colour == InProgress)
                {
                    // Everything from the earlier visit to the top of the path is in the cycle.
                    var start = path.LastIndexOf(key);
                    for (var i = start; i >= 0 && i < path.Count; i++)
                    {
                        onCycle.Add(path[i]);
                    }
                }

                return;
            }

            state[key] = InProgress;
            path.Add(key);

            foreach (var prereq in quest.PrerequisiteQuestKeys)
            {
                Visit(prereq);
            }

            path.RemoveAt(path.Count - 1);
            state[key] = Done;
        }

        foreach (var key in all.Keys)
        {
            Visit(key);
        }

        return onCycle;
    }

    // -----------------------------------------------------------------------
    // Shared plumbing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Runs an edit and turns the outcome into a response. Every builder write goes through
    /// here so the status mapping exists in exactly one place.
    /// </summary>
    internal static async Task<IResult> SaveAsync<T>(
        WorldEditor editor,
        WorldChange change,
        HttpContext http,
        CancellationToken ct,
        Func<Task<T>> reload)
    {
        http.TryGetAccountId(out var accountId);

        var outcome = await editor.ApplyAsync(change, accountId, ct);

        return outcome.Status switch
        {
            EditStatus.Saved => Results.Ok(await reload()),

            // Applied to memory, failed to persist, then rolled back by a world reload. The
            // builder is told plainly rather than being given a 200 for work that vanished.
            EditStatus.NotSaved => Results.Json(
                new { error = "The edit could not be saved and has been rolled back." },
                statusCode: StatusCodes.Status500InternalServerError),

            _ => Refused(outcome.Result),
        };
    }

    private static IResult Refused(MutationResult result) => result.Error switch
    {
        MutationError.NotFound => Results.NotFound(new { error = result.Message }),
        MutationError.Conflict or MutationError.Occupied =>
            Results.Conflict(new { error = result.Message }),
        _ => Results.BadRequest(new { error = result.Message }),
    };

    internal static IResult Invalid(string message) => Results.BadRequest(new { error = message });

    internal static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Turns the request's flat map into a <see cref="FlagSet"/>, dropping anything the
    /// registry does not recognise. Unknown keys are preserved when they come <em>from</em> the
    /// database, but there is no reason to let a client type a new one into existence - it
    /// would be a flag nothing ever reads (PLAN.md §4.10).
    /// </summary>
    internal static FlagSet ToFlagSet(IReadOnlyDictionary<string, bool>? flags)
    {
        var set = new FlagSet();

        if (flags is null)
        {
            return set;
        }

        foreach (var (key, value) in flags.Where(f => RoomFlags.IsKnown(f.Key)))
        {
            set.Set(key, value);
        }

        return set;
    }

    private static bool IsKeySegment(string value) =>
        !string.IsNullOrEmpty(value)
        && value[0] != '-'
        && value[^1] != '-'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Longest attack verb worth allowing - past this the combat log stops reading as prose.</summary>
    private const int MaxAttackVerbLength = 24;

    /// <summary>
    /// Refuses a weapon nobody could balance around. The engine clamps a too-fast delay anyway,
    /// but silently honouring a save that says 1 and running at 4 would leave a builder tuning a
    /// number the game ignores.
    /// </summary>
    /// <summary>
    /// Refuses a slot list that could never be equipped as authored.
    /// </summary>
    /// <remarks>
    /// A refusal rather than a quiet normalisation, because "two-handed shield" is a mistake and
    /// silently dropping half of it is how a builder ends up sure they authored something they
    /// did not. The rule itself lives in <see cref="SlotRules"/>, so this endpoint and the
    /// <c>check-bundle</c> tool cannot disagree about what is authorable.
    /// </remarks>
    private static IResult? ValidateSlots(IReadOnlyList<ItemSlot>? slots, bool? isTwoHanded)
    {
        if (slots is null && isTwoHanded is null)
        {
            return null;
        }

        var normalized = SlotRules.Normalize(slots);

        return SlotRules.Incoherent(normalized, isTwoHanded ?? false) is { } why
            ? Invalid(why)
            : null;
    }

    private static IResult? ValidateWeapon(int? delayPulses, string? verb)
    {
        if (delayPulses is { } delay && delay < AttackTiming.MinDelayPulses)
        {
            return Invalid(
                $"An attack delay must be at least {AttackTiming.MinDelayPulses} pulses (1.0 second).");
        }

        return ValidateVerb(verb, "An attack verb");
    }

    private static IResult? ValidateVerb(string? verb, string subject)
    {
        if (verb is null)
        {
            return null;
        }

        var trimmed = verb.Trim();

        if (trimmed.Length > MaxAttackVerbLength)
        {
            return Invalid($"{subject} must be {MaxAttackVerbLength} characters or fewer.");
        }

        if (trimmed.Any(char.IsDigit))
        {
            return Invalid($"{subject} is a word, not a number - try \"slash\" or \"crush\".");
        }

        return null;
    }

    private static IResult? ValidateAttacks(List<MobAttack>? attacks)
    {
        if (attacks is null)
        {
            return null;
        }

        foreach (var attack in attacks)
        {
            if (attack is null)
            {
                continue;
            }

            if (attack.DelayPulses < AttackTiming.MinDelayPulses)
            {
                return Invalid(
                    $"An attack delay must be at least {AttackTiming.MinDelayPulses} pulses (1.0 second).");
            }

            if (attack.DamageMultiplier is <= 0m)
            {
                return Invalid("An attack's damage multiplier must be greater than zero.");
            }

            if (ValidateVerb(attack.Verb, "An attack message") is { } refusal)
            {
                return refusal;
            }

            // Refused rather than stored, the same way an unknown room flag is (§4.10). An effect
            // key nothing answers to would be a mob attack that silently swings for damage alone,
            // and the builder would have no way to tell that from an effect that simply missed.
            if (!string.IsNullOrWhiteSpace(attack.EffectKey) &&
                !KnownEffects.Contains(attack.EffectKey.Trim()))
            {
                return Invalid($"'{attack.EffectKey}' is not a known effect.");
            }
        }

        return null;
    }

    /// <summary>
    /// The effect executors that exist, for validating what a mob attack may carry.
    /// </summary>
    /// <remarks>
    /// The registry itself rather than a list of keys beside it: a second copy is how the
    /// catalogue test drifted, and this one would drift the same way the day an eighth executor
    /// lands. Stateless, so one shared instance is fine.
    /// </remarks>
    private static readonly EffectRegistry KnownEffects = new();

    /// <summary>
    /// Tidies an authored attack list. A blank verb defaults rather than being refused - the
    /// builder's row starts empty and there is no reason to make someone type "hit" to save.
    /// </summary>
    private static List<MobAttack>? NormalizeAttacks(List<MobAttack>? attacks) =>
        attacks is null
            ? null
            : [.. attacks.Where(a => a is not null).Select(a => new MobAttack
            {
                Verb = AttackTiming.VerbOr(a.Verb),
                DelayPulses = AttackTiming.Clamp(a.DelayPulses),
                DamageMultiplier = a.DamageMultiplier,

                // Carried through, not dropped. Rebuilding the row field by field is what makes
                // this the exact place a newly added property goes missing - silently, since the
                // save succeeds and only the effect is gone.
                EffectKey = string.IsNullOrWhiteSpace(a.EffectKey) ? null : a.EffectKey.Trim(),
                EffectParams = a.EffectParams is { Count: > 0 }
                    ? new Dictionary<string, string>(a.EffectParams, StringComparer.Ordinal)
                    : null,
            })];
}
