using GatewayTests.Fixtures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GatewayTests;

// The cross-file references in the two YARP JSON files are bare strings that nothing checks at build
// time — a bad one reloads silently rather than failing. These assert the three invariants the
// api-gateway skill names, over the configuration the gateway actually loaded.
public sealed class ConfigInvariantTests
{
    private static IEnumerable<IConfigurationSection> Routes(IConfiguration config) =>
        config.GetSection("ReverseProxy:Routes").GetChildren();

    [Fact]
    public void Every_route_targets_a_defined_cluster()
    {
        using var app = new GatewayApp();
        var config = app.Services.GetRequiredService<IConfiguration>();

        var clusters = config.GetSection("ReverseProxy:Clusters").GetChildren()
            .Select(cluster => (string?)cluster.Key)
            .ToHashSet();

        foreach (var route in Routes(config))
        {
            var clusterId = route["ClusterId"];
            Assert.Contains(clusterId, clusters);
        }
    }

    [Fact]
    public async Task Every_route_policy_resolves_to_a_registered_policy()
    {
        using var app = new GatewayApp();
        var config = app.Services.GetRequiredService<IConfiguration>();
        var policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var route in Routes(config))
        {
            var policyName = route["AuthorizationPolicy"];
            if (string.IsNullOrEmpty(policyName))
                continue;

            Assert.NotNull(await policies.GetPolicyAsync(policyName));
        }
    }

    [Fact]
    public void Every_prefixed_route_strips_its_prefix()
    {
        using var app = new GatewayApp();
        var config = app.Services.GetRequiredService<IConfiguration>();

        foreach (var route in Routes(config))
        {
            var path = route["Match:Path"];
            if (path is null || !path.StartsWith('/'))
                continue;

            var stripsPrefix = route.GetSection("Transforms").GetChildren()
                .Any(transform => transform["PathPattern"] is not null || transform["PathRemovePrefix"] is not null);

            Assert.True(stripsPrefix, $"Route '{route.Key}' forwards its prefix intact.");
        }
    }

    [Fact]
    public async Task A_prefixed_route_actually_removes_the_prefix_when_forwarding()
    {
        // The static check above proves the transform is declared; this proves it works. The users
        // route is anonymous, so a plain request reaches the forwarder without a token.
        using var app = new GatewayApp();
        var client = app.CreateClient();

        await client.GetAsync("/users-service/api/users/login");

        Assert.NotNull(app.Forwarder.LastForwarded);
        Assert.Equal("/api/users/login", app.Forwarder.LastForwarded!.RequestUri!.AbsolutePath);
    }
}
