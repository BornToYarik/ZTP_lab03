using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZtpLab03.Orders.Application.UseCases.StartOrderProcessing;
using ZtpLab03.Orders.Domain.Ports;
using ZtpLab03.Orders.Infrastructure.Notifications;
using ZtpLab03.Orders.Infrastructure.Repositories;
using ZtpLab03.SharedKernel.CQRS;

namespace ZtpLab03.Orders.Infrastructure;

/// <summary>
/// Registers the independent Order Domain module into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOrdersModule(
        this IServiceCollection services,
        string? postgresConnectionString = null)
    {
        if (!string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            services.AddSingleton<PostgresOrderStore>(sp =>
                new PostgresOrderStore(
                    postgresConnectionString,
                    sp.GetRequiredService<ILogger<PostgresOrderStore>>(),
                    sp.GetService<TimeProvider>()));

            services.AddSingleton<IOrderRepository>(sp => sp.GetRequiredService<PostgresOrderStore>());
            services.AddSingleton<IOrderNotificationService>(sp => sp.GetRequiredService<PostgresOrderStore>());
        }
        else
        {
            services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
            services.AddSingleton<IOrderNotificationService, InMemoryOrderNotificationService>();
        }

        // Register all CQRS Command, Query, and IntegrationEvent handlers from Orders.Application
        services.AddCqrsHandlersFromAssembly(typeof(StartOrderProcessingCommand).Assembly);

        return services;
    }
}
