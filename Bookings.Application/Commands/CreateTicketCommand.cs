using MediatR;

namespace Bookings.Application.Commands;

public record CreateTicketCommand(): IRequest;