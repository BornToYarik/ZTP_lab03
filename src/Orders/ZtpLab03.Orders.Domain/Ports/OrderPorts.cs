using ZtpLab03.Orders.Domain.Entities;

namespace ZtpLab03.Orders.Domain.Ports;

/// <summary>
/// Repository port for persisting and querying orders in the independent Order Domain.
/// </summary>
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Port for dispatching and retrieving user notifications when order fulfillment starts.
/// Requirement (+0.25): "Jako użytkownik chcę otrzymać powiadomienie o rozpoczęciu realizacji zamówienia,
/// aby mieć pewność, że moje zamówienie jest przetwarzane"
/// </summary>
public interface IOrderNotificationService
{
    Task<OrderNotification> NotifyOrderProcessingStartedAsync(
        Order order,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetUserNotificationsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
