namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// An account was registered. Published by Users.Api, consumed by PaymentSystem, which keeps its own copy of the user.
/// <para>
/// <c>UserId</c> is the id every other service already knows the user by — the JWT subject the gateway forwards as
/// <c>X-Identity-UserId</c>. Registration is the only change Users makes to these fields today, so there is no
/// update message yet.
/// </para>
/// </summary>
public record UserRegisteredIntegrationEvent(Guid UserId, string Email);
