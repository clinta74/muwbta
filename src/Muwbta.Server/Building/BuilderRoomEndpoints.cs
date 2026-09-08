using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Server.Auth;

namespace Muwbta.Server.Building;

/// <summary>
/// Rooms, exits, and walk-and-build (PLAN.md §7.3, §7.6). Split from
/// <see cref="BuilderEndpoints"/> only for size; the route group and policy are the same.
/// </summary>
public static class BuilderRoomEndpoints
{
    /// <summary>
    /// A dig every 2 seconds per account. This guards against a held-down key sending a walk
    /// command forty times and carving forty rooms (PLAN.md §7.6), not against a determined
    /// attacker - the endpoint already requires the Builder role.
    /// </summary>
    private static readonly TimeSpan DigInterval = TimeSpan.FromSeconds(2);

    public static void MapBuilderRoomEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/builder/rooms").RequireAuthorization(Policies.Builder);

        group.MapGet("/{key}", GetRoomAsync);
        group.MapPost("/{key}", CreateRoomAsync);
        group.MapPatch("/{key}", UpdateRoomAsync);
        group.MapDelete("/{key}", DeleteRoomAsync);

        group.MapPost("/{key}/rename", RenameRoomAsync);
        group.MapPost("/{key}/dig", DigAsync);

        group.MapPut("/{key}/exits/{direction}", SetExitAsync);
        group.MapDelete("/{key}/exits/{direction}", RemoveExitAsync);

        group.MapPut("/{key}/flags/{flag}", SetFlagAsync);
    }

    // -----------------------------------------------------------------------
    // Rooms
    // -----------------------------------------------------------------------

    private static async Task<IResult> GetRoomAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var roomKey))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        return await queries.RoomAsync(roomKey, ct) is { } room
            ? Results.Ok(room)
            : Results.NotFound();
    }

    private static async Task<IResult> CreateRoomAsync(
        string key,
        SaveRoomRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var roomKey))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        if (await queries.RoomAsync(roomKey, ct) is not null)
        {
            return Results.Conflict(new { error = $"Room '{key}' already exists." });
        }

        var change = new UpsertRoom(
            roomKey,
            BuilderEndpoints.Trim(request.ZoneKey) ?? roomKey.ZoneKey,
            BuilderEndpoints.Trim(request.Title) ?? "An Unnamed Room",
            request.Description ?? string.Empty,
            BuilderEndpoints.ToFlagSet(request.Flags),
            request.Grid ?? [],
            request.Legend ?? new Dictionary<string, string>(StringComparer.Ordinal),
            request.EditorX,
            request.EditorY);

        return await BuilderEndpoints.SaveAsync(
            editor, change, http, ct, () => queries.RoomAsync(roomKey, ct));
    }

    private static async Task<IResult> UpdateRoomAsync(
        string key,
        SaveRoomRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var roomKey))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        if (await queries.RoomAsync(roomKey, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var title = BuilderEndpoints.Trim(request.Title) ?? existing.Title;
        var description = request.Description ?? existing.Description;
        var flags = request.Flags is null
            ? BuilderEndpoints.ToFlagSet(existing.Flags)
            : BuilderEndpoints.ToFlagSet(request.Flags);

        // Giving a room a real title and description is what takes it off the build to-do
        // list (PLAN.md §7.6) - so it clears itself, without the builder remembering to.
        if (IsFinished(title, description))
        {
            flags.Clear(RoomFlags.Unfinished.Key);
        }

        var change = new UpsertRoom(
            roomKey,
            existing.ZoneKey,
            title,
            description,
            flags,
            request.Grid ?? existing.Grid,
            request.Legend ?? existing.Legend,
            request.EditorX ?? existing.EditorX,
            request.EditorY ?? existing.EditorY);

        return await BuilderEndpoints.SaveAsync(
            editor, change, http, ct, () => queries.RoomAsync(roomKey, ct));
    }

    private static async Task<IResult> DeleteRoomAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var roomKey))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        return await BuilderEndpoints.SaveAsync(
            editor, new DeleteRoom(roomKey), http, ct, () => Task.FromResult<object?>(null));
    }

    private static async Task<IResult> RenameRoomAsync(
        string key,
        RenameRoomRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var from))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        if (!RoomKey.TryParse(request.NewKey, out var to))
        {
            return BuilderEndpoints.Invalid("The new key must be 'world.zone.room'.");
        }

        return await BuilderEndpoints.SaveAsync(
            editor, new RenameRoom(from, to), http, ct, () => queries.RoomAsync(to, ct));
    }

    // -----------------------------------------------------------------------
    // Walk-and-build (PLAN.md §7.6)
    // -----------------------------------------------------------------------

    private static async Task<IResult> DigAsync(
        string key,
        DigRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        DigThrottle throttle,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var from))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        if (!DirectionExtensions.TryParse(request.Direction, out var direction))
        {
            return BuilderEndpoints.Invalid("Which direction? north, east, south, west, up, or down.");
        }

        RoomKey? newKey = null;
        if (!string.IsNullOrWhiteSpace(request.NewRoomKey))
        {
            if (!RoomKey.TryParse(request.NewRoomKey, out var parsed))
            {
                return BuilderEndpoints.Invalid("The new room key must be 'world.zone.room'.");
            }

            newKey = parsed;
        }

        http.TryGetAccountId(out var accountId);

        if (!throttle.TryDig(accountId))
        {
            return Results.Json(
                new { error = "Slow down - one dig at a time." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var change = new DigRoom(
            from,
            direction,
            request.Reciprocal,
            BuilderEndpoints.Trim(request.ZoneKey),
            newKey);

        // Applied directly rather than through SaveAsync, because the response is the room the
        // loop just created and only the loop knows its key - a dug room's key is generated,
        // not supplied (PLAN.md §7.6).
        var outcome = await editor.ApplyAsync(change, accountId, ct);

        return outcome.Status == EditStatus.Saved && outcome.Result.AffectedRoom is { } created
            ? Results.Ok(await queries.RoomAsync(created, ct))
            : Translate(outcome);
    }

    // -----------------------------------------------------------------------
    // Exits
    // -----------------------------------------------------------------------

    private static async Task<IResult> SetExitAsync(
        string key,
        string direction,
        SaveExitRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var from))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        if (!DirectionExtensions.TryParse(direction, out var parsedDirection))
        {
            return BuilderEndpoints.Invalid($"'{direction}' is not a direction.");
        }

        // The destination is deliberately allowed not to exist yet: live editing has no
        // publish gate to defer the link to (PLAN.md §7.4).
        if (!RoomKey.TryParse(request.To, out var to))
        {
            return BuilderEndpoints.Invalid("The destination must be 'world.zone.room'.");
        }

        // ApplyConditions: true - a PUT states the whole exit, so a condition left out of the body
        // is a condition the exit does not have. That is what lets the editor take a lock off
        // (PLAN.md §4.15); the in-game `link` verb sends the same change with it false, and so
        // repoints without touching who may pass.
        return await BuilderEndpoints.SaveAsync(
            editor,
            new LinkExit(
                from,
                parsedDirection,
                to,
                request.Reciprocal,
                ApplyConditions: true,
                request.RequiredFlagKey,
                request.RequiredItemKey,
                request.RefusalMessage,
                request.ReciprocalConditions),
            http,
            ct,
            () => queries.RoomAsync(from, ct));
    }

    private static async Task<IResult> RemoveExitAsync(
        string key,
        string direction,
        bool? reciprocal,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var from))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        if (!DirectionExtensions.TryParse(direction, out var parsedDirection))
        {
            return BuilderEndpoints.Invalid($"'{direction}' is not a direction.");
        }

        return await BuilderEndpoints.SaveAsync(
            editor,
            new UnlinkExit(from, parsedDirection, reciprocal ?? true),
            http,
            ct,
            () => queries.RoomAsync(from, ct));
    }

    // -----------------------------------------------------------------------
    // Flags
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets one flag on one room, leaving its siblings alone.
    /// </summary>
    /// <remarks>
    /// PATCHing the room replaces the whole flag set, so two builders editing one zone erase
    /// each other's flags. This carries a single key, which is also exactly what the three-state
    /// control in the editor means: on, off, or absent so the zone or world decides.
    ///
    /// The flag name is not checked here on purpose - the applier already refuses unknown keys
    /// against the same registry the in-game <c>rflag</c> verb uses, and one authority beats two.
    /// </remarks>
    private static async Task<IResult> SetFlagAsync(
        string key,
        string flag,
        SetFlagRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct)
    {
        if (!RoomKey.TryParse(key, out var roomKey))
        {
            return BuilderEndpoints.Invalid("A room key must be 'world.zone.room'.");
        }

        return await BuilderEndpoints.SaveAsync(
            editor,
            new SetRoomFlag(roomKey, flag, request.ToFlagValue()),
            http,
            ct,
            () => queries.RoomAsync(roomKey, ct));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IResult Translate(EditOutcome outcome) => outcome.Status switch
    {
        EditStatus.NotSaved => Results.Json(
            new { error = "The edit could not be saved and has been rolled back." },
            statusCode: StatusCodes.Status500InternalServerError),
        _ => outcome.Result.Error switch
        {
            MutationError.NotFound => Results.NotFound(new { error = outcome.Result.Message }),
            MutationError.Conflict or MutationError.Occupied =>
                Results.Conflict(new { error = outcome.Result.Message }),
            _ => Results.BadRequest(new { error = outcome.Result.Message }),
        },
    };

    private static bool IsFinished(string title, string description) =>
        !title.StartsWith("An Unfinished", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(description)
        && !description.StartsWith("Bare ground and unshaped air", StringComparison.Ordinal);
}
