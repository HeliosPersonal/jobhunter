using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/cv</c> (catalogue §Profile · State read): the status of the active CV — version, activation date and the
/// count of current matches computed against it — and <strong>nothing of its content</strong>. It reads the
/// metadata-only <see cref="ICvStatusQuery"/>, so there is no path for CV text to reach the reply; this suite
/// pins the three reported facts and the "no active CV" plain line. It is read-only: it never uploads a CV, and
/// the port it depends on carries no <c>extracted_text</c>, which is what lets the F4 leakage scan leave this
/// path uncovered by construction.
/// </summary>
public sealed class CvCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private readonly ICvStatusQuery _status = Substitute.For<ICvStatusQuery>();

    private CvCommandHandler NewHandler() => new(_status, NullLogger<CvCommandHandler>.Instance);

    [Fact]
    public async Task It_reports_the_version_activation_date_and_match_count()
    {
        _status.ActiveAsync(Arg.Any<CancellationToken>()).Returns(
            new CvStatus(3, new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), MatchCount: 128));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("3");
        text.ShouldContain("20 Jul");
        text.ShouldContain("128");
    }

    [Fact]
    public async Task With_no_active_cv_it_says_so_plainly()
    {
        _status.ActiveAsync(Arg.Any<CancellationToken>()).Returns((CvStatus?)null);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("no", Case.Insensitive);
    }

    [Fact]
    public async Task It_never_reports_a_not_yet_activated_version_as_activated()
    {
        _status.ActiveAsync(Arg.Any<CancellationToken>()).Returns(
            new CvStatus(1, ActivatedAt: null, MatchCount: 0));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // A version uploaded but not yet activated is reported honestly, never with a fabricated date.
        var text = messages.ShouldHaveSingleItem().Text;
        text.ShouldContain("1");
        text.ShouldNotContain("activated on", Case.Insensitive);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new CvCommandHandler(null!, NullLogger<CvCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new CvCommandHandler(_status, null!));
    }
}
