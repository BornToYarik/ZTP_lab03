using ZtpLab03.Cart.Domain.Entities;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Cart.Application.UseCases.ExpireInactiveCarts;

/// <summary>
/// CQRS Command for the +0.5 requirement:
/// "Jako użytkownik, chcę, aby blokada produktów w koszyku wygasała po 15 minutach,
/// a koszyk był usuwany, aby zwolnić zarezerwowane przedmioty, gdy przestanę kontynuować zakupy."
/// </summary>
public sealed record ExpireInactiveCartsCommand : ICommand<int>;

/// <summary>
/// Scans active carts, expires any cart that has been inactive for >= 15 minutes,
/// appends <see cref="CartExpiredEvent"/>, and releases all locked product quantities over HTTP.
/// </summary>
public sealed class ExpireInactiveCartsCommandHandler : ICommandHandler<ExpireInactiveCartsCommand, int>
{
    private readonly ICartEventStore _eventStore;
    private readonly IProductServiceClient _productServiceClient;
    private readonly TimeProvider _timeProvider;

    public ExpireInactiveCartsCommandHandler(
        ICartEventStore eventStore,
        IProductServiceClient productServiceClient,
        TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _productServiceClient = productServiceClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> HandleAsync(
        ExpireInactiveCartsCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var activeCartIds = await _eventStore.GetActiveCartIdsAsync(cancellationToken);
        var expiredCount = 0;

        foreach (var cartId in activeCartIds)
        {
            var expired = await _eventStore.ExecuteInCartLockAsync(
                cartId,
                async (cart, ct) =>
                {
                    if (cart is null || cart.Status != CartStatus.Active || !cart.IsExpired(nowUtc))
                    {
                        return (false, null);
                    }

                    // Release all locked products for this expired cart via HTTP Product Service
                    foreach (var item in cart.Items)
                    {
                        await _productServiceClient.ReleaseProductReservationAsync(
                            item.ProductId,
                            cart.Id,
                            item.Quantity,
                            ct);
                    }

                    var expiredEvent = new CartExpiredEvent(cart.Id, nowUtc);
                    return (true, expiredEvent);
                },
                cancellationToken);

            if (expired)
            {
                expiredCount++;
            }
        }

        return expiredCount;
    }
}

/// <summary>
/// CQRS Command used in the interactive Web UI demonstration to simulate the passage of 15 minutes
/// for a specific cart without waiting 15 real-time minutes during the project defense.
/// </summary>
public sealed record SimulateCartTimeoutCommand(Guid CartId) : ICommand<Result<bool>>;

public sealed class SimulateCartTimeoutCommandHandler : ICommandHandler<SimulateCartTimeoutCommand, Result<bool>>
{
    private readonly ICartEventStore _eventStore;
    private readonly IProductServiceClient _productServiceClient;
    private readonly TimeProvider _timeProvider;

    public SimulateCartTimeoutCommandHandler(
        ICartEventStore eventStore,
        IProductServiceClient productServiceClient,
        TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
        _productServiceClient = productServiceClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<bool>> HandleAsync(
        SimulateCartTimeoutCommand command,
        CancellationToken cancellationToken = default)
    {
        var simulatedFutureUtc = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(16);

        return await _eventStore.ExecuteInCartLockAsync(
            command.CartId,
            async (cart, ct) =>
            {
                if (cart is null)
                {
                    return (Result<bool>.Failure($"Cart '{command.CartId}' not found.", ErrorType.NotFound), null);
                }

                if (cart.Status != CartStatus.Active)
                {
                    return (Result<bool>.Failure($"Cart is already in '{cart.Status}' status.", ErrorType.Conflict), null);
                }

                foreach (var item in cart.Items)
                {
                    await _productServiceClient.ReleaseProductReservationAsync(
                        item.ProductId,
                        cart.Id,
                        item.Quantity,
                        ct);
                }

                var expiredEvent = new CartExpiredEvent(cart.Id, simulatedFutureUtc);
                return (Result<bool>.Success(true), expiredEvent);
            },
            cancellationToken);
    }
}
