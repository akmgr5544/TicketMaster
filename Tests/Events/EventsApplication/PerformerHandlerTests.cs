using Events.Application.CommandHandlers;
using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.Queries;
using Events.Application.QueryHandlers;
using Events.Domain.Entities;
using Events.Domain.Exceptions;
using EventsApplication.Fakes;

namespace EventsApplication;

public class PerformerHandlerTests
{
    private readonly FakePerformerRepository _performers = new();
    private readonly FakeEventRepository _events = new();

    private static Performer APerformer() =>
        new("System of a Down", "Armenian-American rock band");

    // --- Get ---

    [Fact]
    public async Task Get_returns_the_performer_as_a_dto()
    {
        var performer = APerformer();
        _performers.Seed(performer);

        var result = await new GetPerformerQueryHandler(_performers)
            .Handle(new GetPerformerQuery(performer.Id), CancellationToken.None);

        Assert.Equal(performer.Id, result.Id);
        Assert.Equal("System of a Down", result.Name);
        Assert.Equal("Armenian-American rock band", result.Description);
    }

    [Fact]
    public async Task Get_throws_when_the_performer_does_not_exist()
    {
        var handler = new GetPerformerQueryHandler(_performers);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetPerformerQuery("missing"), CancellationToken.None));
    }

    // --- List ---

    [Fact]
    public async Task List_passes_the_continuation_token_through_in_both_directions()
    {
        _performers.Seed(APerformer(), APerformer());
        _performers.NextContinuationToken = "next-page";

        var result = await new ListPerformersQueryHandler(_performers)
            .Handle(new ListPerformersQuery(10, "from-client"), CancellationToken.None);

        Assert.Equal("from-client", _performers.LastContinuationTokenRequested);
        Assert.Equal("next-page", result.ContinuationToken);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task List_reports_no_token_when_the_results_are_exhausted()
    {
        _performers.Seed(APerformer());
        _performers.NextContinuationToken = null;

        var result = await new ListPerformersQueryHandler(_performers)
            .Handle(new ListPerformersQuery(10, null), CancellationToken.None);

        Assert.Null(result.ContinuationToken);
    }

    // --- Update ---

    [Fact]
    public async Task Update_renames_the_performer_and_changes_its_description()
    {
        var performer = APerformer();
        _performers.Seed(performer);

        await new UpdatePerformerCommandHandler(_performers).Handle(
            new UpdatePerformerCommand(performer.Id, "SOAD", "Rock band from Los Angeles"),
            CancellationToken.None);

        var updated = await _performers.GetPerformerByIdAsync(performer.Id, CancellationToken.None);
        Assert.Equal("SOAD", updated!.Name);
        Assert.Equal("Rock band from Los Angeles", updated.Description);
    }

    [Fact]
    public async Task Update_throws_when_the_performer_does_not_exist()
    {
        var handler = new UpdatePerformerCommandHandler(_performers);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdatePerformerCommand("missing", "a name", "a description"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Update_rejects_a_blank_name_without_persisting_anything()
    {
        var performer = APerformer();
        _performers.Seed(performer);

        var handler = new UpdatePerformerCommandHandler(_performers);

        await Assert.ThrowsAsync<EventsDomainException>(() =>
            handler.Handle(new UpdatePerformerCommand(performer.Id, "  ", "a description"),
                CancellationToken.None));

        var unchanged = await _performers.GetPerformerByIdAsync(performer.Id, CancellationToken.None);
        Assert.Equal("System of a Down", unchanged!.Name);
        Assert.Equal("Armenian-American rock band", unchanged.Description);
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_removes_a_performer_with_no_upcoming_events()
    {
        var performer = APerformer();
        _performers.Seed(performer);
        _events.UpcomingPerformerEventCount = 0;

        await new DeletePerformerCommandHandler(_performers, _events)
            .Handle(new DeletePerformerCommand(performer.Id), CancellationToken.None);

        Assert.False(_performers.Contains(performer.Id));
    }

    [Fact]
    public async Task Delete_is_refused_when_the_performer_has_upcoming_events()
    {
        var performer = APerformer();
        _performers.Seed(performer);
        _events.UpcomingPerformerEventCount = 2;

        var handler = new DeletePerformerCommandHandler(_performers, _events);

        await Assert.ThrowsAsync<EventsApplicationException>(() =>
            handler.Handle(new DeletePerformerCommand(performer.Id), CancellationToken.None));

        Assert.True(_performers.Contains(performer.Id));
    }

    [Fact]
    public async Task Delete_throws_when_the_performer_does_not_exist()
    {
        var handler = new DeletePerformerCommandHandler(_performers, _events);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeletePerformerCommand("missing"), CancellationToken.None));
    }

    // --- Add ---

    [Fact]
    public async Task Add_returns_the_id_of_the_created_performer()
    {
        var id = await new AddPerformerCommandHandler(_performers).Handle(
            new AddPerformerCommand("A Performer", "A Description"),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(_performers.Contains(id));
    }
}
