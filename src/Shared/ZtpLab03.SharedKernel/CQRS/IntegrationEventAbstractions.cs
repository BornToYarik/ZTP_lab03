namespace ZtpLab03.SharedKernel.CQRS;

/// <summary>
/// Marker interface for cross-domain integration events.
/// Used to decouple independent bounded contexts (e.g., Cart Domain and Order Domain)
/// so they can evolve and be maintained independently.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredAtUtc { get; }
}

/// <summary>
/// Defines an asynchronous handler for an integration event in a decoupled domain module.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
