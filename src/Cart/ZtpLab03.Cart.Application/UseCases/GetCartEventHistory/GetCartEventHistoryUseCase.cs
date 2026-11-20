using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.GetCartEventHistory;

/// <summary>
/// CQRS Query that returns the raw Event Sourcing stream of domain events for a cart.
/// Useful for demonstrating how concurrent multi-device operations are stored as sequential immutable events.
/// </summary>
public sealed record GetCartEventHistoryQuery(
    Guid CartId,
    string UserId) : IQuery<Result<IReadOnlyList<CartDomainEvent>>>;

public sealed class GetCartEventHistoryQueryHandler
    : IQueryHandler<GetCartEventHistoryQuery, Result<IReadOnlyList<CartDomainEvent>>>
{
    private readonly ICartEventStore _eventStore;

    public GetCartEventHistoryQueryHandler(ICartEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<Result<IReadOnlyList<CartDomainEvent>>> HandleAsync(
        GetCartEventHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.UserId))
        {
            return Result<IReadOnlyList<CartDomainEvent>>.Failure("UserId is required.", ErrorType.Validation);
        }

        var cart = await _eventStore.GetCartAsync(query.CartId, cancellationToken);
        if (cart is null)
        {
            return Result<IReadOnlyList<CartDomainEvent>>.Failure($"Cart '{query.CartId}' was not found.", ErrorType.NotFound);
        }

        if (!cart.IsOwnedBy(query.UserId))
        {
            return Result<IReadOnlyList<CartDomainEvent>>.Failure("Access denied: cart belongs to a different user.", ErrorType.Forbidden);
        }

        var events = await _eventStore.GetEventStreamAsync(query.CartId, cancellationToken);
        return Result<IReadOnlyList<CartDomainEvent>>.Success(events);
    }
}
