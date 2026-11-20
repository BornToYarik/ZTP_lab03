# Architecture, Design Decisions & Change Log (`ZTP_lab03`)

This document tracks all structural changes, architectural patterns, and engineering justifications implemented for the **Zaawansowane Techniki Programowania (ZTP) — Lab 03** project.

---

## 1. Change Log (Chronological Implementation Log)

### [2026-11-19] Entry 1 — Solution Architecture & Multi-Module Setup
* **Changed**: Initialized a 10-project Clean Architecture solution in `ZTP_lab03.sln`.
* **Structure Created**:
  * `src/Shared/ZtpLab03.SharedKernel` — Shared CQRS (`ICommand`, `ICommandHandler`, `IQuery`, `IQueryHandler`), Mediator (`IMediator`, `Mediator`), standardized `Result<T>`, and cross-domain integration contracts (`CartCheckedOutIntegrationEvent`).
  * **Shopping Cart Bounded Context (3 Clean Architecture Layers)**:
    * `src/Cart/ZtpLab03.Cart.Domain` — Event-Sourced Aggregate Root (`ShoppingCart`), Domain Events (`CartCreatedEvent`, `ProductAddedToCartEvent`, `ProductRemovedFromCartEvent`, `CartCheckedOutEvent`, `CartExpiredEvent`), Entities (`CartItem`), and Ports (`ICartEventStore`, `IProductServiceClient`).
    * `src/Cart/ZtpLab03.Cart.Application` — Isolated Use Cases implemented as CQRS Command and Query Handlers:
      * `CreateCartCommand` / `CreateCartCommandHandler`
      * `AddProductToCartCommand` / `AddProductToCartCommandHandler`
      * `RemoveProductFromCartCommand` / `RemoveProductFromCartCommandHandler`
      * `GetCartQuery` / `GetCartQueryHandler`
      * `CheckoutCartCommand` / `CheckoutCartCommandHandler`
      * `ExpireInactiveCartsCommand` / `ExpireInactiveCartsCommandHandler`
      * `GetCartEventHistoryQuery` / `GetCartEventHistoryQueryHandler`
    * `src/Cart/ZtpLab03.Cart.Infrastructure` — `InMemoryCartEventStore` (thread-safe per-stream Event Store), `HttpProductServiceClient` (HTTP client communicating with Product Service), and `CartExpirationBackgroundService` (15-minute lock expiration worker).
  * **Order Bounded Context (3 Clean Architecture Layers — Completely Decoupled from Cart)**:
    * `src/Orders/ZtpLab03.Orders.Domain` — `Order` Aggregate Root, `OrderItem`, `OrderStatus`, `OrderNotification`, and Ports (`IOrderRepository`, `IOrderNotificationService`).
    * `src/Orders/ZtpLab03.Orders.Application` — `StartOrderProcessingCommand` / `StartOrderProcessingCommandHandler`, `CartCheckedOutIntegrationEventHandler`, `GetUserOrdersQuery`, and `GetUserNotificationsQuery`.
    * `src/Orders/ZtpLab03.Orders.Infrastructure` — `InMemoryOrderRepository` and `InMemoryOrderNotificationService`.
  * **Product Service Module**:
    * `src/Products/ZtpLab03.Products` — `Product` entity with atomic `AvailableStock` and `ReservedStock` management (`ProductCatalogService`).
  * **Web API Host**:
    * `src/ZtpLab03.Api` — Thin presentation controllers (`CartsController`, `OrdersController`, `ProductsController`) and `ZtpLab03.Api.http` test scenarios.
  * **Automated Unit Tests**:
    * `tests/ZtpLab03.UnitTests` — xUnit test suite (`UseCasesTests`) verifying every Use Case directly without starting the web server.

### [2026-11-20] Entry 2 — PostgreSQL Docker Event Store, Interactive Web UI & Automated HTTP Script
* **Changed**:
  1. **Docker & PostgreSQL Persistence (`docker-compose.yml`, `PostgresCartEventStore.cs`, `PostgresOrderStore.cs`)**:
     * Added a `postgres:16-alpine` service in `docker-compose.yml` exposing port `5432` (`Database=ztp_lab03`).
     * Implemented `PostgresCartEventStore` using `Npgsql`. Events are stored as immutable `JSONB` payloads in the `cart_events` table with a database-level `UNIQUE (cart_id, version)` constraint and transaction-scoped PostgreSQL advisory locks (`pg_advisory_xact_lock(hashtext(cart_id))`). This guarantees distributed operational consistency across horizontally scaled API instances.
     * Implemented `PostgresOrderStore` in `ZtpLab03.Orders.Infrastructure` persisting orders and user notifications in independent `orders` and `order_notifications` tables, with automatic graceful fallback to in-memory stores if PostgreSQL is offline.
  2. **Automated JetBrains Rider HTTP Client Script (`src/ZtpLab03.Api/ZtpLab03.Api.http`)**:
     * Added `> {% client.global.set("cartId", response.body.cartId); %}` after Step 2 (`POST /api/carts`) and replaced manual `<CART_ID>` placeholders with `{{cartId}}` across Steps 3–7 so all 9 requests succeed sequentially without manual editing.
  3. **Interactive Web Presentation Dashboard (`src/ZtpLab03.Api/wwwroot/index.html` & `SimulateCartTimeoutCommand`)**:
     * Built a single-page visual dashboard served at `http://localhost:5100` to demonstrate all grading criteria live: Product Service HTTP stock reservations, multi-user isolation, 15-minute lock expiration simulation (`POST /api/carts/{cartId}/simulate-timeout`), parallel multi-device burst operations (`Promise.all`), live Event Sourcing stream inspection, and decoupled Order Domain notifications.
  4. **Polish Defense & Presentation Guide (`PREZENTACJA_OBRONA_PL.md`)**:
     * Created a comprehensive Polish guide for presenting the project structure, architectural decisions, and live demo to the instructor.

---

## 2. How Each Requirement & Architectural Pattern Works

### 2.1. CQRS (Command Query Responsibility Segregation) & Mediator Pattern
* **How it works**:
  * Every operation that **mutates state** is modeled as a **Command** (`ICommand<TResponse>`) handled by a dedicated `ICommandHandler<TCommand, TResponse>` (`CreateCartCommand`, `AddProductToCartCommand`, `RemoveProductFromCartCommand`, `CheckoutCartCommand`, `ExpireInactiveCartsCommand`, `StartOrderProcessingCommand`).
  * Every operation that **reads state** is modeled as a **Query** (`IQuery<TResponse>`) handled by a dedicated `IQueryHandler<TQuery, TResponse>` (`GetCartQuery`, `GetCartEventHistoryQuery`, `GetUserOrdersQuery`, `GetUserNotificationsQuery`).
  * Controllers never inject repositories or domain services directly; they dispatch commands and queries through `IMediator`.

### 2.2. Event Sourcing & Operational Consistency Across Multiple Devices (Grade 3.0 Requirement)
* **Problem**: A single user can add and remove products from their cart simultaneously on multiple devices (e.g., phone and laptop). In a traditional state-based CRUD database, two concurrent requests read the same cart snapshot, modify it in memory, and overwrite each other in the database (*Lost Update* anomaly).
* **How it works in our code (`ShoppingCart.cs` & `InMemoryCartEventStore.cs`)**:
  1. **Append-Only Event Stream**: The cart state is never overwritten. Every action creates an immutable delta event (`CartCreatedEvent`, `ProductAddedToCartEvent`, `ProductRemovedFromCartEvent`, `CartCheckedOutEvent`, `CartExpiredEvent`) appended to the cart's event stream.
  2. **Per-Cart Atomic Serialization (`ExecuteInCartLockAsync`)**: Each `CartId` has its own dedicated `SemaphoreSlim` stream lock. When Device A and Device B send concurrent modifications for the *same* `CartId`, Device B waits for Device A's event to be appended, immediately rehydrates the updated stream containing Device A's event, and appends its own event. Zero operations are lost or skipped.
  3. **User Isolation & Parallelism**: Because locks and streams are partitioned per `CartId`, thousands of different users manage their carts simultaneously in full parallel without blocking each other. Furthermore, every Use Case verifies `cart.IsOwnedBy(userId)` (`X-User-Id` header), returning `403 Forbidden` if another user attempts to access or mutate the cart.
  4. **Horizontal Scalability**: All Use Case handlers and API controllers are completely stateless. Multiple instances of the Cart API can run behind a load balancer backed by a shared distributed Event Store (such as EventStoreDB or PostgreSQL with a unique `(CartId, Version)` optimistic concurrency index).

### 2.3. Communication with Product Service over HTTP (Grade 3.0 Requirement)
* **How it works (`HttpProductServiceClient.cs` & `ProductsController.cs`)**:
  * The Cart domain defines the `IProductServiceClient` port.
  * `HttpProductServiceClient` uses `System.Net.Http.HttpClient` to communicate over HTTP with the Product Service REST endpoints:
    * `GET /api/products/{productId}`
    * `POST /api/products/{productId}/reserve`
    * `POST /api/products/{productId}/release`
    * `POST /api/products/reservations/{cartId}/confirm`

---

## 3. Analysis of Product Locking: Cart Domain vs. Product Domain (Grade +0.5 Requirement)

> **Requirement**: *"Mechanizm blokady produktów może zostać zaimplementowany w domenie koszyka lub domenie produktów – wymagane jest przeprowadzenie analizy zalet i wad każdej opcji oraz uzasadnienie wybranego rozwiązania."*

### Option A: Implementing Product Locking in the Cart Domain
* **Pros**:
  1. **Local Execution**: Checking whether a product is locked and expiring the 15-minute cart lock happen locally within the Cart module without making outgoing HTTP calls on expiration.
  2. **Simpler Network Topology**: Fewer HTTP round-trips between bounded contexts during cart modifications.
* **Cons**:
  1. **Violates Single Source of Truth (SSOT)**: The Cart Domain would have to maintain a replica of total inventory levels, which legitimately belong to the Product/Inventory Domain.
  2. **Cross-Cart Contention on Horizontal Scaling**: To know if the last item of a product is already reserved, a Cart instance would have to query and lock across *all active carts of all users* in a distributed lock table.
  3. **Risk of Overselling**: If stock is modified outside the Cart Domain (e.g., warehouse adjustment or admin order in the Product Service), the Cart Domain's lock state becomes stale.

### Option B: Implementing Product Locking in the Product Domain (Coordinated by Cart 15-Minute Expiration)
* **Pros**:
  1. **Single Source of Truth (SSOT) & Atomic Inventory Control**: The Product Domain owns both `AvailableStock` and `ReservedStock`. When a user adds an item to their cart, the Product Service atomically decrements `AvailableStock` and increments `ReservedStock` in a single thread-safe operation per `ProductId`.
  2. **Seamless Horizontal Scalability**: Any number of stateless Cart API instances can call `POST /api/products/{id}/reserve` over HTTP without cross-cart synchronization.
  3. **High Modularity (SOLID / SRP)**: The Cart Domain only manages the user's shopping session and 15-minute inactivity timer, while the Product Domain guarantees inventory integrity.
* **Cons**:
  1. **Requires HTTP Coordination**: Adding an item, removing an item, or expiring an abandoned cart after 15 minutes requires an HTTP call to `/api/products/{id}/release` to return reserved stock to `AvailableStock`.

### Justification of the Chosen Solution
We selected **Option B (Authoritative Stock Lock in the Product Domain coordinated with the Cart Domain's 15-minute TTL)**. Since the system requires horizontal scalability, strict multi-user concurrency safety, and HTTP communication with the Product Service, placing the atomic stock lock (`AvailableStock` -> `ReservedStock`) in `ProductCatalogService` guarantees that two users can never reserve the last unit concurrently across any number of Cart instances. Meanwhile, `ShoppingCart.ExpiresAtUtc` (`LastModifiedAtUtc + 15 minutes`) and `CartExpirationBackgroundService` (`ExpireInactiveCartsCommandHandler`) ensure that abandoned carts automatically expire after 15 minutes and release their reserved stock over HTTP.

---

## 4. Separation of Cart and Order Domains & User Notification (Grade +0.25 Requirement)

* **Domain Independence**:
  * `ZtpLab03.Cart.*` and `ZtpLab03.Orders.*` have **zero project references** to each other.
  * When `CheckoutCartCommandHandler` completes in the Cart Domain, it publishes `CartCheckedOutIntegrationEvent` via `IMediator`.
  * `CartCheckedOutIntegrationEventHandler` in `ZtpLab03.Orders.Application` catches this event and dispatches `StartOrderProcessingCommand`.
* **Order Processing Notification**:
  * `StartOrderProcessingCommandHandler` creates the `Order` in `OrderStatus.Processing` and invokes `IOrderNotificationService.NotifyOrderProcessingStartedAsync`.
  * Users can verify their active orders via `GET /api/orders` and read their order processing notifications via `GET /api/orders/notifications`.
