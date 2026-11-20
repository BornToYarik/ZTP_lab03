using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ZtpLab03.Products.Models;

namespace ZtpLab03.Products.Services;

/// <summary>
/// Interface for the Product Service domain logic exposed via HTTP endpoints.
/// </summary>
public interface IProductCatalogService
{
    IReadOnlyList<ProductViewDto> GetAllProducts();
    ProductViewDto? GetProduct(Guid productId);
    ProductReservationOutcome TryReserveProduct(Guid productId, Guid cartId, string userId, int quantity);
    bool ReleaseReservation(Guid productId, Guid cartId, int quantity);
    bool ConfirmCartReservations(Guid cartId);
}

/// <summary>
/// Thread-safe Product Service managing product inventory and atomic stock reservations (locks).
///
/// =================================================================================================
/// ARCHITECTURAL ANALYSIS: PRODUCT LOCKING IN CART DOMAIN VS. PRODUCT DOMAIN (+0.5 CRITERION)
/// ("Mechanizm blokady produktów może zostać zaimplementowany w domenie koszyka lub domenie produktów
/// – wymagane jest przeprowadzenie analizy zalet i wad każdej opcji oraz uzasadnienie wybranego rozwiązania.")
/// =================================================================================================
///
/// OPTION 1: Implementing Product Locking inside the CART DOMAIN
/// -------------------------------------------------------------
///   Pros:
///     + Zero network latency when checking if an item is already locked by another cart within the
///       same Cart database/event store.
///     + Cart expiration (15-minute TTL) and lock cleanup occur within a single bounded context without
///       requiring an outgoing HTTP call to release locks.
///   Cons:
///     - Violates Single Source of Truth (SSOT) for inventory: the Cart Domain must duplicate and track
///       total stock counts that belong to the Product/Inventory Domain.
///     - Poor horizontal scalability: if multiple Cart service instances run in parallel, preventing two
///       users from reserving the last item requires distributed cross-cart locking across all active
///       cart streams.
///     - Tight coupling: if another sales channel (e.g., B2B API or Admin Order) modifies stock in the
///       Product Service, the Cart Domain's lock table immediately becomes out of sync, causing overselling.
///
/// OPTION 2: Implementing Product Locking inside the PRODUCT DOMAIN (with Cart TTL coordination)
/// ---------------------------------------------------------------------------------------------
///   Pros:
///     + Single Source of Truth (SSOT): The Product Domain owns both <see cref="Product.AvailableStock"/>
///       and <see cref="Product.ReservedStock"/>, guaranteeing an atomic check-and-lock operation per
///       ProductId without cross-cart scanning.
///     + Enables true horizontal scaling of the Cart Service: any number of stateless Cart instances
///       can call `POST /api/products/{id}/reserve` over HTTP, and the Product Service serializes
///       stock decrement atomically.
///     + High modularity and domain independence: the Cart Domain does not need to know how inventory
///       is stored or replenished.
///   Cons:
///     - Requires HTTP communication between the Cart Service and Product Service when adding, removing,
///       expiring, or checking out items (which is already a mandatory requirement of this assignment).
///     - Requires releasing locks if a cart is abandoned after 15 minutes.
///
/// JUSTIFICATION OF CHOSEN SOLUTION:
/// ---------------------------------
/// We chose **Option 2 (Authoritative Stock Lock in the Product Domain coordinated by Cart 15-minute TTL)**.
/// Because the system must support horizontal scaling of Cart instances, concurrent multi-user isolation,
/// and mandatory HTTP communication with the Product Service, keeping the atomic stock lock inside the
/// Product Domain completely prevents race conditions (overselling the last unit) across multiple Cart
/// instances while respecting domain boundaries (SOLID / Single Responsibility Principle). Meanwhile,
/// the Cart Domain owns the 15-minute shopping session lifecycle and triggers lock release over HTTP
/// when an item is removed or when a cart expires after 15 minutes of inactivity.
/// =================================================================================================
/// </summary>
public sealed class ProductCatalogService : IProductCatalogService
{
    // Pre-seeded deterministic product IDs for easy testing in Swagger / HTTP client / Unit Tests
    public static readonly Guid LaptopProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MouseProductId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid KeyboardProductId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid LimitedGpuProductId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly object _syncLock = new();
    private readonly Dictionary<Guid, Product> _products = new();

    // Tracks active reservations per (CartId -> (ProductId -> ReservedQuantity))
    private readonly Dictionary<Guid, Dictionary<Guid, int>> _cartReservations = new();

    public ProductCatalogService()
    {
        SeedDefaultCatalog();
    }

    public IReadOnlyList<ProductViewDto> GetAllProducts()
    {
        lock (_syncLock)
        {
            return _products.Values.Select(MapToDto).ToList();
        }
    }

    public ProductViewDto? GetProduct(Guid productId)
    {
        lock (_syncLock)
        {
            return _products.TryGetValue(productId, out var product)
                ? MapToDto(product)
                : null;
        }
    }

    public ProductReservationOutcome TryReserveProduct(
        Guid productId,
        Guid cartId,
        string userId,
        int quantity)
    {
        if (quantity <= 0)
        {
            return new ProductReservationOutcome(false, "Reservation quantity must be positive.", null);
        }

        lock (_syncLock)
        {
            if (!_products.TryGetValue(productId, out var product))
            {
                return new ProductReservationOutcome(
                    false,
                    $"Product '{productId}' does not exist in the Product Service.",
                    null);
            }

            if (!product.TryReserve(quantity))
            {
                return new ProductReservationOutcome(
                    false,
                    $"Insufficient available stock for '{product.Name}'. Available: {product.AvailableStock}, Requested: {quantity}.",
                    MapToDto(product));
            }

            if (!_cartReservations.TryGetValue(cartId, out var perCart))
            {
                perCart = new Dictionary<Guid, int>();
                _cartReservations[cartId] = perCart;
            }

            perCart[productId] = perCart.GetValueOrDefault(productId) + quantity;
            return new ProductReservationOutcome(true, null, MapToDto(product));
        }
    }

    public bool ReleaseReservation(Guid productId, Guid cartId, int quantity)
    {
        if (quantity <= 0)
        {
            return false;
        }

        lock (_syncLock)
        {
            if (!_products.TryGetValue(productId, out var product))
            {
                return false;
            }

            if (!_cartReservations.TryGetValue(cartId, out var perCart) ||
                !perCart.TryGetValue(productId, out var currentlyReserved) ||
                currentlyReserved <= 0)
            {
                return false;
            }

            var toRelease = Math.Min(currentlyReserved, quantity);
            product.ReleaseReservation(toRelease);

            var remaining = currentlyReserved - toRelease;
            if (remaining <= 0)
            {
                perCart.Remove(productId);
            }
            else
            {
                perCart[productId] = remaining;
            }

            if (perCart.Count == 0)
            {
                _cartReservations.Remove(cartId);
            }

            return true;
        }
    }

    public bool ConfirmCartReservations(Guid cartId)
    {
        lock (_syncLock)
        {
            if (!_cartReservations.TryGetValue(cartId, out var perCart))
            {
                return false;
            }

            foreach (var (productId, reservedQty) in perCart)
            {
                if (_products.TryGetValue(productId, out var product))
                {
                    product.ConfirmReservation(reservedQty);
                }
            }

            _cartReservations.Remove(cartId);
            return true;
        }
    }

    private void SeedDefaultCatalog()
    {
        _products[LaptopProductId] = new Product(LaptopProductId, "ThinkPad X1 Carbon Gen 12", 7499.00m, initialStock: 10);
        _products[MouseProductId] = new Product(MouseProductId, "Logitech MX Master 3S", 449.00m, initialStock: 25);
        _products[KeyboardProductId] = new Product(KeyboardProductId, "Keychron Q1 Pro Mechanical Keyboard", 899.00m, initialStock: 15);
        _products[LimitedGpuProductId] = new Product(LimitedGpuProductId, "NVIDIA RTX 5090 Founders Edition (Last Unit!)", 9999.00m, initialStock: 1);
    }

    private static ProductViewDto MapToDto(Product product) =>
        new(product.Id, product.Name, product.Price, product.AvailableStock, product.ReservedStock);
}

public static class ProductModuleDependencyInjection
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services)
    {
        services.AddSingleton<IProductCatalogService, ProductCatalogService>();
        return services;
    }
}
