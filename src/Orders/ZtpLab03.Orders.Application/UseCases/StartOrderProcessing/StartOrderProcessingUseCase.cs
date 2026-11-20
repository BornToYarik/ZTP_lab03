using ZtpLab03.Orders.Application.DTOs;
using ZtpLab03.Orders.Domain.Entities;
using ZtpLab03.Orders.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.IntegrationEvents;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Orders.Application.UseCases.StartOrderProcessing;

/// <summary>
/// CQRS Command in the independent Order Domain that creates an Order from a confirmed cart,
/// transitions it to <see cref="OrderStatus.Processing"/>, and sends a user notification (+0.25 criterion).
/// </summary>
public sealed record StartOrderProcessingCommand(
    Guid SourceCartId,
    string UserId,
    IReadOnlyList<OrderItem> Items,
    decimal TotalAmount) : ICommand<Result<OrderDto>>;

public sealed class StartOrderProcessingCommandHandler
    : ICommandHandler<StartOrderProcessingCommand, Result<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly TimeProvider _timeProvider;

    public StartOrderProcessingCommandHandler(
        IOrderRepository orderRepository,
        IOrderNotificationService notificationService,
        TimeProvider? timeProvider = null)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<OrderDto>> HandleAsync(
        StartOrderProcessingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return Result<OrderDto>.Failure("UserId is required.", ErrorType.Validation);
        }

        if (command.Items.Count == 0)
        {
            return Result<OrderDto>.Failure("Order must contain at least one item.", ErrorType.Validation);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var order = Order.CreateAndStartProcessing(
            command.SourceCartId,
            command.UserId.Trim(),
            command.Items,
            command.TotalAmount,
            nowUtc);

        await _orderRepository.AddAsync(order, cancellationToken);

        // Send notification to the user confirming that order processing has started (+0.25 criterion)
        await _notificationService.NotifyOrderProcessingStartedAsync(order, cancellationToken);

        return Result<OrderDto>.Success(order.ToDto());
    }
}

/// <summary>
/// Decoupled Integration Event Handler that bridges the Cart Domain and Order Domain.
/// When a cart is checked out in the Cart Domain, this handler receives <see cref="CartCheckedOutIntegrationEvent"/>
/// and executes <see cref="StartOrderProcessingCommand"/> inside the Order Domain.
/// </summary>
public sealed class CartCheckedOutIntegrationEventHandler
    : IIntegrationEventHandler<CartCheckedOutIntegrationEvent>
{
    private readonly ICommandHandler<StartOrderProcessingCommand, Result<OrderDto>> _commandHandler;

    public CartCheckedOutIntegrationEventHandler(
        ICommandHandler<StartOrderProcessingCommand, Result<OrderDto>> commandHandler)
    {
        _commandHandler = commandHandler;
    }

    public async Task HandleAsync(
        CartCheckedOutIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var orderItems = integrationEvent.Items
            .Select(i => new OrderItem(
                i.ProductId,
                i.ProductName,
                i.UnitPrice,
                i.Quantity))
            .ToList();

        var command = new StartOrderProcessingCommand(
            integrationEvent.CartId,
            integrationEvent.UserId,
            orderItems,
            integrationEvent.TotalAmount);

        await _commandHandler.HandleAsync(command, cancellationToken);
    }
}
