using JobHunter.Domain.Common;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests;

public sealed class ValueObjectTests
{
    private sealed class Money : ValueObject
    {
        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public decimal Amount { get; }

        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    private sealed class Label : ValueObject
    {
        public Label(string value) => Value = value;

        public string Value { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Value;
        }
    }

    [Fact]
    public void Value_objects_with_equal_components_are_equal()
    {
        var a = new Money(10.50m, "USD");
        var b = new Money(10.50m, "USD");

        (a == b).ShouldBeTrue();
        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Value_objects_with_a_differing_component_are_not_equal()
    {
        (new Money(10m, "USD") == new Money(10m, "EUR")).ShouldBeFalse();
        (new Money(10m, "USD") != new Money(10m, "EUR")).ShouldBeTrue();
    }

    [Fact]
    public void Value_objects_of_different_types_are_not_equal()
    {
        new Money(1m, "USD").Equals(new Label("USD")).ShouldBeFalse();
    }

    [Fact]
    public void Comparison_with_null_is_handled_on_both_operands()
    {
        Money? left = null;
        Money? right = null;

        (left == right).ShouldBeTrue();
        (new Money(1m, "USD") == null).ShouldBeFalse();
        (null == new Money(1m, "USD")).ShouldBeFalse();
    }

    [Fact]
    public void Equals_object_overload_returns_false_for_a_non_value_object()
    {
        new Money(1m, "USD").Equals((object)"USD").ShouldBeFalse();
    }
}
