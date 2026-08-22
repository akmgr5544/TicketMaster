using Bookings.Application.CustomerBookings;
using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using BookingApplication.Fakes;

namespace BookingApplication;

/// <summary>
/// A customer reading and cancelling their own bookings.
/// <para>
/// The recurring assertion is that scoping by caller <i>is</i> the authorization check: somebody
/// else's booking has to answer exactly as a nonexistent one does, because distinguishing them would
/// confirm that a given id exists to someone with no business knowing.
/// </para>
/// </summary>
public class CustomerBookingsTests
{
    private const string Owner = "user-1";
    private const string Stranger = "user-2";

    private readonly FakeBookingRepository _bookings = new();

    private Booking ABookingFor(string userId, params long[] ticketIds)
    {
        var booking = Booking.Create(userId, BookingStatus.Booked, ticketIds);
        booking.ClearDomainEvents();

        // The fake assigns the key, as the database would.
        _bookings.AddAsync(booking).GetAwaiter().GetResult();
        return booking;
    }

    private Task<BookingDto> Get(long bookingId, string userId = Owner) =>
        new GetBookingQueryHandler(_bookings).Handle(new GetBookingQuery(bookingId, userId),
            CancellationToken.None);

    private Task<BookingDto[]> List(string userId = Owner, int page = 1, int pageSize = 25) =>
        new ListBookingsQueryHandler(_bookings).Handle(new ListBookingsQuery(userId, page, pageSize),
            CancellationToken.None);

    private Task Cancel(long bookingId, string userId = Owner) =>
        new CancelBookingCommandHandler(_bookings).Handle(new CancelBookingCommand(bookingId, userId),
            CancellationToken.None);

    // --- Reading one ---

    [Fact]
    public async Task Returns_the_caller_s_own_booking()
    {
        var booking = ABookingFor(Owner, 7, 9);

        var dto = await Get(booking.Id);

        Assert.Equal(booking.Id, dto.Id);
        Assert.Equal(nameof(BookingStatus.Booked), dto.Status);
        Assert.Equal([7L, 9L], dto.TicketIds);
    }

    [Fact]
    public async Task Reports_the_history_of_the_booking()
    {
        var booking = ABookingFor(Owner, 7);

        var dto = await Get(booking.Id);

        var history = Assert.Single(dto.History);
        Assert.Equal(nameof(BookingStatus.Booked), history.Status);
        Assert.Equal(1, history.TicketsCount);
    }

    [Fact]
    public async Task Somebody_else_s_booking_is_not_found()
    {
        var booking = ABookingFor(Stranger, 7);

        await Assert.ThrowsAsync<NotFoundException>(() => Get(booking.Id));
    }

    [Fact]
    public async Task A_booking_that_does_not_exist_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Get(404));
    }

    // --- Listing ---

    [Fact]
    public async Task Lists_only_the_caller_s_bookings()
    {
        ABookingFor(Owner, 7);
        ABookingFor(Stranger, 9);
        ABookingFor(Owner, 11);

        var page = await List();

        Assert.Equal(2, page.Length);
        Assert.All(page, dto => Assert.DoesNotContain(9L, dto.TicketIds));
    }

    /// <summary>
    /// Newest first. <c>Booking</c> has no timestamp, so the key is the only proxy for age.
    /// </summary>
    [Fact]
    public async Task Lists_the_newest_booking_first()
    {
        var first = ABookingFor(Owner, 7);
        var second = ABookingFor(Owner, 9);

        var page = await List();

        Assert.Equal([second.Id, first.Id], page.Select(dto => dto.Id));
    }

    [Fact]
    public async Task Pages_through_the_caller_s_bookings()
    {
        ABookingFor(Owner, 7);
        var second = ABookingFor(Owner, 9);
        var third = ABookingFor(Owner, 11);

        var firstPage = await List(pageSize: 2);
        var secondPage = await List(page: 2, pageSize: 2);

        Assert.Equal([third.Id, second.Id], firstPage.Select(dto => dto.Id));
        Assert.Single(secondPage);
    }

    [Fact]
    public async Task Lists_nothing_for_a_caller_with_no_bookings()
    {
        ABookingFor(Stranger, 7);

        Assert.Empty(await List());
    }

    // --- Cancelling ---

    [Fact]
    public async Task Cancels_the_caller_s_own_booking()
    {
        var booking = ABookingFor(Owner, 7, 9);

        await Cancel(booking.Id);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(1, _bookings.SaveCount);
    }

    /// <summary>
    /// The seats go back through the event, not through this handler — one aggregate per transaction.
    /// </summary>
    [Fact]
    public async Task Cancelling_announces_the_tickets_to_release()
    {
        var booking = ABookingFor(Owner, 7, 9);

        await Cancel(booking.Id);

        var cancelled = Assert.IsType<BookingCancelledDomainEvent>(Assert.Single(booking.DomainEvents));
        Assert.Equal([7L, 9L], cancelled.TicketIds);
    }

    [Fact]
    public async Task Refuses_to_cancel_somebody_else_s_booking()
    {
        var booking = ABookingFor(Stranger, 7);

        await Assert.ThrowsAsync<NotFoundException>(() => Cancel(booking.Id));
        Assert.Equal(BookingStatus.Booked, booking.Status);
        Assert.Equal(0, _bookings.SaveCount);
    }

    [Fact]
    public async Task Refuses_to_cancel_a_booking_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Cancel(404));
    }

    /// <summary>
    /// Cancelling a paid booking would be a refund, which this service does not do. The aggregate
    /// refuses it, so the endpoint answers 400 rather than quietly voiding something paid for.
    /// </summary>
    [Fact]
    public async Task Refuses_to_cancel_a_booking_that_has_been_paid_for()
    {
        var booking = ABookingFor(Owner, 7);
        booking.MarkPaid();

        await Assert.ThrowsAsync<BookingsDomainException>(() => Cancel(booking.Id));
        Assert.Equal(BookingStatus.Payed, booking.Status);
    }
}
