using System.Diagnostics;
using System.Threading.Channels;
using Muwbta.Engine;
using Microsoft.Extensions.Options;

namespace Muwbta.Server.Assist;

/// <summary>
/// Builds the canon's KV cache once, at startup, so no builder ever pays for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured on the deployment, and it is the difference between the feature working and not.</b>
/// The NAS bulk-prefills at about 6 tokens a second. The canon is 10,571 tokens. Cold, that is
/// roughly half an hour before a single word is generated — the first real attempt timed out at
/// ten minutes, and it took several more presses before enough of the prefix had accumulated in
/// llama.cpp's slot for a request to run to completion. Warm, the same machine drafts a room in
/// about three minutes, of which only thirty seconds is prefill of the part that varies.
/// </para>
/// <para>
/// <b>The two prefill rates are not the same number, and the difference is instructive.</b> Bulk
/// prefill of the whole canon runs at about 6 tok/s; the 115-token incremental prefill of what
/// changes per request manages 3.96. A long prompt amortises batch work that a short one cannot,
/// so the per-request tail is proportionally the more expensive of the two.
/// </para>
/// <para>
/// <b>Nothing is generated.</b> <c>num_predict: 1</c> asks for a single token, because the point is
/// the prefill and generation is the slow half — 0.93 tokens a second there. What this leaves
/// behind is the cache; the token is thrown away.
/// </para>
/// <para>
/// <b>It runs on its own thread with no request behind it</b>, which is the whole idea: a cost
/// nobody is waiting on is a cost that can take half an hour. <see cref="Ready"/> is how the
/// worker knows to hold a job until the model can actually answer it, rather than starting a
/// per-job timeout that the queue in front of it will eat.
/// </para>
/// </remarks>
public sealed class AssistWarmUp(
    IHttpClientFactory clients,
    IOptions<AssistOptions> options,
    EngineOptions engine,
    ILogger<AssistWarmUp> logger) : BackgroundService
{
    /// <summary>
    /// A named client rather than a typed one.
    /// </summary>
    /// <remarks>
    /// <c>AddHttpClient&lt;T&gt;</c> registers T as transient, and this has to be a singleton so the
    /// worker can ask it whether the model is ready. Registering both ways makes the singleton
    /// factory resolve itself - which compiles perfectly and stack-overflows on the first request.
    /// A named client keeps the long timeout this needs without that argument.
    /// </remarks>
    public const string ClientName = "assist-warmup";

    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// A request to prefill again, coalesced to one outstanding.
    /// </summary>
    /// <remarks>
    /// Bounded at one and dropping writes, because ten edits in a minute are one re-warm: what
    /// matters is that the canon in front of the model is the current one by the time anybody
    /// drafts, not that every intermediate version was cached on its way past.
    /// </remarks>
    private readonly Channel<byte> _refresh = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>
    /// The canon last sent to the model, or null when none has been. Compared against the live one
    /// to decide whether a re-warm has anything to do.
    /// </summary>
    /// <remarks>
    /// Volatile because it is written on the background loop and read on request threads. A stale
    /// read costs one unnecessary prefill or one missed one, and the next edit corrects it - the
    /// alternative is a lock around a string comparison on a path that runs when a builder saves a
    /// form.
    /// </remarks>
    private volatile string? _prefilled;

    /// <summary>
    /// Completes when the model has the canon cached, or when warming is given up on.
    /// </summary>
    /// <remarks>
    /// Completes rather than faults on failure, deliberately. A warm-up that could not run is a
    /// reason for the first draft to be slow, not a reason to refuse to draft: the model may have
    /// been starting up, and the request behind this will simply pay the prefill itself.
    /// </remarks>
    public Task Ready => _ready.Task;

    /// <summary>Whether the canon is cached. Reported to the builder so it can say why it waits.</summary>
    public bool IsWarm { get; private set; }

    /// <summary>
    /// Says the live canon may have moved, and re-warms in the background if it actually has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The prompt's leading substring is the cache key.</b> Ollama reuses the KV cache only for
    /// a prompt sharing a prefix with the last one, and the canon is that prefix - so changing one
    /// character of it costs the next request a full re-evaluation of every token. Measured on the
    /// Reaches canon, that is seconds against minutes. Doing it here means the server pays it
    /// instead of whoever presses Suggest first.
    /// </para>
    /// <para>
    /// <b>Only when it has genuinely changed.</b> Saving a configuration is one call whatever the
    /// builder touched, so most saves reach here having changed a welcome message or a starting
    /// room; re-warming on those would throw away a good cache and stall the model for minutes to
    /// arrive at the text it already had. The comparison is against what was last <em>sent</em>,
    /// not against what is stored, because those differ until a prefill finishes.
    /// </para>
    /// </remarks>
    /// <returns>True when a re-warm was queued.</returns>
    public bool CanonChanged()
    {
        if (!options.Value.Enabled || !options.Value.WarmUpOnStart)
        {
            return false;
        }

        if (string.Equals(_prefilled, Canon.ForPrompt(engine.Canon), StringComparison.Ordinal))
        {
            return false;
        }

        return _refresh.Writer.TryWrite(0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WarmUpOnStart)
        {
            _ready.TrySetResult();
            return;
        }

        await WarmAsync(stoppingToken).ConfigureAwait(false);

        _ready.TrySetResult();

        // Then stay, and warm again whenever the canon moves. One loop rather than a task per
        // edit, so two prefills can never be in flight at once - which would put the model's
        // cache in a race with itself and leave whichever finished last as the winner.
        try
        {
            while (await _refresh.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (_refresh.Reader.TryRead(out _))
                {
                    // Drain: several edits while a prefill ran are still one re-warm.
                }

                await WarmAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. The process is going away and the cache with it.
        }
    }

    /// <summary>
    /// Puts the live canon in front of the model, and records what was sent.
    /// </summary>
    /// <remarks>
    /// Never throws. A warm-up that could not run is a reason for the next draft to be slow, not a
    /// reason to bring the server down or to stop listening for the next edit.
    /// </remarks>
    private async Task WarmAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.WarmUpTimeoutSeconds)));

        var started = Stopwatch.StartNew();
        var canon = Canon.ForPrompt(engine.Canon);
        AssistLog.WarmingUp(logger, options.Value.Model);

        // Recorded before the request rather than after it. A prefill that fails partway has still
        // changed what the model holds, and a failed attempt that left this null would re-warm on
        // every save from then on; the next real edit queues another attempt either way.
        _prefilled = canon;

        try
        {
            var tokens = await PrefillAsync(canon, timeout.Token).ConfigureAwait(false);

            IsWarm = true;
            AssistLog.Warm(logger, tokens, (int)started.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-warm. Nothing to say; the process is going away.
        }
        catch (Exception e)
        {
            // Never fatal. A server whose model is not up yet still has to serve the builder, and
            // the first draft will pay the prefill instead - slowly, but it will work.
            IsWarm = false;
            AssistLog.WarmUpFailed(logger, (int)started.Elapsed.TotalSeconds, e);
        }
    }

    private async Task<int> PrefillAsync(string canon, CancellationToken cancellationToken)
    {
        var estimated = Canon.EstimateTokens(canon);
        if (estimated > options.Value.CanonTokenBudget)
        {
            AssistLog.CanonOverBudget(logger, estimated, options.Value.CanonTokenBudget);
        }

        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["model"] = options.Value.Model,
            ["stream"] = false,
            // The live one: the active configuration's, which Program.cs has loaded into
            // EngineOptions before any hosted service starts - or the line that says there is none.
            ["prompt"] = canon,
            // One token. The cache is the point; the word is not.
            ["options"] = new System.Text.Json.Nodes.JsonObject { ["num_predict"] = 1 },
            ["keep_alive"] = -1,
        };

        using var content = new StringContent(
            payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using var http = clients.CreateClient(ClientName);

        using var response = await http
            .PostAsync(new Uri("/api/generate", UriKind.Relative), content, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content
            .ReadFromJsonAsync(AssistJsonContext.Default.GenerateResponse, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.PromptEvalCount ?? 0;
    }
}
