---
name: payments-service
description: Use when working on PaymentSystem or PaymentProvider — the pay-in flow, the PaymentEvent (checkout) aggregate and its PaymentOrders, wallets, the double-entry ledger, PSP checkout/webhooks, checkout expiry, the payment outbox, or anything under PaymentSystem/ or Tests/Payments/.
---

# Payments Service

PaymentSystem takes a booking's payment from "requested" to "settled or failed" and tells Bookings which.
It is **pay-in only** — money from buyer to seller. Pay-out to a seller's bank, refunds and FX are out of
scope. The design follows the Pragmatic Engineer "Designing a payment system" article (Alex Xu): a payment
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
  Domain/            PaymentEvent (root), PaymentOrder (child), Wallet, LedgerEntry, PaymentOrderLine,
                     Events/ (domain events), Exceptions/, Shared/ (MoneyAmount, CurrencyCode), Abstractions/
  Enums/             PaymentOrderStatus, EntryType (used by the domain)
  Data/              PaymentDbContext, Configurations/, Interceptors/, Migrations/
  Shared/
    Endpoints/       IEndpointMarker, CallerIdentity (X-Identity-UserId)
    Pipelines/       ITransactionalRequest, TransactionBehavior
    Results/         Error, ErrorType, ErrorResults (ToProblem), Result, Result<T>
    Messaging/       IIntegrationEventPublisher, OutboxIntegrationEventPublisher, OutboxFlushInterceptor
    Psp/             ProviderOutcome (shared by PaymentOrders and Webhooks slices)
  Features/<Aggregate>/<Feature>.cs
    Checkouts/       RequestPayment, GetCheckout, ExpireCheckout, CancelCheckout
    PaymentOrders/   StartCheckout, SubmitPaymentMethod, GetPaymentOrder, GetOrderLedger, Settle, Fail
    Webhooks/        HandleWebhook
    Wallets/         GetMyWallets
  Extensions/        ServiceCollectionExtension (infrastructure, endpoints, migrations), MessagingExtension
```

- **Features are grouped by aggregate, one file per feature, no per-feature folder.** A file holds a static
  class named for the intent (`Command`/`Query`, `Response`, `internal sealed Handler`) plus its trigger: a
  `public sealed ...Endpoints : IEndpointMarker`, a public Wolverine consumer, or a MediatR notification
  handler. Every file in an aggregate folder shares that folder's namespace.
- **Code shared by slices of two aggregates goes in `Shared/`**, never in one area for another to reach into
  (`ProviderOutcome` is the example).
- Endpoints are discovered by Scrutor (`AddFeatureEndpoints`), never registered in `Program.cs`.
- Tests mirror it: `Tests/Payments/PaymentIntegration/Features/<Aggregate>/<Feature>Tests.cs`; shared test
  helpers in `Fixtures/` (`CheckoutSeed`, `MessagingTest`, `PspTest`, `QueryTest`, `StubPsp`,
  `EndpointInvoker`); the real-host tests in `Mechanics/`.

## The domain

**`PaymentEvent` is the aggregate root** — one checkout per booking. It owns its `PaymentOrders` (one per
seller) and is the only way to change them: `StartExecuting`, `SucceedOrder`, `FailOrder`,
`MarkWalletUpdated`, `MarkLedgerUpdated`, `Expire`, `Cancel`. `PaymentOrder`'s mutators are `internal`.
`Wallet` and `LedgerEntry` are separate.

1. **Created whole.** `PaymentEvent.Create(checkoutId, bookingId, buyerId, lines)`: at least one line; every
   line valid (a bad line refuses the whole checkout); **one order per seller**; **the buyer can never be a
   seller**. `BookingId` is unique in the store — that index is what makes a redelivered request a no-op.
2. **State machine per order:** `NotStarted → Executing → Success | Failed`. `Expire`/`Cancel` fail every
   order still `NotStarted` or `Executing`; they never touch `Success` or `Failed`.
3. **At-least-once safe.** Repeating the outcome already reached is a no-op and raises nothing; the opposite
   outcome is refused (a settled payment cannot change its mind). `StartExecuting` replayed with the same
   token — including `null` again, for Braintree — is a no-op; a different token is refused.
4. **`IsPaymentDone` is derived** from the orders at the end of every root operation (all `Success`), no-ops
   included, so a stale flag heals.
5. **The root must be loaded whole.** Orders are an `AutoInclude` navigation, and a persisted `OrderCount`
   makes every root operation throw when fewer orders were loaded (e.g. `IgnoreAutoIncludes` plus a
   filtered `Include`) — otherwise "all orders succeeded" could be decided over a partial list.
6. **Money** (`Domain/Shared/MoneyAmount`): positive, at most 2 decimal places, at most
   `9999999999999999.99` — the `numeric(18,2)` column's range. The domain refuses what the column cannot
   hold exactly, because Postgres would round it and the PSP would charge a different amount than the ledger
   records. The configurations read `MoneyAmount.Precision`/`Scale`, so rule and column cannot drift.
7. **Ledger** entries only exist as a balanced pair from `LedgerEntry.RecordPayIn(order)` (debit the buyer,
   credit the seller, same amount and currency) for a successful order. Unique `(PaymentOrderId, Type)`.
8. **Wallet** — one per seller per currency (unique `(OwnerId, Currency)`); `Credit` refuses another
   currency and a balance past the storable maximum. `Credit` itself is not idempotent:
   `PaymentOrder.WalletUpdated`, saved in the same transaction, is.
9. **`AddDomainEvent` is protected** — only an aggregate raises its own events.

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
Timer     ExpireCheckout after 15 min: fail every unsettled order → BookingPaymentFailed
Bookings  BookingCancelled ─► CancelCheckout: fail unsettled orders; a paid one is left and logged for refund
```

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
- **Price and seller are placeholders set by Bookings** (`Bookings.Application/Services/PaymentPricing`:
  $50 per ticket in USD, seller derived from the event id). Payments treats the request's amount as
  authoritative, so it is correct the moment real pricing exists upstream.

## Messaging and the outbox

- Wolverine over RabbitMQ (`MessagingExtension.ConfigureMessaging`): Postgres message store, conventional
  routing limited to `TicketMaster.Common.IntegrationEvents`, all three durability policies. The expiry
  timer `CheckoutExpiryDue` is pinned with `[MessageIdentity("checkout-expiry-due")]` and routed to a
  durable local queue, so it never touches the broker and a rename cannot strand stored envelopes.
- **Consumes** `PaymentRequested`, `BookingCancelled`. **Publishes** `BookingPaid`, `BookingPaymentFailed`.
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
| `Tests/Payments/PaymentArchitecture` | ArchUnitNET: Domain depends only on itself, `Enums`, the BCL and MediatR; no feature area depends on another; `Shared` and `Data` never depend on `Features`; `PaymentProvider` never references `PaymentSystem`; handlers internal sealed; endpoints public sealed; every `Command` is `ITransactionalRequest` and no `Query` is; feature types live in `PaymentSystem.Features.<Aggregate>` |

- The fast fixture registers a recording `IIntegrationEventPublisher` (`IntegrationEventLog`, with scheduled
  messages kept separately) and `StubPsp`; the PSP is another process, so stubbing it is correct.
- **Seeding stores a state, not a settlement**: `SeedCheckoutAsync` clears the root's domain events, or the
  real `Settle` handler would write the wallet and ledger a test means to arrange itself.
- Needs Docker. `dotnet test Tests/Payments/PaymentIntegration/PaymentIntegration.csproj`.

## Known gaps

- **A cancel that arrives before `PaymentRequested`** is a no-op (no checkout yet); the checkout is then
  created and only the 15-minute expiry fails it. A buyer paying inside that window pays for a cancelled
  booking. Closing it needs a record of cancelled booking ids.
- **Refunds are not modelled.** A booking cancelled after its payment succeeded is logged ("needs a refund")
  on every redelivery; nothing issues the refund.
- **No reconciliation job** against PSP settlement files; a Braintree charge whose write fails twice stays
  `Executing` until `LookupAsync`-based reconciliation exists (it doesn't).
- **The order does not store its provider**, so changing `PaymentProviders:Default` mid-checkout sends
  submit to the wrong provider. The Braintree sale also runs while the request's DB transaction is open.
- Two checkouts settling at once for a brand-new seller and currency can collide on the wallet's unique
  index; the loser rolls back and the webhook redelivery settles it.
