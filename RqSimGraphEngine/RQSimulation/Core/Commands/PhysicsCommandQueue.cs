using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RQSimulation.Core.Commands;

/// <summary>
/// Manages a queue of physics commands and executes them in order.
/// Provides better traceability and control over physics operation execution.
/// </summary>
public class PhysicsCommandQueue
{
    private readonly ConcurrentQueue<IPhysicsCommand> _commands = new();
    private readonly ILogger<PhysicsCommandQueue> _logger;
    private int _executedCommandCount;

    public PhysicsCommandQueue(ILogger<PhysicsCommandQueue>? logger = null)
    {
        _logger = logger ?? NullLogger<PhysicsCommandQueue>.Instance;
    }

    /// <summary>
    /// Number of commands currently in the queue.
    /// </summary>
    public int QueuedCount => _commands.Count;

    /// <summary>
    /// Total number of commands executed since creation.
    /// </summary>
    public int ExecutedCount => _executedCommandCount;

    /// <summary>
    /// Enqueue a command for execution.
    /// </summary>
    /// <param name="command">The command to enqueue</param>
    public void Enqueue(IPhysicsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Enqueue(command);
        _logger.LogTrace("Enqueued command: {CommandDescription}", command.Description);
    }

    /// <summary>
    /// Enqueue multiple commands for execution.
    /// </summary>
    /// <param name="commands">The commands to enqueue</param>
    public void EnqueueRange(IEnumerable<IPhysicsCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var command in commands)
        {
            Enqueue(command);
        }
    }

    /// <summary>
    /// Try to dequeue and execute the next command.
    /// </summary>
    /// <returns>True if a command was executed, false if queue was empty</returns>
    public bool TryExecuteNext()
    {
        if (_commands.TryDequeue(out var command))
        {
            _logger.LogDebug("Executing command: {CommandDescription}", command.Description);
            try
            {
                command.Execute();
                Interlocked.Increment(ref _executedCommandCount);
                _logger.LogTrace("Completed command: {CommandDescription}", command.Description);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command execution failed: {CommandDescription}", command.Description);
                throw;
            }
        }
        return false;
    }

    /// <summary>
    /// Try to dequeue and execute the next command asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if a command was executed, false if queue was empty</returns>
    public async Task<bool> TryExecuteNextAsync(CancellationToken ct = default)
    {
        if (_commands.TryDequeue(out var command))
        {
            _logger.LogDebug("Executing command async: {CommandDescription}", command.Description);
            try
            {
                await command.ExecuteAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref _executedCommandCount);
                _logger.LogTrace("Completed command: {CommandDescription}", command.Description);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Async command execution failed: {CommandDescription}", command.Description);
                throw;
            }
        }
        return false;
    }

    /// <summary>
    /// Execute all queued commands synchronously.
    /// </summary>
    public void ExecuteAll()
    {
        _logger.LogDebug("Executing all {Count} queued commands", _commands.Count);
        while (TryExecuteNext())
        {
            // Continue until queue is empty
        }
    }

    /// <summary>
    /// Execute all queued commands asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    public async Task ExecuteAllAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Executing all {Count} queued commands async", _commands.Count);
        while (await TryExecuteNextAsync(ct).ConfigureAwait(false))
        {
            // Continue until queue is empty
        }
    }

    /// <summary>
    /// Clear all queued commands without executing them.
    /// </summary>
    public void Clear()
    {
        var count = _commands.Count;
        _commands.Clear();
        _logger.LogDebug("Cleared {Count} queued commands", count);
    }
}
