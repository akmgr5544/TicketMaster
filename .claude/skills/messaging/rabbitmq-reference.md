# RabbitMQ Reference

Broker-level concepts underneath the `messaging` skill. Read this when reasoning about topology,
delivery guarantees, or failure behavior directly — not needed for ordinary publish/consume work.

Condensed from the official RabbitMQ docs, retrieved 2026-08-08.

- AMQP 0-9-1 model: https://www.rabbitmq.com/tutorials/amqp-concepts
- Dead letter exchanges: https://www.rabbitmq.com/docs/dlx
- Quorum queues: https://www.rabbitmq.com/docs/quorum-queues
- Publisher confirms: https://www.rabbitmq.com/docs/confirms

## The model

Publishers never write to queues. They publish to an **exchange**, which routes to zero or more
**queues** according to **bindings**. A message routed nowhere is silently dropped unless the
publisher opted into mandatory/return handling.

```
publisher ──► exchange ──(binding, binding key)──► queue ──► consumer
                          routing key matched here
```

## Exchange types

| Type | Routing rule | Use for |
|---|---|---|
| **Direct** | Binding key equals routing key exactly | Point-to-point; commands to one queue |
| **Fanout** | Every bound queue gets a copy; routing key ignored | Broadcast events to all interested services |
| **Topic** | Pattern match between routing key and binding pattern | Selective subscription by category |
| **Headers** | Match on header values instead of routing key | Multi-attribute routing |

Wolverine's conventional routing publishes to **fanout** exchanges named after the message type —
correct for events, wrong for commands that must be handled once.

### Topic wildcards

Routing keys are dot-separated words (`booking.created.eu`). In a binding pattern:

- `*` matches **exactly one** word — `booking.*.eu` matches `booking.created.eu`, not `booking.eu`
- `#` matches **zero or more** words — `booking.#` matches `booking`, `booking.created`, and
  `booking.created.eu`

## Queue properties

| Property | Meaning |
|---|---|
| **Durable** | Queue *metadata* survives broker restart. Does not by itself persist messages |
| **Exclusive** | Single connection; deleted when that connection closes |
| **Auto-delete** | Deleted when the last consumer unsubscribes |
| **Arguments** | TTL, length limits, dead-lettering, queue type |

**Durability is two separate switches.** A durable queue holding non-persistent messages still
loses them on restart — the publisher must also mark messages persistent. The docs are explicit
that persistence costs performance.

## Acknowledgements

- **Automatic** — the broker considers the message delivered the moment it is sent. A consumer
  crash loses it.
- **Explicit** — the consumer sends `basic.ack`. Until then the broker keeps the message, and
  redelivers it if the connection drops.

Rejection: `basic.reject` (one message) or `basic.nack` (supports multiple). With `requeue: false`
the message is dead-lettered if a DLX is configured, otherwise dropped.

**Prefetch (QoS)** bounds how many unacknowledged messages a consumer may hold. Unset, one consumer
can drain a queue into memory while its peers idle. RabbitMQ supports channel-level prefetch;
quorum queues require per-consumer QoS and do not support global prefetch.

## Dead letter exchanges

A message is dead-lettered when:

1. A consumer rejects it with `requeue: false`
2. Its per-message TTL expires
3. Its queue exceeds a configured length limit
4. It exceeds the delivery limit (quorum queues)

**An expiring *queue* does not dead-letter its contents** — only the four cases above.

Configure with the policy keys `dead-letter-exchange` and `dead-letter-routing-key`. The docs
recommend policies over the `x-dead-letter-exchange` queue argument, because "hardcoded x-arguments
are strongly recommended against since they cannot be updated without redeploying applications."
Arguments override policies when both are present.

Dead-lettering rewrites the exchange name, may rewrite the routing key, strips `CC`/`BCC`, and
appends death history in `x-death` (AMQP 0-9-1) or `x-opt-deaths` (AMQP 1.0). Each record carries
queue, reason (`rejected`, `expired`, `maxlen`, `delivery_limit`), count and timestamps — read
these when diagnosing why something landed in the DLQ.

**Dead-lettered messages are republished without publisher confirms by default**, so they can be
lost if the target queue is unavailable. Quorum queues enable internal confirms and give
at-least-once dead-lettering.

RabbitMQ detects dead-letter cycles and drops the message if no rejection occurred anywhere in the
cycle — a loop won't run forever, but it also won't be preserved.

## Quorum queues

Raft-replicated, durable-only queues. The modern default for anything whose loss matters.

| Feature | Classic | Quorum |
|---|---|---|
| Non-durable | Yes | **No** |
| Replication | No | **Yes** |
| Exclusive | Yes | No |
| Server-named | Yes | No |
| Poison message handling | No | **Yes** |
| Consumer timeout | No | Yes |

Declare with `x-queue-type: quorum`. Default three members; control with
`x-quorum-initial-group-size`. A majority must be available — three members tolerate one failure,
five tolerate two. A message confirmed to the publisher survives as long as the majority does.

**Poison message handling:** redelivery is tracked in `x-delivery-count`, with a default delivery
limit of 20 from RabbitMQ 4.0. Exceeding it drops or dead-letters the message. Explicit `nack`
returns do not count toward the limit — only genuine failures.

**Use quorum queues for** critical long-lived queues such as booking and payment flows.

**Avoid them for** high-churn temporary queues, latency-critical paths, backlogs beyond ~5 million
messages, and large fanout (streams suit that better). Budget roughly 32 bytes of metadata per
message — about 1 MB per 30,000 messages.

## Delivery guarantees, end to end

A message survives a broker restart only if **all** of these hold:

1. The queue is durable (or quorum)
2. The message was published as persistent
3. The publisher used confirms and waited for the ack
4. The consumer acknowledges explicitly, after its work succeeds

Break any one and the guarantee is best-effort. Wolverine's durable inbox/outbox addresses the
application side of this; it does not substitute for getting the broker side right.
