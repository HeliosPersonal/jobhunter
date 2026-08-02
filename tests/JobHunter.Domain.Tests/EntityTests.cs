using JobHunter.Domain.Common;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests;

public sealed class EntityTests
{
    private sealed class Marker : Entity
    {
        public Marker(Guid id)
            : base(id)
        {
        }
    }

    private sealed class OtherMarker : Entity
    {
        public OtherMarker(Guid id)
            : base(id)
        {
        }
    }

    [Fact]
    public void Constructing_with_an_empty_id_throws()
    {
        Should.Throw<ArgumentException>(() => new Marker(Guid.Empty));
    }

    [Fact]
    public void Entities_of_the_same_type_and_id_are_equal()
    {
        var id = Guid.CreateVersion7();

        var a = new Marker(id);
        var b = new Marker(id);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Entities_with_different_ids_are_not_equal()
    {
        new Marker(Guid.CreateVersion7()).Equals(new Marker(Guid.CreateVersion7())).ShouldBeFalse();
    }

    [Fact]
    public void Entities_of_different_types_with_the_same_id_are_not_equal()
    {
        var id = Guid.CreateVersion7();

        new Marker(id).Equals(new OtherMarker(id)).ShouldBeFalse();
    }

    [Fact]
    public void Entity_is_not_equal_to_a_non_entity()
    {
        new Marker(Guid.CreateVersion7()).Equals("not an entity").ShouldBeFalse();
    }
}
