using System.Text.Json.Serialization;

namespace ZtpLab03.Cart.Domain.Events;

/// <summary>
/// Base record for all domain events in the Shopping Cart event stream.
/// By using Event Sourcing (append-only stream of immutable events), concurrent product
/// additions and removals from multiple user devices are recorded sequentially without
/// overwriting each other (solving the Lost Update problem and ensuring Operational Consistency).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(CartCreatedEvent), nameof(CartCreatedEvent))]
[JsonDerivedType(typeof(ProductAddedToCartEvent), nameof(ProductAddedToCartEvent))]
[JsonDerivedType(typeof(ProductRemovedFromCartEvent), nameof(ProductRemovedFromCartEvent))]
[JsonDerivedType(typeof(CartCheckedOutEvent), nameof(CartCheckedOutEvent))]
[JsonDerivedType(typeof(CartExpiredEvent), nameof(CartExpiredEvent))]
public abstract record CartDomainEvent(
    Guid CartId,
    DateTime OccurredAtUtc);

/// <summary>
/// Emitted when a user initializes a new shopping cart.
/// </summary>
public sealed record CartCreatedEvent(
    Guid CartId,
    string UserId,
    DateTime OccurredAtUtc) : CartDomainEvent(CartId, OccurredAtUtc);

/// <summary>
/// Emitted when a user adds a product (and reserves its stock) to the shopping cart.
/// </summary>
public sealed record ProductAddedToCartEvent(
    Guid CartId,
    string UserId,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    DateTime OccurredAtUtc) : CartDomainEvent(CartId, OccurredAtUtc);

/// <summary>
/// Emitted when a user removes a quantity of a product from the shopping cart.
/// </summary>
public sealed record ProductRemovedFromCartEvent(
    Guid CartId,
    string UserId,
    Guid ProductId,
    int QuantityToRemove,
    DateTime OccurredAtUtc) : CartDomainEvent(CartId, OccurredAtUtc);

/// <summary>
/// Emitted when a user confirms the cart as ready for order fulfillment.
/// </summary>
public sealed record CartCheckedOutEvent(
    Guid CartId,
    string UserId,
    DateTime OccurredAtUtc) : CartDomainEvent(CartId, OccurredAtUtc);

/// <summary>
/// Emitted when a cart's 15-minute reservation lock expires due to user inactivity,
/// causing the cart to be closed/deleted and its reserved products released.
/// </summary>
public sealed record CartExpiredEvent(
    Guid CartId,
    DateTime OccurredAtUtc) : CartDomainEvent(CartId, OccurredAtUtc);
