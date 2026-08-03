using JobHunter.Domain.Companies;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The port the re-detection handler probes through (SAD §5, §6.2). Its one implementation lives in
/// <c>JobHunter.Scrapers</c> (the <c>AtsProbeDetector</c> adapter), so the Application layer re-detects a
/// company's ATS binding without referencing the scrapers — a new provider is a change to the adapter,
/// never to the handler. The returned <see cref="BindingDetectionResult.Binding"/> is unsaved; the caller
/// persists it and decides migration (AC-05).
/// </summary>
public interface IBindingDetector
{
    Task<BindingDetectionResult> DetectAsync(Company company, CancellationToken cancellationToken = default);
}
