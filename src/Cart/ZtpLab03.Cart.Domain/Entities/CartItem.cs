namespace ZtpLab03.Cart.Domain.Entities;

/// <summary>
/// Represents an individual product line inside a user's shopping cart.
/// </summary>
public sealed class CartItem
{
    public Guid ProductId { get; }
    public string ProductName { get; }
    public decimal UnitPrice { get; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;

    public CartItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void IncreaseQuantity(int addedQuantity)
    {
        Quantity += addedQuantity;
    }

    internal void DecreaseQuantity(int removedQuantity)
    {
        Quantity = Math.Max(0, Quantity - removedQuantity);
    }
}

public enum CartStatus
{
    Active = 0,
    CheckedOut = 1,
    Expired = 2
}
