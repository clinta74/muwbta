using System.Security.Claims;
using System.Text.RegularExpressions;
using Muwbta.Domain.Accounts;
using Muwbta.Engine;
using Muwbta.Engine.Protocol;
using Muwbta.Persistence;
using Muwbta.Server.Infrastructure;
using Muwbta.Server.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Auth;

public sealed record RegisterRequest(string Email, string Username, string Password);

public sealed record LoginRequest(string Username, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AccountResponse(Guid Id, string Username, string Email, string Role);

public static partial class AuthEndpoints
{
    /// <summary>
    /// An account that exists only to be verified against when the username does not.
    /// </summary>
    /// <remarks>
    /// Login answers 401 for "no such user" and for "wrong password" so the two cannot be told
    /// apart by the response — but the first used to return before hashing anything and the
    /// second after the full cost of PBKDF2, which told them apart by the clock instead. An
    /// unknown name is now verified against this account's hash, so both paths take the same
    /// time. The hash is minted once per process by the same hasher, so it carries the same
    /// iteration count as every real one; the password behind it is random and thrown away.
    /// </remarks>
    private static readonly Account Decoy = new()
    {
        Email = "decoy@invalid",
        Username = "decoy",
        PasswordHash = string.Empty,
        Role = AccountRole.Player,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static string? _decoyHash;

    private static string DecoyHash(IPasswordHasher<Account> hasher) =>
        _decoyHash ??= hasher.HashPassword(Decoy, Guid.NewGuid().ToString("N"));

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        // The only surface a stranger can reach, so the only one where the limit is tight
        // (PLAN.md §8, Phase 6). Partitioned by address, since there is no account yet to
        // partition by.
        group.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(RateLimiting.Auth);

        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting(RateLimiting.Auth);

        // Left alone: it reads the cookie the caller already has and touches no database row a
        // stranger could guess at.
        group.MapGet("/me", MeAsync);

        // Cast required. LogoutAsync's signature is Func<HttpContext, Task<IResult>>, which
        // also matches RequestDelegate (Func<HttpContext, Task>) because Task<IResult> is a
        // Task. Without the cast the compiler binds the RequestDelegate overload and the
        // IResult is silently discarded - the endpoint would return an empty 200 and never
        // run the result. ASP0016 catches this.
        group.MapPost("/logout", (Delegate)LogoutAsync);

        // Rate limited alongside login, not with the authenticated endpoints: it takes a password
        // and answers whether that password was right, which is the shape login has and the reason
        // login is limited. The limit is per address, so guessing here costs the same as guessing
        // there.
        group.MapPost("/password", ChangePasswordAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.Auth);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        MuwbtaDbContext db,
        IPasswordHasher<Account> hasher,
        ServerMetrics metrics,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Captured into locals so the compiler's null analysis carries through to the
        // Account below. The record's properties are non-nullable but JSON deserialisation
        // can still hand us nulls.
        var username = request.Username;
        var email = request.Email;
        var password = request.Password;

        if (username is null || !UsernamePattern().IsMatch(username))
        {
            metrics.Registration("refused");
            return Results.BadRequest(new { error = "Username must be 3-24 letters, digits, or underscores." });
        }

        // Reserved after the pattern, so the message about the shape of a name comes first when
        // both apply. A username is what the admin panel shows, which is one of the two places a
        // name gets to claim it is staff (the other is the character, checked at creation).
        if (ReservedNames.IsReserved(username))
        {
            metrics.Registration("refused");
            return Results.BadRequest(new { error = "That username is reserved." });
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            metrics.Registration("refused");
            return Results.BadRequest(new { error = "A valid email address is required." });
        }

        // The one policy, not a private copy of its floor. This surface had its own minimum and
        // no maximum at all - and the maximum is the part that matters here, because PBKDF2
        // hashes whatever it is given and registration is the surface a stranger can reach.
        if (!PasswordPolicy.IsAcceptable(password, out var passwordError))
        {
            metrics.Registration("refused");
            return Results.BadRequest(new { error = passwordError });
        }

        // citext makes these comparisons case-insensitive in the database, so this check
        // and the unique index agree with each other.
        var taken = await db.Accounts.AnyAsync(
            a => a.Username == username || a.Email == email,
            cancellationToken);

        if (taken)
        {
            metrics.Registration("refused");
            return Results.Conflict(new { error = "That username or email is already registered." });
        }

        // The first account on an empty database becomes Admin. Without this there is no way
        // to reach the world builder at all on a fresh install except by hand-editing a row,
        // and a first-run step that requires SQL is a first-run step nobody completes.
        // Safe by construction: it can only ever happen once, when there is nobody to escalate
        // over (PLAN.md Phase 2 - roles).
        var isFirstAccount = !await db.Accounts.AnyAsync(cancellationToken);

        var account = new Account
        {
            Email = email,
            Username = username,
            PasswordHash = string.Empty,
            Role = isFirstAccount ? AccountRole.Admin : AccountRole.Player,
            CreatedAt = clock.GetUtcNow(),

            // The caller as the server sees it - which, behind a proxy, is only the caller once
            // the forwarded headers are honoured (ProxyOptions). Before that it was the proxy.
            RegisteredFromAddress = http.Connection.RemoteIpAddress?.ToString(),
        };

        account.PasswordHash = hasher.HashPassword(account, password);

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        if (isFirstAccount)
        {
            ServerLog.FirstAccountPromoted(
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Muwbta.Server"),
                account.Username);
        }

        metrics.Registration("created");
        await SignInAsync(http, account);
        return Results.Ok(ToResponse(account));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        MuwbtaDbContext db,
        IPasswordHasher<Account> hasher,
        LoginThrottle throttle,
        ServerMetrics metrics,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var username = request.Username ?? string.Empty;

        // Per account, on top of the per-address limit: a guesser with many addresses has many
        // address budgets and, without this, one account to spend them all against. Checked
        // before the lookup so an unknown name is slowed exactly like a known one.
        if (throttle.RetryAfter(username) is { } wait)
        {
            metrics.SignIn(SignInOutcome.Paused);
            return TooManyFailures(http, wait);
        }

        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == username, cancellationToken);

        // Deliberately the same response for "no such user" and "wrong password", so the
        // endpoint cannot be used to enumerate which usernames exist - and, since the timing
        // would otherwise give the game away, the same work too (see Decoy).
        if (account is null)
        {
            _ = hasher.VerifyHashedPassword(Decoy, DecoyHash(hasher), request.Password ?? string.Empty);
            metrics.SignIn(SignInOutcome.UnknownUser);
            if (throttle.RecordFailure(username))
            {
                metrics.SignInPaused();
            }

            return Results.Unauthorized();
        }

        if (hasher.VerifyHashedPassword(account, account.PasswordHash, request.Password ?? string.Empty)
            == PasswordVerificationResult.Failed)
        {
            metrics.SignIn(SignInOutcome.WrongPassword);
            if (throttle.RecordFailure(username))
            {
                metrics.SignInPaused();
            }

            return Results.Unauthorized();
        }

        throttle.RecordSuccess(username);

        if (account.IsBanned)
        {
            metrics.SignIn(SignInOutcome.Banned);
            return Results.Json(
                new { error = "This account is banned.", reason = account.BanReason },
                statusCode: StatusCodes.Status403Forbidden);
        }

        metrics.SignIn(SignInOutcome.Success);
        account.LastLoginAt = clock.GetUtcNow();
        account.LastLoginAddress = http.Connection.RemoteIpAddress?.ToString();
        await db.SaveChangesAsync(cancellationToken);

        await SignInAsync(http, account);
        return Results.Ok(ToResponse(account));
    }

    /// <summary>
    /// Changes the signed-in account's own password (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// The current password is required even though the caller is already authenticated, because
    /// the cookie is <c>SameSite=Lax</c> and long-lived: without it, a borrowed laptop or a stolen
    /// cookie converts into permanent ownership of the account in one request.
    /// </remarks>
    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        MuwbtaDbContext db,
        IPasswordHasher<Account> hasher,
        LoginThrottle throttle,
        GameGateway gateway,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return Results.Unauthorized();
        }

        // The current password is a guess too, made by whoever holds the cookie. It draws on the
        // same fuse as sign-in, so a stolen session cannot be turned into a password by trying.
        if (throttle.RetryAfter(account.Username) is { } wait)
        {
            return TooManyFailures(http, wait);
        }

        if (hasher.VerifyHashedPassword(
                account, account.PasswordHash, request.CurrentPassword ?? string.Empty)
            == PasswordVerificationResult.Failed)
        {
            throttle.RecordFailure(account.Username);
            return Results.BadRequest(new { error = "That is not your current password." });
        }

        throttle.RecordSuccess(account.Username);

        if (!PasswordPolicy.IsAcceptable(request.NewPassword, out var error))
        {
            return Results.BadRequest(new { error });
        }

        account.PasswordHash = hasher.HashPassword(account, request.NewPassword);
        account.PasswordChangedAt = PasswordStamp.At(clock);
        await db.SaveChangesAsync(cancellationToken);

        // Every other cookie for this account is now stale (§7.7), including this caller's - so
        // re-sign them in with the new stamp. Without this the person who just changed their
        // password would be the first one signed out by it.
        await SignInAsync(http, account);

        // An SSE stream was authorised when it opened and does not re-check, so revalidation alone
        // would leave an intruder watching the world - unable to act, since every command is a
        // fresh authenticated POST, but watching. Eviction is by account and therefore catches the
        // legitimate owner's own characters too; they can walk back in, and the case this exists
        // for is "somebody else has my password".
        gateway.TrySubmit(new EvictAccount
        {
            AccountId = account.Id,
            Message = "Your password changed. Sign in again to continue.",
        });

        // Every access token this account holds stopped working a moment ago, for the reason the
        // cookies did: each one snapshotted PasswordChangedAt at issue, and AccessTokenHandler
        // compares it on every request (docs/PAT-AND-MCP.md §7). No sweep runs and none is
        // needed - but it is a surprise if nobody says so, because a token lives in a config file
        // somewhere rather than in a browser that visibly signs out.
        var tokens = await db.AccessTokens
            .CountAsync(t => t.AccountId == account.Id && t.RevokedAt == null && t.ExpiresAt > clock.GetUtcNow(),
                cancellationToken);

        return tokens == 0
            ? Results.NoContent()
            : Results.Ok(new
            {
                message = $"Password changed. {tokens} access token{(tokens == 1 ? string.Empty : "s")} "
                    + "stopped working and will need reissuing.",
            });
    }

    /// <summary>
    /// 429 with a Retry-After, the same shape the address limit answers with, so a client that
    /// already knows how to wait for one knows how to wait for this.
    /// </summary>
    private static IResult TooManyFailures(HttpContext http, TimeSpan wait)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.NumberFormatInfo.InvariantInfo);

        return Results.Json(
            new { error = $"Too many failed sign-ins. Try again in {seconds} seconds." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        HttpContext http,
        MuwbtaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        return account is null ? Results.Unauthorized() : Results.Ok(ToResponse(account));
    }

    private static Task SignInAsync(HttpContext http, Account account)
    {
        // The ticket records which password it was issued against, so a later change can
        // invalidate it (PLAN.md §7.7).
        var properties = new AuthenticationProperties();
        PasswordStamp.Apply(properties, account);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(account.Id, account.Username, account.Role),
            properties);
    }

    /// <summary>
    /// The one place the claim set is defined. Shared with
    /// <see cref="PrincipalRevalidator"/>, which rebuilds it when a role changes mid-session -
    /// two constructions of the same principal would drift the moment a claim was added.
    /// </summary>
    internal static ClaimsPrincipal BuildPrincipal(Guid id, string username, AccountRole role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

    private static AccountResponse ToResponse(Account account) =>
        new(account.Id, account.Username, account.Email, account.Role.ToString());

    [GeneratedRegex("^[A-Za-z0-9_]{3,24}$")]
    private static partial Regex UsernamePattern();
}

public static class HttpContextExtensions
{
    public static bool TryGetAccountId(this HttpContext http, out Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(http);

        accountId = default;
        var claim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out accountId);
    }
}
