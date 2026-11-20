using ZtpLab03.Orders.Application.DTOs;
using ZtpLab03.Orders.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Orders.Application.UseCases.GetUserNotifications;

/// <summary>
/// CQRS Query to retrieve order processing notifications sent to a specific user (+0.25 criterion).
/// </summary>
public sealed record GetUserNotificationsQuery(string UserId)
    : IQuery<Result<IReadOnlyList<OrderNotificationDto>>>;

public sealed class GetUserNotificationsQueryHandler
    : IQueryHandler<GetUserNotificationsQuery, Result<IReadOnlyList<OrderNotificationDto>>>
{
    private readonly IOrderNotificationService _notificationService;

    public GetUserNotificationsQueryHandler(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task<Result<IReadOnlyList<OrderNotificationDto>>> HandleAsync(
        GetUserNotificationsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.UserId))
        {
            return Result<IReadOnlyList<OrderNotificationDto>>.Failure("UserId is required.", ErrorType.Validation);
        }

        var notifications = await _notificationService.GetUserNotificationsAsync(
            query.UserId.Trim(),
            cancellationToken);

        var dtos = notifications.Select(n => n.ToDto()).ToList();
        return Result<IReadOnlyList<OrderNotificationDto>>.Success(dtos);
    }
}
