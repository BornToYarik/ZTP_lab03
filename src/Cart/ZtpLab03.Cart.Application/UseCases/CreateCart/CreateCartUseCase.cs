using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Domain.Aggregates;
using ZtpLab03.Cart.Domain.Entities;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.CreateCart;

/// <summary>
/// CQRS Command for Use Case 1:
/// "Jako użytkownik, chcę utworzyć nowy koszyk, aby rozpocząć proces zakupowy."
/// Every user is identified by a unique identifier (UserId).
/// </summary>
public sealed record CreateCartCommand(string UserId) : ICommand<Result<CartDto>>;

/// <summary>
/// Handler for <see cref="CreateCartCommand"/>.
/// If the user already has a previously open active cart, its product reservations are released
/// and it is closed before initializing the new cart stream, preventing orphaned product locks
/// on the same user account.
/// </summary>
public sealed class CreateCartCommandHandler : ICommandHandler<CreateCartCommand, Result<CartDto>>
{
    private readonly ICartEventStore _eventStore;
    private readonly IProductServiceClient? _productServiceClient;
    private readonly TimeProvider _timeProvider;

    public CreateCartCommandHandler(
        ICartEventStore eventStore,
        IProductServiceClient? productServiceClient = null,
        TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _productServiceClient = productServiceClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<CartDto>> HandleAsync(
        CreateCartCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return Result<CartDto>.Failure("UserId is required to identify the user.", ErrorType.Validation);
        }

        var normalizedUserId = command.UserId.Trim();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // Release and close any previous active cart belonging to the SAME user so products are not left locked
        var activeCartIds = await _eventStore.GetActiveCartIdsAsync(cancellationToken);
        foreach (var existingCartId in activeCartIds)
        {
            await _eventStore.ExecuteInCartLockAsync(
                existingCartId,
                async (existingCart, ct) =>
                {
                    if (existingCart is null ||
                        existingCart.Status != CartStatus.Active ||
                        !existingCart.IsOwnedBy(normalizedUserId))
                    {
                        return (false, null);
                    }

                    if (_productServiceClient is not null)
                    {
                        foreach (var item in existingCart.Items)
                        {
                            await _productServiceClient.ReleaseProductReservationAsync(
                                item.ProductId,
                                existingCart.Id,
                                item.Quantity,
                                ct);
                        }
                    }

                    return (true, (CartDomainEvent)new CartExpiredEvent(existingCart.Id, nowUtc));
                },
                cancellationToken);
        }

        var cartId = Guid.NewGuid();
        var createdEvent = new CartCreatedEvent(cartId, normalizedUserId, nowUtc);
        await _eventStore.AppendEventAsync(cartId, createdEvent, cancellationToken);

        var cart = ShoppingCart.Rehydrate([createdEvent]);
        return Result<CartDto>.Success(cart.ToDto());
    }
}
