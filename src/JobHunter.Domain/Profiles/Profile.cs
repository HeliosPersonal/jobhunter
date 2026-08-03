using System.Collections.ObjectModel;
using JobHunter.Domain.Common;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Profiles;

/// <summary>
/// The Owner's structured career facts (data-model §profiles). Exactly one Profile is active at a time,
/// enforced by a partial unique index rather than a column here. The Profile holds the preferences the
/// Owner stated <em>outright</em> — a salary floor, a home timezone band, preferred countries and
/// acceptable employment types; the <em>learned</em> weights live in F7's separate table so a learned
/// value can never silently overwrite something the Owner said.
///
/// <para>Single Owner: there is no tenant column and no role model (invariant 9). The Profile carries no
/// CV content — that lives only on <see cref="CvVersion.ExtractedText"/> (data-model §cv_versions).</para>
/// </summary>
public sealed class Profile : Entity
{
    private readonly List<string> _preferredCountries = [];
    private readonly List<EmploymentType> _employmentTypes = [];

    /// <summary>
    /// Builds a Profile. A blank display name is rejected. A salary floor is optional, but an amount and
    /// its currency travel together: supplying one without the other is a programmer error, because a
    /// floor without a currency cannot be compared against a job's pay.
    /// </summary>
    public Profile(
        Guid id,
        bool isActive,
        string displayName,
        decimal? salaryFloor,
        string? salaryFloorCurrency,
        TimezoneBand timezoneBand,
        IReadOnlyList<string> preferredCountries,
        IReadOnlyList<EmploymentType> employmentTypes,
        DateTimeOffset updatedAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(preferredCountries);
        ArgumentNullException.ThrowIfNull(employmentTypes);

        var (floor, currency) = NormaliseSalaryFloor(salaryFloor, salaryFloorCurrency);

        IsActive = isActive;
        DisplayName = displayName.Trim();
        SalaryFloor = floor;
        SalaryFloorCurrency = currency;
        TimezoneBand = timezoneBand;
        _preferredCountries = Clean(preferredCountries);
        _employmentTypes = employmentTypes.Distinct().ToList();
        UpdatedAt = updatedAt;
    }

    private Profile()
    {
    }

    /// <summary>True when this is the one active Profile; the partial unique index enforces exactly one.</summary>
    public bool IsActive { get; private set; }

    /// <summary>A human label for the Profile, never used in matching.</summary>
    public string DisplayName { get; private set; } = null!;

    /// <summary>The Owner's salary floor amount, or null when none is set. A down-weight, not a filter (O5).</summary>
    public decimal? SalaryFloor { get; private set; }

    /// <summary>The ISO-4217 currency of <see cref="SalaryFloor"/>; null exactly when the floor is null.</summary>
    public string? SalaryFloorCurrency { get; private set; }

    /// <summary>The Owner's own timezone band, compared against the job's expected overlap.</summary>
    public TimezoneBand TimezoneBand { get; private set; }

    /// <summary>The updated-at stamp, moved forward whenever the Profile changes (IClock-sourced).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Explicit country preferences the Owner stated; F7 learns additional weights separately.</summary>
    public IReadOnlyList<string> PreferredCountries => new ReadOnlyCollection<string>(_preferredCountries);

    /// <summary>The employment types the Owner will consider, e.g. <c>FullTime</c> and <c>Contract</c>.</summary>
    public IReadOnlyList<EmploymentType> EmploymentTypes => new ReadOnlyCollection<EmploymentType>(_employmentTypes);

    private static (decimal? Floor, string? Currency) NormaliseSalaryFloor(decimal? amount, string? currency)
    {
        var hasAmount = amount is not null;
        var hasCurrency = !string.IsNullOrWhiteSpace(currency);

        if (hasAmount != hasCurrency)
        {
            throw new ArgumentException(
                "A salary floor amount and its currency must be supplied together, or neither.",
                nameof(amount));
        }

        if (!hasAmount)
        {
            return (null, null);
        }

        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A salary floor cannot be negative.");
        }

        var code = currency!.Trim().ToUpperInvariant();
        if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A salary floor currency must be a three-letter ISO-4217 code.",
                nameof(currency));
        }

        return (amount, code);
    }

    private static List<string> Clean(IReadOnlyList<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
