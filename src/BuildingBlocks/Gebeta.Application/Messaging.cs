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
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
