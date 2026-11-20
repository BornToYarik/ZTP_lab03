using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.GetCart;

/// <summary>
/// CQRS Query for Use Case 4:
/// "Jako użytkownik, chcę obejrzeć zawartość koszyka i łączną wartość, aby zweryfikować wydatek przed finalizacją."
/// Strictly read-only query following the CQRS pattern.
/// </summary>
public sealed record GetCartQuery(
    Guid CartId,
    string UserId) : IQuery<Result<CartDto>>;

/// <summary>
/// Handler for <see cref="GetCartQuery"/>.
/// Rehydrates the ShoppingCart from its event stream and projects it to <see cref="CartDto"/>.
/// </summary>
public sealed class GetCartQueryHandler : IQueryHandler<GetCartQuery, Result<CartDto>>
{
    private readonly ICartEventStore _eventStore;
    private readonly TimeProvider _timeProvider;

    public GetCartQueryHandler(ICartEventStore eventStore, TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<CartDto>> HandleAsync(
        GetCartQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.UserId))
        {
            return Result<CartDto>.Failure("UserId is required.", ErrorType.Validation);
        }

        var cart = await _eventStore.GetCartAsync(query.CartId, cancellationToken);
        if (cart is null)
        {
            return Result<CartDto>.Failure($"Cart '{query.CartId}' was not found.", ErrorType.NotFound);
        }

        if (!cart.IsOwnedBy(query.UserId))
        {
            return Result<CartDto>.Failure("Access denied: cart belongs to a different user.", ErrorType.Forbidden);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (cart.IsExpired(nowUtc))
        {
            return Result<CartDto>.Failure("Cart has expired after 15 minutes of inactivity.", ErrorType.Conflict);
        }

        return Result<CartDto>.Success(cart.ToDto());
    }
}
