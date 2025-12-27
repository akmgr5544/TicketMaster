using Bookings.Application.Commands;
using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ITicketsRepository _ticketsRepository;

    public MakeBookingCommandHandler(IBookingRepository bookingRepository,
        ITicketsRepository ticketsRepository)
    {
        _bookingRepository = bookingRepository;
        _ticketsRepository = ticketsRepository;
    }
    
    public async Task Handle(MakeBookingCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsForBookingAsync([..request.Tickets],
            request.EventId,
            cancellationToken);
        
        if (tickets.Length == 0)
        {
            throw new BookingException("No tickets found");
        }
        //TODO:: lock tickets for booking use redis

        var booking = Booking.Create(request.UserId, BookingStatus.Booked, request.Tickets.Length);
        
        foreach (var ticket in tickets)
        {
            booking.AddBookedTicket(ticket.Id);
        }
        
        booking.AddDomainEvent(new BookingCreatedDomainEvent(tickets));

        await _bookingRepository.AddAsync(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
    }
}