using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Services.Interfaces;
using Medallion.Threading;
using MediatR;

namespace Bookings.Application.CommandHandlers;

public class ReserveTicketCommandHandler : IRequestHandler<ReserveTicketCommand>
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ICacheService _cacheService;
    private const int TicketCountConfig = 2;

    public ReserveTicketCommandHandler(IDistributedLockProvider lockProvider,
        ICacheService cacheService)
    {
        _lockProvider = lockProvider;
        _cacheService = cacheService;
    }

    public async Task Handle(ReserveTicketCommand request, CancellationToken cancellationToken)
    {
        if (request.Tickets.Length == 0)
            throw new BookingException("Select tickets to book");

        if (request.Tickets.Length > TicketCountConfig)
            throw new BookingException("Too many tickets");

        var distributedLock =
            await _lockProvider.TryAcquireLockAsync("SomeUniqKey", cancellationToken: cancellationToken);
        if (distributedLock == null)
            throw new BookingException("Tickets reservation is in progress");

        using (distributedLock)
        {
            var keys = request.Tickets.Select(id => id.ToString()).ToArray();
            var reservedTickets = await _cacheService.GetByKeysAsync<ReserveTicketDto>(keys);

            if (reservedTickets.Count > 0)
                throw new BookingException("One of the tickets already reserved");

            var redisData = new List<KeyValuePair<string, ReserveTicketDto>>();
            foreach (var ticketId in request.Tickets)
            {
                var ticket = new ReserveTicketDto(ticketId, request.EventId, request.UserId);
                var data = new KeyValuePair<string, ReserveTicketDto>(ticketId.ToString(), ticket);
                redisData.Add(data);
            }

            await _cacheService.SetToCacheAsync(redisData.ToArray(), TimeSpan.FromMinutes(5));
        }
    }
}