using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZtpLab03.Orders.Domain.Entities;
using ZtpLab03.Orders.Domain.Ports;

namespace ZtpLab03.Orders.Infrastructure.Notifications;

/// <summary>
/// Dispatches and stores user notifications when order fulfillment starts (+0.25 criterion).
/// Logs the notification and keeps an in-memory inbox per user so it can be queried via the API.
/// </summary>
public sealed class InMemoryOrderNotificationService : IOrderNotificationService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<OrderNotification>> _userNotifications = new();
    private readonly ILogger<InMemoryOrderNotificationService>? _logger;
    private readonly TimeProvider _timeProvider;

    public InMemoryOrderNotificationService(
        ILogger<InMemoryOrderNotificationService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<OrderNotification> NotifyOrderProcessingStartedAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var message =
            $"Your order '{order.Id}' (from cart '{order.SourceCartId}', total: {order.TotalAmount:F2} PLN) " +
            $"has entered '{order.Status}' status and is now being fulfilled.";

        var notification = new OrderNotification(
            Guid.NewGuid(),
            order.Id,
            order.UserId,
            message,
            nowUtc);

        var inbox = _userNotifications.GetOrAdd(order.UserId, _ => new ConcurrentQueue<OrderNotification>());
        inbox.Enqueue(notification);

        _logger?.LogInformation(
            "[ORDER NOTIFICATION -> User '{UserId}']: {Message}",
            order.UserId,
            message);

        return Task.FromResult(notification);
    }

    public Task<IReadOnlyList<OrderNotification>> GetUserNotificationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!_userNotifications.TryGetValue(userId, out var inbox))
        {
            return Task.FromResult<IReadOnlyList<OrderNotification>>([]);
        }

        var list = inbox.OrderByDescending(n => n.SentAtUtc).ToList();
        return Task.FromResult<IReadOnlyList<OrderNotification>>(list);
    }
}
