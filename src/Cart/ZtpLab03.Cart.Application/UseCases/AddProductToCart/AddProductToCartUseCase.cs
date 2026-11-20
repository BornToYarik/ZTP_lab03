using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Domain.Entities;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.AddProductToCart;

/// <summary>
/// CQRS Command for Use Case 2:
/// "Jako użytkownik, chcę dodać produkt do koszyka, aby następnie móc przejść do finalizacji i zakupu produktów z koszyka."
/// Also satisfies +0.5 requirement:
/// "Jako użytkownik, chcę, aby system blokował dany produkt od momentu dodania go do mojego koszyka,
/// aby nikt inny nie mógł go zarezerwować równolegle."
/// </summary>
public sealed record AddProductToCartCommand(
    Guid CartId,
    string UserId,
    Guid ProductId,
    int Quantity) : ICommand<Result<CartDto>>;

/// <summary>
/// Handler for <see cref="AddProductToCartCommand"/>.
/// 
/// OPERATIONAL CONSISTENCY (Multi-Device Concurrency):
/// Uses <see cref="ICartEventStore.ExecuteInCartLockAsync{TResult}"/> to serialize concurrent
/// product additions/removals on the same cart across multiple devices while appending immutable
/// <see cref="ProductAddedToCartEvent"/> records to the cart's event stream.
/// </summary>
public sealed class AddProductToCartCommandHandler : ICommandHandler<AddProductToCartCommand, Result<CartDto>>
{
    private readonly ICartEventStore _eventStore;
    private readonly IProductServiceClient _productServiceClient;
    private readonly TimeProvider _timeProvider;

    public AddProductToCartCommandHandler(
        ICartEventStore eventStore,
        IProductServiceClient productServiceClient,
        TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _productServiceClient = productServiceClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<CartDto>> HandleAsync(
        AddProductToCartCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return Result<CartDto>.Failure("UserId is required.", ErrorType.Validation);
        }

        if (command.Quantity <= 0)
        {
            return Result<CartDto>.Failure("Quantity must be greater than zero.", ErrorType.Validation);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var executionResult = await _eventStore.ExecuteInCartLockAsync<Result<bool>>(
            command.CartId,
            async (cart, ct) =>
            {
                if (cart is null)
                {
                    return (Result<bool>.Failure($"Cart '{command.CartId}' was not found.", ErrorType.NotFound), null);
                }

                // Enforce user isolation: one user cannot modify another user's cart
                if (!cart.IsOwnedBy(command.UserId))
                {
                    return (Result<bool>.Failure("Access denied: cart belongs to a different user.", ErrorType.Forbidden), null);
                }

                if (cart.IsExpired(nowUtc))
                {
                    return (Result<bool>.Failure("Cart has expired after 15 minutes of inactivity.", ErrorType.Conflict), null);
                }

                if (cart.Status != CartStatus.Active)
                {
                    return (Result<bool>.Failure($"Cannot modify a cart in '{cart.Status}' state.", ErrorType.Conflict), null);
                }

                // Communicate with Product Service over HTTP to reserve (lock) the requested quantity
                var reservationResult = await _productServiceClient.TryReserveProductAsync(
                    command.ProductId,
                    command.CartId,
                    cart.UserId,
                    command.Quantity,
                    ct);

                if (!reservationResult.Succeeded || reservationResult.Product is null)
                {
                    return (
                        Result<bool>.Failure(
                            reservationResult.ErrorMessage ?? "Failed to reserve product stock.",
                            ErrorType.Conflict),
                        null);
                }

                var addedEvent = new ProductAddedToCartEvent(
                    command.CartId,
                    cart.UserId,
                    reservationResult.Product.Id,
                    reservationResult.Product.Name,
                    reservationResult.Product.Price,
                    command.Quantity,
                    nowUtc);

                return (Result<bool>.Success(true), addedEvent);
            },
            cancellationToken);

        if (!executionResult.IsSuccess)
        {
            return Result<CartDto>.Failure(executionResult.ErrorMessage!, executionResult.ErrorType);
        }

        var updatedCart = await _eventStore.GetCartAsync(command.CartId, cancellationToken);
        return Result<CartDto>.Success(updatedCart!.ToDto());
    }
}
