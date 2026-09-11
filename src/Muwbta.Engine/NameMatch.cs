namespace Muwbta.Engine;

/// <summary>
/// Matches what a player typed against a thing's display name and template key.
/// </summary>
/// <remarks>
/// Every targeting command used to demand something exact: items matched the full display name
/// or the full key, so <c>destroy coin</c> failed on "old coin" and only <c>destroy old-coin</c>
/// worked. Mobs matched with <c>TemplateKey.EndsWith</c>, which accepted "rat" for "giant-rat"
/// but also accepted "t", and never looked at the display name at all.
///
/// Matching is derived from the name rather than authored, so no content needs a keyword list
/// for the common case: a builder who writes "old coin" gets "coin", "old", and "co" for free.
/// Ranked rather than boolean, so when several things in a room answer to the same word, the
/// closest one wins instead of whichever happened to be first in the list.
/// </remarks>
public static class NameMatch
{
    /// <summary>No match. Higher-numbered ranks are worse matches.</summary>
    private const int NoMatch = int.MaxValue;

    /// <summary>
    /// How well <paramref name="typed"/> identifies the thing, or null when it does not.
    /// </summary>
    public static int? Rank(string? typed, string? name, string? key)
    {
        var needle = typed?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return null;
        }

        var rank = RankOfAnyForm(needle, name, key);
        return rank == NoMatch ? null : rank;
    }

    /// <summary>True when what was typed identifies this thing at all.</summary>
    public static bool Matches(string? typed, string? name, string? key) =>
        Rank(typed, name, key) is not null;

    /// <summary>
    /// True when what was typed is one of the words of <paramref name="name"/>, or the plural of
    /// one: not a prefix, and not the key.
    /// </summary>
    /// <remarks>
    /// The question a quest summary is asked (<c>BundleValidator</c>): does the sentence a player
    /// reads name the thing they are meant to bring, in words they could type back?
    /// </remarks>
    public static bool NamesAWordOf(string? typed, string? name) =>
        RankOfAnyForm(typed?.Trim(), name, key: null) is 0 or 2 or 4;

    /// <summary>
    /// The best match in <paramref name="candidates"/>, or null when nothing answers to it.
    /// Ties keep the earlier candidate, so room order still breaks a genuine draw.
    /// </summary>
    /// <remarks>
    /// <b>A trailing number picks between equals: <c>attack crow 2</c>.</b> This is what makes the
    /// ordinals <see cref="MobLabel"/> shows typeable — a label a player can read and not act on
    /// sends them to "you don't see that here", which is worse than not numbering them at all.
    ///
    /// <b>Tried only after the whole string fails.</b> A mob genuinely called "guard 2" matches
    /// exactly on the first pass and is returned, so authored names that end in a digit keep
    /// working; the ordinal reading is a fallback rather than a parse, which is the ordering that
    /// makes it safe to apply to items and everything else as well.
    /// </remarks>
    public static T? Best<T>(
        IEnumerable<T> candidates,
        string? typed,
        Func<T, string?> name,
        Func<T, string?> key)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(key);

        var needle = typed?.Trim();

        T? best = null;
        var bestRank = NoMatch;

        foreach (var candidate in candidates)
        {
            var rank = RankOfAnyForm(needle, name(candidate), key(candidate));
            if (rank < bestRank)
            {
                best = candidate;
                bestRank = rank;

                if (rank == 0)
                {
                    break;
                }
            }
        }

        return best ?? Nth(candidates, needle, name, key);
    }

    /// <summary>
    /// Reads <paramref name="needle"/> as "<c>&lt;word&gt; &lt;n&gt;</c>" and returns the nth thing
    /// that answers to the word, in rank order. Null when it does not read that way, or when there
    /// is no nth one.
    /// </summary>
    private static T? Nth<T>(
        IEnumerable<T> candidates,
        string? needle,
        Func<T, string?> name,
        Func<T, string?> key)
        where T : class
    {
        if (string.IsNullOrEmpty(needle))
        {
            return null;
        }

        var split = needle.LastIndexOf(' ');
        if (split <= 0 ||
            !int.TryParse(
                needle.AsSpan(split + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var wanted) ||
            wanted < 1)
        {
            return null;
        }

        var word = needle[..split].TrimEnd();

        // Ordered by rank, ties keeping room order - the same rule the single-match path uses, so
        // "(2)" in the room listing and "crow 2" at the prompt agree about which one is second.
        // OrderBy is stable, which is what carries the tie-break through.
        var ranked = candidates
            .Select(c => (Candidate: c, Rank: RankOfAnyForm(word, name(c), key(c))))
            .Where(x => x.Rank != NoMatch)
            .OrderBy(x => x.Rank)
            .ToList();

        return wanted <= ranked.Count ? ranked[wanted - 1].Candidate : null;
    }

    /// <summary>
    /// <see cref="RankOf"/>, and when that fails, the same question asked of the singular.
    /// </summary>
    /// <remarks>
    /// <b>Players type plurals, and names are singular.</b> A quest asks for "eight crew tags" and
    /// the item is "a crew tag", so <c>give tags vance</c> was "You don't have tags." Tried only
    /// after the word as typed has failed, so nothing that matched before matches differently now,
    /// and the best of the candidate singulars wins - "pages" tries "pag" and "page", and "page" is
    /// the whole word. Only the last word is changed, since that is the noun.
    /// </remarks>
    private static int RankOfAnyForm(string? needle, string? name, string? key)
    {
        var rank = RankOf(needle, name, key);
        if (rank != NoMatch || string.IsNullOrEmpty(needle))
        {
            return rank;
        }

        foreach (var form in Singulars(needle))
        {
            rank = Math.Min(rank, RankOf(form, name, key));
        }

        return rank;
    }

    /// <summary>The ways the last word of <paramref name="needle"/> might be a plural, undone.</summary>
    private static IEnumerable<string> Singulars(string needle)
    {
        const StringComparison Ordinal = StringComparison.OrdinalIgnoreCase;

        var split = needle.LastIndexOf(' ');
        var head = needle[..(split + 1)];
        var word = needle[(split + 1)..];

        if (word.Length < 3 || !word.EndsWith("s", Ordinal) || word.EndsWith("ss", Ordinal))
        {
            yield break;
        }

        if (word.EndsWith("ies", Ordinal))
        {
            yield return head + word[..^3] + "y";       // berries -> berry
        }

        if (word.EndsWith("ves", Ordinal))
        {
            yield return head + word[..^3] + "f";       // leaves -> leaf
            yield return head + word[..^3] + "fe";      // knives -> knife
        }

        if (word.EndsWith("es", Ordinal))
        {
            yield return head + word[..^2];             // boxes -> box
        }

        yield return head + word[..^1];                 // tags -> tag
    }

    private static int RankOf(string? needle, string? name, string? key)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return NoMatch;
        }

        const StringComparison Ordinal = StringComparison.OrdinalIgnoreCase;

        // An exact name is unambiguous and beats everything, so "old coin" always finds the
        // old coin even in a room that also holds an "old coin purse".
        if (!string.IsNullOrEmpty(name) && name.Equals(needle, Ordinal))
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(key) && key.Equals(needle, Ordinal))
        {
            return 1;
        }

        // A whole word, which is how players actually refer to things: "coin", "blade", "rat".
        // The last word ranks best - "dagger" should find the rusty dagger rather than the
        // dagger hilt, because the last word is the noun in English noun phrases.
        var nameWords = Words(name);
        var keyWords = Words(key);

        var lastNameWord = nameWords.Count > 0 ? nameWords[^1] : null;
        if (lastNameWord is not null && lastNameWord.Equals(needle, Ordinal))
        {
            return 2;
        }

        var lastKeyWord = keyWords.Count > 0 ? keyWords[^1] : null;
        if (lastKeyWord is not null && lastKeyWord.Equals(needle, Ordinal))
        {
            return 3;
        }

        if (nameWords.Any(w => w.Equals(needle, Ordinal)))
        {
            return 4;
        }

        if (keyWords.Any(w => w.Equals(needle, Ordinal)))
        {
            return 5;
        }

        // A prefix of a word, so "dag" reaches the dagger. Whole-name prefixes come first so
        // "old c" still finds "old coin".
        if (!string.IsNullOrEmpty(name) && name.StartsWith(needle, Ordinal))
        {
            return 6;
        }

        if (!string.IsNullOrEmpty(key) && key.StartsWith(needle, Ordinal))
        {
            return 7;
        }

        if (lastNameWord is not null && lastNameWord.StartsWith(needle, Ordinal))
        {
            return 8;
        }

        if (nameWords.Any(w => w.StartsWith(needle, Ordinal)))
        {
            return 9;
        }

        if (keyWords.Any(w => w.StartsWith(needle, Ordinal)))
        {
            return 10;
        }

        return NoMatch;
    }

    /// <summary>
    /// The words in a name or key. Keys are hyphenated ("rusty-dagger") and names are spaced
    /// ("a rusty dagger"), so both separators split - a key and a name should answer to the
    /// same words whichever the builder happened to type it in.
    /// </summary>
    private static List<string> Words(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : [.. text.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)];
}
