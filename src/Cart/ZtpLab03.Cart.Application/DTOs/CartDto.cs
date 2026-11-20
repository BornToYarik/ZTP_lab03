using ZtpLab03.Cart.Domain.Aggregates;

namespace ZtpLab03.Cart.Application.DTOs;

/// <summary>
/// Read-model DTO representing the current state of a user's shopping cart,
/// including all items, total price, version (event count), and 15-minute expiration timestamp.
/// </summary>
public sealed record CartDto(
    Guid CartId,
    string UserId,
    string Status,
    IReadOnlyList<CartItemDto> Items,
    decimal TotalPrice,
    DateTime CreatedAtUtc,
    DateTime LastModifiedAtUtc,
    DateTime ExpiresAtUtc,
    int Version);

public sealed record CartItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

/// <summary>
/// Centralized mapper for converting a rehydrated ShoppingCart aggregate into a CartDto (DRY principle).
/// </summary>
public static class CartMappingExtensions
{
    public static CartDto ToDto(this ShoppingCart cart)
    {
        var items = cart.Items
            .Select(i => new CartItemDto(
                i.ProductId,
                i.ProductName,
                i.UnitPrice,
                i.Quantity,
                i.LineTotal))
            .ToList();

        return new CartDto(
            cart.Id,
            cart.UserId,
            cart.Status.ToString(),
            items,
            cart.TotalPrice,
            cart.CreatedAtUtc,
            cart.LastModifiedAtUtc,
            cart.ExpiresAtUtc,
            cart.Version);
    }
}
