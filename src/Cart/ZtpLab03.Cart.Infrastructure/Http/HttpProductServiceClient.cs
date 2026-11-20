using System.Net;
using System.Net.Http.Json;
using ZtpLab03.Cart.Domain.Ports;

namespace ZtpLab03.Cart.Infrastructure.Http;

/// <summary>
/// HTTP client adapter implementing <see cref="IProductServiceClient"/>.
/// 
/// REQUIRED ENGINEERING PRACTICE:
/// "Komunikacja z serwisem produktowym musi odbywać się poprzez protokół HTTP."
/// All product lookups, stock reservations (locks), lock releases, and checkout confirmations
/// are transmitted over HTTP to the Product Service REST API.
/// </summary>
public sealed class HttpProductServiceClient : IProductServiceClient
{
    private readonly HttpClient _httpClient;

    public HttpProductServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProductReservationDto?> GetProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/products/{productId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductReservationDto>(cancellationToken: cancellationToken);
    }

    public async Task<ProductLockResult> TryReserveProductAsync(
        Guid productId,
        Guid cartId,
        string userId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var payload = new ReserveProductHttpRequest(cartId, userId, quantity);
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/products/{productId}/reserve",
            payload,
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<ProductLockHttpResponse>(cancellationToken: cancellationToken);
        if (response.IsSuccessStatusCode && body?.Product is not null)
        {
            return new ProductLockResult(true, null, body.Product);
        }

        return new ProductLockResult(
            false,
            body?.ErrorMessage ?? $"HTTP {(int)response.StatusCode}: Failed to reserve product '{productId}'.",
            null);
    }

    public async Task<bool> ReleaseProductReservationAsync(
        Guid productId,
        Guid cartId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var payload = new ReleaseProductHttpRequest(cartId, quantity);
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/products/{productId}/release",
            payload,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ConfirmCartReservationsAsync(
        Guid cartId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/products/reservations/{cartId}/confirm",
            content: null,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    private sealed record ReserveProductHttpRequest(Guid CartId, string UserId, int Quantity);
    private sealed record ReleaseProductHttpRequest(Guid CartId, int Quantity);
    private sealed record ProductLockHttpResponse(bool Succeeded, string? ErrorMessage, ProductReservationDto? Product);
}
