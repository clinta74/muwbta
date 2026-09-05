using System.Diagnostics;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WarmUpOnStart)
        {
            _ready.TrySetResult();
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.WarmUpTimeoutSeconds)));

        var started = Stopwatch.StartNew();
        AssistLog.WarmingUp(logger, options.Value.Model);

        try
        {
            var tokens = await PrefillAsync(timeout.Token).ConfigureAwait(false);

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
            AssistLog.WarmUpFailed(logger, (int)started.Elapsed.TotalSeconds, e);
        }
        finally
        {
            _ready.TrySetResult();
        }
    }

    private async Task<int> PrefillAsync(CancellationToken cancellationToken)
    {
        var estimated = Canon.EstimateTokens(Canon.ForPrompt(engine.Canon));
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
            ["prompt"] = Canon.ForPrompt(engine.Canon),
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
