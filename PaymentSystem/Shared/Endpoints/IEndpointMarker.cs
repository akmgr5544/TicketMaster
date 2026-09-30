namespace PaymentSystem.Shared.Endpoints;

public interface IEndpointMarker
{
    void MapEndpoint(IEndpointRouteBuilder endpoints);
}
