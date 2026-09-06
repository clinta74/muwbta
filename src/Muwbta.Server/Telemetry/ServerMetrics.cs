using System.Diagnostics.Metrics;

namespace Muwbta.Server.Telemetry;

/// <summary>
/// What the server measures about the people trying to get in and the things that refuse them.
/// </summary>
/// <remarks>
/// <b>The gap this closes.</b> The hardening work added a per-account sign-in backoff, made the
/// per-address rate limit real, and reserved staff names — and every one of those now
/// <em>refuses</em> things silently. A credential-stuffing run against one account, a scripted
/// flood against sign-in, a burst of new registrations from one place: each is a line in a log
/// nobody is reading at the time, and a log has no shape. These counters give the refusals a
/// shape the dashboard can draw and, later, alert on.
///
/// <b>Same line as <see cref="Muwbta.Engine.Telemetry.EngineMetrics"/>.</b> Instruments only,
/// from the base library; where they go is decided in <c>MetricsExport</c>. A counter with no
/// listener costs a handful of nanoseconds, so nothing here is conditional on being scraped.
///
/// <b>Counters, tagged, never per-account.</b> A sign-in is counted by <em>outcome</em>, not by
/// who attempted it: a series per username would grow without bound under the very attack this
/// exists to reveal, and the metrics endpoint is not the place a username belongs. The account
/// under attack is a question for the admin panel, which shows the pause; this answers "is it
/// happening at all, and how much".
/// </remarks>
public sealed class ServerMetrics : IDisposable
{
    /// <summary>The name an exporter subscribes to. Stable: renaming it breaks every dashboard.</summary>
    public const string MeterName = "Muwbta.Server";

    private readonly Meter _meter;
    private readonly Counter<long> _signIns;
    private readonly Counter<long> _signInPauses;
    private readonly Counter<long> _registrations;
    private readonly Counter<long> _rateLimitRejections;
    private readonly Counter<long> _moderationActions;
    private readonly Counter<long> _saveFailures;
    private readonly Counter<long> _tokenAuth;

    public ServerMetrics(IMeterFactory? factory = null)
    {
        _meter = factory?.Create(MeterName) ?? new Meter(MeterName);

        _signIns = _meter.CreateCounter<long>(
            "muwbta.signins",
            description: "Sign-in attempts, by outcome: success, wrong_password, unknown_user, paused, banned.");

        // Its own counter rather than a slice of the one above: "the fuse lit" is a rarer and
        // more interesting event than "an attempt was refused", and a panel that has to divide
        // one by the other is a panel that gets drawn wrong.
        _signInPauses = _meter.CreateCounter<long>(
            "muwbta.signin.pauses",
            description: "Times an account's sign-in was paused for too many wrong passwords.");

        _registrations = _meter.CreateCounter<long>(
            "muwbta.registrations",
            description: "Registration attempts, by outcome: created or refused.");

        _rateLimitRejections = _meter.CreateCounter<long>(
            "muwbta.ratelimit.rejections",
            description: "Requests refused with 429 by a rate-limit policy, by policy.");

        _moderationActions = _meter.CreateCounter<long>(
            "muwbta.moderation.actions",
            description: "Moderation actions taken, by action - the audit table as a rate.");

        // Counted as well as logged, for the reason the slow pulses are: the log says which batch
        // failed, the counter says how often, and "how often" is the question that decides
        // whether a database is going wrong.
        _saveFailures = _meter.CreateCounter<long>(
            "muwbta.saves.failed",
            description: "Character save batches that failed to reach the database.");

        // A personal access token is a credential nobody is watching: no browser, no person to
        // notice a refusal. A rise in `unknown` is somebody trying tokens at the door, and it is
        // the only place that would show.
        _tokenAuth = _meter.CreateCounter<long>(
            "muwbta.token.auth",
            description:
                "Access token authentication attempts, by outcome: accepted, malformed, unknown, "
                + "expired, revoked, banned, stale_password, scope_refused.");
    }

    public void SignIn(string outcome) =>
        _signIns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void SignInPaused() => _signInPauses.Add(1);

    public void Registration(string outcome) =>
        _registrations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RateLimited(string policy) =>
        _rateLimitRejections.Add(1, new KeyValuePair<string, object?>("policy", policy));

    public void Moderation(string action) =>
        _moderationActions.Add(1, new KeyValuePair<string, object?>("action", action));

    public void SaveFailed(int characters) => _saveFailures.Add(1);

    public void TokenAuth(string outcome) =>
        _tokenAuth.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}

/// <summary>The outcome tags, spelled once so the dashboard's queries and the code agree.</summary>
public static class SignInOutcome
{
    public const string Success = "success";
    public const string WrongPassword = "wrong_password";
    public const string UnknownUser = "unknown_user";
    public const string Paused = "paused";
    public const string Banned = "banned";
}

/// <summary>
/// The outcomes of presenting an access token, spelled once (docs/PAT-AND-MCP.md §8).
/// </summary>
public static class TokenAuthOutcome
{
    public const string Accepted = "accepted";

    /// <summary>Not a token at all, and refused before any lookup.</summary>
    public const string Malformed = "malformed";

    /// <summary>Well-formed and matches no row, or the secret is wrong. The interesting one.</summary>
    public const string Unknown = "unknown";

    public const string Expired = "expired";
    public const string Revoked = "revoked";

    /// <summary>The account is gone or banned since the token was issued.</summary>
    public const string Banned = "banned";

    /// <summary>Issued against a password that has since changed.</summary>
    public const string StalePassword = "stale_password";

    /// <summary>A real token, pointed somewhere its scope does not reach.</summary>
    public const string ScopeRefused = "scope_refused";
}
