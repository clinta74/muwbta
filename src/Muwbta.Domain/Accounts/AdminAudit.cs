namespace Muwbta.Domain.Accounts;

/// <summary>
/// One row per administrative action taken against an account (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// Deliberately not <c>content_audit</c>. An account is not content, and merging the two makes
/// both questions harder to answer: "who edited this room" would have to filter out promotions,
/// and "who made this person a builder" would have to filter out every room save ever made.
///
/// Phase 6's mute, kick, and ban write here too.
/// </remarks>
public sealed class AdminAudit
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Who did it. Null only for actions taken by the system rather than a person.</summary>
    public Guid? ActorAccountId { get; init; }

    public required Guid TargetAccountId { get; init; }

    public required AdminAction Action { get; init; }

    /// <summary>Prior value, as plain text. A role name today; a mute duration later.</summary>
    public string? Before { get; init; }

    public string? After { get; init; }

    /// <summary>Optional free text, for the moderation actions that will want one.</summary>
    public string? Reason { get; init; }

    public required DateTimeOffset At { get; init; }
}

public enum AdminAction
{
    RoleChanged = 0,

    /// <summary>Sign-in refused and any open session evicted (PLAN.md §8, Phase 6).</summary>
    Banned = 1,

    /// <summary>The ban lifted. Recorded, because "who let them back in" is also a question.</summary>
    Unbanned = 2,

    /// <summary>
    /// Silenced on the player-to-player channels until a stated time.
    /// </summary>
    /// <remarks>
    /// Both directions land here rather than getting a <c>Muted</c>/<c>Unmuted</c> pair, because
    /// a mute is a value with an expiry rather than a state to toggle: lifting one is setting that
    /// value to nothing, and <see cref="AdminAudit.Before"/> and <see cref="AdminAudit.After"/>
    /// already say which way it went.
    /// </remarks>
    MuteChanged = 3,

    /// <summary>
    /// A character retired by an administrator (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// Audited like every other administrative act, and for a sharper reason than most: this is
    /// the only one that takes something away from a player permanently, so "who deleted Kael"
    /// has to be answerable. <see cref="AdminAudit.Before"/> carries the character's name and
    /// level, because after the fact there is nothing left to look it up from.
    /// </remarks>
    CharacterDeleted = 4,

    /// <summary>
    /// An administrator set someone else's password (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// There is no email sender in this deployment, so a locked-out player has no self-service
    /// route back in — an admin setting the password is the only one. That makes this the single
    /// administrative act that hands over an account outright, so it is audited hardest:
    /// <see cref="AdminAudit.Before"/> and <see cref="AdminAudit.After"/> carry no password
    /// material of any kind, only the fact and the hour.
    /// </remarks>
    PasswordReset = 5,

    /// <summary>
    /// The sign-in backoff cleared by an admin, so a player somebody was hammering can get back
    /// in without waiting out the ceiling. Recorded because "who let them in" is a question here
    /// as much as for a ban.
    /// </summary>
    LoginUnlocked = 6,

    /// <summary>
    /// A personal access token minted (docs/PAT-AND-MCP.md §8).
    /// </summary>
    /// <remarks>
    /// Self-service, so actor and target are usually the same account - which is not a reason to
    /// leave it out. "Who holds a standing credential for the builder API" is an administrative
    /// question of the same kind as "who is a builder", and the answer has to survive the token
    /// being revoked. <see cref="AdminAudit.After"/> carries the token's name and scope, and
    /// never any part of the secret.
    /// </remarks>
    TokenIssued = 7,

    /// <summary>
    /// A token revoked, by its owner or by an administrator. <see cref="AdminAudit.ActorAccountId"/>
    /// says which.
    /// </summary>
    TokenRevoked = 8,
}
