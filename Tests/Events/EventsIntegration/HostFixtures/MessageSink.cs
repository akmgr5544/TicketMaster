using System.Threading.Channels;
using TicketMaster.Common.IntegrationEvents;

namespace EventsIntegration.HostFixtures;

// Where the probe consumer drops what it received off the broker, so a test can await the relay
// rather than poll for it.
public sealed class MessageSink
{
    private readonly Channel<EventCreatedIntegrationEvent> _received =
        Channel.CreateUnbounded<EventCreatedIntegrationEvent>();

    public ValueTask WriteAsync(EventCreatedIntegrationEvent message) => _received.Writer.WriteAsync(message);

    public async Task<EventCreatedIntegrationEvent> WaitForNextAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await _received.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A bare cancellation stack says nothing; the likely cause is the outbox never relaying.
            throw new TimeoutException(
                $"No EventCreatedIntegrationEvent was relayed within {timeout.TotalSeconds:0}s.");
        }
    }
}
