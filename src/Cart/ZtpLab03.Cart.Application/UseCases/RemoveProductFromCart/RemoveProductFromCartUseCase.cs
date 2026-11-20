using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Domain.Entities;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.RemoveProductFromCart;

/// <summary>
/// CQRS Command for Use Case 3:
/// "Jako użytkownik, chcę usunąć produkt z koszyka, aby móc poprawić zawartość przed złożeniem zamówienia."
/// </summary>
public sealed record RemoveProductFromCartCommand(
    Guid CartId,
    string UserId,
    Guid ProductId,
    int Quantity) : ICommand<Result<CartDto>>;

/// <summary>
/// Handler for <see cref="RemoveProductFromCartCommand"/>.
/// Appends a <see cref="ProductRemovedFromCartEvent"/> to the cart's event stream and releases
/// the corresponding product stock reservation in the Product Service over HTTP.
/// </summary>
public sealed class RemoveProductFromCartCommandHandler : ICommandHandler<RemoveProductFromCartCommand, Result<CartDto>>
{
    private readonly ICartEventStore _eventStore;
    private readonly IProductServiceClient _productServiceClient;
    private readonly TimeProvider _timeProvider;

    public RemoveProductFromCartCommandHandler(
        ICartEventStore eventStore,
        IProductServiceClient productServiceClient,
        TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _productServiceClient = productServiceClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<CartDto>> HandleAsync(
        RemoveProductFromCartCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return Result<CartDto>.Failure("UserId is required.", ErrorType.Validation);
        }

        if (command.Quantity <= 0)
        {
            return Result<CartDto>.Failure("Quantity to remove must be greater than zero.", ErrorType.Validation);
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

                var currentQtyInCart = cart.GetProductQuantity(command.ProductId);
                if (currentQtyInCart <= 0)
                {
                    return (Result<bool>.Failure($"Product '{command.ProductId}' is not in the cart.", ErrorType.NotFound), null);
                }

                var actualQuantityToRelease = Math.Min(currentQtyInCart, command.Quantity);

                // Release the reserved product quantity via HTTP call to the Product Service
                await _productServiceClient.ReleaseProductReservationAsync(
                    command.ProductId,
                    command.CartId,
                    actualQuantityToRelease,
                    ct);

                var removedEvent = new ProductRemovedFromCartEvent(
                    command.CartId,
                    cart.UserId,
                    command.ProductId,
                    actualQuantityToRelease,
                    nowUtc);

                return (Result<bool>.Success(true), removedEvent);
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
