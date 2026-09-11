using System.Text.RegularExpressions;

namespace Muwbta.Engine.Tests.Architecture;

/// <summary>
/// Every field of an <c>Upsert*</c> change must reach both places that consume it.
/// </summary>
/// <remarks>
/// <para>
/// A content edit travels as one <see cref="Mutations.WorldChange"/> and is consumed twice:
/// <c>WorldMutationApplier</c> puts it into the live cache, and <c>WorldWriter</c> writes the row.
/// Adding a field means touching a dozen files — domain object, EF configuration, change record,
/// builder contracts, endpoints, bundle, exporter, importer, and both consumers — and the two
/// consumers are the easiest to miss, because missing one is <b>silent in a particular direction</b>
/// rather than broken outright:
/// </para>
/// <list type="bullet">
///   <item>Miss the writer and the field works until the process restarts, then reverts.</item>
///   <item>Miss the applier and the field does nothing until the process restarts, then works.</item>
/// </list>
/// <para>
/// Both have happened. <c>isLore</c>, <c>isNoDrop</c> and <c>paths</c> were never written to a row
/// from the day the <c>ItemRestrictions</c> migration added them — the API accepted them, the
/// applier cached them, the exporter wrote them, and nothing persisted them. <c>RewardFlagKey</c>
/// never reached the cache, so the four attunement gates — the game's only progression lock —
/// stayed shut until a restart (BUGS.md #6, #7, #23).
/// </para>
/// <para>
/// Neither was caught by a test, and neither could have been by the sort of test that was there:
/// the suite builds its <c>Quest</c> and <c>ItemTemplate</c> objects directly, so the consumers are
/// bypassed entirely. This reads the source instead, which is what <c>check-bundle.py</c> and
/// <c>check-builder-keys.py</c> already do for the seams no single-language test can see across.
/// </para>
/// <para>
/// <b>It scans text, deliberately.</b> Reflection can see that a property exists and cannot see
/// whether a method body reads it, which is the entire question. The cost is that it is fooled by a
/// field named as a substring of another; the mitigation is a word-boundary match and the fact that
/// a false pass is the failure mode, not a false alarm.
/// </para>
/// </remarks>
public sealed class ChangeRecordCompletenessTests
{
    /// <summary>
    /// Fields a consumer legitimately ignores, each with the reason it is not a bug.
    /// </summary>
    /// <remarks>
    /// Kept short on purpose. Every entry here is a hole in the guard, so an addition should be
    /// argued for in the same commit that makes it — the whole class of defect this exists to catch
    /// looks exactly like a missing entry until somebody decides it is fine.
    /// </remarks>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // An edit says what a configuration means, never which one the server obeys. Only
        // ActivateGameConfiguration moves that, so the applier reads the two fields the running
        // loop actually needs and ignores the rest.
        ["applier:UpsertGameConfiguration.Key"] = "the loop keys configurations by nothing",
        ["applier:UpsertGameConfiguration.Name"] = "presentation only",
        ["applier:UpsertGameConfiguration.Description"] = "presentation only",
        ["writer:UpsertGameConfiguration.Live"] = "IsActive moves only through activation",
        ["applier:UpsertGameConfiguration.WorldKeys"] = "which worlds a configuration owns is export scope, not loop state",
        ["applier:UpsertGameConfiguration.StartingKit"] = "a kit is read from the row when a character is made, which the loop never sees",
    };

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoPath.Root(), .. parts]));

    /// <summary>Every <c>Upsert*</c> record and the positional fields it declares.</summary>
    private static IEnumerable<(string Name, IReadOnlyList<string> Fields)> ChangeRecords()
    {
        var source = Source("src", "Muwbta.Engine", "Mutations", "WorldChange.cs");

        var matches = Regex.Matches(
            source,
            @"public sealed record (Upsert\w+)\(((?:[^()]|\([^()]*\))*)\)\s*:\s*WorldChange",
            RegexOptions.Singleline);

        Assert.NotEmpty(matches);

        foreach (Match match in matches)
        {
            yield return (match.Groups[1].Value, PositionalFields(match.Groups[2].Value));
        }
    }

    /// <summary>
    /// The parameter names of a positional record, split on top-level commas only.
    /// </summary>
    /// <remarks>
    /// Depth-aware because the types are generic: a naive split on ',' turns
    /// <c>Dictionary&lt;string, object&gt; BaseStats</c> into two parameters, one of which is
    /// "object" — which then reports as missing on every record that carries a bag.
    /// </remarks>
    private static IReadOnlyList<string> PositionalFields(string parameters)
    {
        var fields = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length > 0)
            {
                // Before any default: `List<T>? Kit = null` is the field Kit, not the field null.
                fields.Add(text.Split('=')[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
            }

            current.Clear();
        }

        foreach (var c in parameters)
        {
            if (c is '<' or '(' or '[')
            {
                depth++;
            }
            else if (c is '>' or ')' or ']')
            {
                depth--;
            }

            if (c == ',' && depth == 0)
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return fields;
    }

    /// <summary>
    /// The code that consumes one change: a dedicated <c>Apply…</c>/<c>case</c> block, or the
    /// inline switch arm for the handful handled in one expression.
    /// </summary>
    private static string? ConsumerBody(string source, string record)
    {
        foreach (var pattern in new[]
        {
            $@"Apply{record}\({record} \w+\)\s*\{{(.*?)\n    \}}",
            $@"case {record} c:\s*\{{(.*?)\n            \}}",
            $@"{record} change =>(.*?),\n            [A-Z]",
        })
        {
            var match = Regex.Match(source, pattern, RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static void AssertEveryFieldIsRead(string consumer, string sourcePath, params string[] parts)
    {
        var source = Source(parts);
        var unread = new List<string>();

        foreach (var (record, fields) in ChangeRecords())
        {
            var body = ConsumerBody(source, record);

            if (body is null)
            {
                unread.Add($"{record} — {sourcePath} has no branch for it at all");
                continue;
            }

            foreach (var field in fields)
            {
                if (Exempt.ContainsKey($"{consumer}:{record}.{field}"))
                {
                    continue;
                }

                // `change.Field` or `c.Field`, with a word boundary so `Name` does not match
                // `TemplateName` and report a field as read when it is not.
                if (!Regex.IsMatch(body, $@"\b\w+\.{Regex.Escape(field)}\b"))
                {
                    unread.Add($"{record}.{field}");
                }
            }
        }

        Assert.True(
            unread.Count == 0,
            $"{sourcePath} never reads these change fields, so they are accepted everywhere and "
            + "land nowhere. Either consume them or add an argued exemption to "
            + $"{nameof(ChangeRecordCompletenessTests)}.{nameof(Exempt)}:\n  "
            + string.Join("\n  ", unread));
    }

    /// <summary>Miss one here and the field does nothing until the server restarts.</summary>
    [Fact]
    public void The_applier_reads_every_field_of_every_change()
    {
        AssertEveryFieldIsRead(
            "applier",
            "WorldMutationApplier",
            "src", "Muwbta.Engine", "Mutations", "WorldMutationApplier.cs");
    }

    /// <summary>Miss one here and the field works until the server restarts.</summary>
    /// <remarks>
    /// The writer lives in <c>Muwbta.Server</c> while the change records live in
    /// <c>Muwbta.Engine</c>, so this reaches across a project boundary. That is the point: the
    /// contract belongs to the record, and a guard that could only see one side of it would have
    /// caught neither of the two bugs that produced this file.
    /// </remarks>
    [Fact]
    public void The_writer_reads_every_field_of_every_change()
    {
        AssertEveryFieldIsRead(
            "writer",
            "WorldWriter",
            "src", "Muwbta.Server", "Building", "WorldWriter.cs");
    }
}
