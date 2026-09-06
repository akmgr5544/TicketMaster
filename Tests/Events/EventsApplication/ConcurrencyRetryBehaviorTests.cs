using Events.Application.CommandHandlers;
using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.Pipelines;
using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;
using EventsApplication.Fakes;
using MediatR;

namespace EventsApplication;

/// <summary>
/// The retry seam for optimistic concurrency. Cosmos reports a lost race as 412 and the repository
/// turns that into <see cref="ConcurrencyConflictException"/>; everything from that point on is
/// exercised here without a Cosmos, because the conflict is the only thing the store contributes.
/// </summary>
public class ConcurrencyRetryBehaviorTests
{
    private readonly FakeEventRepository _events = new();
    private readonly FakeIntegrationEventPublisher _publisher = new();

    private const int MaxAttempts = 3;

    private static readonly RescheduleEventCommand ARequest = new("some-id", DateTime.UtcNow.AddDays(20));

    private static ConcurrencyRetryBehavior<RescheduleEventCommand, Unit> Behavior() => new();

    private static ConcurrencyConflictException AConflict() => new(nameof(Event), "some-id");

    [Fact]
    public async Task Runs_the_handler_once_when_nothing_conflicts()
    {
        var attempts = 0;

        var result = await Behavior().Handle(ARequest,
            _ =>
            {
                attempts++;
                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task Retries_the_whole_request_and_succeeds_on_the_second_attempt()
    {
        var attempts = 0;

        await Behavior().Handle(ARequest,
            _ =>
            {
                attempts++;
                return attempts == 1 ? throw AConflict() : Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// A conflict that survives the retries is sustained contention, not a lost race. It has to reach
    /// the caller as one of the three exceptions the API maps — 409 here — rather than as the
    /// internal signal, which nothing maps and which would surface as a 500.
    /// </summary>
    [Fact]
    public async Task Gives_up_after_a_bounded_number_of_attempts_and_reports_a_conflict()
    {
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<EventsApplicationException>(() =>
            Behavior().Handle(ARequest,
                _ =>
                {
                    attempts++;
                    throw AConflict();
                },
                CancellationToken.None));

        Assert.Equal(MaxAttempts, attempts);
        Assert.IsType<ConcurrencyConflictException>(exception.InnerException);

        // Not a NotFoundException, which derives from the same type and would map to 404.
        Assert.IsType<EventsApplicationException>(exception);
    }

    [Fact]
    public async Task Does_not_retry_anything_other_than_a_conflict()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Behavior().Handle(ARequest,
                _ =>
                {
                    attempts++;
                    throw new NotFoundException(nameof(Event), "some-id");
                },
                CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// The point of putting the retry outside the handler rather than inside the repository: the
    /// second attempt goes back through the read, so the change is re-applied to the copy that won
    /// instead of the stale one being written again.
    /// </summary>
    [Fact]
    public async Task A_retried_command_re_reads_before_writing_again()
    {
        var @event = AnEvent();
        var newDate = DateTime.UtcNow.AddDays(30);
        var command = new RescheduleEventCommand(@event.Id, newDate);
        var handler = new RescheduleEventCommandHandler(_events, _publisher);

        _events.ConflictsBeforeSuccess = 1;

        await Behavior().Handle(command, async cancellationToken =>
        {
            await handler.Handle(command, cancellationToken);
            return Unit.Value;
        }, CancellationToken.None);

        Assert.Equal(2, _events.GetCalls);
        Assert.Equal(newDate, Assert.Single(_events.Updated).StartDate);

        // What this cannot show: the fake hands back the same instance every time, where Cosmos hands
        // back a freshly deserialized one. So the re-applied mutation here bumps Version twice and
        // re-raises its domain event on an object that never went away — in production the second
        // attempt starts from a clean aggregate. The re-read itself is what is being asserted.
    }

    [Fact]
    public async Task A_command_that_keeps_losing_the_race_surfaces_as_a_conflict_not_a_crash()
    {
        var @event = AnEvent();
        var command = new RescheduleEventCommand(@event.Id, DateTime.UtcNow.AddDays(30));
        var handler = new RescheduleEventCommandHandler(_events, _publisher);

        _events.ConflictsBeforeSuccess = MaxAttempts;

        await Assert.ThrowsAsync<EventsApplicationException>(() =>
            Behavior().Handle(command, async cancellationToken =>
            {
                await handler.Handle(command, cancellationToken);
                return Unit.Value;
            }, CancellationToken.None));

        Assert.Empty(_events.Updated);

        // Nothing was stored, so nothing may have been announced either.
        Assert.Empty(_publisher.Published);
    }

    private Event AnEvent()
    {
        var venue = new Venue("Karen Demirchyan Complex",
            "Tsitsernakaberd Hwy 1",
            new GeoLocation(40.1872, 44.5152),
            ["A1"]);

        var @event = new Event(DateTime.UtcNow.AddDays(11), venue, [new Performer("System of a Down", "Band")]);
        @event.ClearDomainEvents();
        _events.Seed(@event);

        return @event;
    }
}
