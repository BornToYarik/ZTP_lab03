using Microsoft.AspNetCore.Mvc;
using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Application.UseCases.AddProductToCart;
using ZtpLab03.Cart.Application.UseCases.CheckoutCart;
using ZtpLab03.Cart.Application.UseCases.CreateCart;
using ZtpLab03.Cart.Application.UseCases.ExpireInactiveCarts;
using ZtpLab03.Cart.Application.UseCases.GetCart;
using ZtpLab03.Cart.Application.UseCases.GetCartEventHistory;
using ZtpLab03.Cart.Application.UseCases.RemoveProductFromCart;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Api.Controllers;

/// <summary>
/// Thin Presentation Layer controller for the Shopping Cart bounded context.
/// Follows Clean Architecture and CQRS: contains zero business logic and delegates every action
/// as a Command or Query through <see cref="IMediator"/>.
/// </summary>
[ApiController]
[Route("api/carts")]
public sealed class CartsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CartsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Use Case 1 (Command): Create a new shopping cart for a uniquely identified user.
    /// User ID can be provided via the 'X-User-Id' HTTP header or the request body.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateCart(
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromBody] CreateCartHttpRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(headerUserId, request?.UserId);
        var command = new CreateCartCommand(userId);
        var result = await _mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(command, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            return CreatedAtAction(
                nameof(GetCart),
                new { cartId = result.Value.CartId },
                result.Value);
        }

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Use Case 2 (Command): Add a product to the cart and immediately lock/reserve its stock for 15 minutes.
    /// </summary>
    [HttpPost("{cartId:guid}/items")]
    public async Task<IActionResult> AddProductToCart(
        Guid cartId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromBody] ModifyCartItemHttpRequest request,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(headerUserId, request.UserId);
        var command = new AddProductToCartCommand(cartId, userId, request.ProductId, request.Quantity);
        var result = await _mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(command, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Use Case 3 (Command): Remove a product quantity from the cart and release its stock reservation.
    /// </summary>
    [HttpDelete("{cartId:guid}/items/{productId:guid}")]
    public async Task<IActionResult> RemoveProductFromCart(
        Guid cartId,
        Guid productId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromQuery] string? queryUserId,
        [FromQuery] int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        var userId = ResolveUserId(headerUserId, queryUserId);
        var command = new RemoveProductFromCartCommand(cartId, userId, productId, quantity);
        var result = await _mediator.SendCommandAsync<RemoveProductFromCartCommand, Result<CartDto>>(command, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Use Case 4 (Query): View the current cart contents and total price before checkout.
    /// </summary>
    [HttpGet("{cartId:guid}")]
    public async Task<IActionResult> GetCart(
        Guid cartId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = ResolveUserId(headerUserId, userId);
        var query = new GetCartQuery(cartId, resolvedUserId);
        var result = await _mediator.SendQueryAsync<GetCartQuery, Result<CartDto>>(query, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Use Case 5 (Command): Confirm (checkout) the cart as ready for ordering, triggering order processing
    /// and user notification in the decoupled Order Domain.
    /// </summary>
    [HttpPost("{cartId:guid}/checkout")]
    public async Task<IActionResult> CheckoutCart(
        Guid cartId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromBody] CheckoutCartHttpRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(headerUserId, request?.UserId);
        var command = new CheckoutCartCommand(cartId, userId);
        var result = await _mediator.SendCommandAsync<CheckoutCartCommand, Result<CartDto>>(command, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Diagnostic Query: View the raw Event Sourcing event stream for a shopping cart.
    /// </summary>
    [HttpGet("{cartId:guid}/events")]
    public async Task<IActionResult> GetCartEvents(
        Guid cartId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = ResolveUserId(headerUserId, userId);
        var query = new GetCartEventHistoryQuery(cartId, resolvedUserId);
        var result = await _mediator.SendQueryAsync<GetCartEventHistoryQuery, Result<IReadOnlyList<CartDomainEvent>>>(
            query,
            cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Administrative/Demo endpoint to manually trigger the expiration sweep for inactive carts (>= 15 min).
    /// </summary>
    [HttpPost("expire-inactive")]
    public async Task<IActionResult> ExpireInactiveCarts(CancellationToken cancellationToken)
    {
        var expiredCount = await _mediator.SendCommandAsync<ExpireInactiveCartsCommand, int>(
            new ExpireInactiveCartsCommand(),
            cancellationToken);

        return Ok(new { expiredCartsCount = expiredCount });
    }

    /// <summary>
    /// Demo endpoint for the live presentation: simulates the passage of 15 minutes for a specific cart,
    /// immediately expiring its product locks over HTTP and appending <see cref="CartExpiredEvent"/>.
    /// </summary>
    [HttpPost("{cartId:guid}/simulate-timeout")]
    public async Task<IActionResult> SimulateCartTimeout(
        Guid cartId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.SendCommandAsync<SimulateCartTimeoutCommand, Result<bool>>(
            new SimulateCartTimeoutCommand(cartId),
            cancellationToken);

        return result.ToActionResult(this);
    }

    private static string ResolveUserId(string? headerUserId, string? fallbackUserId) =>
        !string.IsNullOrWhiteSpace(headerUserId)
            ? headerUserId.Trim()
            : fallbackUserId?.Trim() ?? string.Empty;
}

public sealed record CreateCartHttpRequest(string? UserId);
public sealed record ModifyCartItemHttpRequest(Guid ProductId, int Quantity = 1, string? UserId = null);
public sealed record CheckoutCartHttpRequest(string? UserId);
