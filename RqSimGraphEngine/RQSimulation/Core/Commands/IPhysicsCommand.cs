using RQSimulation.Core.Plugins;

namespace RQSimulation.Core.Commands;

/// <summary>
/// Command pattern interface for physics module operations.
/// Encapsulates a physics operation as an object for better traceability,
/// queuing, and execution control.
/// </summary>
public interface IPhysicsCommand
{
    /// <summary>
    /// The module this command operates on.
    /// </summary>
    IPhysicsModule Module { get; }

    /// <summary>
    /// Description of the command for logging and debugging.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the command synchronously.
    /// </summary>
    void Execute();

    /// <summary>
    /// Execute the command asynchronously with cancellation support.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task that completes when execution finishes</returns>
    Task ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Base class for physics commands providing common functionality.
/// </summary>
public abstract class PhysicsCommandBase : IPhysicsCommand
{
    protected PhysicsCommandBase(IPhysicsModule module, string description)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public IPhysicsModule Module { get; }
    public string Description { get; }

    public abstract void Execute();

    public virtual Task ExecuteAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Execute();
        }, ct);
    }

    public override string ToString() => $"{Description} [{Module.Name}]";
}
