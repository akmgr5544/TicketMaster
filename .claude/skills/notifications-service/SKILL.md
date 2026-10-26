---
name: notifications-service
description: Use when working on the Notifications service — booking outcome emails to buyers, the BookingBuyer/Recipient/Notification tables, the SendEmail local message, SMTP/MailKit sending, Mailpit, the notifications.<Message> queues, adding a new email kind, or anything under Notifications/ or Tests/Notifications/.
---

# Notifications Service

Emails a booking's buyer what happened to it: paid, payment failed, cancelled, refunded. It only reacts to
messages — **no public API, no gateway route, publishes nothing**; `GET /health` is the one endpoint. Own
database `notifications_db` (Postgres, EF Core, migrations at startup via `ApplyMigrationsAsync`).

**Scope:** `Notifications/`, `Tests/Notifications/`. **Load alongside:** `messaging`, `efcore`, `testing`.

## Layout

Vertical slices in one project, one file per feature (like PaymentSystem; no MediatR — every handler is a plain
Wolverine handler):

```
Domain/          BookingBuyer, Recipient, Notification (+ Subject/Body per kind), NotificationKind
Data/            NotificationsDbContext (all mapping inline in OnModelCreating), Migrations/
Email/           IEmailSender + EmailDeliveryException, SmtpEmailSender (MailKit) + EmailOptions
Extensions/      AddNotificationsServices + ApplyMigrationsAsync; MessagingExtension.ConfigureMessaging
Features/Bookings/RecordBuyer.cs             PaymentRequested → BookingBuyer (booking id → buyer id)
Features/Users/RecordRecipient.cs            UserRegistered   → Recipient (user id → email)
Features/Notifications/NotifyBuyer.cs        BookingPaid / BookingPaymentFailed / BookingCancelled / BookingRefunded
                                             → Notification row + SendEmail
Features/Notifications/SendEmail.cs          SendEmail → SMTP, then SentAt
```

Outcome messages carry only the booking id, so the service keeps two lookups built from other messages:
`PaymentRequested` is the one message naming the buyer, `UserRegistered` the one carrying the address.

## Flow

```
Outcome message ─► BookingOutcomeHandler (transaction): dedupe → find buyer → find address
                   → insert Notification, return SendEmail  (both commit together; the outbox sends after)
SendEmail (local durable queue "emails") ─► SendEmailHandler [NonTransactional]: load, skip if SentAt set,
                   SMTP send, MarkSent, SaveChanges
```

- **One email per outcome.** `Notifications` has a unique index on `(BookingId, Kind, RefundId)`; `RefundId` is
  `Guid.Empty` for anything but a refund (keeps the key null-free), so each distinct refund gets its own email.
  A redelivered outcome finds its row and returns nothing; two copies racing to insert lose on the index
  (`DbUpdateException`, retried 100 ms / 1 s / 5 s) and the retry finds the winner's row.
- **A voided booking gets one email**, whichever of `BookingCancelled` / `BookingPaymentFailed` lands first —
  both arrive for the same booking (Bookings announces the cancel, Payments fails the payment). The handler's
  read-first check covers the usual order; two handled at the same instant are stopped by the partial unique index
  `IX_Notifications_BookingId_Voided` (one `PaymentFailed`/`BookingCancelled` row per booking), and the loser's
  retry finds the winner's row.
- **Unknown booking or no address → logged (Warning) and skipped, not retried**: asking again will not make the
  row appear. A user registered before Notifications subscribed has no `Recipient` until an admin runs
  **`POST api/users/resync`** (Users.Api) — run it once after deploying; it re-publishes `UserRegistered` for
  every user, and `RecordRecipient` skips one it already has. Outcomes skipped before the resync are not
  re-sent.
- **`SendEmail` is `[NonTransactional]`** so the mail server is never called with a database transaction open
  (rule shared with Payments and its PSPs): a slow SMTP call would hold the transaction and its connection, and
  a rollback cannot unsend an email. The handler saves `SentAt` itself in one `SaveChanges` after the send. An unreachable server throws `EmailDeliveryException`, scheduled
  for retry at 10 s / 1 min / 5 min / 30 min.
- **At least once, never lost**: a crash after the SMTP send and before `SentAt` is saved sends the email again
  on redelivery. `SentAt` stops every other repeat.
- `SendEmail` pins `[MessageIdentity("send-email")]` so a rename cannot strand stored envelopes.

## Adding an email kind

1. A value in `Domain/NotificationKind.cs` (stored as a string, max 30 chars — no migration for a new value).
2. Its `Subject` and `Body` cases in `Domain/Notification.cs` (the `_` arm is the refund text — add the case
   above it).
3. A `Handle` overload in `Features/Notifications/NotifyBuyer.cs` calling `NotifyAsync` with the kind.
4. If it comes from a new contract: add the type to `MessagingExtension.Consumed`, which declares its queue and
   binding. If the kind needs data the service has not got, record it from a message as `RecordBuyer` does.
5. Tests under `Tests/Notifications/NotificationsIntegration/Features/Notifications/`.

## Messaging

`Extensions/MessagingExtension.cs`. **No conventional routing.** For each type in `Consumed`
(`PaymentRequested`, `UserRegistered`, `BookingPaid`, `BookingPaymentFailed`, `BookingCancelled`,
`BookingRefunded`) it binds the type's fanout exchange (named by the type's full name, as publishers send) to
its own queue **`notifications.<TypeName>`** and listens there. The default queue names belong to the services
already consuming those contracts (Payments, Bookings); sharing a queue makes two services compete for its
messages instead of each getting a copy — Payments would silently lose about half. `SendEmail` goes to the local
queue `emails`. Postgres message store, EF Core transactions with `AutoApplyTransactions`, durable inbox,
outbox and local queues. `ApplicationAssembly` is pinned because under a test host the entry assembly is the
test runner.

## Configuration and running

`ConnectionStrings:NotificationsDatabase`, `ConnectionStrings:RabbitMQ` (a connection *name*), `Email:Host`,
`Email:Port` (default 25), `Email:From` — `Host` and `From` are validated on start. Locally and in compose the
mail server is **Mailpit**: SMTP on 1025, the inbox UI at **http://localhost:8025**. Compose publishes the
service on `5300` for `/health` only.

```bash
dotnet run --project Notifications/Notifications.csproj
dotnet test Tests/Notifications/NotificationsIntegration/NotificationsIntegration.csproj   # needs Docker
```

## Tests

`NotificationsIntegration`, two collections (details in the `testing` skill): `NotificationsFixture` — the
production registration on Postgres with `RecordingEmailSender` in place of SMTP, handlers run through
`HandleAsync<THandler>`; `NotificationsHostFixture` — the real host on Postgres, RabbitMQ and Mailpit beside a
Payments stand-in on the default queues, proving a shared contract reaches both and an email reaches Mailpit.

## Known gaps

- At-least-once email: a crash between the SMTP send and saving `SentAt` sends it twice.
- An outcome that arrived while its buyer had no `Recipient` (before the post-deploy `POST api/users/resync`)
  is skipped for good, not replayed.
- Plain-text emails only; the text lives in `Notification.Subject`/`Body`, no templates or localisation.
- No architecture test project.
