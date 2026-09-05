using System.Net.Http.Json;
using System.Text;
using Muwbta.Engine;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Nodes;
using Muwbta.Server.Building;
using Microsoft.Extensions.Options;

namespace Muwbta.Server.Assist;

/// <summary>
/// Drafts content by asking a local Ollama, with the output shape constrained by
/// <see cref="AssistSchema"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ollama's native API, not its OpenAI-compatible one.</b> The compatible surface has no field
/// for <c>num_ctx</c>, and <c>keep_alive</c> is likewise only reachable here. Given that everything
/// about this design is arranged around one large prefix staying cached, giving up the two
/// parameters that control the window and the residency to gain a familiar request shape would be
/// a poor trade.
/// </para>
/// <para>
/// <b>The prompt is built in one order, always.</b> Canon, then zone, then the room, then whatever
/// the builder typed. Everything that varies goes after everything that does not, because the KV
/// cache is reused for exactly as long as the prefix matches - so a builder's free-text steer, the
/// most variable thing in the request, is last by construction rather than by care.
/// </para>
/// </remarks>
public sealed class OllamaContentAssistant : IContentAssistant
{
    /// <summary>Low, because this output has to satisfy a schema and be believed.</summary>
    private const double Temperature = 0.4;

    /// <summary>How many of the zone's rooms are shown as exemplars.</summary>
    /// <remarks>
    /// Three, and the budget is why: the canon is 10,183 tokens of a 16,384 window, the schema is
    /// another 946, and a room description runs to 900 characters. Three exemplars is roughly 700
    /// tokens and leaves room to generate. This is the number to revisit if the window changes,
    /// and <c>CanonTests</c> is what will notice if the canon grows into it first.
    /// </remarks>
    private const int ExemplarCount = 3;

    private readonly HttpClient _http;
    private readonly AssistOptions _options;
    // The live configuration's canon, read per request so an edit or an activation is heard
    // with no cache to invalidate. Empty means the embedded one (Canon.Resolve).
    private readonly EngineOptions _engine;
    private readonly ILogger<OllamaContentAssistant> _logger;

    public OllamaContentAssistant(
        HttpClient http,
        IOptions<AssistOptions> options,
        EngineOptions engine,
        ILogger<OllamaContentAssistant> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options.Value;
        _engine = engine;
        _logger = logger;
    }

    public async Task<RoomDraft> DraftRoomAsync(
        RoomDraftRequest request,
        ZoneContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // The room being drafted is not somewhere it can link to. Left in, a model that has been
        // told the room exists will happily give it an exit to itself.
        var destinations = context.RoomKeys
            .Where(k => !string.Equals(k, request.RoomKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var json = await GenerateAsync(
            AssistSchema.ForRoom(destinations),
            Prompt(request, context),
            request.Subject,
            cancellationToken).ConfigureAwait(false);

        var draft = Parse(json, AssistJsonContext.Default.RoomDraft);

        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.Description))
        {
            throw new InvalidOperationException($"The draft has no title or no prose: {Trim(json)}");
        }

        return draft;
    }

    /// <summary>
    /// One constrained generation. Everything both callers share is here.
    /// </summary>
    /// <returns>The model's answer, which the grammar guarantees is JSON of the given shape.</returns>
    private async Task<string?> GenerateAsync(
        JsonObject schema, string prompt, string subject, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["format"] = schema,
            ["prompt"] = prompt,
            ["options"] = new JsonObject { ["temperature"] = Temperature },
            // Never let one request be the reason the model unloads: an unload discards the canon
            // prefix, and rebuilding it is minutes rather than seconds.
            ["keep_alive"] = -1,
        };

        using var content = new StringContent(
            payload.ToJsonString(), Encoding.UTF8, "application/json");

        AssistLog.Requesting(_logger, _options.Model, subject);

        using var response = await _http
            .PostAsync(new Uri("/api/generate", UriKind.Relative), content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"The model refused the request ({(int)response.StatusCode}). {Trim(body)}");
        }

        var envelope = await response.Content
            .ReadFromJsonAsync(AssistJsonContext.Default.GenerateResponse, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The model returned nothing at all.");

        AssistLog.Generated(_logger, subject, envelope.PromptEvalCount, envelope.EvalCount);

        return envelope.Response;
    }

    public async Task<ProseDraft> DraftProseAsync(
        ProseDraftRequest request,
        ProseContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var json = await GenerateAsync(
            AssistSchema.ForProse(request.Kind),
            ProsePrompt(request, context),
            request.Subject,
            cancellationToken).ConfigureAwait(false);

        var draft = Parse(json, AssistJsonContext.Default.ProseDraft);

        if (string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.Description))
        {
            throw new InvalidOperationException($"The draft has no name or no prose: {Trim(json)}");
        }

        return draft;
    }

    /// <summary>
    /// The mechanical facts first, then the voice, then what to write.
    /// </summary>
    /// <remarks>
    /// <b>The facts are the feature.</b> A description written without them contradicts the thing
    /// it describes - a two-kilogram "massive greatsword", a level 4 rat that "would kill a company
    /// of men" - and a builder who has to correct that every time stops pressing the button. They
    /// go in as context precisely so they do not have to come out as output, which is where they
    /// would be dangerous (AssistSchema.MobNotGenerated).
    /// </remarks>
    private string ProsePrompt(ProseDraftRequest request, ProseContext context)
    {
        var what = request.Kind switch
        {
            AssistSchema.ProseKind.Mob => "creature",
            AssistSchema.ProseKind.Item => "item",
            _ => "quest",
        };

        var prompt = new StringBuilder(Canon.ForPrompt(_engine.Canon));

        prompt.Append("\n---\n\nYou are writing the words for one ").Append(what)
            .Append(" in the world above. Someone else has already decided what it is; these are ")
            .Append("its facts, and the words have to match them.\n\n")
            .Append(context.Facts).Append('\n');

        if (context.Exemplars.Count > 0)
        {
            // Framed hard as belonging to something else, and told not to reuse them.
            //
            // The first live run copied an exemplar's opening sentence word for word into both
            // drafts - a bare list under "reads like this" is a list the model continues rather
            // than imitates. Left alone that puts the same sentence at the front of every
            // description in a zone, which is duplication a builder skims straight past, and in the
            // item's case it also dragged the prose away from the facts: an exemplar about a short
            // blade turned a 6.4 kg two-handed axe into one.
            prompt.Append("For voice and length only, here is how OTHER ").Append(what)
                .Append("s are written. They are different ").Append(what)
                .Append("s and none of them is the one you are writing. Do not reuse their ")
                .Append("sentences, their objects, or their opening words:\n\n");

            foreach (var exemplar in context.Exemplars.Take(ExemplarCount))
            {
                prompt.Append("- (a different ").Append(what).Append(") ").Append(exemplar).Append('\n');
            }

            prompt.Append('\n');
        }

        var hasProse = !string.IsNullOrWhiteSpace(request.Description);

        if (!string.IsNullOrWhiteSpace(request.Name) || hasProse)
        {
            prompt.Append("The builder has already started this. Keep what works and improve it ")
                .Append("rather than replacing it with something else.\n\n");

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                prompt.Append("Current name: ").Append(request.Name!.Trim()).Append('\n');
            }

            if (!string.IsNullOrWhiteSpace(request.Summary))
            {
                prompt.Append("Current summary: ").Append(request.Summary!.Trim()).Append('\n');
            }

            if (hasProse)
            {
                prompt.Append("Current description:\n").Append(request.Description!.Trim()).Append('\n');
            }

            prompt.Append('\n');
        }

        prompt.Append(hasProse ? "Rewrite it." : "Write it.")
            .Append(" Describe only this ").Append(what)
            .Append(". Do not invent other content: anything you name that is not in the facts ")
            .Append("above does not exist.\n");

        // Last, always, so nothing a builder types can move the cached prefix.
        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            prompt.Append("\nThe builder adds: ").Append(request.Instruction.Trim()).Append('\n');
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Canon first, unchanged, every time.
    /// </summary>
    /// <remarks>
    /// The first line after the canon starts a section the model can tell apart from the world
    /// itself, because the canon is a document about a world and what follows is an instruction
    /// about a room - and without a visible seam the second reads as more of the first.
    /// </remarks>
    private string Prompt(RoomDraftRequest request, ZoneContext context)
    {
        var prompt = new StringBuilder(Canon.ForPrompt(_engine.Canon));

        prompt.Append("\n---\n\nYou are drafting one room for a builder, in the world above.\n\n")
            .Append("Zone: ").Append(context.ZoneName).Append('\n')
            .Append(context.ZoneDescription).Append("\n\n");

        if (context.Exemplars.Count > 0)
        {
            prompt.Append("Rooms already written in this zone, for voice and length:\n\n");

            foreach (var exemplar in context.Exemplars.Take(ExemplarCount))
            {
                prompt.Append("## ").Append(exemplar.Title).Append('\n')
                    .Append(exemplar.Description).Append("\n\n");
            }
        }

        // What the builder already has, when there is anything. This changes the job from "write a
        // room" to "improve this room", which is a different task and the more common one - the
        // moment somebody reaches for help is usually the moment they have half a paragraph they
        // are not happy with, not a blank field.
        var hasTitle = !string.IsNullOrWhiteSpace(request.Title);
        var hasProse = !string.IsNullOrWhiteSpace(request.Description);

        if (hasTitle || hasProse)
        {
            prompt.Append("The builder has already started this room. Keep what works, keep the ")
                .Append("same place, and improve it rather than replacing it with something else.")
                .Append("\n\n");

            if (hasTitle)
            {
                prompt.Append("Current title: ").Append(request.Title!.Trim()).Append('\n');
            }

            if (hasProse)
            {
                prompt.Append("Current description:\n").Append(request.Description!.Trim()).Append('\n');
            }

            prompt.Append('\n');
        }

        prompt.Append(hasProse ? "Rewrite the room `" : "Write the room `")
            .Append(request.RoomKey).Append("`.\n")
            .Append("Describe only what is in the room. Do not name mobs, items, or exits: the ")
            .Append("engine lists those, and anything you invent here will not exist.\n");

        // Last, always. Anything a builder types must not be able to move the cached prefix.
        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            prompt.Append("\nThe builder adds: ").Append(request.Instruction.Trim()).Append('\n');
        }

        return prompt.ToString();
    }

    private static T Parse<T>(string? json, JsonTypeInfo<T> shape)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The model returned an empty response.");
        }

        try
        {
            return JsonSerializer.Deserialize(json, shape)
                ?? throw new InvalidOperationException($"The draft was null: {Trim(json)}");
        }
        catch (JsonException e)
        {
            // Should be impossible - the grammar guarantees this parses - so if it happens the
            // interesting fact is that the constraint did not hold, and the body is the evidence.
            throw new InvalidOperationException(
                $"Constrained output did not parse, which should not be possible: {Trim(json)}", e);
        }
    }

    private static string Trim(string? text) =>
        text is null ? string.Empty
        : text.Length <= 400 ? text
        : text[..400] + "...";

}

/// <summary>The fields of Ollama's reply this cares about.</summary>
/// <remarks>
/// <b>The names are spelled out because Ollama speaks snake_case and this codebase does not.</b>
/// Under <c>JsonSerializerDefaults.Web</c> these bound to <c>promptEvalCount</c>, matched nothing,
/// and silently deserialised as zero - so the log line built to be the truncation canary would have
/// reported "0 prompt tokens" forever, which is worse than not logging it. The stub in the tests
/// did not catch it because a test that does not assert a number cannot notice it is zero.
/// </remarks>
internal sealed record GenerateResponse(
    [property: JsonPropertyName("response")] string? Response,
    [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
    [property: JsonPropertyName("eval_count")] int EvalCount);

/// <summary>
/// Source-generated metadata for the two shapes this crosses the wire with.
/// </summary>
/// <remarks>
/// <b>Not decoration, and not premature.</b> Reflection-based serialisation is disabled in some
/// hosts - a file-based app under <c>tools/</c> is one, which is how this was found - and it throws
/// outright rather than degrading. The server enables reflection and was never affected, but this
/// is the second time the same trap has been hit in this feature (the first was
/// <c>JsonArray.Add&lt;T&gt;</c> in <c>AssistSchema</c>), and it costs ten lines to stop being a
/// trap: any tool that wants to draft a room from a script now can.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GenerateResponse))]
[JsonSerializable(typeof(RoomDraft))]
[JsonSerializable(typeof(ProseDraft))]
internal sealed partial class AssistJsonContext : JsonSerializerContext;
