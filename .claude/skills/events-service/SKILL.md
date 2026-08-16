---
name: events-service
description: Use when working on Events — events, venues, performers, the Cosmos DB persistence layer, partition keys, document serialization, or anything in Events.Domain, Events.Application, Events.Cosmos or Events.Api.
---

# Events Service

Events owns the catalogue: what is happening, where, and who is performing. This is reference data
— written rarely, read often, and effectively immutable once created. Bookings depends on it via
the `EventCreatedIntegrationEvent`, which is what causes tickets to exist.

## Scope

Covers `Events.Domain/`, `Events.Application/`, `Events.Cosmos/`, `Events.Api/`.

**Layered Clean Architecture with DDD, on Azure Cosmos DB (NoSQL API).** Bookings shares the
layering but is relational — the aggregate rules below are shaped by the document model and are not
interchangeable with Bookings'. Users.Api is vertical slice; nothing from there applies.

**Load alongside this skill:**
- `cqrs` — before writing a command, handler or pipeline behavior.
- `document-db` — before writing a query, repository or document model.
- `messaging` — before publishing an integration event.

## Project layout and dependency direction

```
Events.Api  ──►  Events.Application  ──►  Events.Domain
     │                                         ▲
     └────────►  Events.Cosmos  ───────────────┘
```

| Project | Owns | Must not reference |
|---|---|---|
| `Events.Domain` | Entities, value objects, repository *interfaces*, domain exceptions | Anything at all — it has zero package and project references |
| `Events.Application` | Commands, handlers, DTOs, DI wiring | `Events.Cosmos` |
| `Events.Cosmos` | `EventsCosmosContext`, serialization, repository implementations, provisioning | `Events.Api` |
| `Events.Api` | HTTP surface, composition root | `Events.Domain` for business decisions |

`Events.Api` references `Events.Cosmos` directly and deliberately: Application stops at the
repository interfaces, so only the composition root knows which store is behind them.

## The decisions you cannot cheaply reverse

1. **Partition key is `/id` on all three containers.** Cosmos cannot change a container's partition
   key in place — switching means copying every item to a new container. `/id` is right here
   because the catalogue is read almost entirely by id, and a point read on id + partition key is
   the cheapest operation Cosmos offers. Queries filtering on anything else (venue geo search,
   events at a venue) fan out across partitions, which is affordable at catalogue size.
2. **Ids are strings generated in the entity constructor** with `Guid.CreateVersion7()` — sortable,
   and known before the write instead of assigned by the driver afterwards.
3. **Throughput is provisioned at the database level** (400 RU/s shared), so three containers cost
   the same floor as one.
4. **Entities have a private parameterless constructor used only for rehydration.** This is not
   ceremony. Binding through the public constructor re-runs creation invariants on every read, and
   `Event`'s minimum-lead-time rule would throw when loading any event that has already happened.
   Reading is not creating.

## Aggregates in a document store

1. **The aggregate boundary is the document boundary.** What loads and saves together as one unit
   is one document.
2. **One document write per business operation.** Cross-document atomicity is confined to a single
   logical partition — and with `/id` partition keys, no two documents ever share one, so there is
   effectively none. Do not design an operation that needs it.
3. **An aggregate is entered through its root.** Embedded entities are reached through the document
   that contains them, never fetched or written independently.
4. **State changes go through behavior, not setters.** Every entity property is `private set`;
   changes go through `Rename`, `Relocate`, `Reschedule`.
5. **The domain owns its id type.** Entities expose `string`. No driver type appears in
   `Events.Domain` — enforced by `DependenceTest`.
6. **One repository per aggregate root.** `IEventRepository`, `IVenueRepository` and
   `IPerformerRepository` each serve exactly one.
7. **The domain project stays persistence-ignorant.** `Events.Domain.csproj` has no references at
   all. Keep it that way.

## Data model and the embedding decision

An `Event` embeds its `Venue` in full and its `Performer` list in full. `Venue` and `Performer`
also exist as their own containers.

**This is a deliberate snapshot, not an accident.** The embedded copy records the venue and
performers *as they were when the event was created*. Renaming a venue must **not** retroactively
rewrite past events.

Consequences to hold onto:

- The standalone `venues` and `performers` containers are the source **for creating new events**.
  They are not a source of truth for events that already exist.
- Updating a venue deliberately does **not** fan out to embedded copies. Divergence between a
  venue document and the copy inside an old event is correct behavior.
- Anything needing "the venue as it is now" reads the `venues` container by id. Anything needing
  "the venue as it was" reads the embedded copy. Be explicit about which one a query wants.
- This satisfies `document-db` rule 4 by choosing **snapshot**. Do not "fix" the staleness.

The bound on rule 3 (no unbounded arrays) holds because performers per event is naturally small,
and matters more under Cosmos than it did under MongoDB — the item cap is 2 MB, not 16 MB, and
document size drives RU cost directly.

## Rules

8. **Events.Api is the only writer of the events catalogue.** No other service writes venues,
   performers or events.
9. **`EventCreatedIntegrationEvent` is a public contract** in `TicketMaster.Common`. Bookings
   creates tickets from it — changing its shape non-additively breaks ticket creation.
10. **The integration event must not be published unless the write succeeded and is durable.**
    Publishing directly after a write loses the message if the process dies in between, and
    Bookings then never creates tickets for an event that exists. See `messaging` rule 4.
    **Events does not satisfy this today** — see Known gaps.
11. **Geographic coordinates use `GeoLocation`**, a validated value object in `Events.Domain`,
    serialized as a GeoJSON Point. GeoJSON orders coordinates `[longitude, latitude]` — reversed
    from how they are written. `GeoLocationConverter` is the single place that order is decided.
12. **Repository signatures are expressed in domain terms.** `UpdateVenueAsync` takes the `Venue`
    aggregate, not loose fields, so an update cannot bypass the entity's validation.
13. **Default indexing is left in place.** Cosmos indexes everything by default, including
    geospatial data, so `ST_DISTANCE` works without a declared spatial index. For a store written
    rarely and read often, the write cost is a fair trade. Note that
    `CreateContainerIfNotExistsAsync` matches on container id alone, so changing an indexing policy
    later needs `ReplaceContainerAsync` — startup provisioning is not a migration mechanism.

## Serialization

`Events.Cosmos/Serialization/` holds everything the serializer knows; no JSON attribute appears in
the domain.

- `CosmosJson.Options` is the single `JsonSerializerOptions` the `CosmosClient` is built with, and
  the same instance the tests exercise. camelCase naming is what turns `Id` into the lowercase `id`
  Cosmos requires.
- `DomainBinding` is a `JsonTypeInfo` modifier that selects the private rehydration constructor and
  writes to private setters and collection backing fields.
- `GeoLocationConverter` maps `GeoLocation` to and from a GeoJSON Point.
- `UseSystemTextJsonSerializerWithOptions` is **mutually exclusive** with `Serializer` and
  `SerializerOptions` — setting both throws.
- `Newtonsoft.Json` must stay an explicit `PackageReference` on `Events.Cosmos` even though nothing
  in our code uses it: the Cosmos SDK uses it internally for system types and its nuspec does not
  declare the dependency.

## Reads

| Need | Use | Cost |
|---|---|---|
| One item by id | `container.PointReadAsync<T>(id, ct)` | ~1 RU, the cheapest read available |
| Many items by id | `ReadManyItemsAsync<T>` | point-read cost per item, no fan-out |
| Anything else | SQL query | fans out across partitions |

A query that merely filters on `id` is **not** a point read and does not get point-read pricing.
`PointReadAsync` turns the SDK's NotFound exception back into `null`; that exception must never
escape the persistence layer.

## Known gaps

**Wrong behavior:**
- `TransactionBehavior` calls `next` and returns. Under Cosmos there is nothing to implement here —
  atomicity is per logical partition and `/id` keys mean nothing shares one. A handler must not
  assume a rollback. The class is documented as a no-op rather than quietly left implying a
  guarantee.
- `CreateEventCommandHandler` publishes the integration event straight after the write, with no
  outbox (rule 10). Wolverine is configured with RabbitMQ but no message persistence, so a crash
  between the write and the publish loses the message and Bookings never creates tickets. This is
  the most valuable remaining fix.

**Missing:**
- No read endpoints. The service is write-only: the three controllers only POST.
- No optimistic concurrency — `_etag` is not read or enforced, so concurrent venue updates
  last-write-wins.
- Emulator geospatial support is unverified; `ST_DISTANCE` has not been exercised against it.

## Adding a feature

1. Model the change on the entity in `Events.Domain` — a method, not a setter. Domain ids stay
   strings.
2. Add the command in `Events.Application/Commands` and the handler in `CommandHandlers`.
3. Add or extend the repository interface in `Events.Domain`; implement it in `Events.Cosmos`.
4. Decide the document shape first: embedded or referenced, and if embedded, snapshot or replica
   (`document-db` rule 4).
5. Shape the read so it can be a point read if at all possible.
6. If another service must learn about it, add the contract to `TicketMaster.Common` and publish
   through an outbox — not inline after the write.

## Common mistakes

| Symptom | Cause |
|---|---|
| A past-dated event throws on load | Deserialization routed through the public constructor, re-running creation invariants |
| Coordinates land in the wrong hemisphere | GeoJSON is `[longitude, latitude]`; the order was swapped |
| Bookings has no tickets for an existing event | Integration event published without an outbox (rule 10) |
| Handler assumes a rollback | `TransactionBehavior` is a no-op, and Cosmos cannot provide one here |
| Venue rename doesn't appear on old events | Correct — embedded copies are snapshots by design |
| Venue rename doesn't appear on the venue page either | Reading the embedded copy where the container was wanted |
| Reads cost far more RU than expected | A query was used where a point read would do |
| Indexing policy change had no effect | `CreateContainerIfNotExistsAsync` matches on id only; needs `ReplaceContainerAsync` |
| `Serializer`/`SerializerOptions` throws at startup | Set alongside `UseSystemTextJsonSerializerWithOptions` |
