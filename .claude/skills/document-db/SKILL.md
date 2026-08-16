---
name: document-db
description: Use when modelling documents, writing queries or repositories, or designing ids, partition keys and indexes against a document database in any TicketMaster service. Covers Azure Cosmos DB (Container, PartitionKey, point reads, RU cost) as the store in use.
---

# Document Databases

A document store rewards modelling around the queries you actually run and punishes relational
habits. There are no joins, atomicity across documents is limited, and the shape you write is the
shape you read.

## Scope

Cross-cutting — every TicketMaster service backed by a document store. Events.Api is the only one,
and it runs on **Azure Cosmos DB (NoSQL API)**. The MongoDB migration is done; `Events.Mongo` no
longer exists.

**The rules below are provider-neutral.** Cosmos specifics live in their own section. For
partitioning, RU costs and indexing policy in depth, read `cosmosdb-reference.md` in this skill
directory.

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

## Cosmos DB today

Microsoft.Azure.Cosmos 3.62.1, NoSQL API. Registration lives in
`Events.Cosmos.Extensions.ServiceCollectionExtension`. Full detail on partitioning, RU cost and
indexing policy in `cosmosdb-reference.md`.

**Lifetimes.** `CosmosClient` is thread-safe, holds the connection pool, and must be a
**singleton** — one per application, never per request. `Container` handles are cheap and can be
held alongside it.

**Partition keys are immutable.** A container's partition key cannot be changed in place; switching
means copying every item into a new container. Choose it before the first write, from the equality
filters your reads actually use. Events uses `/id` on all three containers because it is read
almost entirely by id.

**Point reads are the whole game.** `ReadItemAsync<T>(id, partitionKey)` is the cheapest operation
available, and `ReadManyItemsAsync` extends that price to a set of independent items. A query that
merely filters on `id` and the partition key is **not** a point read and does not get the price.
Shape access so the id is derivable and you avoid queries entirely.

**A missing item is an exception, not a null.** `ReadItemAsync` throws `CosmosException` with
`StatusCode.NotFound`. Catch it in the repository and return `null`; never let it reach application
code. `ReadItemStreamAsync` returns a status instead of throwing if the allocation matters.

**Serialization.** `UseSystemTextJsonSerializerWithOptions` is mutually exclusive with `Serializer`
and `SerializerOptions` — setting both throws. The identity property must serialize to lowercase
`id`; a camelCase naming policy gets there from `Id` without an attribute in the domain. Keep
`Newtonsoft.Json` as an explicit `PackageReference` even if nothing in your code uses it: the SDK
needs it internally and its nuspec does not declare it.

**Everything is indexed by default**, including geospatial data, so `ST_DISTANCE` works without a
declared spatial index. Tuning is therefore subtractive — but for a store written rarely and read
often, leaving the default is usually the right trade. Note that `CreateContainerIfNotExistsAsync`
matches on container **id alone**: an existing container comes back untouched and policy changes
are silently ignored. Provisioning at startup is not a migration mechanism; use
`ReplaceContainerAsync`.

**Writes are immediate and there is no unit of work.** A MediatR pipeline behavior named
`TransactionBehavior` that merely calls `next` provides no transaction — it is decoration, and
worse than nothing because it implies a guarantee that isn't there. Under Cosmos there is often no
honest implementation available: atomicity is confined to one logical partition, so with `/id`
partition keys no two documents can ever be written atomically.

**Throughput** can be provisioned per container or shared at the database level. Shared is the
cheaper floor when several small containers sit together — 400 RU/s total rather than each.

## Common mistakes

| Symptom | Cause |
|---|---|
| Reads cost far more RU than expected | A query was used where a point read would do |
| Reads slow down as data grows | Unbounded embedded array (rule 3) |
| Stale data in embedded copies | Denormalization with no stated consistency decision (rule 4) |
| Reads break after adding a field | Existing documents predate it (rule 10) |
| Changing database rewrites the domain | Driver id type leaked into domain entities (rule 6) |
| Handler assumes a rollback that never happens | No-op transaction behavior over immediate writes |
| Write costs climb unexpectedly | Default index policy indexing every property (rule 7) |
| Indexing or partition-key change had no effect | `CreateContainerIfNotExistsAsync` matches on id only |
| Serializer throws at client construction | `Serializer`/`SerializerOptions` set alongside `UseSystemTextJsonSerializerWithOptions` |
| Entity throws its own validation on load | Deserialization routed through the public constructor instead of a rehydration one |

## Sources

Retrieved 2026-08-08, except where noted.

- Cosmos DB data modeling: https://learn.microsoft.com/azure/cosmos-db/modeling-data
- Cosmos DB partitioning: https://learn.microsoft.com/azure/cosmos-db/partitioning
- Request units: https://learn.microsoft.com/azure/cosmos-db/request-units
- RU consumption: https://learn.microsoft.com/azure/cosmos-db/understand-request-unit-consumption
