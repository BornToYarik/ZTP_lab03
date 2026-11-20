using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using ZtpLab03.Orders.Domain.Entities;
using ZtpLab03.Orders.Domain.Ports;
using ZtpLab03.Orders.Infrastructure.Notifications;

namespace ZtpLab03.Orders.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IOrderRepository"/> and <see cref="IOrderNotificationService"/>
/// for the independent Order Domain.
/// Uses its own isolated tables (`orders` and `order_notifications`) completely decoupled from the Cart Domain's Event Store.
/// </summary>
public sealed class PostgresOrderStore : IOrderRepository, IOrderNotificationService
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresOrderStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly InMemoryOrderRepository _fallbackRepo = new();
    private readonly InMemoryOrderNotificationService _fallbackNotifications;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _useFallback;

    public PostgresOrderStore(
        string connectionString,
        ILogger<PostgresOrderStore> logger,
        TimeProvider? timeProvider = null)
    {
        _connectionString = connectionString;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fallbackNotifications = new InMemoryOrderNotificationService(null, _timeProvider);
    }

    private async Task<bool> EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return !_useFallback;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return !_useFallback;
            }

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                const string sql = """
                    CREATE TABLE IF NOT EXISTS orders (
                        order_id UUID PRIMARY KEY,
                        source_cart_id UUID NOT NULL,
                        user_id VARCHAR(120) NOT NULL,
                        status VARCHAR(40) NOT NULL,
                        items_json JSONB NOT NULL,
                        total_amount NUMERIC(18,2) NOT NULL,
                        created_at_utc TIMESTAMPTZ NOT NULL,
                        processing_started_at_utc TIMESTAMPTZ NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_orders_user_id ON orders(user_id);

                    CREATE TABLE IF NOT EXISTS order_notifications (
                        notification_id UUID PRIMARY KEY,
                        order_id UUID NOT NULL,
                        user_id VARCHAR(120) NOT NULL,
                        message TEXT NOT NULL,
                        sent_at_utc TIMESTAMPTZ NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_order_notifications_user_id ON order_notifications(user_id);
                    """;

                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                _initialized = true;
                _useFallback = false;
                _logger.LogInformation("PostgreSQL Order Domain schema initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                _initialized = true;
                _useFallback = true;
                _logger.LogWarning(
                    ex,
                    "PostgreSQL is unreachable for Order Domain. Falling back to in-memory Order persistence.");
                return false;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (!await EnsureInitializedAsync(cancellationToken))
        {
            await _fallbackRepo.AddAsync(order, cancellationToken);
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO orders (
                order_id, source_cart_id, user_id, status, items_json, total_amount, created_at_utc, processing_started_at_utc
            ) VALUES (
                @orderId, @sourceCartId, @userId, @status, @itemsJson::jsonb, @totalAmount, @createdAtUtc, @processingStartedAtUtc
            );
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("orderId", order.Id);
        cmd.Parameters.AddWithValue("sourceCartId", order.SourceCartId);
        cmd.Parameters.AddWithValue("userId", order.UserId);
        cmd.Parameters.AddWithValue("status", order.Status.ToString());
        cmd.Parameters.AddWithValue("itemsJson", JsonSerializer.Serialize(order.Items));
        cmd.Parameters.AddWithValue("totalAmount", order.TotalAmount);
        cmd.Parameters.AddWithValue("createdAtUtc", order.CreatedAtUtc);
        cmd.Parameters.AddWithValue("processingStartedAtUtc", (object?)order.ProcessingStartedAtUtc ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureInitializedAsync(cancellationToken))
        {
            return await _fallbackRepo.GetByIdAsync(orderId, cancellationToken);
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT order_id, source_cart_id, user_id, status, items_json, total_amount, created_at_utc, processing_started_at_utc
            FROM orders
            WHERE order_id = @orderId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("orderId", orderId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadOrderRow(reader);
    }

    public async Task<IReadOnlyList<Order>> GetByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureInitializedAsync(cancellationToken))
        {
            return await _fallbackRepo.GetByUserIdAsync(userId, cancellationToken);
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT order_id, source_cart_id, user_id, status, items_json, total_amount, created_at_utc, processing_started_at_utc
            FROM orders
            WHERE user_id = @userId
            ORDER BY created_at_utc DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Order>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadOrderRow(reader));
        }

        return list;
    }

    public async Task<OrderNotification> NotifyOrderProcessingStartedAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureInitializedAsync(cancellationToken))
        {
            return await _fallbackNotifications.NotifyOrderProcessingStartedAsync(order, cancellationToken);
        }

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

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO order_notifications (notification_id, order_id, user_id, message, sent_at_utc)
            VALUES (@notificationId, @orderId, @userId, @message, @sentAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("notificationId", notification.NotificationId);
        cmd.Parameters.AddWithValue("orderId", notification.OrderId);
        cmd.Parameters.AddWithValue("userId", notification.UserId);
        cmd.Parameters.AddWithValue("message", notification.Message);
        cmd.Parameters.AddWithValue("sentAtUtc", notification.SentAtUtc);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "[ORDER NOTIFICATION -> User '{UserId}']: {Message}",
            order.UserId,
            message);

        return notification;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetUserNotificationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureInitializedAsync(cancellationToken))
        {
            return await _fallbackNotifications.GetUserNotificationsAsync(userId, cancellationToken);
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT notification_id, order_id, user_id, message, sent_at_utc
            FROM order_notifications
            WHERE user_id = @userId
            ORDER BY sent_at_utc DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<OrderNotification>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new OrderNotification(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4)));
        }

        return list;
    }

    private static Order ReadOrderRow(NpgsqlDataReader reader)
    {
        var orderId = reader.GetGuid(0);
        var sourceCartId = reader.GetGuid(1);
        var userId = reader.GetString(2);
        var status = Enum.TryParse<OrderStatus>(reader.GetString(3), out var parsed)
            ? parsed
            : OrderStatus.Processing;
        var items = JsonSerializer.Deserialize<List<OrderItem>>(reader.GetString(4)) ?? [];
        var totalAmount = reader.GetDecimal(5);
        var createdAtUtc = reader.GetDateTime(6);
        DateTime? processingStartedAtUtc = reader.IsDBNull(7) ? null : reader.GetDateTime(7);

        return Order.Reconstruct(
            orderId,
            sourceCartId,
            userId,
            status,
            items,
            totalAmount,
            createdAtUtc,
            processingStartedAtUtc);
    }
}
