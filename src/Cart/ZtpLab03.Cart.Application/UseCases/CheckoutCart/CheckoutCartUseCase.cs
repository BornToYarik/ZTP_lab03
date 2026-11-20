using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Domain.Entities;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.IntegrationEvents;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.CheckoutCart;

/// <summary>
/// CQRS Command for Use Case 5:
/// "Jako użytkownik, chcę zatwierdzić koszyk jako gotowy do zamówienia, generując zlecenie,
/// które umożliwia przejście do procesu realizacji zakupu."
/// </summary>
public sealed record CheckoutCartCommand(
    Guid CartId,
    string UserId) : ICommand<Result<CartDto>>;

/// <summary>
/// Handler for <see cref="CheckoutCartCommand"/>.
/// Confirms product reservations over HTTP, appends <see cref="CartCheckedOutEvent"/>,
/// and publishes <see cref="CartCheckedOutIntegrationEvent"/> via the Mediator so the independent
/// Order Domain can initiate order processing and notify the user (+0.25 criterion).
/// </summary>
public sealed class CheckoutCartCommandHandler : ICommandHandler<CheckoutCartCommand, Result<CartDto>>
{
    private readonly ICartEventStore _eventStore;
    private readonly IProductServiceClient _productServiceClient;
    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;

    public CheckoutCartCommandHandler(
        ICartEventStore eventStore,
        IProductServiceClient productServiceClient,
        IMediator mediator,
        TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _productServiceClient = productServiceClient;
        _mediator = mediator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<CartDto>> HandleAsync(
        CheckoutCartCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return Result<CartDto>.Failure("UserId is required.", ErrorType.Validation);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<CheckedOutCartItemSnapshot> itemSnapshots = [];
        decimal totalAmount = 0m;
        string cartOwnerId = string.Empty;

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
                    return (Result<bool>.Failure($"Cannot checkout a cart in '{cart.Status}' state.", ErrorType.Conflict), null);
                }

                if (cart.Items.Count == 0)
                {
                    return (Result<bool>.Failure("Cannot checkout an empty cart.", ErrorType.Validation), null);
                }

                // Confirm product reservations in the Product Service over HTTP
                var confirmed = await _productServiceClient.ConfirmCartReservationsAsync(command.CartId, ct);
                if (!confirmed)
                {
                    return (Result<bool>.Failure("Failed to confirm product reservations with Product Service.", ErrorType.Conflict), null);
                }

                cartOwnerId = cart.UserId;
                totalAmount = cart.TotalPrice;
                itemSnapshots = cart.Items
                    .Select(i => new CheckedOutCartItemSnapshot(
                        i.ProductId,
                        i.ProductName,
                        i.UnitPrice,
                        i.Quantity))
                    .ToList();

                var checkedOutEvent = new CartCheckedOutEvent(command.CartId, cart.UserId, nowUtc);
                return (Result<bool>.Success(true), checkedOutEvent);
            },
            cancellationToken);

        if (!executionResult.IsSuccess)
        {
            return Result<CartDto>.Failure(executionResult.ErrorMessage!, executionResult.ErrorType);
        }

        // Publish decoupled integration event to trigger order processing in the Order Domain
        var integrationEvent = new CartCheckedOutIntegrationEvent(
            Guid.NewGuid(),
            command.CartId,
            cartOwnerId,
            itemSnapshots,
            totalAmount,
            nowUtc);

        await _mediator.PublishEventAsync(integrationEvent, cancellationToken);

        var updatedCart = await _eventStore.GetCartAsync(command.CartId, cancellationToken);
        return Result<CartDto>.Success(updatedCart!.ToDto());
    }
}
