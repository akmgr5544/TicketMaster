namespace Users.Api.Extensions;

public static class ServiceCollectionExtension
{
    public static void AddBusinessServices(this IServiceCollection services)
    {
        services.AddMediatR(cf =>
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));
    }
}