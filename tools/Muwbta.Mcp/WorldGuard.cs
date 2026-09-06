using System.Text.Json;
using ModelContextProtocol;

namespace Muwbta.Mcp;

/// <summary>
/// Refuses writes to a world the running game is serving, when asked to (docs/PAT-AND-MCP.md §14).
/// </summary>
/// <remarks>
/// <b>Off unless <c>--protect-active</c> says otherwise</b>, and that default is a correction. It
/// was on, on the theory that an agent editing a live world is a blast radius worth closing - and
/// the first zone drafted through these tools ran straight into it, because the world's own canon
/// says a new zone stays under <c>aldenmoor.</c> and <c>aldenmoor</c> is the world being served.
/// The guard blocked the documented workflow and prevented nothing.
///
/// The deeper reason it was wrong: live editing with no publish gate is a decision this codebase
/// already made deliberately (PLAN.md §7.4, and the comment on the exit endpoint that says a
/// destination is allowed not to exist yet for exactly that reason). A builder deletes the live
/// starting room from the UI with two clicks, and this holds a token for an account with that same
/// role. A guard here was a publish gate invented for agents alone, second-guessing a settled
/// decision.
///
/// It survives as an opt-in because one case is real: an agent pointed at a server people are
/// playing on, where the cost of a mistake is not a re-import but an interrupted evening. That is
/// the operator's call, made at the command line, and not the agent's.
///
/// <b>What this does not cover, and cannot.</b> Mobs, items, quests and abilities have no world:
/// they are global, and one of them may be placed in the live world by a spawner that this has no
/// way to see from the key alone. So the guard covers worlds, zones and rooms - the things that
/// carry a world in their key - and a template edit is guarded only by the author's own care and
/// by <c>where_used</c>, which is what it is for. That hole is stated in the tool descriptions and
/// in the README rather than papered over, because a guard nobody knows the shape of is worse than
/// one they do.
/// </remarks>
public sealed class WorldGuard(BuilderClient client, BuilderOptions options)
{
    /// <summary>
    /// How long the active set is trusted before being asked for again.
    /// </summary>
    /// <remarks>
    /// Not forever, because a configuration can be activated from the Setup tab in the middle of a
    /// session and a process that cached the answer at startup would happily write into a world
    /// that had since gone live. Not per-write either: that would be a round trip on every edit to
    /// answer a question whose answer changes about once a month.
    /// </remarks>
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim gate = new(1, 1);
    private HashSet<string> active = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset fetched = DateTimeOffset.MinValue;

    /// <summary>
    /// Throws if this write would land in an active world.
    /// </summary>
    /// <param name="kind">The content kind, which decides whether a world can be read off the key.</param>
    /// <param name="keys">Every key the write touches - its own, and any it names in its body.</param>
    public async Task EnsureWritableAsync(
        string kind,
        IEnumerable<string?> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (!options.ProtectActive)
        {
            return;
        }

        var worlds = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => ContentKinds.WorldOfKey(kind, k!))
            .Where(w => w is not null)
            .Select(w => w!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (worlds.Count == 0)
        {
            return;
        }

        var live = await ActiveWorldsAsync(cancellationToken).ConfigureAwait(false);
        var blocked = worlds.Where(live.Contains).ToList();

        if (blocked.Count == 0)
        {
            return;
        }

        throw new McpException(
            $"'{string.Join("', '", blocked)}' belongs to the configuration this server is running, "
            + "and this server was started with --protect-active. Author in a world that is not "
            + "live and let a person activate it from Setup when it is ready. Whether an agent may "
            + "edit the live world is settled at the command line by whoever runs this, so it is "
            + "not a decision you can make from here.");
    }

    /// <summary>The worlds the active configuration claims, refreshed when stale.</summary>
    private async Task<HashSet<string>> ActiveWorldsAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (DateTimeOffset.UtcNow - fetched < Freshness)
            {
                return active;
            }

            var json = await client
                .GetAsync("/api/builder/configurations", cancellationToken)
                .ConfigureAwait(false);

            active = Parse(json);
            fetched = DateTimeOffset.UtcNow;

            return active;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The <c>worldKeys</c> of whichever configuration is active, or nothing when none is.
    /// </summary>
    /// <remarks>
    /// No active configuration means the server is running on its configured fallback, and there
    /// is no set of worlds to protect - which is the usual state of a development machine, and the
    /// reason this guard is quiet rather than obstructive there.
    /// </remarks>
    internal static HashSet<string> Parse(string configurationsJson)
    {
        var worlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(configurationsJson);

        if (!document.RootElement.TryGetProperty("configurations", out var rows))
        {
            return worlds;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("isActive", out var isActive) || isActive.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            if (row.TryGetProperty("worldKeys", out var keys) && keys.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keys.EnumerateArray())
                {
                    if (key.GetString() is { Length: > 0 } value)
                    {
                        worlds.Add(value);
                    }
                }
            }
        }

        return worlds;
    }
}
