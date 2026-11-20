namespace ZtpLab03.Products.Models;

/// <summary>
/// Represents a product in the Product Service domain, tracking both available stock
/// and currently locked (reserved) stock across active shopping carts.
/// </summary>
public sealed class Product
{
    public Guid Id { get; }
    public string Name { get; }
    public decimal Price { get; }
    public int AvailableStock { get; private set; }
    public int ReservedStock { get; private set; }

    public Product(Guid id, string name, decimal price, int initialStock)
    {
        Id = id;
        Name = name;
        Price = price;
        AvailableStock = initialStock;
        ReservedStock = 0;
    }

    public bool TryReserve(int quantity)
    {
        if (quantity <= 0 || AvailableStock < quantity)
        {
            return false;
        }

        AvailableStock -= quantity;
        ReservedStock += quantity;
        return true;
    }

    public void ReleaseReservation(int quantity)
    {
        var amountToRelease = Math.Clamp(quantity, 0, ReservedStock);
        ReservedStock -= amountToRelease;
        AvailableStock += amountToRelease;
    }

    public void ConfirmReservation(int quantity)
    {
        var amountToConfirm = Math.Clamp(quantity, 0, ReservedStock);
        ReservedStock -= amountToConfirm;
    }
}

public sealed record ProductViewDto(
    Guid Id,
    string Name,
    decimal Price,
    int AvailableStock,
    int ReservedStock);

public sealed record ProductReservationOutcome(
    bool Succeeded,
    string? ErrorMessage,
    ProductViewDto? Product);
