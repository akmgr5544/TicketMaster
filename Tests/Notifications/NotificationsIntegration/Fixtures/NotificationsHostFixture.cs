using System.Collections.Concurrent;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.RabbitMQ;

namespace NotificationsIntegration.Fixtures;

// Boots the real Notifications host — Program.cs unmodified — against Postgres, RabbitMQ and a Mailpit mail server.
// Beside it runs a stand-in for PaymentSystem: a Wolverine host with conventional routing, so it listens on the
// default queue names PaymentSystem uses. It publishes the test messages, and records the UserRegistered it receives.
public sealed class NotificationsHostFixture : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int MailpitApiPort = 8025;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();
    private readonly IContainer _mailpit = new ContainerBuilder()
        .WithImage("axllent/mailpit:latest")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(MailpitApiPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
            request.ForPort(MailpitApiPort).ForPath("/api/v1/info")))
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private IHost? _paymentsStandIn;

    public IServiceProvider Services =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).Services;

    public IMessageBus Publisher =>
        (_paymentsStandIn ?? throw new InvalidOperationException("The stand-in was not started.")).Services
        .GetRequiredService<IMessageBus>();

    public ReceivedByPayments ReceivedByPayments { get; } = new();

    public HttpClient Mailpit { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _mailpit.StartAsync());

        Mailpit = new HttpClient
        {
            BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(MailpitApiPort)}")
        };

        // Environment variables, not WebApplicationFactory's hooks: Program.cs reads them while composing the builder.
        Environment.SetEnvironmentVariable("ConnectionStrings__NotificationsDatabase", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", _rabbit.GetConnectionString());
        Environment.SetEnvironmentVariable("Email__Host", _mailpit.Hostname);
        Environment.SetEnvironmentVariable("Email__Port", _mailpit.GetMappedPublicPort(SmtpPort).ToString());
        Environment.SetEnvironmentVariable("Email__From", "no-reply@ticketmaster.test");

        _factory = new WebApplicationFactory<Program>();
        // Resolving forces the host to build and start — its own ApplyMigrationsAsync included.
        _ = _factory.Services.GetService(typeof(IServiceProvider));

        _paymentsStandIn = Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.UseRabbitMq(new Uri(_rabbit.GetConnectionString()))
                    .AutoProvision()
                    .UseConventionalRouting();
                // Without this, a message published here with a local handler is handed to it in memory and never
                // reaches the broker — and the test would prove nothing about the queues.
                options.Policies.DisableConventionalLocalRouting();

                options.Discovery.DisableConventionalDiscovery().IncludeType<PaymentsStandInProbe>();
                options.Services.AddSingleton(ReceivedByPayments);
            })
            .Build();
        await _paymentsStandIn.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_paymentsStandIn is not null)
            await _paymentsStandIn.StopAsync();

        if (_factory is not null)
            await _factory.DisposeAsync();

        Mailpit?.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask(), _mailpit.DisposeAsync().AsTask());

        Environment.SetEnvironmentVariable("ConnectionStrings__NotificationsDatabase", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", null);
        Environment.SetEnvironmentVariable("Email__Host", null);
        Environment.SetEnvironmentVariable("Email__Port", null);
        Environment.SetEnvironmentVariable("Email__From", null);
    }

    // The emails Mailpit received for one address.
    public async Task<IReadOnlyList<MailpitMessage>> EmailsToAsync(string address)
    {
        var page = await Mailpit.GetFromJsonAsync<MailpitPage>("api/v1/messages");
        return page!.Messages.Where(message => message.To.Any(to => to.Address == address)).ToArray();
    }
}

[CollectionDefinition(Name)]
public sealed class NotificationsHostCollection : ICollectionFixture<NotificationsHostFixture>
{
    public const string Name = "Notifications host";
}

public sealed class PaymentsStandInProbe
{
    public void Consume(UserRegisteredIntegrationEvent message, ReceivedByPayments received) => received.Add(message);
}

public sealed class ReceivedByPayments
{
    private readonly ConcurrentQueue<UserRegisteredIntegrationEvent> _received = new();

    public void Add(UserRegisteredIntegrationEvent message) => _received.Enqueue(message);

    public bool Has(Guid userId) => _received.Any(message => message.UserId == userId);
}

public sealed record MailpitPage(IReadOnlyList<MailpitMessage> Messages);

public sealed record MailpitMessage(string Subject, IReadOnlyList<MailpitAddress> To);

public sealed record MailpitAddress(string Address);
