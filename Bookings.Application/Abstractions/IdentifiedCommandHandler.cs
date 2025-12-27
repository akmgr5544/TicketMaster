using MediatR;

namespace Bookings.Application.Abstractions;

public class IdentifiedCommandHandler<T, TR> : IRequestHandler<IdentifiedCommand<T, TR>, TR>
    where T : IRequest<TR>
{
    private readonly IMediator _mediator;
    private readonly IRequestManager _requestManager;

    public IdentifiedCommandHandler(IMediator mediator, IRequestManager requestManager)
    {
        _mediator = mediator;
        _requestManager = requestManager;
    }

    public async Task<TR> Handle(IdentifiedCommand<T, TR> request, CancellationToken cancellationToken)
    {
        var alreadyExists = await _requestManager.ExistsAsync(request.Id, cancellationToken);

        if (alreadyExists)
        {
            return default;
        }

        await _requestManager.CreateRequestAsync(request.Id, cancellationToken);
        var result = await _mediator.Send(request.Command, cancellationToken);

        return result;
    }
}