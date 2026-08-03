namespace JobHunter.Application.Normalization;

/// <summary>
/// The curated technology vocabulary and its word-boundary matcher (T07, data-model §job_technologies).
/// It maps every accepted spelling of a technology — the canonical name and its aliases (<c>"golang"</c> →
/// <c>"Go"</c>) — onto the one canonical name, so the same skill from two postings tags identically. The
/// match is deterministic and vocabulary-only: no inference, no model. F3 later writes model-extracted
/// technologies elsewhere, so this deterministic set stays separable from the inferred one.
///
/// <para>Matching is <strong>whole-token, never substring</strong>: a term is a hit only when the
/// characters flanking it are not part of the same token, so <c>"Go"</c> matches <c>"using Go"</c> but not
/// <c>"Google"</c> or <c>"Django"</c>. Symbols that live inside technology names — <c>+</c>, <c>#</c>,
/// <c>.</c> — count as part of a token, so <c>"C++"</c>, <c>"C#"</c> and <c>"Node.js"</c> match whole while
/// <c>".NET"</c> does not match inside <c>"ASP.NET"</c>. Comparison is ordinal on lower-cased text, so the
/// same description tags identically on every machine and culture (SAD S5).</para>
/// </summary>
public sealed class TechnologyVocabulary
{
    private readonly List<(string Term, string Canonical)> _terms;
    private readonly List<string> _canonicalNames;

    /// <summary>
    /// Builds the vocabulary from <paramref name="entries"/>. Each entry contributes its canonical name and
    /// every alias as a search term. A blank canonical name, a duplicated canonical name, or a term claimed
    /// by two different canonical names is a construction failure — the vocabulary would be ambiguous, so it
    /// is rejected at load time rather than tagging inconsistently at run time.
    /// </summary>
    public TechnologyVocabulary(IEnumerable<TechnologyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _terms = [];
        _canonicalNames = [];
        var seenCanonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var termOwner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (string.IsNullOrWhiteSpace(entry.Canonical))
            {
                throw new ArgumentException("A technology entry has a blank canonical name.", nameof(entries));
            }

            var canonical = entry.Canonical.Trim();
            if (!seenCanonical.Add(canonical))
            {
                throw new ArgumentException(
                    $"The technology vocabulary repeats the canonical name '{canonical}'.", nameof(entries));
            }

            _canonicalNames.Add(canonical);

            RegisterTerm(canonical, canonical, termOwner);
            foreach (var alias in entry.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    RegisterTerm(alias.Trim(), canonical, termOwner);
                }
            }
        }
    }

    /// <summary>The number of canonical technologies in the vocabulary.</summary>
    public int Count => _canonicalNames.Count;

    /// <summary>The canonical names, in file order.</summary>
    public IReadOnlyList<string> CanonicalNames => _canonicalNames;

    /// <summary>
    /// The canonical names whose canonical spelling or one of its aliases occurs as a whole token in
    /// <paramref name="text"/>. Deterministic order (first occurrence in vocabulary order); each canonical
    /// name appears at most once even when several of its spellings match.
    /// </summary>
    public IReadOnlyList<string> Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var haystack = text.ToLowerInvariant();
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (term, canonical) in _terms)
        {
            if (seen.Contains(canonical))
            {
                continue;
            }

            if (ContainsWholeToken(haystack, term))
            {
                seen.Add(canonical);
                found.Add(canonical);
            }
        }

        return found;
    }

    private void RegisterTerm(string term, string canonical, Dictionary<string, string> termOwner)
    {
        var key = term.ToLowerInvariant();
        if (termOwner.TryGetValue(key, out var owner))
        {
            if (!string.Equals(owner, canonical, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The technology spelling '{term}' is claimed by both '{owner}' and '{canonical}'.",
                    nameof(canonical));
            }

            return;
        }

        termOwner[key] = canonical;
        _terms.Add((key, canonical));
    }

    private static bool ContainsWholeToken(string haystack, string term)
    {
        if (term.Length == 0 || term.Length > haystack.Length)
        {
            return false;
        }

        var from = 0;
        while (true)
        {
            var index = haystack.IndexOf(term, from, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var end = index + term.Length;
            var leftOk = BoundaryOk(haystack, index - 1, index - 2);
            var rightOk = BoundaryOk(haystack, end, end + 1);
            if (leftOk && rightOk)
            {
                return true;
            }

            from = index + 1;
        }
    }

    // A match is whole-token when the character just outside it (at `edge`) does not glue it to a larger
    // token. A letter or digit always glues ("Go" inside "Google" — rejected). The in-name symbols +, #, .
    // glue only when the character beyond them (`beyond`) is itself a token character, i.e. they sit in the
    // middle of a compound name (".NET" inside "ASP.NET" — rejected); a trailing '.' before a space or the
    // end of text is sentence punctuation, not part of the name ("Node.js." — accepted). Separators like
    // space, '-' and '/' never glue, so "TCP/IP" and "React-Native" split into their tokens.
    private static bool BoundaryOk(string haystack, int edge, int beyond)
    {
        if (edge < 0 || edge >= haystack.Length)
        {
            return true;
        }

        var edgeChar = haystack[edge];
        if (char.IsLetterOrDigit(edgeChar))
        {
            return false;
        }

        if (edgeChar is '+' or '#' or '.')
        {
            var beyondChar = beyond >= 0 && beyond < haystack.Length ? haystack[beyond] : '\0';
            return !IsTokenChar(beyondChar);
        }

        return true;
    }

    private static bool IsTokenChar(char c) =>
        char.IsLetterOrDigit(c) || c is '+' or '#' or '.';
}

/// <summary>
/// One curated technology: its canonical name (the spelling written to <c>job_technologies</c>) and the
/// alternative spellings that map onto it. A pure input record for <see cref="TechnologyVocabulary"/>.
/// </summary>
public sealed record TechnologyEntry(string Canonical, IReadOnlyList<string> Aliases)
{
    /// <summary>An entry with no aliases — the canonical spelling is the only accepted one.</summary>
    public TechnologyEntry(string canonical) : this(canonical, [])
    {
    }
}
