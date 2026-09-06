using Muwbta.Domain.Accounts;
using Muwbta.Persistence;
using Muwbta.Server.Infrastructure;
using Muwbta.Server.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Auth;

/// <param name="Scope">The enum's name, so the wire says <c>BuilderRead</c> and not <c>0</c>.</param>
public sealed record AccessTokenSummary(
    Guid Id,
    string Name,
    string Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsExpired);

/// <summary>The one and only time the secret is sent anywhere.</summary>
public sealed record CreatedAccessToken(AccessTokenSummary Token, string Secret);

public sealed record CreateAccessTokenRequest(string? Name, string? Scope, int? ExpiresInDays);

/// <param name="MaxLifetimeDays">So the form can offer choices the server will accept.</param>
public sealed record AccessTokenList(
    IReadOnlyList<AccessTokenSummary> Tokens,
    int MaxLifetimeDays,
    int MaxTokens);

/// <summary>
/// Issuing, listing and revoking personal access tokens (docs/PAT-AND-MCP.md §7).
/// </summary>
/// <remarks>
/// Under <c>/api/auth</c> rather than <c>/api/builder</c>, and that placement is load-bearing: a
/// token may only reach <c>/api/builder</c> (<see cref="AccessTokenHandler.IsWithinScope"/>), so a
/// token cannot reach these endpoints at all. Minting a token therefore requires the cookie, and
/// escalation from a leaked token to a permanent foothold is not available. No check here
/// implements that - the scheme does, for every endpoint outside the builder at once.
/// </remarks>
public static class AccessTokenEndpoints
{
    public static void MapAccessTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Builder, because that is the only role a token can be useful to. Rate limited with the
        // Auth policy rather than the authenticated one: this is credential issuance, and it
        // carries the tight per-address limit for the reason POST /api/auth/password does.
        var group = routes.MapGroup("/api/auth/tokens")
            .RequireAuthorization(Policies.Builder)
            .RequireRateLimiting(RateLimiting.Auth);

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapDelete("/{id:guid}", RevokeAsync);
    }

    private static async Task<IResult> ListAsync(
        MuwbtaDbContext db,
        AuthOptions auth,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var now = clock.GetUtcNow();

        // Revoked tokens are kept in the table so the audit trail resolves, and left out of this
        // list because a revoked credential is not a thing anybody needs to look at. Expired ones
        // stay: they are clutter the owner can see and clear.
        var rows = await db.AccessTokens.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(new AccessTokenList(
            [.. rows.Select(t => Summarise(t, now))],
            auth.TokenMaxLifetimeDays,
            auth.TokensPerAccount));
    }

    private static async Task<IResult> CreateAsync(
        CreateAccessTokenRequest request,
        MuwbtaDbContext db,
        AuthOptions auth,
        TimeProvider clock,
        ServerMetrics metrics,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(http);

        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
        {
            return Results.BadRequest(new { error = "A token needs a name, of 64 characters or fewer." });
        }

        if (!Enum.TryParse<AccessTokenScope>(request.Scope, ignoreCase: true, out var scope)
            || !Enum.IsDefined(scope))
        {
            return Results.BadRequest(new
            {
                error = $"Scope must be one of: {string.Join(", ", Enum.GetNames<AccessTokenScope>())}.",
            });
        }

        var days = request.ExpiresInDays ?? 0;

        if (days < 1 || days > auth.TokenMaxLifetimeDays)
        {
            return Results.BadRequest(new
            {
                error = $"A token lasts between 1 and {auth.TokenMaxLifetimeDays} days. "
                    + "There is no option for one that never expires.",
            });
        }

        var now = clock.GetUtcNow();

        // The cap counts live tokens, so expired ones do not lock somebody out of making a new one
        // while their old ones sit there harmlessly.
        var live = await db.AccessTokens
            .CountAsync(t => t.AccountId == accountId && t.RevokedAt == null && t.ExpiresAt > now, ct);

        if (live >= auth.TokensPerAccount)
        {
            return Results.BadRequest(new
            {
                error = $"That account already holds {live} live tokens, which is the limit. "
                    + "Revoke one first.",
            });
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null)
        {
            return Results.Unauthorized();
        }

        var id = Guid.CreateVersion7();
        var minted = AccessTokenSecret.Mint(id);
        var expires = now.AddDays(days);

        var row = new AccessToken
        {
            Id = id,
            AccountId = accountId,
            Name = name,
            SecretHash = minted.SecretHash,
            Scope = scope,
            CreatedAt = now,
            ExpiresAt = expires,

            // Snapshotted so a later password change invalidates this token with no sweep to run.
            PasswordChangedAt = account.PasswordChangedAt,
        };

        db.AccessTokens.Add(row);

        db.AdminAudits.Add(new AdminAudit
        {
            ActorAccountId = accountId,
            TargetAccountId = accountId,
            Action = AdminAction.TokenIssued,
            Before = null,
            After = AccessTokenSecret.Describe(name, scope, expires),
            Reason = null,
            At = now,
        });

        metrics.Moderation(nameof(AdminAction.TokenIssued));

        await db.SaveChangesAsync(ct);

        // The only response that carries the secret. It is not stored, so this is the only chance
        // anybody has to read it.
        return Results.Ok(new CreatedAccessToken(Summarise(row, now), minted.Secret));
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        MuwbtaDbContext db,
        TimeProvider clock,
        ServerMetrics metrics,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var row = await db.AccessTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.AccountId == accountId, ct);

        // 404 for somebody else's token as well as for one that does not exist: whether a given id
        // belongs to another account is not this caller's business either way.
        if (row is null)
        {
            return Results.NotFound(new { error = "No such token." });
        }

        // Idempotent. Revoking twice is what a retry looks like, and it is not an error.
        if (row.RevokedAt is not null)
        {
            return Results.NoContent();
        }

        var now = clock.GetUtcNow();
        row.RevokedAt = now;

        db.AdminAudits.Add(new AdminAudit
        {
            ActorAccountId = accountId,
            TargetAccountId = accountId,
            Action = AdminAction.TokenRevoked,
            Before = AccessTokenSecret.Describe(row.Name, row.Scope, row.ExpiresAt),
            After = null,
            Reason = null,
            At = now,
        });

        metrics.Moderation(nameof(AdminAction.TokenRevoked));

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    internal static AccessTokenSummary Summarise(AccessToken token, DateTimeOffset now) =>
        new(
            token.Id,
            token.Name,
            token.Scope.ToString(),
            token.CreatedAt,
            token.ExpiresAt,
            token.LastUsedAt,
            token.ExpiresAt <= now);
}
