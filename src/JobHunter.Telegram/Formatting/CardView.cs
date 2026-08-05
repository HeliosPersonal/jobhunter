namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The display projection of one <see cref="Domain.Reporting.DigestCard"/> — everything the
/// <see cref="CardFormatter"/> needs to render a card and nothing else (F5 message contract §Card). The
/// domain card snapshots only the score, reasons and idempotence key; the human-facing strings (title,
/// company, salary) are joined in from the <c>jobs</c> read model at delivery, so this view is where they
/// meet the layout. It carries <strong>nothing about the Owner</strong> — no CV text — only what the card
/// itself shows (the CV crosses exactly one boundary, and it is not this one).
///
/// <para>Every string here is raw, untrusted job-board text: the formatter escapes each one. The three
/// reasons are the ranking's own explanation (invariant 4), already ≤ 90 chars and de-newlined by the
/// caller — but the formatter still normalises defensively so a stray newline can never break the layout.</para>
/// </summary>
/// <param name="Title">The job title, truncated to 60 graphemes at a word boundary by the formatter.</param>
/// <param name="Company">The company name.</param>
/// <param name="Stage">The company funding stage, or null/blank when <c>Unknown</c> (then omitted).</param>
/// <param name="Location">The location summary — countries joined, or the remote policy.</param>
/// <param name="Salary">The salary line, already composed (published or estimate), or null when absent.</param>
/// <param name="Score">The final 0–100 score, shown as a whole number.</param>
/// <param name="Reasons">Exactly the reasons to show — the formatter takes the first three.</param>
public sealed record CardView(
    string Title,
    string Company,
    string? Stage,
    string Location,
    CardSalary? Salary,
    decimal Score,
    IReadOnlyList<string> Reasons);

/// <summary>
/// A card's salary line (F5 message contract §Card). A published figure is stated plainly; an estimate is
/// marked <c>(est)</c> with its confidence band and is <strong>never presented as fact</strong> — a
/// fabricated certainty about pay is exactly the kind of thing that erodes trust in the whole digest.
/// </summary>
/// <param name="Min">The lower bound in whole currency units (e.g. 150000 — the formatter renders "150k").</param>
/// <param name="Max">The upper bound in whole currency units.</param>
/// <param name="Currency">The currency code (e.g. "EUR"), or null/blank to omit.</param>
/// <param name="IsEstimate">True when this is an estimate, not a published figure.</param>
/// <param name="Confidence">The estimate's confidence band (e.g. "med conf"), shown only when an estimate.</param>
public sealed record CardSalary(
    int Min,
    int Max,
    string? Currency,
    bool IsEstimate,
    string? Confidence);
