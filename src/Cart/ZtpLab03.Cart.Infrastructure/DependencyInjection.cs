using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZtpLab03.Cart.Application.UseCases.CreateCart;
using ZtpLab03.Cart.Domain.Ports;
using ZtpLab03.Cart.Infrastructure.BackgroundServices;
using ZtpLab03.Cart.Infrastructure.EventStore;
using ZtpLab03.Cart.Infrastructure.Http;
using ZtpLab03.SharedKernel.CQRS;

namespace ZtpLab03.Cart.Infrastructure;

/// <summary>
/// Registers the Cart module's Clean Architecture layers (Domain ports, Application Use Case handlers,
/// and Infrastructure adapters) into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCartModule(
        this IServiceCollection services,
        string productServiceBaseUrl,
        string? postgresConnectionString = null)
    {
        services.AddSingleton(TimeProvider.System);

        if (!string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            services.AddSingleton<ICartEventStore>(sp =>
                new PostgresCartEventStore(
                    postgresConnectionString,
                    sp.GetRequiredService<ILogger<PostgresCartEventStore>>()));
        }
        else
        {
            services.AddSingleton<ICartEventStore, InMemoryCartEventStore>();
        }

        // Register all CQRS Command and Query handlers from the Cart.Application assembly
        services.AddCqrsHandlersFromAssembly(typeof(CreateCartCommand).Assembly);

        // Register HTTP client for communication with the Product Service over HTTP protocol
        services.AddHttpClient<IProductServiceClient, HttpProductServiceClient>(client =>
        {
            client.BaseAddress = new Uri(productServiceBaseUrl);
        });

        // Register background service for 15-minute cart & product lock expiration
        services.AddHostedService<CartExpirationBackgroundService>();

        return services;
    }
}
