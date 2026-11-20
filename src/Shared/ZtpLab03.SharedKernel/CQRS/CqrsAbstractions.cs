namespace ZtpLab03.SharedKernel.CQRS;

/// <summary>
/// Marker interface for a CQRS Command that modifies application state and returns a response.
/// According to the CQRS pattern, every state-mutating operation (create cart, add item,
/// remove item, checkout, expire) is modeled as a Command.
/// </summary>
/// <typeparam name="TResponse">The type of result returned by the command handler.</typeparam>
public interface ICommand<out TResponse>
{
}

/// <summary>
/// Defines a handler for a specific CQRS Command (Use Case).
/// Each Use Case is isolated in its own handler class and can be unit-tested without starting the web server.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for a CQRS Query that reads state without modifying it.
/// According to the CQRS pattern, read operations (such as viewing cart contents and total price)
/// are strictly separated from state-modifying commands.
/// </summary>
/// <typeparam name="TResponse">The read-model DTO returned by the query handler.</typeparam>
public interface IQuery<out TResponse>
{
}

/// <summary>
/// Defines a handler for a specific CQRS Query (Read Use Case).
/// </summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
