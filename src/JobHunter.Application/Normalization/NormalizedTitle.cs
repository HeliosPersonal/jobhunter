using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// The comparison form of a job title and the seniority extracted from it (T02, AC-05). The
/// <see cref="Value"/> is used only for the fingerprint and never displayed — the published title is
/// preserved untouched on the job. <see cref="Seniority"/> is a first-class field, so "Senior Backend
/// Engineer" and "Backend Engineer" produce different normalised titles and therefore never merge.
/// </summary>
public sealed record NormalizedTitle(string Value, Seniority? Seniority);
