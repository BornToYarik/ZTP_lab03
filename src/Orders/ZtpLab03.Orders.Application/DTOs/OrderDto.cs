using ZtpLab03.Orders.Domain.Entities;

namespace ZtpLab03.Orders.Application.DTOs;

public sealed record OrderDto(
    Guid OrderId,
    Guid SourceCartId,
    string UserId,
    string Status,
    IReadOnlyList<OrderItemDto> Items,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? ProcessingStartedAtUtc);

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record OrderNotificationDto(
    Guid NotificationId,
    Guid OrderId,
    string UserId,
    string Message,
    DateTime SentAtUtc);

public static class OrderMappingExtensions
{
    public static OrderDto ToDto(this Order order)
    {
        var items = order.Items
            .Select(i => new OrderItemDto(
                i.ProductId,
                i.ProductName,
                i.UnitPrice,
                i.Quantity,
                i.LineTotal))
            .ToList();

        return new OrderDto(
            order.Id,
            order.SourceCartId,
            order.UserId,
            order.Status.ToString(),
            items,
            order.TotalAmount,
            order.CreatedAtUtc,
            order.ProcessingStartedAtUtc);
    }

    public static OrderNotificationDto ToDto(this OrderNotification notification) =>
        new(
            notification.NotificationId,
            notification.OrderId,
            notification.UserId,
            notification.Message,
            notification.SentAtUtc);
}
