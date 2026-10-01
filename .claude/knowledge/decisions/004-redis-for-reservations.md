# ADR-004: Redis Directly for Reservations and Locks, Not HybridCache

## Status

Accepted

## Context

.NET 10's `HybridCache` (L1 memory + L2 distributed, stampede protection, tag invalidation) is the
usual default for caching. Bookings' only Redis usage is not caching:

- **Ticket reservations.** `ReserveTicketCommandHandler` writes reservations with a TTL
  (`ReservationTtl`) through `ICacheService.SetToCacheAsync`; `MakeBookingCommandHandler` reads
  them with `GetByKeysAsync` and removes them after commit. The Redis key *is* the reservation.
- **Distributed locks.** `IDistributedLockProvider` (`DistributedLock.Redis`) serialises
  reservation and booking across instances.

`ICacheService` (`Bookings.Application/Services/Interfaces`) wraps `StackExchange.Redis`'s
`IDatabase` with multi-key get/set/remove.

## Decision

**Keep reservations and locks on `StackExchange.Redis` directly. Do not route them through
`HybridCache`.**

`HybridCache` is wrong for this data:

- Its L1 is per instance. Instance A would still see a reservation instance B removed, and
  double-sell a seat.
- Stampede protection and serialization defaults solve cache-miss load, not ownership.
- A miss in a cache means "recompute"; a miss here means "not reserved". Different semantics.

### When to deviate

Use `HybridCache` for genuine read-through caching of data that can be recomputed and tolerates
brief staleness — e.g. caching an Events gRPC lookup in Bookings. Register it separately; do not
change `ICacheService`'s behaviour.

## Consequences

### Positive

- Reservation visibility is consistent across instances.
- Lock ordering and TTL behaviour are proven against real Redis in `BookingIntegration`.

### Negative

- `ICacheService` is misnamed: it is a reservation store. Renaming it is churn across handlers and
  tests; leave it unless that area is being reworked.
- No stampede protection — none is needed for this data.

### Mitigations

- The `bookings-service` skill documents the reservation and locking flow.
