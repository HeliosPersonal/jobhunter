using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Preferences;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/forget &lt;dimension&gt;</c> (catalogue §Profile, F10 T08): switches the learned weight(s) for a named
/// dimension off, through the same <see cref="DisablePreferenceWeightHandler"/> the API disable endpoint uses —
/// one write path, so a disabled weight stays inspectable and is not relearned until its evidence doubles (F7
/// AC-06). The reply states plainly that the change takes effect on the <strong>next ranking, not mid-Run</strong>
/// (AC-05): the exclusion lives in the read path, so an in-flight Run's ordering stays internally consistent.
///
/// <para>Forgiving by construction: with no argument it lists the dimensions that carry an active weight so the
/// Owner can pick (the missing-argument rule, never an error); an unknown dimension names the valid ones; a
/// dimension the model learned nothing about is reported, not silently disabled. It reads and writes only learned
/// facts — never the CV, which crosses exactly one boundary and not this one. Every value reaches the reply
/// through the one MarkdownV2 escaper.</para>
/// </summary>
internal sealed class ForgetCommandHandler(
    ActiveWeightsQuery weights,
    DisablePreferenceWeightHandler disable,
    IClock clock,
    ILogger<ForgetCommandHandler> logger) : ICommandHandler
{
    private readonly ActiveWeightsQuery _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    private readonly DisablePreferenceWeightHandler _disable = disable ?? throw new ArgumentNullException(nameof(disable));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<ForgetCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var learned = await _weights.ActiveAsync(cancellationToken).ConfigureAwait(false);
        if (learned.Count == 0)
        {
            // Nothing has been learned to forget — say so rather than offering an empty pick-list.
            return [Plain("There are no learned preferences to forget yet.")];
        }

        // The dimensions that actually carry an active (non-disabled) weight — the only ones worth forgetting.
        var forgettable = learned
            .Where(w => !w.Disabled)
            .Select(w => w.Dimension)
            .Distinct()
            .ToList();

        var argument = request.Arguments?.Trim();
        if (string.IsNullOrWhiteSpace(argument))
        {
            // The missing-argument rule (catalogue §Argument parsing): list the choices, never an error.
            return [Plain("Which preference should I forget? " + PickList(forgettable))];
        }

        if (!TryParseDimension(argument, out var dimension))
        {
            _logger.LogDebug("/forget given an unknown dimension name.");
            return [Plain($"I don't recognise \"{argument}\". {PickList(forgettable)}")];
        }

        var toDisable = learned.Where(w => w.Dimension == dimension && !w.Disabled).ToList();
        if (toDisable.Count == 0)
        {
            // A valid dimension the model has no active opinion on: report it, disable nothing.
            return [Plain($"I've learned nothing about {FriendlyName(dimension)} to forget. {PickList(forgettable)}")];
        }

        foreach (var weight in toDisable)
        {
            await _disable
                .Handle(new DisablePreferenceWeightCommand(weight.WeightId, _clock.UtcNow), cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Owner forgot {Count} learned weight(s) for {Dimension}.", toDisable.Count, dimension);

        var what = toDisable.Count == 1 ? "that preference" : $"those {toDisable.Count} preferences";
        return [Plain(
            $"Done — I've switched off {what} for {FriendlyName(dimension)}. "
            + "It takes effect on the next ranking, not the Run already in flight.")];
    }

    // The friendly names the Owner types, mapped to the closed Dimension set. Accepts the display name and the
    // enum name, case-insensitively, so both "salary" and "salaryband" resolve.
    private static bool TryParseDimension(string argument, out Dimension dimension)
    {
        dimension = default;
        foreach (var candidate in Enum.GetValues<Dimension>())
        {
            if (string.Equals(argument, FriendlyName(candidate), StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                dimension = candidate;
                return true;
            }
        }

        return false;
    }

    private static string FriendlyName(Dimension dimension) => dimension switch
    {
        Dimension.SalaryBand => "salary",
        Dimension.Country => "country",
        Dimension.CompanySize => "company size",
        Dimension.Technology => "technology",
        Dimension.TimezoneBand => "timezone",
        Dimension.RemotePolicy => "remote policy",
        Dimension.EmploymentType => "employment type",
        Dimension.AiUsage => "AI usage",
        Dimension.RoleFamily => "role family",
        _ => dimension.ToString(),
    };

    private static string PickList(List<Dimension> dimensions) =>
        dimensions.Count == 0
            ? "There's nothing left to forget."
            : "You can forget: " + string.Join(", ", dimensions.Select(FriendlyName)) + ".";

    // A single plain line, escaped, so a value with MarkdownV2 punctuation always renders literally.
    private static RenderedMessage Plain(string text) =>
        RenderedMessage.PlainText(MarkdownV2Escaper.Escape(text));
}
