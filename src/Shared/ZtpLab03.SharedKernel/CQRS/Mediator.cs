using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ZtpLab03.SharedKernel.CQRS;

/// <summary>
/// Mediator pattern contract that decouples senders (Controllers, Background Services)
/// from receivers (Use Case Command/Query Handlers and Integration Event Handlers).
/// Reference: https://refactoring.guru/pl/design-patterns/mediator
/// </summary>
public interface IMediator
{
    Task<TResponse> SendCommandAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResponse>;

    Task<TResponse> SendQueryAsync<TQuery, TResponse>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>;

    Task PublishEventAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}

/// <summary>
/// Default DI-backed implementation of the Mediator pattern.
/// Dispatches Commands, Queries, and Integration Events to their registered handlers.
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> SendCommandAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(command);
        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();
        return handler.HandleAsync(command, cancellationToken);
    }

    public Task<TResponse> SendQueryAsync<TQuery, TResponse>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(query);
        var handler = _serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();
        return handler.HandleAsync(query, cancellationToken);
    }

    public async Task PublishEventAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var handlers = _serviceProvider.GetServices<IIntegrationEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(integrationEvent, cancellationToken);
        }
    }
}

/// <summary>
/// Extension methods to automatically scan and register all CQRS Command, Query,
/// and Integration Event handlers from an assembly, adhering to the DRY principle.
/// </summary>
public static class MediatorRegistrationExtensions
{
    public static IServiceCollection AddSharedMediator(this IServiceCollection services)
    {
        services.AddScoped<IMediator, Mediator>();
        return services;
    }

    public static IServiceCollection AddCqrsHandlersFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        var handlerInterfaces = new[]
        {
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
            typeof(IIntegrationEventHandler<>)
        };

        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var implementationType in types)
        {
            foreach (var implementedInterface in implementationType.GetInterfaces())
            {
                if (!implementedInterface.IsGenericType)
                {
                    continue;
                }

                var genericDefinition = implementedInterface.GetGenericTypeDefinition();
                if (handlerInterfaces.Contains(genericDefinition))
                {
                    services.AddScoped(implementedInterface, implementationType);
                }
            }
        }

        return services;
    }
}
