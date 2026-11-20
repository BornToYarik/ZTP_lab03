using Microsoft.AspNetCore.Mvc;
using ZtpLab03.Products.Services;

namespace ZtpLab03.Api.Controllers;

/// <summary>
/// HTTP endpoints of the Product Service.
/// The Cart Domain's <see cref="ZtpLab03.Cart.Infrastructure.Http.HttpProductServiceClient"/>
/// communicates with these endpoints strictly over the HTTP protocol using <see cref="HttpClient"/>.
/// </summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductCatalogService _productCatalog;

    public ProductsController(IProductCatalogService productCatalog)
    {
        _productCatalog = productCatalog;
    }

    [HttpGet]
    public IActionResult GetAllProducts()
    {
        return Ok(_productCatalog.GetAllProducts());
    }

    [HttpGet("{productId:guid}")]
    public IActionResult GetProduct(Guid productId)
    {
        var product = _productCatalog.GetProduct(productId);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost("{productId:guid}/reserve")]
    public IActionResult ReserveProduct(Guid productId, [FromBody] ReserveProductRequestDto request)
    {
        var outcome = _productCatalog.TryReserveProduct(
            productId,
            request.CartId,
            request.UserId,
            request.Quantity);

        if (!outcome.Succeeded)
        {
            return Conflict(outcome);
        }

        return Ok(outcome);
    }

    [HttpPost("{productId:guid}/release")]
    public IActionResult ReleaseReservation(Guid productId, [FromBody] ReleaseProductRequestDto request)
    {
        var released = _productCatalog.ReleaseReservation(productId, request.CartId, request.Quantity);
        return released ? Ok() : NotFound();
    }

    [HttpPost("reservations/{cartId:guid}/confirm")]
    public IActionResult ConfirmCartReservations(Guid cartId)
    {
        var confirmed = _productCatalog.ConfirmCartReservations(cartId);
        return confirmed ? Ok() : NotFound();
    }

    public sealed record ReserveProductRequestDto(Guid CartId, string UserId, int Quantity);
    public sealed record ReleaseProductRequestDto(Guid CartId, int Quantity);
}
