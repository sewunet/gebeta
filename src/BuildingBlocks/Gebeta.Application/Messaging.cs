using Gebeta.SharedKernel;

namespace Gebeta.Application;

/// <summary>A request that changes state. One handler per command (docs/conventions.md).</summary>
public interface ICommand<TResponse>;

/// <summary>A request that only reads.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Saves everything a handler changed in one transaction (one per module).</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Queues an integration event (a record from a <c>Gebeta.&lt;Module&gt;.Contracts</c> namespace). It is
    /// stored in the module's outbox by the next <see cref="SaveChangesAsync"/>, in the same transaction,
    /// and then sent to RabbitMQ.
    /// </summary>
    void Publish(object integrationEvent);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Handles an integration event from another module (or the same one). Must be idempotent.</summary>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
