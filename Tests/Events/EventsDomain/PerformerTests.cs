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

    [Fact]
    public void Renames_the_performer()
    {
        var performer = new Performer("System of a Down", "Armenian-American rock band");

        performer.Rename("SOAD");

        Assert.Equal("SOAD", performer.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_to_be_renamed_to_a_blank_name(string name)
    {
        var performer = new Performer("System of a Down", "a description");

        Assert.Throws<EventsDomainException>(() => performer.Rename(name));
        Assert.Equal("System of a Down", performer.Name);
    }

    [Fact]
    public void Changes_its_description()
    {
        var performer = new Performer("a name", "old description");

        performer.ChangeDescription("new description");

        Assert.Equal("new description", performer.Description);
    }

    /// <summary>
    /// A description is optional at creation, so clearing it later has to stay allowed too.
    /// </summary>
    [Fact]
    public void Allows_its_description_to_be_cleared()
    {
        var performer = new Performer("a name", "a description");

        performer.ChangeDescription("");

        Assert.Equal("", performer.Description);
    }
}
