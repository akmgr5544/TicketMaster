using TicketMaster.Common.IntegrationEvents;

namespace EventsIntegration.HostFixtures;

// The consumer end of the round-trip: a Wolverine handler on the probe host that records whatever the
// broker delivered. Its receiving a message is the proof the Events outbox persisted, provisioned,
// serialized and relayed the envelope.
public sealed class EventCreatedProbeHandler
{
    public ValueTask Consume(EventCreatedIntegrationEvent message, MessageSink sink) => sink.WriteAsync(message);
}
