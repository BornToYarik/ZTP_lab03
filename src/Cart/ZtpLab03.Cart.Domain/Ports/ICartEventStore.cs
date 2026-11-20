using ZtpLab03.Cart.Domain.Aggregates;
using ZtpLab03.Cart.Domain.Events;

namespace ZtpLab03.Cart.Domain.Ports;

/// <summary>
/// Port (interface) for the Event Store managing Shopping Cart event streams.
/// 
/// OPERATIONAL CONSISTENCY & CONCURRENCY GUARANTEE:
/// Every cart has an isolated event stream keyed by CartId.
/// Concurrent operations from multiple devices for the same cart are serialized at the stream
/// level (or via optimistic concurrency control in a distributed Event Store), ensuring that
/// simultaneous Add/Remove product commands are appended sequentially and never lost or overwritten.
/// </summary>
public interface ICartEventStore
{
    /// <summary>
    /// Appends a new domain event to the specified cart's event stream atomically.
    /// </summary>
    Task AppendEventAsync(
        Guid cartId,
        CartDomainEvent domainEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an atomic read-validate-append operation against a single cart's event stream.
    /// This guarantees operational consistency when a user modifies the same cart from multiple
    /// devices simultaneously, while allowing operations on different users' carts to run in full parallel.
    /// </summary>
    Task<TResult> ExecuteInCartLockAsync<TResult>(
        Guid cartId,
        Func<ShoppingCart?, CancellationToken, Task<(TResult Result, CartDomainEvent? EventToAppend)>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads and rehydrates a ShoppingCart aggregate from its event stream, or returns null if not found.
    /// </summary>
    Task<ShoppingCart?> GetCartAsync(
        Guid cartId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw chronological history of domain events for a given cart (useful for auditing/debugging).
    /// </summary>
    Task<IReadOnlyList<CartDomainEvent>> GetEventStreamAsync(
        Guid cartId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all currently active cart IDs so the expiration background service can check for 15-minute timeouts.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveCartIdsAsync(
        CancellationToken cancellationToken = default);
}
