namespace ZtpLab03.Cart.Domain.Ports;

/// <summary>
/// Port for communicating with the external Product Service over HTTP.
/// Required engineering practice: "Komunikacja z serwisem produktowym musi odbywać się poprzez protokół HTTP."
/// </summary>
public interface IProductServiceClient
{
    /// <summary>
    /// Fetches product metadata and current availability over HTTP.
    /// </summary>
    Task<ProductReservationDto?> GetProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves (locks) the specified quantity of a product for a cart over HTTP so that
    /// no other user can reserve the same stock concurrently.
    /// </summary>
    Task<ProductLockResult> TryReserveProductAsync(
        Guid productId,
        Guid cartId,
        string userId,
        int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a previously locked product quantity back to available stock over HTTP
    /// (called when a user removes a product from the cart or when the cart expires after 15 minutes).
    /// </summary>
    Task<bool> ReleaseProductReservationAsync(
        Guid productId,
        Guid cartId,
        int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a temporary 15-minute cart reservation into a permanent stock deduction upon cart checkout.
    /// </summary>
    Task<bool> ConfirmCartReservationsAsync(
        Guid cartId,
        CancellationToken cancellationToken = default);
}

public sealed record ProductReservationDto(
    Guid Id,
    string Name,
    decimal Price,
    int AvailableStock,
    int ReservedStock);

public sealed record ProductLockResult(
    bool Succeeded,
    string? ErrorMessage,
    ProductReservationDto? Product);
