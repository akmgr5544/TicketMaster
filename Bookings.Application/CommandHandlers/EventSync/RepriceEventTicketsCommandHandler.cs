using Bookings.Application.Commands;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.EventSync;

// Each ticket guards itself against a stale message and keeps the price of a seat already sold, so a redelivered
// or out-of-order repricing changes nothing it should not. It only updates tickets, so no message-level guard is
// needed the way relocation's is.
internal sealed class RepriceEventTicketsCommandHandler : IRequestHandler<RepriceEventTicketsCommand>
{
    private readonly ITicketsRepository _tickets;

    public RepriceEventTicketsCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    public async Task Handle(RepriceEventTicketsCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);
        var pricing = request.Pricing;

        foreach (var ticket in tickets)
            ticket.Reprice(new TicketPricing(pricing.PriceFor(ticket.Seat), pricing.Currency, pricing.OrganizerId),
                request.Version);

        await _tickets.SaveChangesAsync(cancellationToken);
    }
}
