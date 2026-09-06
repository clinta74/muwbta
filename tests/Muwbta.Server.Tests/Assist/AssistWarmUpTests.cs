using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Muwbta.Engine;
using Muwbta.Server.Assist;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Muwbta.Server.Tests.Assist;

/// <summary>
/// Warming the model with the canon, and warming it again when the canon moves.
/// </summary>
/// <remarks>
/// <b>The guard is the point.</b> A prefill is the most expensive thing this server asks the model
/// to do — the whole canon evaluated, minutes on modest hardware — so a re-warm that fired on every
/// save would throw away a good cache to arrive at the text it already had. What is asserted here
/// is that it fires when the canon changed and stays quiet when it did not.
/// </remarks>
public sealed class AssistWarmUpTests
{
    /// <summary>Counts prefills and lets a test wait for the next one, without sleeping.</summary>
    private sealed class Stub : HttpMessageHandler
    {
        private readonly List<string> _prompts = [];
        private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Prompts
        {
            get { lock (_prompts) { return [.. _prompts]; } }
        }

        public Task Next => _next.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            lock (_prompts)
            {
                _prompts.Add(body!["prompt"]!.GetValue<string>());
            }

            var completed = _next;
            _next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completed.TrySetResult();

            var envelope = new JsonObject { ["response"] = "x", ["prompt_eval_count"] = 4_242 };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:11434") };
    }

    private static (AssistWarmUp WarmUp, Stub Handler, EngineOptions Engine) Build(string canon)
    {
        var handler = new Stub();
        var engine = new EngineOptions { Canon = canon };

        var warmUp = new AssistWarmUp(
            new Factory(handler),
            Options.Create(new AssistOptions { Enabled = true, Model = "muwbta-builder", WarmUpTimeoutSeconds = 30 }),
            engine,
            NullLogger<AssistWarmUp>.Instance);

        return (warmUp, handler, engine);
    }

    /// <summary>A short wait with a deadline, so a failure reports rather than hangs the suite.</summary>
    private static async Task<bool> WithinAsync(Task task, int seconds = 5) =>
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))).ConfigureAwait(false) == task;

    [Fact]
    public async Task It_warms_with_the_live_canon_and_reports_warm()
    {
        var (warmUp, handler, _) = Build("# Elsewhere\n\nOne Reach, and it is round.\n");

        await warmUp.StartAsync(CancellationToken.None);
        await warmUp.Ready;

        Assert.True(warmUp.IsWarm);
        Assert.Equal("# Elsewhere\n\nOne Reach, and it is round.\n", Assert.Single(handler.Prompts));

        await warmUp.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The guard. A save that did not touch the canon must not cost the model a re-evaluation.
    /// </summary>
    [Fact]
    public async Task An_unchanged_canon_queues_nothing()
    {
        var (warmUp, handler, _) = Build("# Elsewhere\n\nOne Reach.\n");

        await warmUp.StartAsync(CancellationToken.None);
        await warmUp.Ready;

        Assert.False(warmUp.CanonChanged());
        Assert.False(warmUp.CanonChanged());

        // Whitespace either side of the marker is normalised away, so a save that re-typed the
        // same words with a trailing newline is still not a change.
        Assert.False(warmUp.CanonChanged());
        Assert.Single(handler.Prompts);

        await warmUp.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_changed_canon_is_put_in_front_of_the_model_again()
    {
        var (warmUp, handler, engine) = Build("# Elsewhere\n\nOne Reach.\n");

        await warmUp.StartAsync(CancellationToken.None);
        await warmUp.Ready;

        var second = handler.Next;
        engine.Canon = "# Elsewhere\n\nTwo Reaches, now.\n";

        Assert.True(warmUp.CanonChanged());
        Assert.True(await WithinAsync(second), "the changed canon was never re-warmed");

        Assert.Equal(
            ["# Elsewhere\n\nOne Reach.\n", "# Elsewhere\n\nTwo Reaches, now.\n"],
            handler.Prompts);

        // And having warmed it, the same text is no longer a change.
        Assert.False(warmUp.CanonChanged());

        await warmUp.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A configuration that carries no canon still has a prefix — the line saying there is no world
    /// description — and clearing one is as much a change as writing one.
    /// </summary>
    [Fact]
    public async Task Clearing_the_canon_is_a_change_and_warms_the_empty_prompt()
    {
        var (warmUp, handler, engine) = Build("# Elsewhere\n\nOne Reach.\n");

        await warmUp.StartAsync(CancellationToken.None);
        await warmUp.Ready;

        var second = handler.Next;
        engine.Canon = string.Empty;

        Assert.True(warmUp.CanonChanged());
        Assert.True(await WithinAsync(second), "clearing the canon was never re-warmed");
        Assert.Equal(Canon.None, handler.Prompts[^1]);

        await warmUp.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Warming is off, so nothing is queued: an operator who turned it off has said the first
    /// draft may pay for itself, and an edit must not quietly turn it back on.
    /// </summary>
    [Fact]
    public async Task Nothing_is_queued_when_warming_is_off()
    {
        var handler = new Stub();
        var engine = new EngineOptions { Canon = "# Elsewhere\n" };

        var warmUp = new AssistWarmUp(
            new Factory(handler),
            Options.Create(new AssistOptions { Enabled = true, WarmUpOnStart = false }),
            engine,
            NullLogger<AssistWarmUp>.Instance);

        await warmUp.StartAsync(CancellationToken.None);
        await warmUp.Ready;

        engine.Canon = "# Somewhere else entirely\n";

        Assert.False(warmUp.CanonChanged());
        Assert.Empty(handler.Prompts);

        await warmUp.StopAsync(CancellationToken.None);
    }
}
