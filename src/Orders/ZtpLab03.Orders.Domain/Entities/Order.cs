namespace ZtpLab03.Orders.Domain.Entities;

/// <summary>
/// Aggregate Root in the independent Order Domain.
/// Completely separated from the Shopping Cart domain in compliance with modularity principles.
/// </summary>
public sealed class Order
{
    private readonly List<OrderItem> _items;

    public Guid Id { get; }
    public Guid SourceCartId { get; }
    public string UserId { get; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? ProcessingStartedAtUtc { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private Order(
        Guid id,
        Guid sourceCartId,
        string userId,
        List<OrderItem> items,
        decimal totalAmount,
        DateTime createdAtUtc)
    {
        Id = id;
        SourceCartId = sourceCartId;
        UserId = userId;
        _items = items;
        TotalAmount = totalAmount;
        CreatedAtUtc = createdAtUtc;
        Status = OrderStatus.Created;
    }

    public static Order CreateAndStartProcessing(
        Guid sourceCartId,
        string userId,
        IEnumerable<OrderItem> items,
        decimal totalAmount,
        DateTime nowUtc)
    {
        var order = new Order(
            Guid.NewGuid(),
            sourceCartId,
            userId,
            items.ToList(),
            totalAmount,
            nowUtc);

        order.StartProcessing(nowUtc);
        return order;
    }

    public static Order Reconstruct(
        Guid id,
        Guid sourceCartId,
        string userId,
        OrderStatus status,
        IEnumerable<OrderItem> items,
        decimal totalAmount,
        DateTime createdAtUtc,
        DateTime? processingStartedAtUtc)
    {
        var order = new Order(
            id,
            sourceCartId,
            userId,
            items.ToList(),
            totalAmount,
            createdAtUtc)
        {
            Status = status,
            ProcessingStartedAtUtc = processingStartedAtUtc
        };
        return order;
    }

    public void StartProcessing(DateTime nowUtc)
    {
        Status = OrderStatus.Processing;
        ProcessingStartedAtUtc = nowUtc;
    }
}

public sealed record OrderItem(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public enum OrderStatus
{
    Created = 0,
    Processing = 1,
    Completed = 2
}

/// <summary>
/// Represents a notification sent to a user when order processing begins (+0.25 criterion).
/// </summary>
public sealed record OrderNotification(
    Guid NotificationId,
    Guid OrderId,
    string UserId,
    string Message,
    DateTime SentAtUtc);
