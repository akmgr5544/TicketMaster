namespace Users.Api.Shared;

public interface IEndpointMarker
{
    void MapEndpoint(IEndpointRouteBuilder endpoints);
}