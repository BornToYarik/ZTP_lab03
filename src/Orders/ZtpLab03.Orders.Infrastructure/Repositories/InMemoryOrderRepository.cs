using System.Collections.Concurrent;
using ZtpLab03.Orders.Domain.Entities;
using ZtpLab03.Orders.Domain.Ports;

namespace ZtpLab03.Orders.Infrastructure.Repositories;

/// <summary>
/// Thread-safe in-memory repository for Orders in the independent Order Domain.
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var userOrders = _orders.Values
            .Where(o => string.Equals(o.UserId, userId, StringComparison.Ordinal))
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<Order>>(userOrders);
    }
}
