using Muwbta.Domain.Accounts;
using Muwbta.Engine;
using Muwbta.Server.Auth;

namespace Muwbta.Server.Admin;

public sealed record SetRoleRequest(string? Role);

public sealed record SetBanRequest(bool Banned, string? Reason);

/// <summary>
/// A mute as a duration rather than an instant, because that is how the decision is actually made
/// ("an hour") and it saves the panel from sending a clock the server has to trust. Null or zero
/// minutes lifts it.
/// </summary>
public sealed record SetMuteRequest(int? Minutes, string? Reason);

public sealed record SetPasswordRequest(string? Password);

/// <summary>
/// Account administration (PLAN.md §7.7, and §8's moderation actions). This is the real
/// interface; the in-game verbs are a convenience over it.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);

        group.MapGet("/accounts", SearchAsync);
        group.MapGet("/accounts/{username}", GetAsync);
        group.MapPatch("/accounts/{username}/role", SetRoleAsync);
        group.MapPatch("/accounts/{username}/ban", SetBanAsync);
        group.MapPatch("/accounts/{username}/mute", SetMuteAsync);

        // POST, not PATCH: it is not a partial update of the account resource, and a body that
        // carries a password should not sit at a URL anyone would think to retry idly.
        group.MapPost("/accounts/{username}/password", SetPasswordAsync);

        // Lifts the sign-in backoff (LoginThrottle). An action rather than a field, since there
        // is nothing to set - only a fuse to clear.
        group.MapPost("/accounts/{username}/unlock", UnlockLoginAsync);

        group.MapDelete("/characters/{name}", DeleteCharacterAsync);

        // Standing credentials for the builder API (docs/PAT-AND-MCP.md §7). Read-only, plus the
        // ability to pull one - an admin cannot mint a token for somebody else, which would be a
        // way to hold an account without ever touching its password.
        group.MapGet("/accounts/{username}/tokens", ListTokensAsync);
        group.MapDelete("/tokens/{id:guid}", RevokeTokenAsync);
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        string? address,
        int? limit,
        AccountAdminService accounts,
        CancellationToken ct) =>
        Results.Ok(await accounts.SearchAsync(q, address, limit ?? 50, ct));

    private static async Task<IResult> GetAsync(
        string username,
        AccountAdminService accounts,
        CancellationToken ct) =>
        await accounts.FindAsync(username, ct) is { } account
            ? Results.Ok(account)
            : Results.NotFound();

    private static async Task<IResult> SetRoleAsync(
        string username,
        SetRoleRequest request,
        AccountAdminService accounts,
        GameGateway gateway,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<AccountRole>(request.Role, ignoreCase: true, out var role)
            || !Enum.IsDefined(role))
        {
            return Results.BadRequest(new
            {
                error = "Role must be one of: Player, Builder, Moderator, Admin.",
            });
        }

        var result = await accounts.SetRoleAsync(actorId, username, role, ct);

        if (result.Outcome == RoleChangeOutcome.Changed && result.TargetAccountId is { } id)
        {
            // Live, for a character already in the world. The cookie is handled separately by
            // revalidation (§7.7) - this only updates the copy the game loop holds.
            AdminLiveEffects.AfterRoleChange(gateway, id, role);
        }

        return result.Outcome switch
        {
            RoleChangeOutcome.NoSuchAccount => Results.NotFound(new { error = result.Message }),
            RoleChangeOutcome.WouldDemoteSelf => Results.Conflict(new { error = result.Message }),
            _ => Results.Ok(await accounts.FindAsync(username, ct)),
        };
    }

    private static async Task<IResult> SetBanAsync(
        string username,
        SetBanRequest request,
        AccountAdminService accounts,
        GameGateway gateway,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        var result = await accounts.SetBanAsync(actorId, username, request.Banned, request.Reason, ct);

        if (result.Ok && result.TargetAccountId is { } id)
        {
            AdminLiveEffects.AfterBan(gateway, id, request.Banned, request.Reason);
        }

        return await RespondAsync(result, username, accounts, ct);
    }

    private static async Task<IResult> SetMuteAsync(
        string username,
        SetMuteRequest request,
        AccountAdminService accounts,
        GameGateway gateway,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        // A week is the ceiling on purpose. Beyond that the honest action is a ban, and an
        // accidental extra zero should not silence somebody until next year.
        if (request.Minutes is { } minutes and > 60 * 24 * 7)
        {
            return Results.BadRequest(new { error = "A mute may last at most seven days." });
        }

        var until = request.Minutes is { } m and > 0
            ? clock.GetUtcNow().AddMinutes(m)
            : (DateTimeOffset?)null;

        var result = await accounts.SetMuteAsync(actorId, username, until, request.Reason, ct);

        if (result.Ok && result.TargetAccountId is { } id)
        {
            AdminLiveEffects.AfterMute(gateway, id, result.MutedUntil);
        }

        return await RespondAsync(result, username, accounts, ct);
    }

    private static async Task<IResult> SetPasswordAsync(
        string username,
        SetPasswordRequest request,
        AccountAdminService accounts,
        GameGateway gateway,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        var result = await accounts.SetPasswordAsync(actorId, username, request.Password, ct);

        if (result.Ok && result.TargetAccountId is { } id)
        {
            AdminLiveEffects.AfterPasswordReset(gateway, id);
        }

        return await RespondAsync(result, username, accounts, ct);
    }

    private static async Task<IResult> UnlockLoginAsync(
        string username,
        AccountAdminService accounts,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        var result = await accounts.UnlockLoginAsync(actorId, username, ct);
        return await RespondAsync(result, username, accounts, ct);
    }

    private static async Task<IResult> ListTokensAsync(
        string username,
        AccountAdminService accounts,
        CancellationToken ct) =>
        await accounts.TokensForAsync(username, ct) is { } tokens
            ? Results.Ok(tokens)
            : Results.NotFound(new { error = $"There is no account named '{username}'." });

    private static async Task<IResult> RevokeTokenAsync(
        Guid id,
        AccountAdminService accounts,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        var result = await accounts.RevokeTokenAsync(actorId, id, ct);

        return result.Ok
            ? Results.Ok(new { message = result.Message })
            : result.Failure == ModerationFailure.NoSuchTarget
                ? Results.NotFound(new { error = result.Message })
                : Results.BadRequest(new { error = result.Message });
    }

    private static async Task<IResult> DeleteCharacterAsync(
        string name,
        AccountAdminService accounts,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        // Unlike the in-game verb, there is no session to pull out of the world first - if that
        // character is playing, the loop finds the row soft-deleted at its next save and the player
        // drops out at their next command. Retiring somebody mid-fight from a web panel is rare
        // enough to be worth the plainer implementation.
        var result = await accounts.DeleteCharacterAsync(actorId, name, ct);

        return result.Ok
            ? Results.Ok(new { message = result.Message })
            : Failure(result);
    }

    /// <summary>
    /// The account as it now stands on success, so the panel does not have to guess what changed,
    /// and the mapped failure otherwise.
    /// </summary>
    private static async Task<IResult> RespondAsync(
        ModerationResult result,
        string username,
        AccountAdminService accounts,
        CancellationToken ct) =>
        result.Ok
            ? Results.Ok(await accounts.FindAsync(username, ct))
            : Failure(result);

    private static IResult Failure(ModerationResult result) =>
        result.Failure switch
        {
            ModerationFailure.NoSuchTarget => Results.NotFound(new { error = result.Message }),
            ModerationFailure.Invalid => Results.BadRequest(new { error = result.Message }),
            _ => Results.Conflict(new { error = result.Message }),
        };
}
