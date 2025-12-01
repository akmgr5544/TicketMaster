using Bookings.Application.Commands;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ITicketsRepository _ticketsRepository;

    public MakeBookingCommandHandler(IBookingRepository bookingRepository, ITicketsRepository ticketsRepository)
    {
        _bookingRepository = bookingRepository;
        _ticketsRepository = ticketsRepository;
    }

    public async Task Handle(MakeBookingCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsByIdAsync([..request.Tickets], cancellationToken);

        //TODO:: logic for ticket checking
        //TODO:: lock tickets for booking use redis

        var booking = new Booking(request.UserId, request.Status);
        
        foreach (var ticket in tickets)
        {
            booking.AddBookedTicket(ticket.Id);
        }
        
        //TODO:: publish event to change ticket status??

        await _bookingRepository.AddAsync(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
    }
}