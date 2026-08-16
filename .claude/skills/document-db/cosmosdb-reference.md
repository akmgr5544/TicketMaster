# Azure Cosmos DB Reference

Depth behind the `document-db` skill's Cosmos section. Read when planning or executing the
migration, designing a partition key, or diagnosing RU cost — not for daily MongoDB work.

Condensed from Microsoft Learn, retrieved 2026-08-08.

- Partitioning: https://learn.microsoft.com/azure/cosmos-db/partitioning
- Data modeling: https://learn.microsoft.com/azure/cosmos-db/modeling-data
- Request units: https://learn.microsoft.com/azure/cosmos-db/request-units
- RU consumption: https://learn.microsoft.com/azure/cosmos-db/understand-request-unit-consumption
- Optimize request cost: https://learn.microsoft.com/azure/cosmos-db/optimize-cost-reads-writes
- Query performance: https://learn.microsoft.com/azure/cosmos-db/query-metrics

## Partitioning

Items in a container are divided into **logical partitions** by the value of the **partition key**.
All items sharing a partition key value live in one logical partition. The partition key plus the
item `id` uniquely identifies an item.

A partition key has two parts: the **path** (`/userId`, supports nested paths and underscores) and
the **value** (string or numeric).

### Immutability — the decision you cannot walk back

> "Partition key values are immutable. Once an item is created, its partition key value cannot be
> changed in place."

You also cannot change a container's partition key. Changing it means copying data to a new
container (container copy jobs), or adding a global secondary index. An item replacement requires
the partition key to match, so "moving" an item between partitions is a create + delete, and
**those two operations cannot be performed atomically across logical partitions.**

### Limits that force the decision

| Limit | Value |
|---|---|
| Storage per logical partition | 20 GB |
| Throughput per logical partition | 10,000 RU/s |
| Item size | 2 MB |

A container needs more than a couple of physical partitions once it exceeds **30,000 RU/s
provisioned** or **100 GB of data**. Below that, cross-partition query cost matters much less —
small containers usually occupy one or two physical partitions.

### Strategies

| Strategy | Use when | Cost |
|---|---|---|
| **Regular** (`/customerId`) | High cardinality and matches query filters | Hot partitions if some values dominate; 20 GB ceiling per value |
| **Synthetic** (`customerId` + `orderDate`) | No single field has both cardinality and query alignment | Queries filtering on only one component go cross-partition |
| **Hierarchical** (`/customerId` then `/orderId`) | Large datasets; queries filter on first and second level | First level must be high cardinality and present in most queries |
| **Global secondary index** (preview) | Multiple independent query patterns | Extra storage and RU; eventually consistent with the source |

### Anti-patterns

- **`/id` as the partition key** — one item per logical partition. Excellent write distribution and
  cheap point reads, but *any* filter on another property goes cross-partition. Only correct for
  workloads that are almost entirely point reads and writes.
- **Low-cardinality fields** (`status`, `type`, `country`) — a fixed small number of partitions,
  uneven distribution, hot partitions under load. Acceptable only while volume per value stays far
  below the 20 GB / 10,000 RU/s limits.
- **High cardinality with no query alignment** — a random GUID distributes writes perfectly and
  makes almost every read cross-partition.

## Request units

Every operation costs RUs; the container has a provisioned RU/s budget. Exceed it and the service
rate-limits, the SDK backs off and retries, and latency rises.

**The same query on the same data always costs the same RUs.** That makes RU cost measurable and
regression-testable — read `RequestCharge` off any response.

### Read efficiency, best to worst

1. **Point read** — item `id` + partition key. Cheapest possible read.
2. Query with a filter on a single partition key
3. Query with an equality or range filter on any property
4. Query with no filter

Point reads are only available through the SDKs/REST. **A query that happens to filter on `id` and
the partition key is not a point read** and does not get the point-read price.

### RU by document size

| Size | Read | Write |
|---|---|---|
| 1 KB | 1.00 | 4.95 |
| 4 KB | 1.14 | 6.67 |
| 16 KB | 1.67 | 9.52 |
| 64 KB | 4.76 | 22.67 |
| 256 KB | 20.28 | 98.29 |
| 1 MB | 145.90 | 625.00 |

Writes cost roughly 5x reads at small sizes, and the gap widens. Document size is the single
biggest lever on cost — this is the concrete reason rule 3 (no unbounded arrays) is about money and
not just correctness.

### Other cost factors

- **Consistency level:** *strong* and *bounded staleness* **double** the RU cost of every read.
- **Indexing:** every indexed property raises write RU. Property count matters because the default
  policy indexes everything.
- **Multi-region:** writes consume RUs in the primary region *and* for replication to each
  additional region, so write cost scales with region count. Reads are charged in the serving
  region.

## Indexing policy

Everything is indexed by default. That makes reads fast and writes expensive, so the tuning
direction is almost always *subtractive*: index only what you filter or sort on.

**Composite indexes** are needed for `ORDER BY` on multiple properties, and help queries filtering
on several properties. Order and direction matter:

| Composite index | `ORDER BY` query | Supported |
|---|---|---|
| `(name ASC, age ASC)` | `name ASC, age ASC` | Yes |
| `(name ASC, age ASC)` | `age ASC, name ASC` | **No** |
| `(name ASC, age ASC)` | `name DESC, age DESC` | Yes |
| `(name ASC, age ASC)` | `name ASC, age DESC` | **No** |
| `(name, age, timestamp)` | `name ASC, age ASC` | **No** |

A single composite index optimizes **at most one range filter** (`>`, `<`, `>=`, `<=`, `!=`), and
the range filter must be defined last.

## Migrating from MongoDB

**Done — Events moved to the Cosmos NoSQL API and `Events.Mongo` no longer exists.** Kept as a
record of what the move cost, and as a checklist if another service ever makes the same trip.

| Concern | Action |
|---|---|
| `ObjectId` ids | Map to `string` at the persistence boundary before migrating (`document-db` rule 6) |
| Partition key | Does not exist in the Mongo model — choose it deliberately, it's immutable |
| Item size | Cap drops 16 MB → 2 MB; check the largest documents you actually store |
| Indexes | Inverted default — Mongo indexes nothing extra, Cosmos indexes everything |
| Transactions | Session transactions become transactional batch, single logical partition only |
| Cost | Shifts from server capacity to per-operation RU; document size becomes a direct cost |

Keep the partition key in the filter of every query you can. Queries that omit it fan out across
physical partitions and pay for each one.
