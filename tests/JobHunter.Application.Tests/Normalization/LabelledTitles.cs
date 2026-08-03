using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// The labelled title corpus behind T02's accuracy gate (≥ 95%). Each case pairs a published title with
/// the normalised comparison form and seniority the <see cref="TitleNormalizer"/> is expected to
/// produce. Adversarial pairs (decoration that must vanish, teams that must survive, levels that must be
/// canonicalised) are included deliberately — the corpus is the specification.
/// </summary>
internal static class LabelledTitles
{
    public sealed record Case(string Title, string ExpectedNormalised, Seniority? ExpectedSeniority);

    public static readonly Case[] Cases =
    [
        new("Software Engineer", "software engineer", null),
        new("Backend Engineer", "backend engineer", null),
        new("Frontend Engineer", "frontend engineer", null),
        new("Full-Stack Engineer", "full-stack engineer", null),
        new("Data Engineer", "data engineer", null),
        new("Platform Engineer", "platform engineer", null),
        new("Site Reliability Engineer", "site reliability engineer", null),
        new("DevOps Engineer", "devops engineer", null),
        new("Machine Learning Engineer", "machine learning engineer", null),
        new("Product Manager", "product manager", Seniority.Manager),

        new("Senior Backend Engineer", "senior backend engineer", Seniority.Senior),
        new("Sr. Backend Engineer", "senior backend engineer", Seniority.Senior),
        new("Snr Backend Engineer", "senior backend engineer", Seniority.Senior),
        new("Backend Engineer III", "backend engineer senior", Seniority.Senior),
        new("Senior Software Engineer", "senior software engineer", Seniority.Senior),
        new("Sr Frontend Engineer", "senior frontend engineer", Seniority.Senior),

        new("Junior Developer", "junior developer", Seniority.Junior),
        new("Jr Developer", "junior developer", Seniority.Junior),
        new("Jnr Developer", "junior developer", Seniority.Junior),
        new("Graduate Software Engineer", "junior software engineer", Seniority.Junior),
        new("Grad Developer", "junior developer", Seniority.Junior),

        new("Staff Engineer", "staff engineer", Seniority.Staff),
        new("Staff Software Engineer", "staff software engineer", Seniority.Staff),
        new("Principal Engineer", "principal engineer", Seniority.Principal),
        new("Principal Software Engineer", "principal software engineer", Seniority.Principal),

        new("Engineering Manager", "engineering manager", Seniority.Manager),
        new("Engineering Mgr", "engineering manager", Seniority.Manager),
        new("Tech Lead", "tech lead", Seniority.Lead),
        new("Lead Developer", "lead developer", Seniority.Lead),

        new("Developer II", "developer mid", Seniority.Mid),
        new("Intermediate Developer", "mid developer", Seniority.Mid),
        new("Mid-Level Engineer", "mid engineer", Seniority.Mid),
        new("Mid Engineer", "mid engineer", Seniority.Mid),

        new("Backend Engineer (Remote)", "backend engineer", null),
        new("Backend Engineer - EMEA", "backend engineer", null),
        new("Backend Engineer [Contract]", "backend engineer", null),
        new("Backend Engineer (m/f/d)", "backend engineer", null),
        new("Software Engineer - Remote", "software engineer", null),
        new("Data Engineer (Berlin)", "data engineer", null),
        new("Senior Data Engineer (Remote)", "senior data engineer", Seniority.Senior),

        new("Backend Engineer | Payments", "backend engineer payments", null),
        new("Backend Engineer | Growth", "backend engineer growth", null),
        new("Software Engineer | Infrastructure", "software engineer infrastructure", null),
        new("Senior Engineer | Search", "senior engineer search", Seniority.Senior),

        new("QA Engineer", "qa engineer", null),
        new("Security Engineer", "security engineer", null),
        new("Mobile Engineer", "mobile engineer", null),
        new("iOS Engineer", "ios engineer", null),
        new("Android Engineer", "android engineer", null),
        new("Cloud Engineer", "cloud engineer", null),
        new("Solutions Architect", "solutions architect", null),
        new("Engineering Team Lead", "engineering team lead", Seniority.Lead),
        new("Senior Staff Engineer", "senior staff engineer", Seniority.Senior),
        new("Principal Software Architect", "principal software architect", Seniority.Principal),
        new("Backend Developer", "backend developer", null),
        new("Full Stack Developer", "full stack developer", null),
        new("Senior Full Stack Developer", "senior full stack developer", Seniority.Senior),
        new("Database Administrator", "database administrator", null),
        new("Systems Engineer", "systems engineer", null),
        new("Network Engineer", "network engineer", null),
    ];
}
