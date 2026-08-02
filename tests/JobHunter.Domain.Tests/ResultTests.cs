using JobHunter.Domain.Common;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests;

public sealed class ResultTests
{
    private static readonly Error SampleError = new("sample.failed", "Something went wrong.");

    [Fact]
    public void Success_carries_the_value_and_no_error()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_carries_the_error_and_is_not_success()
    {
        var result = Result<int>.Failure(SampleError);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result<int>.Failure(SampleError);

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_with_None_error_is_rejected()
    {
        Should.Throw<ArgumentException>(() => Result<int>.Failure(Error.None));
    }

    [Fact]
    public void Failure_with_null_error_is_rejected()
    {
        Should.Throw<ArgumentException>(() => Result<int>.Failure(null!));
    }

    [Fact]
    public void Map_transforms_the_success_payload()
    {
        var mapped = Result<int>.Success(21).Map(x => x * 2);

        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(42);
    }

    [Fact]
    public void Map_propagates_a_failure_unchanged()
    {
        var mapped = Result<int>.Failure(SampleError).Map(x => x * 2);

        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Bind_chains_a_further_fallible_operation()
    {
        var bound = Result<int>.Success(10).Bind(x => Result<string>.Success($"n={x}"));

        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe("n=10");
    }

    [Fact]
    public void Bind_propagates_a_failure_without_invoking_the_binder()
    {
        var invoked = false;
        var bound = Result<int>.Failure(SampleError).Bind(x =>
        {
            invoked = true;
            return Result<string>.Success("unreached");
        });

        invoked.ShouldBeFalse();
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Match_collapses_the_success_branch()
    {
        var text = Result<int>.Success(7).Match(v => $"ok:{v}", e => $"err:{e.Code}");

        text.ShouldBe("ok:7");
    }

    [Fact]
    public void Match_collapses_the_failure_branch()
    {
        var text = Result<int>.Failure(SampleError).Match(v => $"ok:{v}", e => $"err:{e.Code}");

        text.ShouldBe("err:sample.failed");
    }

    [Fact]
    public void Implicit_conversion_from_value_produces_a_success()
    {
        Result<int> result = 99;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(99);
    }

    [Fact]
    public void Implicit_conversion_from_error_produces_a_failure()
    {
        Result<int> result = SampleError;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }
}
