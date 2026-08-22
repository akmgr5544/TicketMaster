namespace Bookings.Domain.Abstractions;

/// <summary>
/// Marks a request whose handler writes to the database, and which therefore needs a transaction
/// around it. Requests without this marker get no transaction — a reservation, for instance, only
/// touches Redis, and opening a Postgres transaction for it buys nothing and rolls back nothing.
/// <para>
/// It lives in the domain project because both the commands (application) and the pipeline behavior
/// that reads it (infrastructure) need to see it, and the application layer must not reference
/// infrastructure.
/// </para>
/// </summary>
public interface ITransactionalRequest;
