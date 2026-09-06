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

## Domain events on the Event aggregate

`Event` inherits `Events.Domain/Abstractions/Entity`, which holds a list of `IDomainEvent`. `Venue`
and `Performer` deliberately do not — nothing outside this service reacts to them.

1. **`IDomainEvent` is a bare marker with no base type.** Bookings' equivalent implements MediatR's
   `INotification`; copying that here would put a package reference on `Events.Domain`, which has
   none (rule 7, enforced by `DependenceTest`).
2. **Dispatch is explicit in the command handler, not an interceptor.** Bookings dispatches from a
   `SaveChangesInterceptor`; Cosmos has no equivalent hook. The order is load → mutate → write →
   publish, and it matters both ways: a refused mutation throws before the write so nothing is
   stored *and* nothing is announced, and publishing after the write means no consumer hears about a
   change that failed to persist.
3. **Every mutation bumps `Version` and raises a domain event.** Consumers use the version to
   discard messages that arrive out of order, so a mutation that forgets to bump it is silently
   unprotected. Validate before bumping, so a refused change leaves the version alone.
4. **Domain events carry resulting state, not deltas** — a relocation says which seats the event now
   has, never which were added or removed. That is what makes a redelivered message harmless
   downstream, and it is why `EventRelocated` carries `StartDate` it did not change: the consumer
   needs it to create tickets for seats that are new.
5. **Translation to public contracts happens in `Events.Application/IntegrationEvents`**, never in
   the domain — `TicketMaster.Common` is a reference `Events.Domain` may not take. A domain event is
   allowed to have no public counterpart: `EventLineupChangedDomainEvent` has none, because nothing
   outside depends on who is performing.
6. **Everything reaches the broker through `IIntegrationEventPublisher`.** It takes the aggregate
   rather than a list of events, so clearing cannot be forgotten, and it is the single place an
   outbox will land.
7. **`Cancel()` is idempotent.** Cancelling an already-cancelled event changes nothing, raises
   nothing and does not move the version — it is the same request arriving twice, not an error. Any
   other mutation on a cancelled event throws `EventsDomainException`.

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
- **Enums are stored as names, not ordinals** (`JsonStringEnumConverter`). Inserting or reordering a
  value would otherwise silently reinterpret every document already written, and `c.status =
  "Cancelled"` is a query a human can read.
- `DomainBinding` is a `JsonTypeInfo` modifier that selects the private rehydration constructor and
  writes to private setters and collection backing fields.
- **`DomainBinding` also drops every property declared on `Entity`** — today that is `DomainEvents`.
  This does not fail loudly if removed, which is why it has its own test. The getter works, so every
  write carries a `"domainEvents":[{}]` array — one empty object per raised event, because
  `IDomainEvent` is an interface with no members — while nothing reads it back. `_domainEvents` is
  private to the base class, so `GetField(..., NonPublic)` on the derived type never finds it and the
  property never gets a setter. The result is silent document bloat charged in RU on every write, not
  an exception. The fix cannot be `[JsonIgnore]`: no JSON attribute may appear in the domain.
- `GeoLocationConverter` maps `GeoLocation` to and from a GeoJSON Point.
- `UseSystemTextJsonSerializerWithOptions` is **mutually exclusive** with `Serializer` and
  `SerializerOptions` — setting both throws.
- `Newtonsoft.Json` must stay an explicit `PackageReference` on `Events.Cosmos` even though nothing
  in our code uses it: the Cosmos SDK uses it internally for system types and its nuspec does not
  declare the dependency.

## Reads

| Need | Use | Cost |
|---|---|---|
| One item by id | `container.PointReadWithETagAsync<T>(id, ct)` → `(Item, ETag)` | ~1 RU, the cheapest read available |
| Many items by id | `ReadManyItemsAsync<T>` | point-read cost per item, no fan-out |
| Anything else | SQL query | fans out across partitions |

A query that merely filters on `id` is **not** a point read and does not get point-read pricing.
`PointReadWithETagAsync` turns the SDK's NotFound exception back into `null`; that exception must never
escape the persistence layer.

**Paging is cursor-based.** `ListVenuesAsync` returns `Page<T>(Items, ContinuationToken)`; the
caller sends the token back to continue. Do not add `OFFSET`/`LIMIT` — Cosmos charges for the rows
it skips, so deep pages get progressively more expensive.

## Failures and status codes

Events breaks the flow by **throwing**, not by returning a result type. The `Result`/`Error`
pattern belongs to Users.Api and must not be introduced here — see `cqrs` rule 4.

**There are exactly three *public* exception types in the whole service.** Do not add a fourth without
a good reason — a class per failure case multiplies with every entity. `ConcurrencyConflictException`
in `Events.Domain/Exceptions` is the one exception to the count and is deliberately not in the table
below: it exists because `Events.Cosmos` references only `Events.Domain` and so cannot throw an
Application type, and `ConcurrencyRetryBehavior` consumes and converts it, so it never reaches the API.

| Type | Where | Thrown when | Status |
|---|---|---|---|
| `EventsDomainException` | `Events.Domain/Exceptions` | An entity refuses a change — a broken invariant. Every entity throws this one; it has no subclasses. | 400 |
| `NotFoundException(entity, id)` | `Events.Application/Exceptions` | Something was asked for by id and is not there. | 404 |
| `EventsApplicationException` | `Events.Application/Exceptions` | The model is intact but the use case cannot proceed — the request conflicts with current state. | 409 |

A lookup that misses is not a domain rule violation: nothing about the aggregate is wrong, the
document simply is not there. That is why "not found" lives in Application, not Domain.

`NotFoundException` is **not** `KeyNotFoundException` on purpose. That is what a `Dictionary`
indexer throws, so mapping it to 404 would turn a stray lookup bug in our own code into a "not
found" for the caller instead of a visible failure.

`Events.Api/Handlers/EventsExceptionHandler` maps all three to `ProblemDetails`. Anything else is
left unhandled and surfaces as a 500 — correct for the genuinely unexpected.

**The switch arms are ordered most-derived-first, and the compiler enforces it.**
`NotFoundException` derives from `EventsApplicationException`, so putting the base arm first makes
the derived arm unreachable and the build fails with `CS8510`. This is not a convention anyone has
to remember.

Adding a failure mode is therefore usually **not** a new type: throw one of the three with a
message that says what happened.

## Known gaps

**Wrong behavior:**
- `TransactionBehavior` calls `next` and returns. Under Cosmos there is nothing to implement here —
  atomicity is per logical partition and `/id` keys mean nothing shares one. A handler must not
  assume a rollback. The class is documented as a no-op rather than quietly left implying a
  guarantee.
- **`WolverineIntegrationEventPublisher` publishes inline, with no outbox (rule 10).** Wolverine is
  configured with RabbitMQ but no message persistence, so a crash between the Cosmos write and the
  publish loses the message. This is now worse than it was when only creation was published: a lost
  `EventCancelled` or `EventRelocated` leaves Bookings' tickets permanently out of sync with the
  catalogue, not merely missing. Still the most valuable remaining fix. Wolverine has **no Cosmos
  message store** (Postgres/SqlServer/Marten only), so the options are a hand-rolled outbox document
  plus a publisher loop, or a Postgres purely for messaging. Everything funnels through
  `IIntegrationEventPublisher`, so it is a change in one place.

**Missing:**
- All three aggregates now have full CRUD. Events use per-facet sub-resources
  (`PUT /{id}/schedule`, `/venue`, `/lineup`, `POST /{id}/cancel`) rather than one PUT — a
  **deliberate** inconsistency with venues and performers, because each event mutation has a
  different downstream consequence and a combined PUT would have to infer which happened by diffing.
- Events are cancelled, never deleted. There is no `DELETE /api/events/{id}` on purpose: tickets
  exist downstream, so removal is a state transition, not a removal.
- Optimistic concurrency is enforced. Reads record the document ETag in a per-request side-channel
  (`ETagCache`, one per **scoped** repository — a singleton repository would turn it into a
  cross-request cache and be worse than no guard); updates and deletes send it as `IfMatchEtag`; a 412
  becomes `ConcurrencyConflictException` and `ConcurrencyRetryBehavior` retries the whole
  read-modify-write three times, then reports 409. This is why a handler must keep the shape
  load → mutate → write → publish: a retry re-runs it from the top, so the conflicting write has to be
  its first irreversible side effect. `Event.Version` is unchanged and still only orders messages for
  consumers. The 412 path itself has no automated test — see the README's "Built but unverified".
- `CreateEventCommandHandler` still throws `EventsDomainException` (→ 400) for a missing venue or
  performer, where the newer handlers throw `NotFoundException` (→ 404). The newer behaviour is the
  correct one; create was left alone rather than silently changing an existing endpoint's status
  code.
- The delete guards on venues and performers are **best-effort**.
  `CountUpcomingEventsAtVenueAsync` / `CountUpcomingEventsWithPerformerAsync` run before the delete,
  and an event can be created in between; with `/id` partition keys no transaction can close that
  window. They prevent the accident, not the race.
- The performer delete guard's `EXISTS` subquery over `c.performers` has not been run against
  Cosmos or the emulator — only its shape is reviewed.
- Emulator geospatial support is unverified; `ST_DISTANCE` has not been exercised against it.
- Nothing in the Cosmos layer has been exercised against a running Cosmos instance.

## Adding a feature

1. Model the change on the entity in `Events.Domain` — a method, not a setter. Domain ids stay
   strings.
2. Add the command in `Events.Application/Commands` and the handler in `CommandHandlers`.
3. Add or extend the repository interface in `Events.Domain`; implement it in `Events.Cosmos`.
4. Decide the document shape first: embedded or referenced, and if embedded, snapshot or replica
   (`document-db` rule 4).
5. Shape the read so it can be a point read if at all possible.
6. If another service must learn about it, raise a domain event from the aggregate, add the contract
   to `TicketMaster.Common`, map it in `IntegrationEventTranslator`, and publish via
   `IIntegrationEventPublisher` — never build the contract by hand in a handler. Carry resulting
   state and the aggregate `Version`, not a delta.

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
| `"domainEvents":[{}]` in stored documents | `DomainBinding.DropDomainEventBookkeeping` removed or bypassed |
| Consumers act on a change that was rolled back | Published before the write instead of after it |
| A consumer reverts a newer change | A mutation didn't bump `Version`, so the consumer couldn't tell the message was stale |
| Same domain event published twice | The aggregate was saved again without `ClearDomainEvents` — use `IIntegrationEventPublisher`, which clears for you |
| A lineup change produces no message | Correct — it has no integration contract by design |
