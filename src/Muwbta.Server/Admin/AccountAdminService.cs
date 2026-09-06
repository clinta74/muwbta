using Muwbta.Domain.Accounts;
using Muwbta.Server.Telemetry;
using Muwbta.Persistence;
using Muwbta.Server.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Admin;

public enum RoleChangeOutcome
{
    Changed = 0,

    /// <summary>Already had that role. Success, but worth saying so rather than implying work.</summary>
    Unchanged = 1,

    NoSuchAccount = 2,

    /// <summary>
    /// An admin tried to reduce their own role. Refused: there is no recovery from an
    /// installation with zero admins except the SQL §7.7 exists to eliminate.
    /// </summary>
    WouldDemoteSelf = 3,
}

public sealed record RoleChangeResult(
    RoleChangeOutcome Outcome,
    string Message,
    Guid? TargetAccountId = null,
    AccountRole? Previous = null,
    AccountRole? Current = null)
{
    public bool Ok => Outcome is RoleChangeOutcome.Changed or RoleChangeOutcome.Unchanged;
}

/// <summary>
/// Why a moderation action did not happen, at the granularity HTTP cares about.
/// </summary>
/// <remarks>
/// Exists because the in-game commands and the admin API want different things from a failure:
/// a command only ever prints <see cref="ModerationResult.Message"/>, while an endpoint has to
/// choose a status code, and "no such account" and "already banned" are not the same answer.
/// Kept as a small enum rather than a status code so the service stays free of HTTP.
/// </remarks>
public enum ModerationFailure
{
    /// <summary>The action succeeded, or failed in a way no caller distinguishes.</summary>
    None = 0,

    /// <summary>No account or character by that name — a 404.</summary>
    NoSuchTarget = 1,

    /// <summary>The request itself was malformed, e.g. a password that fails policy — a 400.</summary>
    Invalid = 2,

    /// <summary>Well-formed and understood, but refused: self-targeting, or already in that state.</summary>
    Refused = 3,
}

/// <summary>
/// The outcome of a moderation action (PLAN.md §8, Phase 6).
/// </summary>
/// <param name="TargetAccountId">
/// Set only on success, and only so the worker can reach a session already open — a ban that
/// leaves the banned player connected is not a ban.
/// </param>
public sealed record ModerationResult(
    bool Ok,
    string Message,
    Guid? TargetAccountId = null,
    DateTimeOffset? MutedUntil = null)
{
    public ModerationFailure Failure { get; init; } = ModerationFailure.None;

    public static ModerationResult NoSuchAccount(string username) =>
        new(false, $"There is no account named '{username}'.")
        {
            Failure = ModerationFailure.NoSuchTarget,
        };

    public static ModerationResult NoSuchCharacter(string name) =>
        new(false, $"There is no character named '{name}'.")
        {
            Failure = ModerationFailure.NoSuchTarget,
        };

    public static ModerationResult Invalid(string message) =>
        new(false, message) { Failure = ModerationFailure.Invalid };

    public static ModerationResult Refused(string message) =>
        new(false, message) { Failure = ModerationFailure.Refused };
}

public sealed record AccountSummary(
    Guid Id,
    string Username,
    string Email,
    string Role,
    bool IsBanned,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Characters)
{
    /// <summary>Why they were banned, when they are. Null otherwise.</summary>
    public string? BanReason { get; init; }

    /// <summary>
    /// Present whether or not it has expired, because the admin panel decides that against its
    /// own clock — a value in the past is how "not muted any more" looks, and hiding it here
    /// would leave the panel unable to tell that from "never muted".
    /// </summary>
    public DateTimeOffset? MutedUntil { get; init; }

    /// <summary>
    /// When the sign-in backoff against this account ends, or null when there is none. Unlike
    /// the mute this is only ever a future time: the throttle forgets an expired pause, so
    /// there is no "used to be paused" to report.
    /// </summary>
    public DateTimeOffset? LoginLockedUntil { get; init; }

    /// <summary>Where the account was created from, and where it last signed in from. See <see cref="Account"/>.</summary>
    public string? RegisteredFromAddress { get; init; }

    public string? LastLoginAddress { get; init; }
}

/// <summary>
/// Account administration: roles (PLAN.md §7.7), moderation (§8), and passwords (§7.8). Shared by
/// the HTTP endpoints and by the worker that drains in-game <c>promote</c> commands, so both paths
/// enforce the same rules and write the same audit row - two copies of "who may do what" is how
/// they end up disagreeing.
/// </summary>
public sealed class AccountAdminService(
    MuwbtaDbContext db,
    TimeProvider clock,
    IPasswordHasher<Account> hasher,
    LoginThrottle throttle,
    ServerMetrics metrics)
{
    /// <summary>
    /// The one place an audit row is written, so the counter beside it cannot be forgotten at
    /// the next action somebody adds. The table is the record; the counter is the same record
    /// as a rate, for the dashboard.
    /// </summary>
    private void Audit(AdminAudit row)
    {
        db.AdminAudits.Add(row);
        metrics.Moderation(row.Action.ToString());
    }

    /// <summary>
    /// The access tokens an account holds, for an administrator (docs/PAT-AND-MCP.md §7).
    /// </summary>
    /// <remarks>
    /// Names and dates, never a secret - there is no secret stored to leak, which is the point of
    /// §3. "Who holds a standing credential for the builder API" is an administrative question of
    /// the same kind as "who is a builder", and it is one nobody can answer from the account row.
    /// </remarks>
    public async Task<IReadOnlyList<AccessTokenSummary>?> TokensForAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username == username, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        var rows = await db.AccessTokens.AsNoTracking()
            .Where(t => t.AccountId == account.Id && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(t => AccessTokenEndpoints.Summarise(t, now))];
    }

    /// <summary>
    /// Revokes somebody else's token.
    /// </summary>
    /// <remarks>
    /// The audit row records the admin as actor and the token's owner as target, which is the
    /// difference between this and the owner revoking their own - and the whole reason the two
    /// paths do not share one method.
    /// </remarks>
    public async Task<ModerationResult> RevokeTokenAsync(
        Guid actorAccountId,
        Guid tokenId,
        CancellationToken cancellationToken)
    {
        var row = await db.AccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId, cancellationToken);

        if (row is null)
        {
            return new ModerationResult(false, "There is no token with that id.")
            {
                Failure = ModerationFailure.NoSuchTarget,
            };
        }

        if (row.RevokedAt is not null)
        {
            return ModerationResult.Refused("That token was already revoked.");
        }

        var now = clock.GetUtcNow();
        row.RevokedAt = now;

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = row.AccountId,
            Action = AdminAction.TokenRevoked,
            Before = AccessTokenSecret.Describe(row.Name, row.Scope, row.ExpiresAt),
            After = null,
            Reason = null,
            At = now,
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(true, $"Revoked '{row.Name}'.", row.AccountId);
    }

    /// <summary>
    /// Clears the sign-in backoff against an account (see <see cref="LoginThrottle"/>).
    /// </summary>
    /// <remarks>
    /// This is what the cap on the backoff is for. Whoever hammers an account can make its real
    /// owner wait, and without a way to lift the wait that is a way to keep them out for as long
    /// as the hammering lasts. Refused rather than a silent no-op when there is nothing to lift,
    /// so the panel says so instead of pretending to have done something.
    /// </remarks>
    public async Task<ModerationResult> UnlockLoginAsync(
        Guid actorAccountId,
        string targetUsername,
        CancellationToken cancellationToken)
    {
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return ModerationResult.NoSuchAccount(targetUsername);
        }

        if (!throttle.Lift(target.Username))
        {
            return ModerationResult.Refused($"{target.Username} is not waiting out a sign-in pause.");
        }

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = AdminAction.LoginUnlocked,
            Before = "paused",
            After = "clear",
            Reason = null,
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(true, $"{target.Username} may sign in again.", target.Id);
    }

    public async Task<RoleChangeResult> SetRoleAsync(
        Guid actorAccountId,
        string targetUsername,
        AccountRole role,
        CancellationToken cancellationToken)
    {
        // citext, so this matches however they typed it.
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return new RoleChangeResult(
                RoleChangeOutcome.NoSuchAccount, $"There is no account named '{targetUsername}'.");
        }

        if (target.Id == actorAccountId && role != AccountRole.Admin)
        {
            return new RoleChangeResult(
                RoleChangeOutcome.WouldDemoteSelf,
                "You cannot reduce your own role. Have another admin do it.");
        }

        var previous = target.Role;

        if (previous == role)
        {
            return new RoleChangeResult(
                RoleChangeOutcome.Unchanged,
                $"{target.Username} is already {role}.",
                target.Id,
                previous,
                role);
        }

        target.Role = role;

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = AdminAction.RoleChanged,
            Before = previous.ToString(),
            After = role.ToString(),
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new RoleChangeResult(
            RoleChangeOutcome.Changed,
            $"{target.Username} is now {role}.",
            target.Id,
            previous,
            role);
    }

    /// <summary>
    /// Bans or lifts a ban (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// One method for both directions, because they are the same edit to the same column with the
    /// same audit row — a separate unban path is how one of the two ends up not writing one.
    ///
    /// Self-banning is refused for the reason self-demotion is: there is no recovery from it
    /// except the SQL §7.7 exists to eliminate. Banning <em>another</em> admin is permitted, since
    /// the alternative is an account nobody can act on.
    /// </remarks>
    public async Task<ModerationResult> SetBanAsync(
        Guid actorAccountId,
        string targetUsername,
        bool banned,
        string? reason,
        CancellationToken cancellationToken)
    {
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return ModerationResult.NoSuchAccount(targetUsername);
        }

        if (target.Id == actorAccountId)
        {
            return ModerationResult.Refused("You cannot ban yourself.");
        }

        if (target.IsBanned == banned)
        {
            return ModerationResult.Refused(
                $"{target.Username} is already {(banned ? "banned" : "not banned")}.");
        }

        target.IsBanned = banned;
        target.BanReason = banned ? reason : null;

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = banned ? AdminAction.Banned : AdminAction.Unbanned,
            Before = banned ? "active" : "banned",
            After = banned ? "banned" : "active",
            Reason = reason,
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            banned
                ? $"{target.Username} is banned.{(reason is null ? string.Empty : $" ({reason})")}"
                : $"{target.Username} may sign in again.",
            target.Id);
    }

    /// <summary>
    /// Silences an account until a stated time, or lifts the silence when <paramref name="until"/>
    /// is null (PLAN.md §8, Phase 6).
    /// </summary>
    public async Task<ModerationResult> SetMuteAsync(
        Guid actorAccountId,
        string targetUsername,
        DateTimeOffset? until,
        string? reason,
        CancellationToken cancellationToken)
    {
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return ModerationResult.NoSuchAccount(targetUsername);
        }

        if (target.Id == actorAccountId)
        {
            return ModerationResult.Refused("You cannot mute yourself.");
        }

        var before = target.MutedUntil;

        if (until is null && (before is null || before <= clock.GetUtcNow()))
        {
            return ModerationResult.Refused($"{target.Username} is not muted.");
        }

        target.MutedUntil = until;

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = AdminAction.MuteChanged,
            Before = before?.ToString("u") ?? "not muted",
            After = until?.ToString("u") ?? "not muted",
            Reason = reason,
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            until is null
                ? $"{target.Username} may speak again."
                : $"{target.Username} is muted until {until:u}.{(reason is null ? string.Empty : $" ({reason})")}",
            target.Id,
            until);
    }

    /// <summary>
    /// Retires a character by name (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// <b>A soft delete.</b> The row keeps its <c>DeletedAt</c> and everything hanging off it —
    /// items, quest progress, the audit trail — stays referentially intact. Hard deletion would
    /// cascade through all of it, and "we deleted the wrong Kael" is not a recoverable sentence.
    /// The character list already filters on <c>DeletedAt == null</c>, so it disappears from the
    /// account it belonged to without anything else having to learn about deletion.
    ///
    /// <b>It does not remove them from the world.</b> That is the worker's job, because only the
    /// loop may touch a session (§2.1) — and doing it here would be exactly the race that rule
    /// exists to forbid. The name is freed either way: names are unique across live characters,
    /// and this one is no longer live.
    /// </remarks>
    public async Task<ModerationResult> DeleteCharacterAsync(
        Guid actorAccountId,
        string characterName,
        CancellationToken cancellationToken)
    {
        var character = await db.Characters.FirstOrDefaultAsync(
            c => c.Name == characterName && c.DeletedAt == null, cancellationToken);

        if (character is null)
        {
            return ModerationResult.NoSuchCharacter(characterName);
        }

        character.DeletedAt = clock.GetUtcNow();

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = character.AccountId,
            Action = AdminAction.CharacterDeleted,

            // The name and level, because after this there is nothing left to look them up from.
            Before = $"{character.Name} (level {character.Level})",
            After = "deleted",
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            $"{character.Name} has been deleted.",
            character.AccountId);
    }

    /// <summary>
    /// Sets someone else's password, for a player who cannot sign in to change it themselves.
    /// </summary>
    /// <remarks>
    /// <b>Why an admin does this at all.</b> There is no email sender in this deployment, so there
    /// is no self-service recovery — an admin setting the password and telling the player out of
    /// band is the whole of the recovery story. The alternative was hand-written SQL, which is the
    /// thing §7.7 exists to eliminate.
    ///
    /// <b>Self-reset is refused</b>, in the same spirit as self-demotion and self-banning, though
    /// for a softer reason: an admin who is signed in can already change their own password on the
    /// account screen, and that path proves they know the current one. Routing themselves through
    /// here would only skip that proof — and would sign them out of their own session a moment
    /// later, via <see cref="Account.PasswordChangedAt"/>.
    ///
    /// <b>The audit row records the act, never the password</b> — not the new one, not its hash,
    /// not its length.
    /// </remarks>
    public async Task<ModerationResult> SetPasswordAsync(
        Guid actorAccountId,
        string targetUsername,
        string? newPassword,
        CancellationToken cancellationToken)
    {
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return ModerationResult.NoSuchAccount(targetUsername);
        }

        if (target.Id == actorAccountId)
        {
            return ModerationResult.Refused(
                "Change your own password from the account screen, where the current one is required.");
        }

        if (!PasswordPolicy.IsAcceptable(newPassword, out var error))
        {
            return ModerationResult.Invalid(error);
        }

        // Truncated to what the column can hold, so the stamp in a freshly issued cookie and the
        // stamp read back out of the database are the same instant (see PasswordStamp).
        var now = PasswordStamp.At(clock);

        target.PasswordHash = hasher.HashPassword(target, newPassword!);
        target.PasswordChangedAt = now;

        Audit(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = AdminAction.PasswordReset,
            Before = "set by administrator",
            After = "set by administrator",
            At = now,
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            $"{target.Username}'s password has been set. Their other sessions are signed out.",
            target.Id);
    }

    public async Task<AccountSummary?> FindAsync(string username, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username == username, cancellationToken);

        return account is null ? null : await SummariseAsync(account, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountSummary>> SearchAsync(
        string? query,
        string? address,
        int limit,
        CancellationToken cancellationToken)
    {
        var accounts = db.Accounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            // citext makes Contains case-insensitive without a lower() wrapper, so the index
            // stays usable and the behaviour matches how usernames compare everywhere else.
            accounts = accounts.Where(a => a.Username.Contains(query));
        }

        // "Accounts from this address" - the question a ban raises next. Exact match on either
        // column: an address is not a name, and a prefix search would make 10.0.0.1 find 10.0.0.10.
        if (!string.IsNullOrWhiteSpace(address))
        {
            accounts = accounts.Where(a =>
                a.RegisteredFromAddress == address || a.LastLoginAddress == address);
        }

        var rows = await accounts
            .OrderBy(a => a.Username)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        var summaries = new List<AccountSummary>(rows.Count);
        foreach (var account in rows)
        {
            summaries.Add(await SummariseAsync(account, cancellationToken));
        }

        return summaries;
    }

    private async Task<AccountSummary> SummariseAsync(Account account, CancellationToken cancellationToken)
    {
        var characters = await db.Characters.AsNoTracking()
            .Where(c => c.AccountId == account.Id && c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        return new AccountSummary(
            account.Id,
            account.Username,
            account.Email,
            account.Role.ToString(),
            account.IsBanned,
            account.CreatedAt,
            account.LastLoginAt,
            characters)
        {
            BanReason = account.BanReason,
            MutedUntil = account.MutedUntil,
            LoginLockedUntil = throttle.LockedUntil(account.Username),
            RegisteredFromAddress = account.RegisteredFromAddress,
            LastLoginAddress = account.LastLoginAddress,
        };
    }
}
