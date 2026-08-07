using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Commands;

public sealed class ArgumentSpecTests
{
    [Fact]
    public void Exposes_its_fields()
    {
        var spec = new ArgumentSpec("count", required: false, "How many, 1-20.");

        spec.Name.ShouldBe("count");
        spec.Required.ShouldBeFalse();
        spec.Description.ShouldBe("How many, 1-20.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name) =>
        Should.Throw<ArgumentException>(() => new ArgumentSpec(name, required: true, "d"));

    [Fact]
    public void Rejects_a_null_name() =>
        Should.Throw<ArgumentException>(() => new ArgumentSpec(null!, required: true, "d"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_description(string description) =>
        Should.Throw<ArgumentException>(() => new ArgumentSpec("n", required: true, description));
}
