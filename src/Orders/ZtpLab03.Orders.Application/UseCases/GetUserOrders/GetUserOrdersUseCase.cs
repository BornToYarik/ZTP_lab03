using ZtpLab03.Orders.Application.DTOs;
using ZtpLab03.Orders.Domain.Ports;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Orders.Application.UseCases.GetUserOrders;

/// <summary>
/// CQRS Query to retrieve all orders belonging to a specific user.
/// </summary>
public sealed record GetUserOrdersQuery(string UserId) : IQuery<Result<IReadOnlyList<OrderDto>>>;

public sealed class GetUserOrdersQueryHandler
    : IQueryHandler<GetUserOrdersQuery, Result<IReadOnlyList<OrderDto>>>
{
    private readonly IOrderRepository _orderRepository;

    public GetUserOrdersQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(
        GetUserOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.UserId))
        {
            return Result<IReadOnlyList<OrderDto>>.Failure("UserId is required.", ErrorType.Validation);
        }

        var orders = await _orderRepository.GetByUserIdAsync(query.UserId.Trim(), cancellationToken);
        var dtos = orders.Select(o => o.ToDto()).ToList();
        return Result<IReadOnlyList<OrderDto>>.Success(dtos);
    }
}
