using Microsoft.AspNetCore.Mvc;
using ZtpLab03.Orders.Application.DTOs;
using ZtpLab03.Orders.Application.UseCases.GetUserNotifications;
using ZtpLab03.Orders.Application.UseCases.GetUserOrders;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Api.Controllers;

/// <summary>
/// Presentation Layer controller for the independent Order Domain (+0.25 criterion).
/// Allows users to query their orders and order processing notifications via CQRS Queries.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CQRS Query: Retrieve all orders for a specific user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUserOrders(
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = !string.IsNullOrWhiteSpace(headerUserId)
            ? headerUserId.Trim()
            : userId?.Trim() ?? string.Empty;

        var query = new GetUserOrdersQuery(resolvedUserId);
        var result = await _mediator.SendQueryAsync<GetUserOrdersQuery, Result<IReadOnlyList<OrderDto>>>(
            query,
            cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// CQRS Query: Retrieve all order processing notifications sent to a specific user (+0.25 criterion).
    /// </summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetUserNotifications(
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = !string.IsNullOrWhiteSpace(headerUserId)
            ? headerUserId.Trim()
            : userId?.Trim() ?? string.Empty;

        var query = new GetUserNotificationsQuery(resolvedUserId);
        var result = await _mediator.SendQueryAsync<GetUserNotificationsQuery, Result<IReadOnlyList<OrderNotificationDto>>>(
            query,
            cancellationToken);

        return result.ToActionResult(this);
    }
}
