using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using JobHunter.TestKit;
using JobHunter.Telegram.Commands;
using JobHunter.Telegram.Search;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// The adapter that lets the real F9 <c>/search</c> handler plug into the T11 command router: <c>/search</c>
/// is registered (F9), not a placeholder, so its handler produces genuine results. The adapter wraps the
/// existing <see cref="SearchCommandHandler"/> (which returns rendered text) as an
/// <see cref="ICommandHandler"/>, passing the arguments through and wrapping the text as one message. There
/// is one search path — the shared <see cref="ISearchQuery"/> port — and this only bridges the return shape.
/// </summary>
public sealed class SearchCommandAdapterTests
{
    private const long OwnerChat = 4242;

    private readonly ISearchQuery _search = Substitute.For<ISearchQuery>();

    private SearchCommandAdapter NewAdapter() =>
        new(new SearchCommandHandler(_search, new FakeClock(), NullLogger<SearchCommandHandler>.Instance));

    [Fact]
    public async Task It_passes_the_arguments_through_and_returns_the_rendered_text_as_one_message()
    {
        SearchQuery? captured = null;
        _search.SearchAsync(Arg.Do<SearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(
                new SearchResults([], 0, new Dictionary<string, IReadOnlyList<FacetCount>>(), null, false)));

        var messages = await NewAdapter().HandleAsync(new CommandRequest(OwnerChat, "staff sre"));

        captured.ShouldNotBeNull();
        captured!.Text.ShouldBe("staff sre");
        messages.ShouldHaveSingleItem().Text.ShouldContain("No results");
    }

    [Fact]
    public void A_null_inner_handler_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new SearchCommandAdapter(null!));
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewAdapter().HandleAsync(null!));
    }
}
