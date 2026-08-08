---
name: document-db
description: Use when modelling documents, writing queries or repositories, or designing ids and indexes against a document database in any TicketMaster service. Covers MongoDB (IMongoCollection, BsonClassMap, ObjectId, Builders filters) today and Azure Cosmos DB for the planned migration.
---

# Document Databases

A document store rewards modelling around the queries you actually run and punishes relational
habits. There are no joins, atomicity across documents is limited, and the shape you write is the
shape you read.

## Scope

Cross-cutting — every TicketMaster service backed by a document store. Events.Api is the only one
today (MongoDB), with a planned move to Cosmos DB.

**The rules below are provider-neutral.** MongoDB and Cosmos DB specifics live in their own
sections. For Cosmos partitioning, RU costs and indexing policy in depth, read
`cosmosdb-reference.md` in this skill directory — you need it when migrating, not for daily work.

## Rules

1. **Model for the queries you run.** The MongoDB guidance is blunt about this: *"Data that's
   accessed together should be stored together"*, and *"design your schema for the queries you
   actually run, not theoretical use cases."* Normalizing by instinct produces a schema that needs
   joins the database doesn't have.

2. **Embed what is read together; reference what is read separately.** Embedding removes a round
   trip and is usually right for read-heavy, rarely-changing data. Reference when the related data
   is queried on its own, is large, or changes on a different cadence.

3. **Never embed an unbounded array.** A collection that grows without limit will eventually exceed
   the item size cap — 16 MB in MongoDB, 2 MB in Cosmos DB — and it degrades every read of the
   parent document long before it gets there. If it can grow forever, it's a separate collection.

4. **Every denormalized copy needs a stated consistency decision.** Duplicated data is a deliberate
   trade, and there are exactly two answers: it's a **snapshot** (frozen on purpose — the copy
   records how things were) or it's a **replica** (must be updated when the source changes, and
   something has to do that). Write down which. An undocumented copy is a bug waiting to be
   discovered as stale data.

5. **Give documents a stable, meaningful id.** A point read by id is the cheapest operation in any
   document store — dramatically cheaper than a query in Cosmos. If you can shape access so the id
   is derivable, you avoid queries entirely.

6. **The domain owns its id type; the driver's id type stays in the infrastructure layer.** Domain
   entities expose a `string` or a typed id (`EventId`), and the persistence layer maps to whatever
   the driver wants. A driver id type in the domain project means changing databases rewrites the
   domain instead of the mapping.

7. **Index what you filter and sort on — and nothing else.** Every index is paid for on every
   write. In Cosmos this is explicit: indexing raises the RU cost of writes, so an unrestricted
   index policy is a permanent tax.

8. **There is no join.** A read that needs two collections is either a modelling mistake (should
   have been embedded) or a deliberate second round trip. Never a surprise.

9. **Design so one business operation is one document write.** Cross-document atomicity is limited,
   slow, and in Cosmos confined to a single logical partition. If an operation must touch two
   documents atomically, the aggregate boundary is probably wrong.

10. **Old documents keep their old shape.** There is no migration that rewrites every document for
    free. Either version documents explicitly or make readers tolerant of missing and extra fields.
    Adding a non-nullable field to an entity silently breaks reads of every document written before
    it.

11. **Query construction stays inside the repository.** Filter builders, projections and driver
    types must not escape into application code — that's what makes the store swappable.

## MongoDB today

MongoDB.Driver 3.7.1. Registration lives in `Events.Mongo.Extensions.ServiceCollectionExtension`.

**Lifetimes.** `IMongoClient` is thread-safe, holds the connection pool, and must be a **singleton**
— one per application, never per request. `IMongoDatabase` and `IMongoCollection<T>` are cheap
handles over it and can be singletons too. Resolve the interface you registered: registering
`IMongoClient` and then asking for the concrete `MongoClient` fails at runtime.

**Class maps.** `BsonClassMap.RegisterClassMap<T>` is process-global and **throws if called twice
for the same type**. Register once at startup, guarded, not from a method that could run again in
tests or a second host.

**Ids.** `ObjectId` is a driver type. Map it at the boundary (rule 6). Cosmos has no equivalent —
its `id` is a string — so every `ObjectId` in the domain is migration debt.

**Writes are immediate.** `InsertOneAsync` / `UpdateOneAsync` hit the server there and then. There
is no unit of work and no change tracking. A MediatR pipeline behavior named `TransactionBehavior`
that merely calls `next` provides no transaction — it is decoration, and worse than nothing because
it implies a guarantee that isn't there. Real multi-document transactions need an explicit session
and a replica set.

**Indexes.** Nothing creates them for you. Declare them at startup with `CreateIndexesAsync`, and
treat the list as part of the schema.

## Cosmos DB when you migrate

Full detail in `cosmosdb-reference.md`. What changes conceptually:

| | MongoDB | Cosmos DB (NoSQL) |
|---|---|---|
| Id | `ObjectId`, unique per collection | `string`, unique **within a logical partition** |
| Partitioning | Optional sharding | **Mandatory partition key, immutable once set** |
| Cost model | Server resources | Request Units per operation, provisioned |
| Item size cap | 16 MB | 2 MB |
| Indexing | Opt-in per field | **Everything indexed by default** — opt out to save write RUs |
| Atomicity | Session transactions | Transactional batch within one logical partition |

The two decisions worth making before the migration, because they're expensive afterwards:

- **The id.** Rule 6 turns this from a domain rewrite into a mapping change.
- **The partition key.** It cannot be changed in place — moving to a different key means copying
  the container. For Events, a read-heavy store of rarely-changing data, pick the property that
  appears as an equality filter in the queries you actually run.

## Common mistakes

| Symptom | Cause |
|---|---|
| Resolution fails at startup | Registered an interface, resolved the concrete type |
| "class map already registered" | `RegisterClassMap` ran twice (tests, second host build) |
| Reads slow down as data grows | Unbounded embedded array (rule 3) |
| Stale data in embedded copies | Denormalization with no stated consistency decision (rule 4) |
| Reads break after adding a field | Existing documents predate it (rule 10) |
| Changing database rewrites the domain | Driver id type leaked into domain entities (rule 6) |
| Handler assumes a rollback that never happens | No-op transaction behavior over immediate writes |
| Cosmos write costs climb unexpectedly | Default index policy indexing every property (rule 7) |

## Sources

Retrieved 2026-08-08.

- MongoDB data modeling: https://www.mongodb.com/docs/manual/data-modeling/
- Cosmos DB data modeling: https://learn.microsoft.com/azure/cosmos-db/modeling-data
- Cosmos DB partitioning: https://learn.microsoft.com/azure/cosmos-db/partitioning
- Request units: https://learn.microsoft.com/azure/cosmos-db/request-units
- RU consumption: https://learn.microsoft.com/azure/cosmos-db/understand-request-unit-consumption
