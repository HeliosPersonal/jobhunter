using System.Globalization;
using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Http;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Detection;

/// <summary>
/// Detects where a company's jobs live by probing each provider and scoring the evidence
/// (contract §Detection probes, AC-03/AC-04). It never guesses: a candidate only scores if a real fetch
/// returned at least one posting, and the full probe trail — scoring and non-scoring — is recorded as the
/// binding's evidence so a wrong binding is explainable without re-running detection. The decision is
/// deliberately conservative: two providers both scoring ≥ 0.80 is <see cref="DetectionStatus.Ambiguous"/>
/// and the company stays inactive, because attributing another company's jobs is a far worse failure than
/// missing a company.
/// </summary>
public sealed class AtsProbeDetector : IBindingDetector
{
    // Detection table (contract §Detection probes). A responding board is required; the rest are bonuses.
    private const decimal RespondedWeight = 0.60m;
    private const decimal ApplyUrlMatchWeight = 0.25m;
    private const decimal CareersLinkWeight = 0.10m;
    private const decimal ExactTokenWeight = 0.05m;

    // How many postings to read before deciding a board "responded". One is enough evidence; a handful
    // lets the apply-URL check see more than the first posting without draining a 400-posting board.
    private const int ProbeSampleSize = 5;

    private readonly IReadOnlyList<IJobSource> _sources;
    private readonly GatedHttpClient _http;
    private readonly IIdGenerator _ids;
    private readonly IClock _clock;
    private readonly ILogger<AtsProbeDetector> _logger;

    public AtsProbeDetector(
        IEnumerable<IJobSource> sources,
        GatedHttpClient http,
        IIdGenerator ids,
        IClock clock,
        ILogger<AtsProbeDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToList();
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Probes every provider for <paramref name="company"/> and resolves a binding, no board, or
    /// ambiguity. The returned <see cref="DetectionResult.Binding"/> (when present) is unsaved — the caller
    /// persists it and decides activation.
    /// </summary>
    public async Task<DetectionResult> DetectAsync(Company company, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(company);

        var tokens = CandidateTokens.Derive(company.CanonicalDomain, company.CareersUrl);
        var careersHtml = await FetchCareersPageAsync(company.CareersUrl, cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<ProbeCandidate>();
        foreach (var source in _sources)
        {
            foreach (var token in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = await ProbeAsync(company, source, token, careersHtml, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return Decide(company, candidates);
    }

    /// <summary>
    /// The <see cref="IBindingDetector"/> port (SAD §6.2): probes the company and projects the rich
    /// <see cref="DetectionResult"/> onto the Domain-level <see cref="BindingDetectionResult"/> the
    /// Application re-detection handler consumes, so the handler never references this scrapers type.
    /// </summary>
    async Task<BindingDetectionResult> IBindingDetector.DetectAsync(
        Company company,
        CancellationToken cancellationToken)
    {
        var result = await DetectAsync(company, cancellationToken).ConfigureAwait(false);
        var status = result.Status switch
        {
            DetectionStatus.Bound => BindingDetectionStatus.Bound,
            DetectionStatus.Ambiguous => BindingDetectionStatus.Ambiguous,
            _ => BindingDetectionStatus.NoBoardFound,
        };
        return new BindingDetectionResult(status, result.Binding);
    }

    private async Task<ProbeCandidate?> ProbeAsync(
        Company company,
        IJobSource source,
        CandidateToken token,
        string? careersHtml,
        CancellationToken cancellationToken)
    {
        var probeBinding = new AtsBinding(
            _ids.NewId(),
            company.Id,
            source.Kind,
            token.Token,
            BindingConfidence.TryCreate(RespondedWeight).Value,
            "{}",
            _clock.UtcNow);

        var sampled = new List<FetchedPosting>();
        await foreach (var posting in source.FetchAsync(probeBinding, cancellationToken).ConfigureAwait(false))
        {
            sampled.Add(posting);
            if (sampled.Count >= ProbeSampleSize)
            {
                break;
            }
        }

        if (sampled.Count == 0)
        {
            // A non-responding probe is not a candidate, but it is still part of the trail so a
            // NoBoardFound is explainable. We record it with a zero score.
            return new ProbeCandidate(source.Kind, token.Token, 0m, 0, false, false, false, token.DerivedFromDomainExactly);
        }

        var applyMatches = sampled.Any(p =>
            p.RawPayload.Contains(company.CanonicalDomain.Value, StringComparison.OrdinalIgnoreCase));
        var careersLinks = careersHtml is not null
            && BoardHost(source.Kind) is { } host
            && careersHtml.Contains(host, StringComparison.OrdinalIgnoreCase);

        var score = RespondedWeight
            + (applyMatches ? ApplyUrlMatchWeight : 0m)
            + (careersLinks ? CareersLinkWeight : 0m)
            + (token.DerivedFromDomainExactly ? ExactTokenWeight : 0m);

        return new ProbeCandidate(
            source.Kind,
            token.Token,
            decimal.Round(score, 2, MidpointRounding.AwayFromZero),
            sampled.Count,
            RespondedWithPostings: true,
            applyMatches,
            careersLinks,
            token.DerivedFromDomainExactly);
    }

    private DetectionResult Decide(Company company, List<ProbeCandidate> candidates)
    {
        // Collapse to the best candidate per provider so two tokens of the same provider are one signal;
        // ambiguity is two distinct providers both scoring, not two spellings of one.
        var bestPerKind = candidates
            .GroupBy(c => c.Kind)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .ToList();

        var confident = bestPerKind
            .Where(c => c.Score >= BindingConfidence.DiscoveryThreshold)
            .OrderByDescending(c => c.Score)
            .ToList();

        if (confident.Count == 0)
        {
            _logger.LogInformation(
                "Detection for {Domain}: no board reached the discovery threshold ({Probed} probe(s)).",
                company.CanonicalDomain.Value, candidates.Count);
            return new DetectionResult(DetectionStatus.NoBoardFound, null, candidates);
        }

        if (confident.Count > 1)
        {
            _logger.LogWarning(
                "Detection for {Domain}: {Count} providers scored ≥ threshold; left ambiguous.",
                company.CanonicalDomain.Value, confident.Count);
            return new DetectionResult(DetectionStatus.Ambiguous, null, candidates);
        }

        var winner = confident[0];
        var binding = new AtsBinding(
            _ids.NewId(),
            company.Id,
            winner.Kind,
            winner.Token,
            BindingConfidence.TryCreate(winner.Score).Value,
            SerializeEvidence(candidates),
            _clock.UtcNow);

        return new DetectionResult(DetectionStatus.Bound, binding, candidates);
    }

    private async Task<string?> FetchCareersPageAsync(string? careersUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(careersUrl))
        {
            return null;
        }

        var response = await _http.GetAsync(careersUrl, cancellationToken).ConfigureAwait(false);
        return response.Outcome == GatedOutcome.Ok ? response.Body : null;
    }

    private static string? BoardHost(AtsKind kind) => kind switch
    {
        AtsKind.Greenhouse => "greenhouse.io",
        AtsKind.Lever => "lever.co",
        AtsKind.Ashby => "ashbyhq.com",
        AtsKind.Workable => "workable.com",
        _ => null,
    };

    private string SerializeEvidence(IReadOnlyList<ProbeCandidate> candidates) =>
        JsonSerializer.Serialize(
            new DetectionEvidence(_clock.UtcNow.ToString("O", CultureInfo.InvariantCulture), candidates),
            DetectionEvidenceJson.Options);
}
