---
name: payments-service
description: Use when working on PaymentSystem or PaymentProvider — the pay-in flow, refunds, the PaymentEvent (checkout) aggregate and its PaymentOrders, wallets, the double-entry ledger, saved payment methods, PSP checkout/webhooks/refunds, checkout expiry, reconciliation, booking claims, the read-side query context, or anything under PaymentSystem/ or Tests/Payments/.
---

# Payments Service

PaymentSystem takes a booking's payment from "requested" to "paid or failed", tells Bookings which, and gives
money back — always all of it: refunds are whole only. Money moves buyer → seller and, on a refund, back. No
pay-out to a seller's bank, no FX. The design follows the Pragmatic Engineer "Designing a payment system" article:
a checkout (`PaymentEvent`) holding a `PaymentOrder` (exactly one here: a booking is one event, paid to its one
organizer), a PSP-hosted page with the order id as the PSP's idempotency key, a wallet per seller, a double-entry
ledger.

**Scope:** `PaymentSystem/`, `PaymentProvider/` (the PSP anti-corruption library: Stripe, Braintree behind
`IPaymentGateway`), `Tests/Payments/`. **Load alongside:** `cqrs`, `efcore`, `messaging`, `testing`, `api-gateway`.

## Layout

Vertical slices on a DDD domain, one project. Keep this layout; add to it.

```
Domain/          PaymentEvent (root) + PaymentOrder (owned), RefundOrder, Wallet, LedgerEntry, PaymentMethod,
                 BookingClaim, User, SettlementRecord, SettlementDiscrepancy; Shared/Money;
                 Exceptions/PaymentDomainException
Enums/           PaymentOrderStatus, RefundOrderStatus, OrderUpdate, BookingClaimStatus, EntryType, RefType,
                 SettlementEntryKind, SettlementDiscrepancyKind, ...
Query/           read models: plain data, no logic, never reference Domain
Data/            PaymentDbContext (writes) · PaymentQueryDbContext (reads, NoTracking)
                 Configurations/Domain/ · Configurations/Query/ · Interceptors/ · Migrations/
Shared/          Endpoints (IEndpointMarker, CallerIdentity, DomainExceptionHandler) · Pipelines (TransactionBehavior)
                 Results (Result<T>, Error, ToProblem) · Messaging (local Wolverine messages)
Features/<Area>/<Feature>.cs   Checkouts, PaymentOrders, RefundOrders, PaymentMethods, Wallets, LedgerEntries, Webhooks,
                               Users, Settlements
Extensions/      AddInfrastructureServices, AddFeatureEndpoints, ApplyMigrationsAsync, ConfigureMessaging
```

## Two kinds of slice

| Trigger | Shape | Writes through |
|---|---|---|
| HTTP | static class: `Command`/`Query`, `Response`, `internal sealed Handler` (MediatR, returns `Result<T>`), and a `public sealed …Endpoint : IEndpointMarker` (found by Scrutor, never registered by hand) | reads: `PaymentQueryDbContext`; writes: `PaymentDbContext`, a writing `Command` is `ITransactionalRequest` |
| Message (from Bookings or local) | `public sealed …Handler` with `Handle(message, PaymentDbContext, ct)` returning `OutgoingMessages` | Wolverine's EF middleware: it saves the handler's changes **and** the returned messages in one transaction, sending them only after commit |

- Message handlers never call `SaveChangesAsync` themselves and never go through MediatR — two transaction
  managers on one context fight (see the `messaging` skill). ADR-005 still holds: Wolverine is not the
  in-process mediator for HTTP.
- An HTTP slice that must publish invokes a Wolverine handler inline (`IMessageBus.InvokeAsync`) so the publish
  goes through the outbox — `SubmitPaymentMethod` and the webhook both invoke `RecordPaymentOutcome`.
- **Never call a PSP inside a database transaction.** The two commands that call one — `StartCheckout`,
  `SubmitPaymentMethod` — are deliberately not `ITransactionalRequest`; `ProcessRefund` is `[NonTransactional]`
  and reads through the query context. The answer is recorded afterwards in its own transaction.
- Identity comes only from `X-Identity-UserId` (`CallerIdentity.TryGetUserId`) → 401 without it. A read is
  scoped by the caller in its query, so someone else's data is **404, never 403**. PSP tokens and references are
  never in a response.

## Domain rules

- **`PaymentEvent` is the only way to change its orders:** `AddPaymentOrder`, `UsePaymentMethod`, `StartOrder`,
  `SucceedOrder`, `FailOrder`, `Cancel`, `MarkOrderSettled`, `MarkOrderRefunded`, `ReinstateOrderPayment`. **Exactly one order per
  checkout:** `AddPaymentOrder` refuses a second. The collection stays so the checkout/order split of the design
  does; nothing should loop over several orders as if they could exist.
- **Order states:** `Init → Executing → Success | Failed`, `Success ⇄ Refunded` (back to `Success` only when a
  refund fails after it succeeded, via `ReinstateOrderPayment`). `Cancel()` (also used by the
  expiry) fails `Init`/`Executing` only and returns how many it failed.
- **PSP answers are reported, not thrown** (`OrderUpdate`): `Applied`, `AlreadyApplied` (a repeat), `Superseded`
  (the order settled the other way), `NotStarted`. PSPs repeat themselves and answer late; that is normal traffic.
- **`Version`** is bumped by every applied change to the checkout or its order and is the concurrency token, so a
  webhook racing the expiry or a cancellation conflicts and the retry decides over the winner's state. No-ops leave
  it alone.
- **`Money`** (`Domain/Shared`): 3-letter currency (uppercased), never negative, at most 2 decimals — what
  `numeric(18,2)` holds exactly. `+`/`-` refuse mixed currencies; `-` refuses going below zero.
- **Wallet** — one per user per currency; debit refuses more than the balance. **LedgerEntry** — written in
  balanced pairs, append-only: corrections are new reversing entries, never edits.
- **RefundOrder** has its own `RefundOrderStatus`; the domain never references `PaymentProvider`. States:
  `Pending` (recorded, PSP not yet answered) → `Processing` (PSP accepted, not finished; `ProcessingSince` set,
  nothing moved) → `Succeeded | Failed`; `Succeeded → Failed` only via `MarkAsFailedAfterSettlement`. An answered
  refund cannot be answered again (the domain throws; the handler checks first).

## Persistence

- **Mapping rule:** an entity in its own table is `OwnsMany`/`OwnsOne` (orders under the checkout); a value object
  in its owner's row is `ComplexProperty` (`Money`). EF Core 10 cannot put a complex property inside an owned
  type, so the order's `Money` is `OwnsOne` — keep the comment that says so.
- **EF Core 10 cannot index a complex property's column.** One wallet per user per currency is the unique
  `IX_Wallets_UserId_Currency`, **hand-written in `InitialCreate`**; keep it if that migration is ever regenerated.
  Other hand edits: the `PaymentMethods.UserId` text→uuid cast in `AddClaimsVersionAndReconciliation`.
- Uniqueness each handler relies on: one checkout per booking, one claim per booking (key), one order per
  checkout (`IX_PaymentOrders_CheckoutId`), one refund per order per request id, one default payment method per
  user (a filtered index), one settlement record per (provider, reference, kind) and one discrepancy per
  (provider, kind, reference) — re-reading a day adds nothing.
- Concurrency tokens: `PaymentEvent.Version`; Postgres `xmin` on `Wallet` and on `RefundOrder` (a webhook and the
  refund reconciler answering one refund at once conflict; the retry finds it answered).
- `IX_RefundOrders_PspRefundReference` (not unique) is how a refund event without our ids finds its refund.
- The read context maps the same tables with `ExcludeFromMigrations`; only `PaymentDbContext` owns the schema.
  `dotnet ef` needs `--context PaymentDbContext`. Migrations run at startup (`ApplyMigrationsAsync`).

## The flow

```
Bookings  PaymentRequested ─► RequestPayment: checkout + Requested claim; CheckoutExpiryDue scheduled +15 min
                              (booking already claimed Cancelled → nothing opened, nothing sent;
                               domain refuses → BookingPaymentFailed)
Buyer     POST orders/{id}/checkout [saved method?] ─► PSP session, order Executing (provider, ExecutingSince)
Buyer     POST orders/{id}/payment-method [token | saved method] ─► Braintree charges now; Stripe → webhook
PSP       POST webhooks/{provider} (signature-verified; ungated at the gateway)
            ─► RecordPaymentOutcome: success → wallet credit + ledger pair + BookingPaid
                                     cancel  → BookingPaymentFailed
Job       ReconcileOrdersJob every 1 min: Executing > 2 min → PSP lookup → RecordPaymentOutcome
Timer     CheckoutExpiryDue: unpaid → failed → BookingPaymentFailed
Bookings  BookingCancelled ─► CancelCheckout: unpaid fail (BookingPaymentFailed); paid → refund of the remainder;
                              no checkout yet → Cancelled claim, so the later request is refused
Bookings  RefundRequested(BookingId, RefundId) ─► RequestRefund: one refund of everything still paid on the order
Users     UserRegistered ─► RecordUser: keeps Payments' copy of the user (id + email); a redelivery adds nothing.
          Users registered before Payments subscribed arrive when an admin runs POST api/users/resync (Users.Api)
Local     ProcessRefund (no transaction) ─► PSP refund ─► RecordRefundOutcome (Final unless the PSP said Pending):
            succeeded → wallet debit + reversing pair, order Refunded, BookingRefunded(BookingId, RefundId)
            pending   → refund Processing, nothing moves, nothing sent
            refused   → refund Failed, Error log, BookingRefundFailed(BookingId, RefundId)
PSP       POST webhooks/stripe refund.updated / refund.failed / charge.refund.updated (final status only)
            ─► refund found by (order id, refund id) from Stripe's metadata, else by PSP reference ─► RecordRefundOutcome
Job       ReconcileRefundsJob every 1 min: Processing > 2 min → LookupRefundAsync → final → RecordRefundOutcome
            (the only way a Braintree refund is followed up: Braintree never pushes card refunds)
Any       a refusal for a Succeeded refund (failed after it was done) → wallet credited back, a second pair the
            other way (ledger stays append-only), refund Failed, order back to Success, Critical log,
            BookingRefundFailed (Notifications emails the buyer; Bookings already released the seats, so ignores it)
Job       ReconcileSettlementsJob every 6 h, per configured provider: GetSettlementAsync(last 8 full UTC days)
            ─► SettlementRecords (new lines only) ─► SettlementDiscrepancies (new findings only, each logged at Error)
            a RefundFailure line for a Succeeded/unanswered refund ─► RecordRefundOutcome(refused, Final) as well
```

- **Settlement reconciliation** is the last line of defence: it records, it does not correct. Discrepancy kinds:
  `UnknownCharge`, `ChargeForUnpaidOrder` (paid after expiry or cancellation — previously only an Error log),
  `ChargeAmountMismatch`, `RefundNotRecorded`, `RefundAmountMismatch`, `RefundFailedAfterSuccess`, and in the other
  direction `OrderNotSettled` / `RefundNotSettled`. The single correction is a refund the PSP took back: that is
  its final word, so it goes through `RecordRefundOutcome` like a webhook's answer.
  - Lines match by the PSP reference our rows store (`PspTokenRef`: Stripe's payment intent, Braintree's sale;
    `PspRefundReference`), falling back to the order / refund ids the PSP was tagged with.
  - Stripe writes a refund's line when the refund is created, still pending, and a separate `refund_failure` line
    if it fails: a refund line beside a `Processing` refund is normal, and beside a `Failed` one only when its
    failure line is recorded too.
  - **The window is grace (5 days) + 3 days of settlement lag**, and the "never settled" checks look only at
    orders started (`ExecutingSince`) / refunds created inside it and past the grace — so every day their line
    could fall on was read in the same run. Never shorten the window without shortening what is checked, or the
    first run after a deploy flags every older order. Refunds that took longer than 3 days to succeed are not
    checked.
  - A Braintree sale refunded before it settled is voided: its refund's reference is the sale's own, and neither
    is expected to settle.
  - A provider that is not configured, or whose read fails (Warning), is skipped for that run and its rows are
    not checked.
  - No HTTP endpoint: Payments has no admin role; the table is for operators.

- **Booking claims:** a request and a cancellation for one booking both insert `BookingClaims` (key = booking id).
  The second insert fails on the key; Wolverine's retry sees what won. Never lock instead.
- **Refund ids:** the request id is Bookings' `RefundId`, or the order id when Bookings named none (a booking
  cancelled after payment); `BookingRefunded` echoes the `RefundId`, or null for one nobody named. The request id
  is the PSP's idempotency key, so a retried refund replays.
- **Refunds are whole.** "Still paid" is the order amount minus earlier refunds that did not fail, so a refund
  asked for again after a failed attempt (under a new request id) still gives everything back.
- A refund the PSP refuses, or a PSP success for an order already failed, is logged at Error ("needs a manual
  refund") — never retried, never reported to Bookings as refunded.
- **`RecordRefundOutcome.Final`** is the last field and defaults to `true`, so envelopes stored before it existed
  still read as the final answers they were. Keep it last and defaulted.
- **Only `Processing` refunds are polled.** `Pending` is still owned by the durable `ProcessRefund` message (its
  Transient retries); polling it would race that.
- **`StartCheckout` retries a lost race itself** (up to 3 attempts, clearing the tracker and reloading): lost to
  the expiry, a cancellation or a PSP answer → `payment_order_settled`; lost to the buyer's own second start → the
  same session comes back (the order id is the PSP's idempotency key). Only a writer that wins every attempt
  yields 409 `payment_order_changed`.

## Messaging

`ConfigureMessaging`: RabbitMQ with conventional routing limited to `TicketMaster.Common.IntegrationEvents`;
Postgres message store; durable inbox, outbox and local queues (`checkout-expiry`, `refunds`). Local messages pin
`[MessageIdentity]` so a rename cannot strand stored envelopes. Retries: `DbUpdateException` (unique-key and
`Version` losers) 100 ms / 1 s / 5 s; a `Transient` PSP error 10 s / 1 min / 5 min / 30 min. Consumes
`PaymentRequested`, `BookingCancelled`, `RefundRequested` (Bookings) and `UserRegistered` (Users); publishes
`BookingPaid`, `BookingPaymentFailed`, `BookingRefunded`, `BookingRefundFailed`. User ids still come from `X-Identity-UserId`; the
synced `User` is a copy for when Payments needs more than the id (an email), not an authorization source.
Notifications consumes several of the same contracts on its own `notifications.<Message>` queues; Payments keeps
the default-named ones — never move either onto the other's queue (see the `messaging` skill).

## HTTP

```
GET    api/payments/checkouts/{bookingId:long}          buyer
GET    api/payments/orders/{id}                         buyer or seller   (…/refunds, …/ledger: same)
POST   api/payments/orders/{id}/checkout                buyer — { paymentMethodId? }
POST   api/payments/orders/{id}/payment-method          buyer — { paymentMethod? }
POST   api/payments/webhooks/{provider}                 PSP — ungated, signature-verified
GET/POST/PUT/DELETE  api/payments/payment-methods[/me|/{id}|/{id}/default]   caller's own; one default
GET/POST/DELETE      api/wallets[/me|/{id}]             caller's own; no update — balances move only by settlement/refund
```

Ledger entries are read-only over HTTP. Domain rule violations → 400 via `DomainExceptionHandler`.

## Tests

| Project | Covers |
|---|---|
| `PaymentDomain` | every entity rule and transition, `Version` moves, `Money` limits |
| `PaymentIntegration` | every slice and message handler on real Postgres via the production `AddInfrastructureServices`; `StubPsp` replaces the PSP and a recording `IMessageBus` replaces Wolverine's; `HandleAsync<THandler>` runs a message handler and saves like the middleware; `Persistence/` for indexes, precision, concurrency |
| `…/Mechanics` | the real host on Postgres + RabbitMQ: broker messages, durable expiry, Stripe-signed webhooks, replies at a stand-in Bookings, outbox rollback (a trigger fails the write; a sequence counts attempts) |
| `PaymentArchitecture` | domain purity, read models independent of Domain, handler/endpoint visibility, transactional-command rule |
| `PaymentAdapters` | the PSP adapters in `PaymentProvider` |

Write tests from the requirement, not the code; a schema/model mismatch fails fixture start (`MigrateAsync`).

## Known gaps

- A failed refund is left for a person to return the money another way: `BookingRefundFailed` tells Bookings
  (which releases the seats of a booking still waiting for it) and Notifications (which emails the buyer), but
  nothing retries it and there is no record of the manual refund.
- A Braintree refund declined at settlement after it counted as succeeded is found only by settlement
  reconciliation (up to 6 hours, plus Braintree's batch), not by the refund poller.
- Settlement discrepancies are recorded and logged for a person; nothing resolves or closes them, and there is no
  endpoint to read them.
- Two Payments instances reconciling settlements at once can collide on the unique indexes; the losing run fails
  and the next one picks up.
- Opening a Stripe checkout, the Braintree charge end to end and a real refund are tested against `StubPsp` only.
