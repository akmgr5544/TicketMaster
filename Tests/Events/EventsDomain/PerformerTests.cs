using Events.Domain.Entities;
using Events.Domain.Exceptions;

namespace EventsDomain;

public class PerformerTests
{
    [Fact]
    public void Generates_its_own_id_on_creation()
    {
        var performer = new Performer("System of a Down", "Armenian-American rock band");

        Assert.False(string.IsNullOrWhiteSpace(performer.Id));
    }

    [Fact]
    public void Gives_each_performer_a_distinct_id()
    {
        var first = new Performer("a name", "a description");
        var second = new Performer("a name", "a description");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name)
    {
        Assert.Throws<EventsDomainException>(() => new Performer(name, "a description"));
    }

    [Fact]
    public void Allows_an_empty_description()
    {
        var performer = new Performer("a name", "");

        Assert.Equal("", performer.Description);
    }
}
