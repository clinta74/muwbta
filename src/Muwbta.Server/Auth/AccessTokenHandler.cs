using System.Security.Claims;
using System.Text.Encodings.Web;
using Muwbta.Domain.Accounts;
using Muwbta.Persistence;
using Muwbta.Server.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Muwbta.Server.Auth;

public static class AccessTokenDefaults
{
    public const string Scheme = "token";

    /// <summary>
    /// The claim carrying <see cref="AccessTokenScope"/>. Absent on a cookie principal, which is
    /// what <c>AuthEndpoints</c> checks to keep a token from minting another token.
    /// </summary>
    public const string ScopeClaim = "muwbta:scope";

    /// <summary>The token's own id, so a request can be traced back to the credential.</summary>
    public const string TokenIdClaim = "muwbta:token";

    /// <summary>The only prefix a token may reach (docs/PAT-AND-MCP.md §4).</summary>
    public const string AllowedPathPrefix = "/api/builder";
}

/// <summary>
/// Authenticates <c>Authorization: Bearer muwbta_pat_...</c> against the access token table
/// (docs/PAT-AND-MCP.md §5).
/// </summary>
/// <remarks>
/// <b>This reads the account row on every request, unconditionally</b> - the opposite of the
/// interval <see cref="PrincipalRevalidator"/> uses, and correct here. That interval exists
/// because a cookie sits on the path of every command POST from every player, so a database read
/// per request would be a read per keystroke. A builder token issues low-volume editing calls
/// against a policy already capped by <c>RateLimiting.Builder</c>. Paying the read buys immediate
/// ban and demotion with no timestamp in a ticket, no renewal, and no staleness window at all.
///
/// The role comes off the account row and never off the token. A token stores what it may reach,
/// not who its holder is - so promoting or demoting an account changes what its tokens can do at
/// the next request, which is the property that makes them safe to hand out.
/// </remarks>
public sealed class AccessTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDbContextFactory<MuwbtaDbContext> factory,
    AuthOptions auth,
    TimeProvider clock,
    ServerMetrics metrics)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadBearer(out var presented))
        {
            // NoResult and not Fail: no Authorization header means this scheme has nothing to say,
            // and the cookie scheme may still authenticate the request perfectly well.
            return AuthenticateResult.NoResult();
        }

        if (!AccessTokenSecret.TryParse(presented, out var id, out var secret))
        {
            return Refuse(TokenAuthOutcome.Malformed, "That is not a muwbta access token.");
        }

        await using var db = await factory.CreateDbContextAsync(Context.RequestAborted).ConfigureAwait(false);

        var row = await db.AccessTokens
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id, Context.RequestAborted)
            .ConfigureAwait(false);

        // The secret is verified even when the row is missing would be the careful thing here, but
        // there is nothing to compare against and nothing to learn: an id is public, and both
        // paths are a constant-time-irrelevant lookup on a primary key. The comparison that does
        // matter - secret against stored hash - is constant time.
        if (row is null || !AccessTokenSecret.Matches(secret, row.SecretHash))
        {
            return Refuse(TokenAuthOutcome.Unknown, "No such access token.");
        }

        var now = clock.GetUtcNow();

        if (row.RevokedAt is not null)
        {
            return Refuse(TokenAuthOutcome.Revoked, "That access token was revoked.");
        }

        if (row.ExpiresAt <= now)
        {
            return Refuse(TokenAuthOutcome.Expired, "That access token expired.");
        }

        // Everything PrincipalRevalidator checks for a cookie, checked here for a token. A token
        // that only proved itself against its own row would keep working for a banned account.
        var account = row.Account;

        if (account is null || account.IsBanned)
        {
            return Refuse(TokenAuthOutcome.Banned, "That account cannot sign in.");
        }

        if (account.PasswordChangedAt != row.PasswordChangedAt)
        {
            return Refuse(
                TokenAuthOutcome.StalePassword,
                "That access token was issued before the account's password changed, so it is no "
                + "longer valid. Issue a new one.");
        }

        if (!IsWithinScope(row.Scope, out var refusal))
        {
            return Refuse(TokenAuthOutcome.ScopeRefused, refusal);
        }

        await TouchAsync(db, row, now).ConfigureAwait(false);

        metrics.TokenAuth(TokenAuthOutcome.Accepted);

        var principal = AuthEndpoints.BuildPrincipal(account.Id, account.Username, account.Role);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(AccessTokenDefaults.ScopeClaim, row.Scope.ToString()));
        identity.AddClaim(new Claim(AccessTokenDefaults.TokenIdClaim, row.Id.ToString()));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>
    /// Whether the request is somewhere this scope reaches.
    /// </summary>
    /// <remarks>
    /// Checked here rather than on sixty endpoints, which is the point: a builder endpoint written
    /// next month is covered on the day it is written, and no future author has to remember a
    /// token attribute. The GET-only rule for a read scope is a proxy - every mutating builder
    /// endpoint today is POST/PATCH/PUT/DELETE - and it is a proxy the design document names as
    /// such rather than pretending otherwise.
    /// </remarks>
    private bool IsWithinScope(AccessTokenScope scope, out string refusal)
    {
        var path = Request.Path;

        if (!path.StartsWithSegments(AccessTokenDefaults.AllowedPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            refusal =
                $"An access token reaches {AccessTokenDefaults.AllowedPathPrefix} and nothing else. "
                + $"'{path}' is outside it, including for an account that could reach it in a browser.";
            return false;
        }

        if (scope == AccessTokenScope.BuilderRead && !HttpMethods.IsGet(Request.Method))
        {
            refusal = $"That token is {nameof(AccessTokenScope.BuilderRead)}, so it may only GET.";
            return false;
        }

        refusal = string.Empty;
        return true;
    }

    /// <summary>
    /// Records use, but only once the stored value is stale enough to be worth a write.
    /// </summary>
    private async Task TouchAsync(MuwbtaDbContext db, AccessToken row, DateTimeOffset now)
    {
        if (row.LastUsedAt is { } last && now - last < auth.TokenLastUsedInterval)
        {
            return;
        }

        row.LastUsedAt = now;

        try
        {
            await db.SaveChangesAsync(Context.RequestAborted).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Bookkeeping. Failing the request because a "last used" stamp would not write turns a
            // cosmetic problem into an outage.
            Logger.LogWarning(ex, "Could not record last use of access token {TokenId}.", row.Id);
        }
    }

    private bool TryReadBearer(out string token)
    {
        token = string.Empty;
        var header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header["Bearer ".Length..].Trim();
        return token.Length > 0;
    }

    /// <summary>
    /// Fails with a message the caller can act on.
    /// </summary>
    /// <remarks>
    /// Deliberately more forthcoming than the login endpoint, which answers 401 for both "no such
    /// user" and "wrong password" so the two cannot be told apart. Nothing here is enumerable: the
    /// caller already holds a token id and a secret, and telling them the secret is wrong rather
    /// than that the token is revoked reveals nothing they could not test in one more request. The
    /// caller is a program, and a program given "unauthorized" retries forever.
    /// </remarks>
    private AuthenticateResult Refuse(string outcome, string message)
    {
        metrics.TokenAuth(outcome);
        Context.Items["muwbta.token.refusal"] = message;

        return AuthenticateResult.Fail(message);
    }

    /// <summary>
    /// 401 with the reason, rather than the framework's empty body.
    /// </summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";

        var message = Context.Items.TryGetValue("muwbta.token.refusal", out var stored)
            ? stored as string
            : "An access token is required.";

        await Response.WriteAsJsonAsync(
            new { error = message }, Context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// 403 for a token whose account is real but whose role is not enough.
    /// </summary>
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        await Response.WriteAsJsonAsync(
            new { error = "That account does not hold the role this needs." },
            Context.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>The scheme names, so Program.cs and the endpoints spell them the same way.</summary>
public static class MuwbtaAuthentication
{
    /// <summary>Chooses cookie or token per request. The application's default scheme.</summary>
    public const string PolicyScheme = "muwbta";
}
