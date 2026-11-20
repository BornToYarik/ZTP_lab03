using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZtpLab03.Cart.Application.DTOs;
using ZtpLab03.Cart.Application.UseCases.AddProductToCart;
using ZtpLab03.Cart.Application.UseCases.CheckoutCart;
using ZtpLab03.Cart.Application.UseCases.CreateCart;
using ZtpLab03.Cart.Application.UseCases.ExpireInactiveCarts;
using ZtpLab03.Cart.Application.UseCases.GetCart;
using ZtpLab03.Cart.Application.UseCases.GetCartEventHistory;
using ZtpLab03.Cart.Application.UseCases.RemoveProductFromCart;
using ZtpLab03.Cart.Domain.Events;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.Cart.Infrastructure.EventStore;
using ZtpLab03.Cart.Infrastructure.Http;
using ZtpLab03.Orders.Application.DTOs;
using ZtpLab03.Orders.Application.UseCases.GetUserNotifications;
using ZtpLab03.Orders.Application.UseCases.GetUserOrders;
using ZtpLab03.Orders.Application.UseCases.StartOrderProcessing;
using ZtpLab03.Orders.Infrastructure;
using ZtpLab03.Products.Services;
using ZtpLab03.SharedKernel.CQRS;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.UnitTests;

/// <summary>
/// Unit tests verifying every Use Case directly without starting the ASP.NET Core web server.
/// Satisfies the +0.25 engineering requirement:
/// "Kod każdego Use Case’a powinien być łatwo testowalny (np. bez potrzeby uruchamiania serwera)."
/// </summary>
public sealed class UseCasesTests
{
    [Fact]
    public async Task CreateAddRemoveAndGetCart_UseCases_WorkCorrectlyAndCalculateTotal()
    {
        // Arrange
        var env = new TestEnvironment();
        const string userId = "user-101";

        // Act 1: Create Cart (Command)
        var createResult = await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand(userId));

        Assert.True(createResult.IsSuccess);
        var cartId = createResult.Value!.CartId;

        // Act 2: Add 2 Mice (2 * 449.00 = 898.00) and 1 Keyboard (899.00) -> Total = 1797.00
        var addMiceResult = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(cartId, userId, ProductCatalogService.MouseProductId, Quantity: 2));
        Assert.True(addMiceResult.IsSuccess);

        var addKeyboardResult = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(cartId, userId, ProductCatalogService.KeyboardProductId, Quantity: 1));
        Assert.True(addKeyboardResult.IsSuccess);

        // Act 3: Remove 1 Mouse -> Remaining: 1 Mouse (449) + 1 Keyboard (899) = 1348.00
        var removeMouseResult = await env.Mediator.SendCommandAsync<RemoveProductFromCartCommand, Result<CartDto>>(
            new RemoveProductFromCartCommand(cartId, userId, ProductCatalogService.MouseProductId, Quantity: 1));
        Assert.True(removeMouseResult.IsSuccess);

        // Act 4: Query Cart (Query)
        var getCartResult = await env.Mediator.SendQueryAsync<GetCartQuery, Result<CartDto>>(
            new GetCartQuery(cartId, userId));

        // Assert
        Assert.True(getCartResult.IsSuccess);
        var cart = getCartResult.Value!;
        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(1348.00m, cart.TotalPrice);
        Assert.Equal(4, cart.Version); // Created + Added + Added + Removed = 4 events in stream
    }

    [Fact]
    public async Task UserIsolation_PreventsOtherUserFromViewingOrModifyingCart()
    {
        // Arrange
        var env = new TestEnvironment();
        var createResult = await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand("owner-user"));
        var cartId = createResult.Value!.CartId;

        // Act: Attacker user tries to add product and view owner's cart
        var attackerAdd = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(cartId, "attacker-user", ProductCatalogService.MouseProductId, 1));

        var attackerGet = await env.Mediator.SendQueryAsync<GetCartQuery, Result<CartDto>>(
            new GetCartQuery(cartId, "attacker-user"));

        // Assert
        Assert.False(attackerAdd.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, attackerAdd.ErrorType);
        Assert.False(attackerGet.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, attackerGet.ErrorType);
    }

    [Fact]
    public async Task MultiDeviceOperationalConsistency_ConcurrentAddsAndRemovals_NeverLoseOperations()
    {
        // Arrange: "W tym samym czasie użytkownik może dodawać i usuwać produkty z koszyka na różnych urządzeniach.
        // System musi zapewniać, że żadna z tych operacji nie zostanie utracona ani pominięta."
        var env = new TestEnvironment();
        const string userId = "multi-device-user";

        var createResult = await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand(userId));
        var cartId = createResult.Value!.CartId;

        // Seed 5 mice so concurrent removals and additions can execute simultaneously
        await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(cartId, userId, ProductCatalogService.MouseProductId, Quantity: 5));

        // Act: Simulate 10 concurrent "Add 1 Mouse" requests from Device A/B/C and
        // 4 concurrent "Remove 1 Mouse" requests from Device D/E running in parallel.
        // Expected final quantity: 5 + 10 - 4 = 11 mice.
        var tasks = new List<Task<Result<CartDto>>>();

        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
                env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
                    new AddProductToCartCommand(cartId, userId, ProductCatalogService.MouseProductId, Quantity: 1))));
        }

        for (var i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(() =>
                env.Mediator.SendCommandAsync<RemoveProductFromCartCommand, Result<CartDto>>(
                    new RemoveProductFromCartCommand(cartId, userId, ProductCatalogService.MouseProductId, Quantity: 1))));
        }

        var results = await Task.WhenAll(tasks);

        // Assert: All 14 concurrent operations succeeded and none were lost
        Assert.All(results, r => Assert.True(r.IsSuccess));

        var finalCart = (await env.Mediator.SendQueryAsync<GetCartQuery, Result<CartDto>>(
            new GetCartQuery(cartId, userId))).Value!;

        var mouseItem = Assert.Single(finalCart.Items);
        Assert.Equal(11, mouseItem.Quantity);

        // Verify Event Sourcing stream recorded all 1 + 1 + 10 + 4 = 16 events
        var history = (await env.Mediator.SendQueryAsync<GetCartEventHistoryQuery, Result<IReadOnlyList<CartDomainEvent>>>(
            new GetCartEventHistoryQuery(cartId, userId))).Value!;
        Assert.Equal(16, history.Count);
    }

    [Fact]
    public async Task ProductReservationLock_PreventsConcurrentReservation_AndExpiresAfter15Minutes()
    {
        // Arrange (+0.5 criterion: product lock on add & 15-minute expiration)
        var env = new TestEnvironment();

        var user1Cart = (await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand("user-1"))).Value!;
        var user2Cart = (await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand("user-2"))).Value!;

        // Act 1: User 1 adds the only unit of LimitedGpuProductId (stock = 1)
        var user1Reserve = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(user1Cart.CartId, "user-1", ProductCatalogService.LimitedGpuProductId, 1));
        Assert.True(user1Reserve.IsSuccess);

        // Act 2: User 2 tries to reserve the same product in parallel -> blocked!
        var user2ReserveWhileLocked = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(user2Cart.CartId, "user-2", ProductCatalogService.LimitedGpuProductId, 1));
        Assert.False(user2ReserveWhileLocked.IsSuccess);
        Assert.Equal(ErrorType.Conflict, user2ReserveWhileLocked.ErrorType);

        // Act 3: Advance clock by 16 minutes (> 15-minute lock expiration window),
        // keeping user2Cart alive or creating a fresh cart for user-2
        env.Clock.Advance(TimeSpan.FromMinutes(16));

        var expiredCount = await env.Mediator.SendCommandAsync<ExpireInactiveCartsCommand, int>(
            new ExpireInactiveCartsCommand());
        Assert.Equal(2, expiredCount); // Both inactive carts expired and released locks

        // Act 4: User 2 creates a new cart and can now reserve the released GPU!
        var user2NewCart = (await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand("user-2"))).Value!;
        var user2ReserveAfterExpiry = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(user2NewCart.CartId, "user-2", ProductCatalogService.LimitedGpuProductId, 1));

        // Assert
        Assert.True(user2ReserveAfterExpiry.IsSuccess);
    }

    [Fact]
    public async Task CheckoutCart_TriggersDecoupledOrderDomainProcessing_AndNotifiesUser()
    {
        // Arrange (+0.25 criterion: Cart checkout triggers Order Domain & sends user notification)
        var env = new TestEnvironment();
        const string userId = "buyer-99";

        var cart = (await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand(userId))).Value!;

        await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(cart.CartId, userId, ProductCatalogService.LaptopProductId, Quantity: 1));

        // Act: Checkout the cart
        var checkoutResult = await env.Mediator.SendCommandAsync<CheckoutCartCommand, Result<CartDto>>(
            new CheckoutCartCommand(cart.CartId, userId));

        // Assert 1: Cart status is CheckedOut
        Assert.True(checkoutResult.IsSuccess);
        Assert.Equal("CheckedOut", checkoutResult.Value!.Status);

        // Assert 2: Independent Order Domain created the Order in 'Processing' state
        var ordersResult = await env.Mediator.SendQueryAsync<GetUserOrdersQuery, Result<IReadOnlyList<OrderDto>>>(
            new GetUserOrdersQuery(userId));
        Assert.True(ordersResult.IsSuccess);
        var order = Assert.Single(ordersResult.Value!);
        Assert.Equal(cart.CartId, order.SourceCartId);
        Assert.Equal("Processing", order.Status);
        Assert.Equal(7499.00m, order.TotalAmount);

        // Assert 3: User received a notification confirming that order processing started
        var notificationsResult = await env.Mediator.SendQueryAsync<GetUserNotificationsQuery, Result<IReadOnlyList<OrderNotificationDto>>>(
            new GetUserNotificationsQuery(userId));
        Assert.True(notificationsResult.IsSuccess);
        var notification = Assert.Single(notificationsResult.Value!);
        Assert.Equal(order.OrderId, notification.OrderId);
        Assert.Contains("Processing", notification.Message);
    }

    [Fact]
    public async Task CreatingNewCartForSameUser_AutomaticallyClosesPreviousCartAndReleasesProductLocks()
    {
        // Arrange: User creates Cart #1 and locks the only unit of LimitedGpuProductId (stock = 1)
        var env = new TestEnvironment();
        const string userId = "user-yarik-01";

        var firstCart = (await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand(userId))).Value!;

        var addGpuToFirstCart = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(firstCart.CartId, userId, ProductCatalogService.LimitedGpuProductId, 1));
        Assert.True(addGpuToFirstCart.IsSuccess);

        // Act: The SAME user creates Cart #2 without manually closing Cart #1
        var secondCart = (await env.Mediator.SendCommandAsync<CreateCartCommand, Result<CartDto>>(
            new CreateCartCommand(userId))).Value!;

        // Assert: Cart #1 was automatically closed and its product reservation on LimitedGpuProductId was released,
        // allowing the user to add LimitedGpuProductId into Cart #2!
        var addGpuToSecondCart = await env.Mediator.SendCommandAsync<AddProductToCartCommand, Result<CartDto>>(
            new AddProductToCartCommand(secondCart.CartId, userId, ProductCatalogService.LimitedGpuProductId, 1));
        Assert.True(addGpuToSecondCart.IsSuccess);
    }

    /// <summary>
    /// Self-contained in-memory test harness that wires up all Clean Architecture Use Cases
    /// and routes <see cref="HttpProductServiceClient"/> through an in-memory HTTP handler
    /// (verifying real HTTP serialization without needing to bind a TCP port).
    /// </summary>
    private sealed class TestEnvironment
    {
        public FakeTimeProvider Clock { get; } = new();
        public IMediator Mediator { get; }
        public IProductCatalogService ProductCatalog { get; }

        public TestEnvironment()
        {
            var services = new ServiceCollection();

            services.AddSingleton<TimeProvider>(Clock);
            services.AddSharedMediator();

            ProductCatalog = new ProductCatalogService();
            services.AddSingleton(ProductCatalog);

            services.AddSingleton<ICartEventStore, InMemoryCartEventStore>();
            services.AddCqrsHandlersFromAssembly(typeof(CreateCartCommand).Assembly);

            // Wire HttpProductServiceClient over a real HttpClient backed by an in-memory HTTP handler
            services.AddScoped<IProductServiceClient>(_ =>
            {
                var handler = new InMemoryProductApiHttpHandler(ProductCatalog);
                var httpClient = new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://product-service.internal")
                };
                return new HttpProductServiceClient(httpClient);
            });

            services.AddOrdersModule();

            var provider = services.BuildServiceProvider();
            Mediator = provider.GetRequiredService<IMediator>();
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _currentUtc = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _currentUtc;

        public void Advance(TimeSpan delta) => _currentUtc = _currentUtc.Add(delta);
    }

    private sealed class InMemoryProductApiHttpHandler : HttpMessageHandler
    {
        private readonly IProductCatalogService _catalog;

        public InMemoryProductApiHttpHandler(IProductCatalogService catalog)
        {
            _catalog = catalog;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // POST /api/products/{id}/reserve
            if (request.Method == HttpMethod.Post && segments.Length == 4 && segments[3] == "reserve")
            {
                var productId = Guid.Parse(segments[2]);
                var body = await request.Content!.ReadFromJsonAsync<ReserveBody>(cancellationToken: cancellationToken);
                var outcome = _catalog.TryReserveProduct(productId, body!.CartId, body.UserId, body.Quantity);

                return new HttpResponseMessage(outcome.Succeeded ? HttpStatusCode.OK : HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(outcome)
                };
            }

            // POST /api/products/{id}/release
            if (request.Method == HttpMethod.Post && segments.Length == 4 && segments[3] == "release")
            {
                var productId = Guid.Parse(segments[2]);
                var body = await request.Content!.ReadFromJsonAsync<ReleaseBody>(cancellationToken: cancellationToken);
                var released = _catalog.ReleaseReservation(productId, body!.CartId, body.Quantity);
                return new HttpResponseMessage(released ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            }

            // POST /api/products/reservations/{cartId}/confirm
            if (request.Method == HttpMethod.Post && segments.Length == 5 && segments[2] == "reservations" && segments[4] == "confirm")
            {
                var cartId = Guid.Parse(segments[3]);
                var confirmed = _catalog.ConfirmCartReservations(cartId);
                return new HttpResponseMessage(confirmed ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private sealed record ReserveBody(Guid CartId, string UserId, int Quantity);
        private sealed record ReleaseBody(Guid CartId, int Quantity);
    }
}
