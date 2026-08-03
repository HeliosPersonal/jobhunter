using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// T03: the registry upsert service is idempotent and provenance-preserving. A curated seed creates an
/// active company with a confident binding and an operational source; a re-run inserts nothing. A crawled
/// company is proposed inactive and is never activated automatically. Repository ports are substituted so
/// these are pure Application-layer tests with zero database.
/// </summary>
public sealed class CompanyRegistryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 7, 0, 0, TimeSpan.Zero);

    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly IJobSourceRepository _sources = Substitute.For<IJobSourceRepository>();
    private readonly FakeClock _clock = new(Now);
    private readonly SequentialIdGenerator _ids = new();

    private CompanyRegistryService CreateService() =>
        new(_companies, _sources, _clock, _ids, NullLogger<CompanyRegistryService>.Instance);

    [Fact]
    public async Task Seeding_a_new_company_creates_it_active_with_a_confident_binding_and_a_source()
    {
        Company? captured = null;
        AtsBinding? capturedBinding = null;
        JobSource? capturedSource = null;
        await _companies.AddAsync(Arg.Do<Company>(c => captured = c));
        await _companies.AddBindingAsync(Arg.Do<AtsBinding>(b => capturedBinding = b));
        await _sources.AddAsync(Arg.Do<JobSource>(s => capturedSource = s));

        var change = await CreateService().SeedAsync(
        [
            new CompanySeedEntry("stripe.com", "Stripe", AtsKind.Greenhouse, "stripe", "https://stripe.com/jobs", "US"),
        ]);

        change.Inserted.ShouldBe(1);
        change.Skipped.ShouldBe(0);
        change.BindingsAdded.ShouldBe(1);

        captured.ShouldNotBeNull();
        captured!.IsActive.ShouldBeTrue();
        captured.Source.ShouldBe(CompanySource.Curated);
        captured.CanonicalDomain.Value.ShouldBe("stripe.com");

        capturedBinding.ShouldNotBeNull();
        capturedBinding!.Confidence.IsConfident.ShouldBeTrue();
        capturedBinding.AtsKind.ShouldBe(AtsKind.Greenhouse);
        capturedBinding.BoardToken.ShouldBe("stripe");

        capturedSource.ShouldNotBeNull();
        capturedSource!.EndpointUrl.ShouldBe("https://boards-api.greenhouse.io/v1/boards/stripe/jobs");

        await _companies.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seeding_a_company_that_already_exists_inserts_nothing()
    {
        var domain = CanonicalDomain.TryCreate("stripe.com").Value;
        _companies.FindByDomainAsync(Arg.Is<CanonicalDomain>(d => d!.Value == "stripe.com"), Arg.Any<CancellationToken>())
            .Returns(new Company(_ids.NewId(), domain, "Stripe", CompanySource.Curated, Now));

        var change = await CreateService().SeedAsync(
        [
            new CompanySeedEntry("stripe.com", "Stripe", AtsKind.Greenhouse, "stripe"),
        ]);

        change.Inserted.ShouldBe(0);
        change.Skipped.ShouldBe(1);
        change.BindingsAdded.ShouldBe(0);
        await _companies.DidNotReceive().AddAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>());
        await _sources.DidNotReceive().AddAsync(Arg.Any<JobSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seeding_the_same_entries_twice_is_idempotent()
    {
        // First pass: nothing exists, so it inserts. Second pass: the store now returns the created
        // company, so it skips — the T03 "run it twice, zero inserts" guarantee.
        var created = new List<Company>();
        _companies.When(c => c.AddAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>()))
            .Do(ci => created.Add(ci.ArgAt<Company>(0)));
        _companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(ci => created.Find(c => c.CanonicalDomain.Value == ci.ArgAt<CanonicalDomain>(0).Value));

        var entries = new[]
        {
            new CompanySeedEntry("stripe.com", "Stripe", AtsKind.Greenhouse, "stripe"),
        };
        var service = CreateService();

        var first = await service.SeedAsync(entries);
        var second = await service.SeedAsync(entries);

        first.Inserted.ShouldBe(1);
        second.Inserted.ShouldBe(0);
        second.Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task Seeding_an_uncanonicalisable_domain_is_a_programmer_error()
    {
        // The loader validates every row, so an invalid domain reaching the service is a bug, not an
        // expected outcome — it throws rather than returning a value.
        var service = CreateService();

        await Should.ThrowAsync<ArgumentException>(() => service.SeedAsync(
        [
            new CompanySeedEntry("not-a-domain", "Bad", AtsKind.Greenhouse, "bad"),
        ]));
    }

    [Fact]
    public async Task Expanding_a_new_crawled_company_proposes_it_inactive_with_no_binding()
    {
        Company? captured = null;
        await _companies.AddAsync(Arg.Do<Company>(c => captured = c));

        var change = await CreateService().ExpandAsync(
        [
            new CrawledCompany("newco.com", "NewCo"),
        ]);

        change.Inserted.ShouldBe(1);
        change.BindingsAdded.ShouldBe(0);
        captured.ShouldNotBeNull();
        captured!.IsActive.ShouldBeFalse();
        captured.Source.ShouldBe(CompanySource.DirectoryCrawl);
        await _companies.DidNotReceive().AddBindingAsync(Arg.Any<AtsBinding>(), Arg.Any<CancellationToken>());
        await _sources.DidNotReceive().AddAsync(Arg.Any<JobSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expanding_a_junk_domain_skips_it_without_aborting_the_pass()
    {
        var change = await CreateService().ExpandAsync(
        [
            new CrawledCompany("not-a-domain", "Junk"),
            new CrawledCompany("good.com", "Good"),
        ]);

        change.Inserted.ShouldBe(1);
        change.Skipped.ShouldBe(1);
        await _companies.Received(1).AddAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expanding_a_company_already_in_the_registry_skips_it()
    {
        var domain = CanonicalDomain.TryCreate("known.com").Value;
        _companies.FindByDomainAsync(Arg.Is<CanonicalDomain>(d => d!.Value == "known.com"), Arg.Any<CancellationToken>())
            .Returns(new Company(_ids.NewId(), domain, "Known", CompanySource.Curated, Now));

        var change = await CreateService().ExpandAsync(
        [
            new CrawledCompany("known.com", "Known"),
        ]);

        change.Inserted.ShouldBe(0);
        change.Skipped.ShouldBe(1);
        await _companies.DidNotReceive().AddAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expanding_a_crawled_company_with_a_blank_display_name_skips_it()
    {
        var change = await CreateService().ExpandAsync(
        [
            new CrawledCompany("blank.com", "   "),
        ]);

        change.Inserted.ShouldBe(0);
        change.Skipped.ShouldBe(1);
        await _companies.DidNotReceive().AddAsync(Arg.Any<Company>(), Arg.Any<CancellationToken>());
    }
}
