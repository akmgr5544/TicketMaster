using Bookings.Application.Commands;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand>
{
    public Task Handle(MakeBookingCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}