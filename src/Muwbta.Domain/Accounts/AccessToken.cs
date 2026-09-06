namespace Muwbta.Domain.Accounts;

/// <summary>
/// A non-interactive credential for the builder API (docs/PAT-AND-MCP.md, Part 1).
/// </summary>
/// <remarks>
/// The session cookie cannot serve a headless process: it is <c>HttpOnly</c>, <c>SameSite=Lax</c>,
/// and minting one takes a password POST. An agent authoring through MCP needs something it can
/// put in a header, and the alternative to this is a program holding a builder's password.
///
/// <b>It is deliberately able to do less than the person holding it.</b> A token that simply
/// carried its account's role would be a full account takeover when it leaked - it could change
/// the password, and for an admin it could ban people. So it carries a <see cref="Scope"/> that
/// admits the builder surface and nothing else, and an expiry that is not optional: a credential
/// with no end date is one nobody ever revokes, because nothing ever reminds them it exists.
///
/// Nothing here is a role. The role is read from the account row on every request, so a demotion
/// bites a token exactly as it bites a cookie.
/// </remarks>
public sealed class AccessToken
{
    /// <summary>
    /// The public half of the token, and the primary key.
    /// </summary>
    /// <remarks>
    /// Carried in the token string itself so verification is an indexed lookup. Without it,
    /// checking a token means scanning every row and hashing against each one - the design that
    /// quietly stops working somewhere in the low hundreds and is nobody's idea of a security
    /// property.
    /// </remarks>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid AccountId { get; init; }

    /// <summary>What the builder called it, so the revoke list is readable.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Base64 SHA-256 of the secret half. Never the secret, which is shown once and not stored.
    /// </summary>
    /// <remarks>
    /// SHA-256 and <b>not</b> the PBKDF2 that <see cref="Account.PasswordHash"/> uses, which is a
    /// difference somebody will eventually try to correct. The secret is 256 bits of CSPRNG
    /// output: no dictionary, no reuse across sites, no structure to guess. Stretching buys
    /// nothing against that, and it would be paid on every request rather than once per sign-in.
    /// </remarks>
    public required string SecretHash { get; set; }

    public required AccessTokenScope Scope { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it stops working. Not nullable, and capped at issue.</summary>
    public required DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Last time it authenticated anything, so a token nobody uses is visible as one to revoke.
    /// </summary>
    /// <remarks>
    /// Written on use, which is a database round trip on a path that already takes one. Coarse on
    /// purpose - see <c>AccessTokenHandler</c>, which only writes it when the stored value is
    /// older than an hour, because the question this answers is "is this still in use" and not
    /// "what was the last minute it was used".
    /// </remarks>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Set when revoked. A revoked token is kept, so the audit trail still resolves.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// The account's <see cref="Account.PasswordChangedAt"/> when this was issued.
    /// </summary>
    /// <remarks>
    /// The same mechanism <c>PasswordStamp</c> gives the auth cookie, for the same reason: a
    /// password change exists for the case where somebody else knows the old one, and a change
    /// that left standing tokens working would not fix it. This makes revoke-all fall out of a
    /// password change with no sweep to write and none to forget.
    /// </remarks>
    public DateTimeOffset? PasswordChangedAt { get; init; }

    public Account? Account { get; init; }

    /// <summary>Whether this token is usable at <paramref name="now"/>, ignoring the account.</summary>
    public bool IsLive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>
/// What a token is allowed to reach. Narrower than a role, always.
/// </summary>
/// <remarks>
/// There is deliberately no admin scope and no scope that can play the game. Authoring needs
/// <see cref="AccountRole.Builder"/>; <c>/api/admin</c> is bans, role changes, and account
/// deletion, which is a much worse thing to hand a program for no authoring benefit. A token that
/// could play would be a bot API, which is a different decision and should not arrive as a side
/// effect of this one. Adding either later is a new decision, not an extension of this enum.
/// </remarks>
public enum AccessTokenScope
{
    /// <summary>GETs under <c>/api/builder</c>. Nothing else.</summary>
    BuilderRead = 0,

    /// <summary>Reads and writes under <c>/api/builder</c>. Not admin, not auth, not the game.</summary>
    BuilderWrite = 1,
}
