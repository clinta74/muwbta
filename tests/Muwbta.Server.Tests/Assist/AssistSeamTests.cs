using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Muwbta.Engine;
using Muwbta.Server.Assist;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Muwbta.Server.Tests.Assist;

/// <summary>
/// What the review catches that the grammar could not.
/// </summary>
public sealed class RoomDraftReviewTests
{
    private static readonly string[] Rooms =
    [
        "ossara.gatetown.the-market",
        "ossara.gatetown.the-north-road",
    ];

    private static RoomDraft Draft(string description, params DraftExit[] exits) =>
        new("The Tollhouse Steps", description, exits);

    /// <summary>Clean drafts produce nothing to say.</summary>
    [Fact]
    public void A_good_draft_has_no_warnings()
    {
        var draft = Draft(
            "Cold flagstones, worn smooth in the middle and rough at the edges.",
            new DraftExit("south", Rooms[0]));

        Assert.Empty(RoomDraftReview.Review(draft, Rooms));
    }

    /// <summary>
    /// Two exits the same way.
    /// </summary>
    /// <remarks>
    /// The gap worth having a review for at all: JSON Schema has no uniqueness constraint over a
    /// field, so a six-item array of enumerated directions can legally be six norths. The schema
    /// cannot say it and the import would take the last one silently.
    /// </remarks>
    [Fact]
    public void Two_exits_the_same_way_is_a_warning()
    {
        var draft = Draft(
            "Cold flagstones.",
            new DraftExit("north", Rooms[0]),
            new DraftExit("north", Rooms[1]));

        var warnings = RoomDraftReview.Review(draft, Rooms);

        Assert.Single(warnings);
        Assert.Contains("north", warnings[0], StringComparison.Ordinal);
    }

    /// <summary>A destination nobody offered means the constraint did not hold.</summary>
    [Fact]
    public void An_invented_destination_is_a_warning()
    {
        var draft = Draft("Cold flagstones.", new DraftExit("north", "ossara.gatetown.invented"));

        Assert.Contains(
            RoomDraftReview.Review(draft, Rooms),
            w => w.Contains("invented", StringComparison.Ordinal));
    }

    /// <summary>
    /// A room key in the title.
    /// </summary>
    /// <remarks>
    /// Not invented: a 4B model, given this exact prompt, answered the title with
    /// <c>ossara.gatetown.the-tollhouse-steps</c>. Schema-valid, the right type, the right length,
    /// and a look line no player should ever see - which is the whole argument for a review beside
    /// a grammar. The prose review already caught this shape; the room review did not, because the
    /// failure had not happened yet.
    /// </remarks>
    [Fact]
    public void A_room_key_in_the_title_is_a_warning()
    {
        var draft = new RoomDraft(
            "ossara.gatetown.the-tollhouse-steps", "Cold flagstones.", []);

        Assert.Contains(
            RoomDraftReview.Review(draft, Rooms),
            w => w.Contains("content key", StringComparison.Ordinal));
    }

    /// <summary>An ordinary title with a full stop in it is not a key.</summary>
    [Fact]
    public void A_title_with_punctuation_is_not_a_key()
    {
        Assert.DoesNotContain(
            RoomDraftReview.Review(Draft("Cold flagstones."), Rooms),
            w => w.Contains("content key", StringComparison.Ordinal));
    }

    /// <summary>
    /// Prose that describes a way out.
    /// </summary>
    /// <remarks>
    /// Taken from the first real generation, which wrote "a steep, winding staircase ... rises from
    /// the gate" after being told not to. The engine writes the exit line, so prose naming one can
    /// contradict what the room actually offers.
    /// </remarks>
    [Fact]
    public void Prose_that_names_a_way_out_is_a_warning()
    {
        var draft = Draft("A steep, winding staircase rises from the gate.");

        Assert.Contains(
            RoomDraftReview.Review(draft, Rooms),
            w => w.Contains("way out", StringComparison.Ordinal));
    }
}

/// <summary>
/// The queue's promises: it refuses rather than over-accepts, and it forgets.
/// </summary>
public sealed class AssistQueueTests
{
    /// <summary>A clock the test moves by hand, so the sweep does not need a real minute.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static AssistQueue Queue(
        int depth = 8, int retentionMinutes = 30, TimeProvider? clock = null) =>
        new(
            Options.Create(new AssistOptions
            {
                MaxQueued = depth,
                JobRetentionMinutes = retentionMinutes,
            }),
            clock);

    private static RoomDraftRequest Request => new("ossara.gatetown", "ossara.gatetown.a-room", null);

    [Fact]
    public void An_enqueued_job_is_findable_and_queued()
    {
        var queue = Queue();

        var id = queue.TryEnqueue(Request);

        Assert.NotNull(id);
        Assert.Equal(AssistJobState.Queued, queue.Find(id!.Value)!.State);
    }

    /// <summary>
    /// A full queue refuses rather than accepting work it will not do.
    /// </summary>
    /// <remarks>
    /// The alternative - accept and drop - is the one behaviour worse than refusing, because the
    /// builder then waits for an answer that was discarded at the door. With one worker and three
    /// minutes a job, a deep queue is a promise nobody would wait for anyway.
    /// </remarks>
    [Fact]
    public void A_full_queue_refuses()
    {
        var queue = Queue(depth: 2);

        Assert.NotNull(queue.TryEnqueue(Request));
        Assert.NotNull(queue.TryEnqueue(Request));
        Assert.Null(queue.TryEnqueue(Request));
    }

    [Fact]
    public void A_finished_job_carries_its_draft_and_warnings()
    {
        var queue = Queue();
        var id = queue.TryEnqueue(Request)!.Value;

        queue.Started(id);
        queue.Succeeded(id, new RoomDraft("A Room", "Stone.", []), ["something to look at"]);

        var job = queue.Find(id)!;

        Assert.Equal(AssistJobState.Succeeded, job.State);
        Assert.Equal("A Room", job.Draft!.Title);
        Assert.Equal(["something to look at"], job.Warnings);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public void A_failed_job_carries_why()
    {
        var queue = Queue();
        var id = queue.TryEnqueue(Request)!.Value;

        queue.Failed(id, "the model was not there");

        Assert.Equal(AssistJobState.Failed, queue.Find(id)!.State);
        Assert.Equal("the model was not there", queue.Find(id)!.Error);
    }

    /// <summary>
    /// Finished jobs are swept once they are old; a running one never is, however long it takes.
    /// </summary>
    /// <remarks>
    /// The second half matters more than the first. A draft measured at three minutes could
    /// plausibly outlive a short retention window, and sweeping a job while its worker is still
    /// filling it in would lose the answer at the moment it arrived - so the sweep keys on
    /// <c>FinishedAt</c>, which a running job does not have.
    /// </remarks>
    [Fact]
    public void Old_finished_jobs_are_forgotten_and_running_ones_are_not()
    {
        var clock = new Clock();
        var queue = Queue(retentionMinutes: 30, clock: clock);

        var stale = queue.TryEnqueue(Request)!.Value;
        var running = queue.TryEnqueue(Request)!.Value;

        queue.Failed(stale, "old news");
        queue.Started(running);

        clock.Advance(TimeSpan.FromMinutes(31));

        // The sweep runs on enqueue, which is the only moment the dictionary grows.
        queue.TryEnqueue(Request);

        Assert.Null(queue.Find(stale));
        Assert.NotNull(queue.Find(running));
    }

    /// <summary>And a finished job is readable for as long as it was promised.</summary>
    [Fact]
    public void A_finished_job_survives_its_retention_window()
    {
        var clock = new Clock();
        var queue = Queue(retentionMinutes: 30, clock: clock);

        var id = queue.TryEnqueue(Request)!.Value;
        queue.Succeeded(id, new RoomDraft("A Room", "Stone.", []), []);

        clock.Advance(TimeSpan.FromMinutes(29));
        queue.TryEnqueue(Request);

        Assert.NotNull(queue.Find(id));
    }
}

/// <summary>
/// What the Ollama client puts on the wire.
/// </summary>
/// <remarks>
/// Asserted against a stub handler rather than a container, because the properties that matter are
/// properties of the request: the prefix, its position, and the model name. A live model can tell
/// you the answer is good; only this can tell you the cache will be hit.
/// </remarks>
public sealed class OllamaContentAssistantTests
{
    private sealed class Stub(string responseJson) : HttpMessageHandler
    {
        public JsonNode? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = JsonNode.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            var envelope = new JsonObject
            {
                ["response"] = responseJson,
                ["prompt_eval_count"] = 10_183,
                ["eval_count"] = 226,
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private static readonly ZoneContext Context = new(
        "Gatetown",
        "The last town before the rim.",
        ["ossara.gatetown.the-market", "ossara.gatetown.a-room"],
        [new RoomExemplar("The Market", "Awnings snap in the wind off the rim.")]);

    private static (OllamaContentAssistant Assistant, Stub Handler) Build(string responseJson, string canon = "")
    {
        var handler = new Stub(responseJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };

        var assistant = new OllamaContentAssistant(
            http,
            Options.Create(new AssistOptions { Model = "muwbta-builder" }),
            new EngineOptions { Canon = canon },
            NullLogger<OllamaContentAssistant>.Instance);

        return (assistant, handler);
    }

    private const string Good = """
        {"title":"A Room","description":"Stone, and cold.","exits":[]}
        """;

    private static RoomDraftRequest Request(string? instruction = null) =>
        new("ossara.gatetown", "ossara.gatetown.a-room", instruction);

    [Fact]
    public async Task It_parses_a_draft()
    {
        var (assistant, _) = Build(Good);

        var draft = await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        Assert.Equal("A Room", draft.Title);
        Assert.Equal("Stone, and cold.", draft.Description);
    }

    /// <summary>
    /// The canon leads, byte for byte.
    /// </summary>
    /// <remarks>
    /// The single most important assertion in the file. Prefix caching is reuse of a KV cache for a
    /// shared leading substring; measured, having it is 4.4 s and losing it is 187 s. Anything
    /// prepended to the canon - a system line, a timestamp, a greeting - costs three minutes per
    /// request and changes nothing else, so nothing would look wrong.
    /// </remarks>
    [Fact]
    public async Task The_prompt_begins_with_the_canon()
    {
        var (assistant, handler) = Build(Good, canon: "# Elsewhere\n\nOne Reach, and it is round.\n");

        await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        var prompt = handler.Sent!["prompt"]!.GetValue<string>();

        Assert.StartsWith("# Elsewhere\n\nOne Reach, and it is round.\n", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// And with none, it begins by saying so - not with somebody else's world, which is what the
    /// embedded fallback used to hand every server that had not written its own.
    /// </summary>
    [Fact]
    public async Task Without_a_canon_the_prompt_begins_by_saying_there_is_none()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        var prompt = handler.Sent!["prompt"]!.GetValue<string>();

        Assert.StartsWith(Canon.None, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Reaches", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the builder's own words come last.
    /// </summary>
    /// <remarks>
    /// The other half of the same property. Free text is the most variable thing in the request, so
    /// it goes after everything that does not vary - by construction here, rather than by whoever
    /// next edits the prompt remembering to.
    /// </remarks>
    [Fact]
    public async Task The_builders_instruction_lands_at_the_very_end()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(
            Request("make it colder"), Context, CancellationToken.None);

        var prompt = handler.Sent!["prompt"]!.GetValue<string>();

        Assert.EndsWith("make it colder\n", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// Existing prose is sent to the model, and it changes the job.
    /// </summary>
    /// <remarks>
    /// The common case is not a blank field. Somebody reaches for help when they have half a
    /// paragraph they are unhappy with, so the draft has to be seeded by it rather than ignoring
    /// it and returning something unrelated to what they were writing.
    /// </remarks>
    [Fact]
    public async Task Existing_prose_seeds_the_draft_and_makes_it_a_rewrite()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(
            Request() with { Title = "The Tollhouse Steps", Description = "Half a paragraph." },
            Context,
            CancellationToken.None);

        var prompt = handler.Sent!["prompt"]!.GetValue<string>();

        Assert.Contains("The Tollhouse Steps", prompt, StringComparison.Ordinal);
        Assert.Contains("Half a paragraph.", prompt, StringComparison.Ordinal);
        Assert.Contains("Rewrite the room", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Write the room", prompt, StringComparison.Ordinal);
    }

    /// <summary>And a blank room is still asked for from nothing.</summary>
    [Fact]
    public async Task An_empty_room_is_written_rather_than_rewritten()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        var prompt = handler.Sent!["prompt"]!.GetValue<string>();

        Assert.Contains("Write the room", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("already started this room", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// Existing text still lands after the canon, like everything else that varies.
    /// </summary>
    /// <remarks>
    /// It is the largest variable thing in the request now, so it is the one most able to wreck the
    /// prefix cache if it ever drifted forward of the canon.
    /// </remarks>
    [Fact]
    public async Task Existing_prose_does_not_move_the_cached_prefix()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(
            Request("make it colder") with { Description = "Half a paragraph." },
            Context,
            CancellationToken.None);

        var prompt = handler.Sent!["prompt"]!.GetValue<string>();

        Assert.StartsWith(Canon.None, prompt, StringComparison.Ordinal);
        Assert.EndsWith("make it colder\n", prompt, StringComparison.Ordinal);
    }

    /// <summary>The derived model, never the base — the base answers at 4096.</summary>
    [Fact]
    public async Task It_asks_for_the_configured_model()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        Assert.Equal("muwbta-builder", handler.Sent!["model"]!.GetValue<string>());
    }

    /// <summary>
    /// The room being drafted is not offered as a place it can lead to.
    /// </summary>
    /// <remarks>
    /// Left in, a model told the room exists gives it an exit to itself, and every layer downstream
    /// accepts that: the schema enumerated it, so it is legal.
    /// </remarks>
    [Fact]
    public async Task A_room_is_not_offered_an_exit_to_itself()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        var destinations = handler.Sent!["format"]!["properties"]!["exits"]!["items"]!
            ["properties"]!["to"]!["enum"]!.AsArray()
            .Select(v => v!.GetValue<string>());

        Assert.Equal(["ossara.gatetown.the-market"], destinations);
    }

    /// <summary>A request must never be the reason the model unloads.</summary>
    [Fact]
    public async Task It_asks_the_model_to_stay_loaded()
    {
        var (assistant, handler) = Build(Good);

        await assistant.DraftRoomAsync(Request(), Context, CancellationToken.None);

        Assert.Equal(-1, handler.Sent!["keep_alive"]!.GetValue<int>());
    }

    /// <summary>
    /// Ollama's counts are read, and they are read from the names Ollama actually uses.
    /// </summary>
    /// <remarks>
    /// These bound to nothing under <c>JsonSerializerDefaults.Web</c> - Ollama sends
    /// <c>prompt_eval_count</c> and camelCase looked for <c>promptEvalCount</c> - so they
    /// deserialised as zero and the log line built to be the truncation canary would have read "0
    /// prompt tokens" for the life of the feature. Found by running it against the real thing;
    /// missed by the stub above, because a test that asserts no number cannot notice one is zero.
    /// That is what this test is for, and it is why it asserts the values rather than the shape.
    /// </remarks>
    [Fact]
    public void The_token_counts_are_read_from_ollamas_own_spelling()
    {
        const string Body = """
            {"response":"{}","prompt_eval_count":10183,"eval_count":226}
            """;

        var envelope = JsonSerializer.Deserialize(Body, AssistJsonContext.Default.GenerateResponse);

        Assert.NotNull(envelope);
        Assert.Equal(10_183, envelope!.PromptEvalCount);
        Assert.Equal(226, envelope.EvalCount);
        Assert.Equal("{}", envelope.Response);
    }

    /// <summary>An empty draft is a failure, not a room with no words in it.</summary>
    [Theory]
    [InlineData("""{"title":"","description":"Stone.","exits":[]}""")]
    [InlineData("""{"title":"A Room","description":"  ","exits":[]}""")]
    public async Task A_draft_with_nothing_in_it_is_refused(string response)
    {
        var (assistant, _) = Build(response);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            assistant.DraftRoomAsync(Request(), Context, CancellationToken.None));
    }

    /// <summary>
    /// Unparseable output says so loudly.
    /// </summary>
    /// <remarks>
    /// This should be impossible - the grammar guarantees the shape - so if it ever happens the
    /// interesting fact is that the constraint did not hold, which is worth an exception naming the
    /// body rather than a null quietly moving downstream.
    /// </remarks>
    [Fact]
    public async Task Output_that_is_not_json_is_an_error_that_names_it()
    {
        var (assistant, _) = Build("not json at all");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            assistant.DraftRoomAsync(Request(), Context, CancellationToken.None));

        Assert.Contains("not json at all", error.Message, StringComparison.Ordinal);
    }
}
