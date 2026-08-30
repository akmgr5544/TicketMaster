using Bookings.Application.Services.Interfaces;
using Bookings.Sql;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingIntegration.Fixtures;

[Collection(BookingsCollection.Name)]
public abstract class IntegrationTest : IAsyncLifetime
{
    private readonly BookingsFixture _fixture;
    private AsyncServiceScope _act;

    protected IntegrationTest(BookingsFixture fixture)
    {
        _fixture = fixture;
    }

    protected IServiceProvider Act => _act.ServiceProvider;

    protected ISender Sender => Act.GetRequiredService<ISender>();

    protected ICacheService Cache => Act.GetRequiredService<ICacheService>();

    protected IDatabase Redis => Act.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    protected Seed Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _act = _fixture.Services.CreateAsyncScope();
        Seed = new Seed(_fixture.Services);
    }

    public async Task DisposeAsync()
    {
        await _act.DisposeAsync();
    }

    /// <summary>
    /// Reads through a scope of its own. Asserting through the scope that performed the write returns
    /// the tracked instance and proves nothing about what reached the database — which is the exact
    /// bug the domain event dispatch tests exist to catch.
    /// <para>
    /// private protected, not protected: BookingDomainContext is internal to Bookings.Sql, and the
    /// compiler's accessibility-consistency check for a protected member ignores InternalsVisibleTo.
    /// Every subclass lives in this assembly, so private protected exposes the exact same thing to
    /// every caller that matters.
    /// </para>
    /// </summary>
    private protected async Task<T> ReadAsync<T>(Func<BookingDomainContext, Task<T>> read)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<BookingDomainContext>());
    }
}
