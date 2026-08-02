using JobHunter.Domain.Common;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void None_is_the_empty_sentinel()
    {
        Error.None.Code.ShouldBe(string.Empty);
        Error.None.Message.ShouldBe(string.Empty);
    }

    [Fact]
    public void Errors_are_value_equal_by_code_and_message()
    {
        var a = new Error("x.failed", "boom");
        var b = new Error("x.failed", "boom");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Errors_differ_when_code_differs()
    {
        new Error("a", "m").ShouldNotBe(new Error("b", "m"));
    }
}
