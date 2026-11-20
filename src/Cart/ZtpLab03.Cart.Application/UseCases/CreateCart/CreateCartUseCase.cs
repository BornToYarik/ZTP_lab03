using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Domain.Aggregates;
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
/// Appends a <see cref="CartCreatedEvent"/> to a new isolated event stream.
/// </summary>
public sealed class CreateCartCommandHandler : ICommandHandler<CreateCartCommand, Result<CartDto>>
{
    private readonly ICartEventStore _eventStore;
    private readonly TimeProvider _timeProvider;

    public CreateCartCommandHandler(ICartEventStore eventStore, TimeProvider? timeProvider = null)
    {
        _eventStore = eventStore;
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
        var cartId = Guid.NewGuid();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var createdEvent = new CartCreatedEvent(cartId, normalizedUserId, nowUtc);
        await _eventStore.AppendEventAsync(cartId, createdEvent, cancellationToken);

        var cart = ShoppingCart.Rehydrate([createdEvent]);
        return Result<CartDto>.Success(cart.ToDto());
    }
}
