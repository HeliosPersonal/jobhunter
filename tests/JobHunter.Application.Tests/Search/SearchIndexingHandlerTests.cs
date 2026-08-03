using JobHunter.Application.Search;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Search;

/// <summary>
/// The indexing handler (F9-T02): one message, <see cref="JobIndexRequested"/>, projected from PostgreSQL
/// through the pure allowlist and written by id. The load-bearing behaviours: an index failure is raised
/// as a <see cref="SearchIndexingException"/> so Wolverine retries then dead-letters on the indexer's own
/// queue and no other stage is affected (QG-3); a replay re-projects the same source to the same document
/// (invariant 8); and an upsert for a vanished job degrades to a delete so the index never keeps a
/// document PostgreSQL no longer backs (QG-1).
/// </summary>
public sealed class SearchIndexingHandlerTests
{
    private static readonly Guid JobId = Guid.Parse("0192e8b7-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset Occurred = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IJobProjectionSource _source = Substitute.For<IJobProjectionSource>();
    private readonly ISearchIndex _index = Substitute.For<ISearchIndex>();

    private SearchIndexingHandler CreateHandler() =>
        new(_source, _index, NullLogger<SearchIndexingHandler>.Instance);

    private static JobProjectionSource SourceRow() => new()
    {
        Id = JobId,
        Title = "Engineer",
        Description = "desc",
        Status = "Live",
        CompanyName = "Acme",
        CompanyDomain = "acme.com",
        RemotePolicy = "Remote",
        EmploymentType = "FullTime",
        FirstSeenAt = Occurred,
    };

    [Fact]
    public async Task An_upsert_request_projects_the_job_and_upserts_the_document()
    {
        _source.ProjectAsync(JobId, Arg.Any<CancellationToken>()).Returns(SourceRow());
        _index.UpsertAsync(Arg.Any<JobDocument>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));

        await CreateHandler().Handle(
            new JobIndexRequested(JobId, JobIndexRequested.Upsert, Occurred), CancellationToken.None);

        await _index.Received(1).UpsertAsync(
            Arg.Is<JobDocument>(d => d != null && d.Id == JobId.ToString()), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_delete_request_removes_the_document_and_never_projects()
    {
        _index.DeleteAsync(JobId, Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));

        await CreateHandler().Handle(
            new JobIndexRequested(JobId, JobIndexRequested.Delete, Occurred), CancellationToken.None);

        await _index.Received(1).DeleteAsync(JobId, Arg.Any<CancellationToken>());
        await _source.DidNotReceive().ProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_upsert_for_a_vanished_job_degrades_to_a_delete()
    {
        _source.ProjectAsync(JobId, Arg.Any<CancellationToken>()).Returns((JobProjectionSource?)null);
        _index.DeleteAsync(JobId, Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));

        await CreateHandler().Handle(
            new JobIndexRequested(JobId, JobIndexRequested.Upsert, Occurred), CancellationToken.None);

        await _index.Received(1).DeleteAsync(JobId, Arg.Any<CancellationToken>());
        await _index.DidNotReceive().UpsertAsync(Arg.Any<JobDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_index_failure_on_upsert_is_raised_so_wolverine_retries_then_dead_letters()
    {
        _source.ProjectAsync(JobId, Arg.Any<CancellationToken>()).Returns(SourceRow());
        _index.UpsertAsync(Arg.Any<JobDocument>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("search.index.unavailable", "down")));

        var ex = await Should.ThrowAsync<SearchIndexingException>(() => CreateHandler().Handle(
            new JobIndexRequested(JobId, JobIndexRequested.Upsert, Occurred), CancellationToken.None));

        ex.JobId.ShouldBe(JobId);
        ex.Operation.ShouldBe(JobIndexRequested.Upsert);
    }

    [Fact]
    public async Task An_index_failure_on_delete_is_raised()
    {
        _index.DeleteAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("search.index.unavailable", "down")));

        await Should.ThrowAsync<SearchIndexingException>(() => CreateHandler().Handle(
            new JobIndexRequested(JobId, JobIndexRequested.Delete, Occurred), CancellationToken.None));
    }

    [Fact]
    public async Task A_replayed_request_projects_and_upserts_the_same_document_bytes()
    {
        _source.ProjectAsync(JobId, Arg.Any<CancellationToken>()).Returns(SourceRow());
        _index.UpsertAsync(Arg.Any<JobDocument>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));

        var captured = new List<JobDocument>();
        await _index.UpsertAsync(Arg.Do<JobDocument>(d => captured.Add(d)), Arg.Any<CancellationToken>());

        var handler = CreateHandler();
        var message = new JobIndexRequested(JobId, JobIndexRequested.Upsert, Occurred);
        await handler.Handle(message, CancellationToken.None);
        await handler.Handle(message, CancellationToken.None);

        captured.Count.ShouldBe(2);
        captured[0].ShouldBe(captured[1]);
    }

    [Fact]
    public async Task A_null_message_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            CreateHandler().Handle(null!, CancellationToken.None));
    }
}
