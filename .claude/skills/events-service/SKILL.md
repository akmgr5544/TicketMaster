---
name: events-service
description: Use when working on Events — events, venues, performers, the MongoDB persistence layer, the planned Cosmos DB migration, or anything in Events.Domain, Events.Application, Events.Mongo or Events.Api.
---

# Events Service

Events owns the catalogue: what is happening, where, and who is performing. This is reference data
— written rarely, read often, and effectively immutable once created. Bookings depends on it via
the `EventCreatedIntegrationEvent`, which is what causes tickets to exist.

## Scope

Covers `Events.Domain/`, `Events.Application/`, `Events.Mongo/`, `Events.Api/`.

**Layered Clean Architecture with DDD, on a document store.** Bookings shares the layering but is
relational — the aggregate rules below are shaped by the document model and are not
interchangeable with Bookings'. Users.Api is vertical slice; nothing from there applies.

**Load alongside this skill:**
- `cqrs` — before writing a command, handler or pipeline behavior.
- `document-db` — before writing a query, repository or document model.
- `messaging` — before publishing an integration event.

## Project layout and dependency direction

```
Events.Api  ──►  Events.Application  ──►  Events.Domain
                                              ▲
                        Events.Mongo  ────────┘
```

| Project | Owns | Must not reference |
|---|---|---|
| `Events.Domain` | Entities, repository *interfaces*, domain exceptions | Any driver or persistence package |
| `Events.Application` | Commands, handlers, DTOs, DI wiring | `Events.Mongo` |
| `Events.Mongo` | `MongoDomainContext`, class maps, repository implementations | `Events.Api` |
| `Events.Api` | HTTP surface | `Events.Domain` for business decisions |

`Events.Application` currently references `Events.Mongo`, which inverts this. See Known gaps.

## Aggregates in a document store

1. **The aggregate boundary is the document boundary.** What loads and saves together as one unit
   is one document. If two things must change atomically, they belong in the same document.
2. **One document write per business operation.** Cross-document atomicity is limited and, after
   the Cosmos migration, confined to a single logical partition.
3. **An aggregate is entered through its root.** Embedded entities are reached through the document
   that contains them, never fetched or written independently.
4. **State changes go through behavior, not setters.** Public setters on every property make the
   entity a bag of data that any caller can put into an invalid state.
5. **The domain owns its id type.** Entities expose `string` or a typed id; `ObjectId` belongs in
   `Events.Mongo`. This is what keeps the Cosmos migration a mapping change (`document-db` rule 6).
6. **One repository per aggregate root.** A repository that fetches performers, venues *and* writes
   events serves three aggregates and belongs to none.
7. **The domain project stays persistence-ignorant.** No driver packages, no BSON attributes, no
   shapes chosen for the serializer.

## Data model and the embedding decision

An `Event` embeds its `Venue` in full and its `Performer` list in full. `Venue` and `Performer`
also exist as their own collections.

**This is a deliberate snapshot, not an accident.** The embedded copy records the venue and
performers *as they were when the event was created*. Renaming a venue must **not** retroactively
rewrite past events.

Consequences to hold onto:

- The standalone `venues` and `performers` collections are the source **for creating new events**.
  They are not a source of truth for events that already exist.
- Updating a venue deliberately does **not** fan out to embedded copies. Divergence between a
  venue document and the copy inside an old event is correct behavior.
- Anything needing "the venue as it is now" reads the `venues` collection by id. Anything needing
  "the venue as it was" reads the embedded copy. Be explicit about which one a query wants.
- This satisfies `document-db` rule 4 by choosing **snapshot**. Do not "fix" the staleness.

The bound on rule 3 (no unbounded arrays) holds because performers per event is naturally small.
If that ever stops being true, the embedding decision has to be revisited.

## Rules

8. **Events.Api is the only writer of the events catalogue.** No other service writes venues,
   performers or events.
9. **`EventCreatedIntegrationEvent` is a public contract** in `TicketMaster.Common`. Bookings
   creates tickets from it — changing its shape non-additively breaks ticket creation.
10. **The integration event must not be published unless the write succeeded and is durable.**
    Publishing directly after an insert loses the message if the process dies in between, and
    Bookings then never creates tickets for an event that exists. See `messaging` rule 4.
11. **Geographic coordinates use a geo type, not a 2D integer point.** `System.Drawing.Point` is
    integer X/Y for drawing surfaces — it cannot represent latitude and longitude, and it makes geo
    queries impossible.
12. **Repository signatures are expressed in domain terms.** A repository method taking a drawing
    primitive or a driver type has leaked infrastructure into the contract.
13. **Reference data still needs indexes.** "Rarely written" is an argument for *more* indexes, not
    fewer — the write cost barely matters and the read benefit is permanent.

## Known gaps

**Stops the service from running at all:**
- `AddSingleton<IMongoDatabase>` resolves `MongoClient` (the concrete type) but only `IMongoClient`
  is registered. Throws on first resolution.
- `appsettings.json` has no `MongoConfigs` section, so `MongoOptions.ConnectionString` is null.
- **`ConfigureRabbitMq` is never called from `Program.cs`.** Wolverine never starts, `IMessageBus`
  is never registered, and `CreateEventCommandHandler` cannot be constructed.
- `UseRabbitMqUsingNamedConnection("")` passes an empty connection name.
- There are no controllers or endpoints, and no `AddControllers()` / `MapControllers()`. The
  service exposes nothing.

**Architecture:**
- `Events.Domain` references `MongoDB.Bson` and every entity's `Id` is `ObjectId` (rule 5). This is
  the single largest obstacle to the Cosmos migration.
- `Events.Application` references `Events.Mongo`, inverting the dependency direction.
- `IEventRepository` exposes performer, venue and event operations while `IPerformerRepository` and
  `IVenueRepository` also exist (rule 6).
- `Events.Domain/Extensions/ServiceCollectionExtension.AddDomainServices` is an empty no-op, and
  the domain project references DI abstractions to provide it.
- `Events.Domain` has an empty `IntegrationEvents/` folder; contracts belong in
  `TicketMaster.Common`.

**Wrong behavior:**
- `TransactionBehavior` calls `next` and returns — it is a no-op wearing the name of a guarantee.
- `CreateEventCommandHandler` publishes the integration event straight after the insert, with no
  outbox and no message persistence configured anywhere in Events (rule 10).
- `Venue.Location` is `System.Drawing.Point` (rule 11), and that type appears in
  `IVenueRepository.UpdateVenueAsync` (rule 12).
- No indexes are ever created (rule 13).
- `BsonClassMap.RegisterClassMap` is called from `AddInfrastructureServices`; a second call for the
  same type throws.
- `EventRepository.UpdateEventAsync` throws `NotImplementedException`.
- Entities have public setters throughout (rule 4) and no domain behavior.
- `Events.Application/Extensions/ServiceCollectionExtension.cs` has an unused `using MassTransit;`
  alongside the Wolverine configuration.

## Adding a feature

1. Model the change on the entity in `Events.Domain` — a method, not a setter. Domain ids stay
   domain types.
2. Add the command in `Events.Application/Commands` and the handler in `CommandHandlers`.
3. Add or extend the repository interface in `Events.Domain`; implement it in `Events.Mongo`.
4. Decide the document shape first: embedded or referenced, and if embedded, snapshot or replica
   (`document-db` rule 4).
5. Add any index the new query needs, at startup.
6. If another service must learn about it, add the contract to `TicketMaster.Common` and publish
   through the outbox — not inline after the write.

## Common mistakes

| Symptom | Cause |
|---|---|
| Startup fails resolving Mongo services | Interface registered, concrete type resolved |
| Bookings has no tickets for an existing event | Integration event published without an outbox (rule 10) |
| Handler assumes a rollback | `TransactionBehavior` is a no-op |
| Venue rename doesn't appear on old events | Correct — embedded copies are snapshots by design |
| Venue rename doesn't appear on the venue page either | Reading the embedded copy where the collection was wanted |
| Cosmos migration touches every domain file | `ObjectId` in the domain (rule 5) |
| Geo query impossible to write | Location stored as a 2D integer point (rule 11) |
