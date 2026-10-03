---
name: payments-service
description: Use when working on PaymentSystem or PaymentProvider — the pay-in flow, refunds, the PaymentEvent (checkout) aggregate and its PaymentOrders, wallets, the double-entry ledger, PSP checkout/webhooks/refunds, checkout expiry, the payment outbox, or anything under PaymentSystem/ or Tests/Payments/.
---

# Payments Service

PaymentSystem takes a booking's payment from "requested" to "settled or failed" and tells Bookings which, and
gives a settled payment back — whole when the booking it paid for is voided, in part when the customer cancels
some seats. Money moves buyer to seller and, on a refund, back. Pay-out to a seller's bank and FX are out of scope. The design follows the Pragmatic Engineer "Designing a payment system" article (Alex Xu): a payment
event (checkout) with one payment order per seller, a PSP-hosted payment page with the order id as the
PSP's idempotency nonce, a wallet per seller, and a double-entry ledger.

## Scope

Covers `PaymentSystem/` (the service), `PaymentProvider/` (the PSP anti-corruption layer: Stripe and
Braintree behind `IPaymentGateway`) and `Tests/Payments/`.

**This service is vertical slices on a DDD domain** — a deliberate hybrid. Features are Users-style slices;
`Domain/` is a rich model built the way Bookings builds aggregates. Neither the Bookings layering nor a
Users-style anaemic entity applies here.

**Load alongside:** `cqrs` (commands, handlers, pipeline), `efcore` (queries, configuration, migrations),
`messaging` (contracts, outbox), `testing` (fixtures), `api-gateway` (the ungated webhook route).

## Layout

```
PaymentSystem/
  Domain/            PaymentEvent (root), PaymentOrder (child), OrderRefund (the order's child), Wallet,
                     LedgerEntry, BookingClaim,
                     Events/ (domain events), Exceptions/, Shared/ (MoneyAmount, CurrencyCode), Abstractions/
  Enums/             PaymentOrderStatus, EntryType, EntryReason (used by the domain)
  Data/              PaymentDbContext, Configurations/, Interceptors/, Migrations/
  Shared/
    Endpoints/       IEndpointMarker, CallerIdentity (X-Identity-UserId)
    Pipelines/       ITransactionalRequest, TransactionBehavior
    Results/         Error, ErrorType, ErrorResults (ToProblem), Result, Result<T>
    Messaging/       IIntegrationEventPublisher, OutboxIntegrationEventPublisher, OutboxFlushInterceptor
  Features/<Aggregate>/<Feature>.cs
    Checkouts/       RequestPayment, GetCheckout, ExpireCheckout, CancelCheckout, RefundCheckout, RecordRefund
    PaymentOrders/   StartCheckout, SubmitPaymentMethod, RecordOutcome, ReconcileOrders, GetPaymentOrder,
                     GetOrderLedger, Settle, Fail
    Webhooks/        HandleWebhook
    Wallets/         GetMyWallets
  Extensions/        ServiceCollectionExtension (infrastructure, endpoints, migrations), MessagingExtension
```

- **Features are grouped by aggregate, one file per feature, no per-feature folder.** A file holds a static
  class named for the intent (`Command`/`Query`, `Response`, `internal sealed Handler`) plus its trigger: a
  `public sealed ...Endpoints : IEndpointMarker`, a public Wolverine consumer, or a MediatR notification
  handler. Every file in an aggregate folder shares that folder's namespace.
- **No area reaches into another.** A rule two areas need belongs on the aggregate (`ApplyProviderAnswer` is
  shared by `RecordOutcome` and `HandleWebhook`), a capability on the type that owns it
  (`IPaymentGatewayFactory.ForProvider`, `Error.FromProvider`). The few lines of plumbing around it — the query,
  the save-and-retry — are written in each handler, like every other checkout handler's retry loop. **No static
  helper takes a `DbContext`**; `ProviderOutcome` was one, and it was removed for that.
- Endpoints are discovered by Scrutor (`AddFeatureEndpoints`), never registered in `Program.cs`.
- Tests mirror it: `Tests/Payments/PaymentIntegration/Features/<Aggregate>/<Feature>Tests.cs`; shared test
  helpers in `Fixtures/` (`CheckoutSeed`, `MessagingTest`, `PspTest`, `QueryTest`, `StubPsp`,
  `EndpointInvoker`); the real-host tests in `Mechanics/`.

## The domain

**`PaymentEvent` is the aggregate root** — one checkout per booking. It owns its `PaymentOrders` (one per
seller) and is the only way to change them: `StartExecuting`, `SucceedOrder`, `FailOrder`,
`MarkWalletUpdated`, `MarkLedgerUpdated`, `Expire`, `Cancel`, `RefundOrder`. `PaymentOrder`'s mutators are `internal`.
`Wallet` and `LedgerEntry` are separate.

1. **Created, then given its orders.** `PaymentEvent.Create(checkoutId, bookingId, buyerId)` makes an empty
   checkout; `AddOrder(merchantId, amount, currency)` adds one order: valid money and currency, **one order
   per seller**, **the buyer can never be a seller**, and **none once any order has left `NotStarted`**. A
   refused order leaves the checkout unchanged. **An empty checkout is never paid** (`IsPaymentDone` needs at
   least one order) **and never stored** — check constraint `CK_PaymentEvents_OrderCount` (`OrderCount > 0`).
   `BookingId` is unique in the store — that index is what makes a redelivered request a no-op.
2. **State machine per order:** `NotStarted → Executing → Success | Failed`, then `Success → Refunded`.
   `Expire`/`Cancel` fail every order still `NotStarted` or `Executing`; they never touch `Success`, `Failed`
   or `Refunded`. **Only a settled success is refunded** — `RefundOrder(id, refundId, amount, reference)`
   refuses one whose `WalletUpdated` and `LedgerUpdated` are not both set, since there is nothing yet to
   reverse. **A refund may be partial:** each is an `OrderRefund` (keyed `(PaymentOrderId, RefundId)`,
   auto-included with the order), `RefundedAmount` adds them up, and the order stays `Success` until they reach
   its whole `Amount` — then it is `Refunded`. `RefundableAmount` is what is left; a refund past it is refused,
   and check constraint `CK_PaymentOrders_RefundedAmount` keeps `0 ≤ RefundedAmount ≤ Amount`. **A repeated
   refund id returns `false`**, which is what keeps the money from being reversed twice. `IsFullyRefunded` is
   true once nothing is left `Success` and something is `Refunded`; a `Failed` order took no money and does not
   hold it back.
   **Every slice that refuses a settled order must list `Refunded` too** — `StartCheckout` and
   `SubmitPaymentMethod` would otherwise charge a refunded order again.
3. **At-least-once safe.** Repeating the outcome already reached is a no-op and raises nothing; the opposite
   outcome is refused (a settled payment cannot change its mind). A provider's answer goes through
   `ApplyProviderAnswer(id, succeeded)`, which **reports** instead of throwing — `Applied`, `AlreadyApplied`,
   `Superseded` or `NotStarted` (`Enums/OrderUpdate`) — because repeats, stale answers and early ones are normal
   traffic from a provider, not errors. A `Refunded` order takes a late success as `AlreadyApplied`. `StartExecuting` replayed with the same
   provider and token — including `null` again, for Braintree — is a no-op; a different either is refused.
4. **`IsPaymentDone` is derived** from the orders at the end of every root operation (all `Success`), no-ops
   included, so a stale flag heals.
5. **The root must be loaded whole.** Orders are an `AutoInclude` navigation, and a persisted `OrderCount`
   makes every root operation throw when fewer orders were loaded (e.g. `IgnoreAutoIncludes` plus a
   filtered `Include`) — otherwise "all orders succeeded" could be decided over a partial list.
6. **Money** (`Domain/Shared/MoneyAmount`): positive, at most 2 decimal places, at most
   `9999999999999999.99` — the `numeric(18,2)` column's range. The domain refuses what the column cannot
   hold exactly, because Postgres would round it and the PSP would charge a different amount than the ledger
   records. The configurations read `MoneyAmount.Precision`/`Scale`, so rule and column cannot drift.
7. **Ledger** entries only exist as balanced pairs: `LedgerEntry.RecordPayIn(order)` (debit the buyer,
   credit the seller) for a successful order, and `RecordRefund(order, refundId)` (debit the seller, credit the
   buyer) for each refund, for that refund's amount, each tagged with its `EntryReason` and — a refund pair —
   its `RefundId`. Refund pairs are written beside the pay-in, never in its place, so an order refunded in
   parts keeps its history and, once wholly refunded, sums to zero. Unique
   `(PaymentOrderId, Reason, Type, RefundId)` with `NULLS NOT DISTINCT`, so the pay-in pair (no refund id) stays
   one per order.
8. **Wallet** — one per seller per currency (unique `(OwnerId, Currency)`); `Credit` refuses another
   currency and a balance past the storable maximum. `Credit` itself is not idempotent:
   `PaymentOrder.WalletUpdated`, saved in the same transaction, is. `Debit` (a refund, for its own amount) **may go below
   zero** on purpose — the provider has already returned the money — and is made idempotent by
   `RefundOrder`'s return value, in the same transaction.
9. **`AddDomainEvent` is protected** — only an aggregate raises its own events.
10. **An order remembers its provider.** `StartExecuting` records the PSP's name (a string, so the domain
   never references `PaymentProvider`), and every later PSP call for the order goes through
   `gateways.ForProvider(order.Provider)` — that provider, or the default for an order with none recorded (not
   started, or started before the column existed). Never call `gateways.Default` for an existing order.

## Persistence and interceptors

Three `SaveChangesInterceptor`s, registered in this order in `AddInfrastructureServices`:

| Interceptor | Does |
|---|---|
| `AggregateVersionInterceptor` | Sets `PaymentEvent.Version` to *loaded version + 1* when the checkout or any of its orders changed. |
| `AuditTimestampsInterceptor` | Stamps `CreatedAt`/`UpdatedAt` by property name from the injected `TimeProvider`; never lets an update rewrite `CreatedAt`. |
| `DomainEventPublisherInterceptor` | Publishes domain events through MediatR **after** the write (scoped, like Bookings'). Clears them before publishing. A synchronous save with pending events is refused *before* writing. |

- **Version before timestamps** — the version bump is what marks an order-only change's checkout row
  modified, so the audit interceptor stamps it.
- **The domain never moves `Version`.** A domain-side bump broke disconnected `Update`: EF used the bumped
  value as the original, so a stale copy that had changed as often as the other writer overwrote a newer
  row. The interceptor always puts the *loaded* version in the `WHERE`.
- **Concurrency tokens:** `PaymentEvent.Version` (serialises every change within one checkout — two orders
  settling at once would otherwise each see the other unfinished and leave the checkout never done), and
  Postgres `xmin` on `Wallet` (two orders for one seller crediting at once). A conflict is a
  `DbUpdateConcurrencyException`; retry by **clearing the change tracker and reloading the whole checkout**.
  The version only guards the checkout row, so a retry that reloads just the root still tracks stale
  orders — which is why `Settle` refreshes the unchanged sibling orders before deciding the checkout is
  done (`ConcurrencyTests.TwoOrders_LoserReloadsRootInSameContextAndRetries_CheckoutEndsDone`).
- **Migrations** live in `Data/Migrations` and are applied at startup (`ApplyMigrationsAsync`, a
  single-instance convenience). `dotnet ef migrations add <Name> -p PaymentSystem -s PaymentSystem -o
  Data/Migrations`. Wolverine creates its own envelope tables at startup; they are not in the model.

## The pay-in flow

```
Bookings  MakeBooking ──► PaymentRequested(BookingId, BuyerId, SellerId, Amount, Currency)   [Bookings outbox]
Payments  RequestPayment ─ creates checkout ─ schedules CheckoutExpiryDue(+15 min)           [same transaction]
Client    POST orders/{id}/checkout ─► PSP session (nonce = PaymentOrderId) ─► StartExecuting ─► ClientToken
Client    pays on the PSP-hosted page (Stripe) / POST orders/{id}/payment-method (Braintree charges here)
PSP       POST webhooks/{provider} ─► Succeeded → SucceedOrder │ Canceled → FailOrder │ anything else → no-op
Payments  Settle (on PaymentOrderSucceeded): credit wallet + ledger pair + both flags; BookingPaid once all done
          Fail   (on PaymentOrderFailed):    BookingPaymentFailed
Job       ReconcileOrdersJob every 1 min: orders Executing > 2 min → LookupAsync at their provider →
          RecordOutcome (same write as a synchronous charge); errors per order are logged and skipped
Timer     ExpireCheckout after 15 min: fail every unsettled order → BookingPaymentFailed
Bookings  BookingCancelled ─► CancelCheckout: fail unsettled orders; a paid one is left, and CheckoutRefundDue
          is staged on a local queue                                                       [same transaction]
          no checkout yet ─► claim the booking Cancelled; a later PaymentRequested is refused (booking_cancelled)
Bookings  RefundRequested ─► RefundCheckout ─┐
Local     CheckoutRefundDue ─► RefundCheckout ┴► no amount: per Success order, what is left on it
                                                 amount:    that much off the single paid order
          ─► RefundAsync at its provider (no transaction)
          ─► RecordRefund: OrderRefund + wallet debit + refund ledger pair, order Refunded once all of it is back;
             BookingRefunded(BookingId, RefundId) — a partial one at once, a whole one once every order is refunded
```

## Refunds

- **Two halves, like a synchronous charge.** `RefundCheckout.Command` is the second command that is not
  `ITransactionalRequest` (`TransactionTest` names both): it calls the PSP with no transaction open, then sends
  `RecordRefund.Command`, which is transactional and does every write. Never move the PSP call inside a
  transaction, and never let `CancelCheckout` call it directly — it runs inside one, which is why it stages
  `CheckoutRefundDue` (pinned `[MessageIdentity("checkout-refund-due")]`, durable local queue `checkout-refund`)
  instead.
- **Every refund has an id, and the id is the idempotency.** `RefundRequested` carries the `RefundId` Bookings
  named it by. A partial refund is recorded under it; a whole-checkout refund records each order's part under
  it, or — when Bookings named none (`CheckoutRefundDue`, an old message) — under the order's own id, which is
  also what the `AddPartialRefunds` migration gave every refund made before parts existed. `RefundRequest`
  passes the id to the provider. Stripe: idempotency key `refund:{PaymentOrderId}:{RefundId}` and `refund_id`
  metadata; past the key's 24 hours an "already refunded" answer is matched to the refund carrying this id, and
  refused if none does. Braintree, which has no key: each refund carries the id as its `order_id`, and a repeat
  is found among the sale's `RefundIds` by it — an earlier part of the same order is not mistaken for this one.
  `RecordRefund` reverses the money only when `RefundOrder` returns true.
- **A partial refund comes off the checkout's single paid order.** Bookings never puts two sellers in one
  booking; two paid orders, another currency, or more than is left is logged as needing a manual refund without
  calling the provider. A partial refund that finds nothing paid is a whole refund that got there first and
  gave its money back too — logged at Warning, not an error.
- **`BookingRefunded` is published after the save**, as `Settle` publishes `BookingPaid`, so a concurrency
  retry cannot leave a copy in the outbox. It echoes the request's `RefundId`. A partial refund publishes when
  its one part is recorded; a whole one only when the last paid order's part finds `IsFullyRefunded`.
- **Pending counts as refunded.** The provider has taken the instruction; no refund webhook is handled, so one
  that fails later is not heard about (known gap).
- **A refusal is logged for a person, not retried.** `InvalidRequest` from the provider, a `Failed` refund, or a
  `RecordRefund` failure is logged at Error ("needs a manual refund") and counted in `NeedsAttention`; the
  booking is not reported refunded. A `Transient` provider error propagates, and Wolverine retries it on the
  schedule in `MessagingExtension` (10 s, 1 min, 5 min, 30 min).
- **Braintree voids an unsettled sale** instead of refunding it, and only for the full amount — a void returns
  everything, so a partial amount is refused rather than voiding all of it.

- **A booking is claimed once, by whichever message lands first** (`Domain/BookingClaim`, table
  `BookingClaims`, primary key `BookingId`). `RequestPayment` inserts a `Requested` claim with the checkout,
  in one save; `CancelCheckout` with no checkout inserts a `Cancelled` one. Two inserts of the same key
  serialise in Postgres — the second waits for the first to commit, then fails on the key — so a request and
  a cancellation arriving together cannot each miss the other's write. The loser clears its tracker and reads
  what won (EF saved inside a savepoint, so the open transaction stays usable). **No lock is taken**; a new
  handler that must order against these two claims the key the same way rather than locking.
- **Both handlers look for the checkout before the claim.** Checkouts stored before claims existed got a
  `Requested` claim from the `AddBookingClaims` backfill, but the order keeps a claim-less checkout safe
  regardless. Test seeding (`SeedCheckoutAsync`) inserts the claim too, matching production.
- A refused `booking_cancelled` request publishes nothing — Bookings already released the seats.
- **Rejected alternatives:** a Redis lock as in Bookings is released when the handler returns, *before*
  Wolverine commits its transaction, so the race reopens in that gap — and Payments has no Redis. EF Core 10
  has no pessimistic-lock or upsert API, and optimistic concurrency needs a row both sides update; here
  neither row exists yet. `Serializable` isolation would work but Wolverine owns the transaction. The
  `pg_advisory_xact_lock` this replaced was correct but held the rule by convention, not by schema.

- **PSP `Failed` is not final.** The provider lets the buyer retry with another method, so the order stays
  `Executing`; only `Canceled`, the 15-minute expiry, or a booking cancellation fails it.
- **A PSP success after expiry or cancel is refused** by the domain and logged as needing reconciliation.
- **Webhooks** are verified by the provider (`ParseWebhook`); an invalid signature is 400. Duplicate and
  out-of-order events are safe because only final statuses change an order and the domain treats repeats as
  no-ops — `EventId` is not stored.
- **Braintree** creates nothing until a payment method is submitted, so its order starts `Executing` with a
  `null` token and the order id alone correlates the outcome.
- **Calling checkout twice** asks the PSP again with the same nonce (the client token is never stored);
  the same reference returns the token and writes nothing, a different one is 409 `psp_session_mismatch`.
- **Price and seller come from the event.** Events stores one ticket price and an `OrganizerId` (whoever
  created the event); Bookings copies them onto each ticket and sends the booked tickets' prices summed, with
  the organizer as seller. Payments treats the request's amount as authoritative. An organizer buying a ticket
  to their own event is refused here, since the seller is the buyer.

## Messaging and the outbox

- Wolverine over RabbitMQ (`MessagingExtension.ConfigureMessaging`): Postgres message store, conventional
  routing limited to `TicketMaster.Common.IntegrationEvents`, all three durability policies. The expiry
  timer `CheckoutExpiryDue` is pinned with `[MessageIdentity("checkout-expiry-due")]` and routed to a
  durable local queue, so it never touches the broker and a rename cannot strand stored envelopes.
- **Consumes** `PaymentRequested`, `BookingCancelled`, `RefundRequested`. **Publishes** `BookingPaid`,
  `BookingPaymentFailed`, `BookingRefunded`.
- **Always publish through `IIntegrationEventPublisher`** (`PublishAsync`, `ScheduleAsync`). It stages the
  message in Wolverine's `DbContextOutbox` on the transaction already open, and `OutboxFlushInterceptor`
  sends it only after that transaction commits (dropped on rollback). Never call Wolverine's
  `SaveChangesAndFlushMessagesAsync` — it commits the transaction itself and would commit
  `TransactionBehavior`'s unit of work early.
- A request the domain refuses (bad amount, self-payment…) publishes `BookingPaymentFailed` and acks
  rather than throwing, so it cannot poison the queue and Bookings releases the seats.

## The HTTP surface

Behind the gateway at `/payments-service/**` (`GatewayAuthPolicy`), except webhooks (see below).

```
GET  api/payments/checkouts/{bookingId:long}                 the buyer's own checkout and its orders
GET  api/payments/orders/{paymentOrderId:guid}               the order's buyer or its merchant
GET  api/payments/orders/{paymentOrderId:guid}/ledger        the order's ledger pair + signed sum (0)
GET  api/wallets/me                                          the caller's wallets, one per currency
POST api/payments/orders/{paymentOrderId:guid}/checkout      start the PSP session → client token
POST api/payments/orders/{paymentOrderId:guid}/payment-method  synchronous charge (Braintree); 202 for Stripe
POST api/payments/webhooks/{provider}                        PSP callback — ungated route, signature-verified
```

- **Identity comes only from `X-Identity-UserId`** (`CallerIdentity.TryGetUserId`) → 401 without it. Never a
  route value or body field.
- **A read scoped by caller is the authorization check**: somebody else's checkout/order is 404, never 403.
- **`PspToken` never appears in a response**; the `ClientToken` is returned once and never logged or stored.
- Errors are `Result<T>` mapped by `ErrorResults.ToProblem`: NotFound 404, BadRequest 400, Conflict 409,
  Unauthorized 401, Forbidden 403. PSP errors: InvalidRequest 400, Transient 409 `psp_unavailable`,
  Configuration/Unknown propagate as 500.
- **The webhook route is ungated at the gateway** (`payments-webhooks-route`, `Order: -1`): PSPs send no user
  token. Its protection is the provider's signature check, so never add `GatewayAuthPolicy` to it.

## Configuration

`ConnectionStrings:DefaultConnection` (Postgres), `ConnectionStrings:RabbitMQ`, and
`PaymentProviders:{Default, Stripe:{SecretKey, WebhookSecret} | Braintree:{...}}`. The provider options are
validated at startup, so the service does not boot with empty secrets — set them with user-secrets.
Launch profile `https://localhost:7291` (the gateway's `payments-cluster`). In `compose.yaml` it runs as
`payments-api` (`PaymentSystem/Dockerfile`) against `payments_db` on the shared Postgres, with the Stripe keys
from `.env` (`PAYMENTS_STRIPE_SECRET_KEY`, `PAYMENTS_STRIPE_WEBHOOK_SECRET`).

## Tests

| Project | Covers |
|---|---|
| `Tests/Payments/PaymentDomain` | Every aggregate rule, the full transition matrix, money limits, ledger balance — no infrastructure |
| `Tests/Payments/PaymentIntegration` | Postgres (Testcontainers) via the production `AddInfrastructureServices`; schema from `MigrateAsync`. `Features/` per slice, plus Concurrency, Integrity, RoundTrip, Precision, Timestamps, DomainEvents |
| `…/Mechanics` | The real host (`WebApplicationFactory<Program>`) on Postgres + RabbitMQ with a stand-in Bookings host: durable endpoints, end-to-end request/cancel/expiry, outbox rollback |
| `Tests/Payments/PaymentAdapters` | The PSP adapters in `PaymentProvider` |
| `Tests/Payments/PaymentArchitecture` | ArchUnitNET: Domain depends only on itself, `Enums`, the BCL and MediatR; no feature area depends on another; `Shared` and `Data` never depend on `Features`; `PaymentProvider` never references `PaymentSystem`; handlers internal sealed; endpoints public sealed; every `Command` is `ITransactionalRequest` and no `Query` is — the two named exceptions share one reason: `SubmitPaymentMethod.Command` charges the PSP and `RefundCheckout.Command` refunds through it, each with no transaction open, and each sends a transactional command (`RecordOutcome`, `RecordRefund`) for the write (never add another exception without that reason); feature types live in `PaymentSystem.Features.<Aggregate>` |

- The fast fixture registers a recording `IIntegrationEventPublisher` (`IntegrationEventLog`, with scheduled
  messages kept separately) and `StubPsp`; the PSP is another process, so stubbing it is correct.
- **Seeding stores a state, not a settlement**: `SeedCheckoutAsync` clears the root's domain events, or the
  real `Settle` handler would write the wallet and ledger a test means to arrange itself.
- Needs Docker. `dotnet test Tests/Payments/PaymentIntegration/PaymentIntegration.csproj`.

## Known gaps

- **A Braintree sale that has not settled cannot be refunded in part** — only voided, which returns all of it.
  A customer cancelling one seat soon after paying through Braintree gets "needs a manual refund" until the sale
  settles; nothing retries it then.
- **A refund that fails after being accepted as pending is not heard about** — no refund webhook is handled,
  and `ReconcileOrdersJob` does not look at refunded orders. A refused or failed refund is only logged.
- **No settlement-file reconciliation.** `ReconcileOrdersJob` (`Features/PaymentOrders/ReconcileOrders.cs`)
  covers orders still `Executing`; an order already failed by expiry or cancellation whose payment the PSP
  then took is only logged ("needs reconciling").
- Two orders crediting one seller's existing wallet at once conflict on its `xmin` token; the loser answers
  409 and the webhook redelivery settles it (the synchronous Braintree path reports
  `payment_outcome_not_recorded`). Creating a seller's *first* wallet concurrently is not a gap: `Settle`
  catches the loss on the unique index and credits the winner's wallet in the same transaction
  (`SettlementTests.ConcurrentFirstCredit_…`).
