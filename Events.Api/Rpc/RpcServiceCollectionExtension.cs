namespace Events.Api.Rpc;

public static class RpcServiceCollectionExtension
{
    // The single source of the gRPC wiring — the DomainExceptionInterceptor is what turns Events'
    // domain exceptions into the right statuses, so it must travel with every host that serves the
    // gRPC endpoint, the integration test included. Program.cs and the seam test both call this so
    // neither can drift from the other.
    public static IServiceCollection AddEventsRpc(this IServiceCollection services)
    {
        services.AddGrpc(options => options.Interceptors.Add<DomainExceptionInterceptor>());
        return services;
    }
}
