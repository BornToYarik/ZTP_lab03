using ZtpLab03.SharedKernel.CQRS;

namespace ZtpLab03.SharedKernel.IntegrationEvents;

/// <summary>
/// Integration event published when a user confirms (checks out) their shopping cart.
/// It transfers the minimal snapshot required for the independent Order Domain
/// to initiate order processing and notify the user, without coupling the Cart Domain
/// to the Order Domain's internal entities or database.
/// </summary>
public sealed record CartCheckedOutIntegrationEvent(
    Guid EventId,
    Guid CartId,
    string UserId,
    IReadOnlyList<CheckedOutCartItemSnapshot> Items,
    decimal TotalAmount,
    DateTime OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Immutable item snapshot included in the CartCheckedOutIntegrationEvent.
/// </summary>
public sealed record CheckedOutCartItemSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);
