namespace RQSimulation.Core.Plugins;

/// <summary>
/// Specifies how a physics module executes during simulation.
/// </summary>
public enum ExecutionType
{
    /// <summary>
    /// Executes synchronously on the CPU thread.
    /// Suitable for lightweight modules or those requiring strict ordering.
    /// </summary>
    SynchronousCPU,

    /// <summary>
    /// Executes asynchronously as a Task.
    /// Suitable for I/O-bound or parallel CPU work.
    /// </summary>
    AsynchronousTask,

    /// <summary>
    /// Executes on GPU via ComputeSharp.
    /// Suitable for heavy parallel computations.
    /// </summary>
    GPU
}
