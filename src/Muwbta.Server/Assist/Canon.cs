namespace Muwbta.Server.Assist;

/// <summary>
/// The world canon: the block of text that goes in front of every builder-assist request, and
/// where it comes from (PLAN.md §4.16).
/// </summary>
/// <remarks>
/// <para>
/// <b>It belongs to the configuration, and the server embeds none of its own.</b> It used to be
/// <c>docs/WORLD.md</c> compiled into the assembly - present, identical everywhere, and the
/// Reaches' forever: any configuration with no canon of its own, on any server, was handed the
/// Reaches, including a new world's. Now a configuration carries its canon
/// (<c>GameConfiguration.Canon</c>), the seeder plants one for Aldenmoor, the merge tool writes
/// <c>docs/WORLD.md</c> into the Reaches' configuration on the way to the import, and a
/// configuration with none is told so rather than told about somebody else's world.
/// </para>
/// <para>
/// <b>Byte-stable is still the requirement.</b> Ollama reuses the KV cache for a prompt that
/// shares a prefix with the last one, and measured, that is the difference between 4.4 s and
/// 187 s (tools/ollama/README.md). The text arrives from a textarea or a file, which is not
/// byte-stable, so <see cref="Resolve"/> normalises it the one way, every time.
/// </para>
/// <para>
/// <b>Cut at a marker the document declares itself.</b> WORLD.md's §10 is authoring process, true
/// and useful and no part of what the world <em>is</em>, and it is 3,000 tokens the budget does not
/// have. Cutting on the marker rather than a heading means renumbering the sections does not
/// change what the model is told, and pasting the whole file into the panel gets the same slice
/// the merge tool takes.
/// </para>
/// </remarks>
public static class Canon
{
    /// <summary>The line in <c>docs/WORLD.md</c> that ends the canon and begins the process notes.</summary>
    public const string EndMarker = "<!-- canon:end -->";

    /// <summary>Measured against Gemma 3 on the Reaches canon: 33,970 chars to 10,183 tokens.</summary>
    /// <remarks>
    /// A ratio rather than a tokeniser, because a tokeniser is a dependency, a download and a
    /// second thing to keep in step with the model - and the question is never "exactly how many"
    /// but "has this grown past what the window holds", which a ratio answers.
    /// </remarks>
    public const double CharsPerToken = 3.34;

    /// <summary>
    /// What the model is told when the live configuration has no canon: that there is none, and
    /// how to write anyway.
    /// </summary>
    /// <remarks>
    /// Short and generic on purpose. The alternative - a fallback world - is what this class used
    /// to be, and it meant a server that had not written its world got somebody else's. Telling
    /// the model to invent nothing is the honest default; the panel says the same to the builder.
    /// </remarks>
    public const string None =
        "# No world canon\n\n"
        + "This server has not written a description of its world yet. Draft in plain, concrete, "
        + "present-tense prose from the facts you are given. Name no gods, places, peoples or "
        + "history that those facts do not name.\n";

    /// <summary>Roughly how many tokens <paramref name="text"/> costs the model.</summary>
    public static int EstimateTokens(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)(text.Length / CharsPerToken);

    /// <summary>
    /// A canon as the model should receive it: line endings normalised, cut at
    /// <see cref="EndMarker"/>, trailing whitespace trimmed, one final newline. Empty in, empty
    /// out.
    /// </summary>
    public static string Resolve(string? live)
    {
        if (string.IsNullOrWhiteSpace(live))
        {
            return string.Empty;
        }

        var text = live.Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);

        if (end >= 0)
        {
            text = text[..end];
        }

        text = text.TrimEnd();

        return text.Length == 0 ? string.Empty : text + "\n";
    }

    /// <summary>
    /// What actually leads the prompt: the resolved canon, or <see cref="None"/> when there is
    /// nothing to resolve.
    /// </summary>
    public static string ForPrompt(string? live)
    {
        var resolved = Resolve(live);

        return resolved.Length == 0 ? None : resolved;
    }
}
