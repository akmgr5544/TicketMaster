using Bookings.Application.Commands;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class CreateTicketCommandHandler : IRequestHandler<CreateTicketCommand>
{
    public Task Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}