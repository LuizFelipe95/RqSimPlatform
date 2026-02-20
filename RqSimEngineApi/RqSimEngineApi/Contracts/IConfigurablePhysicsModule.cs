namespace RqSimEngineApi.Contracts;

/// <summary>
/// Extended interface for physics modules that support dynamic configuration.
/// 
/// Modules implementing this interface can receive updated physics parameters
/// each frame from the UI without requiring simulation restart.
/// 
/// MIGRATION PATH:
/// ===============
/// 1. Existing modules continue using IPhysicsModule unchanged
/// 2. New modules or refactored modules implement IConfigurablePhysicsModule
/// 3. Pipeline checks for this interface and passes parameters automatically
/// 
/// Example implementation:
/// <code>
/// public class GravityModule : IConfigurablePhysicsModule
/// {
///     private SimulationParameters _params;
///     
///     public void UpdateParameters(in SimulationParameters p) => _params = p;
///     
///     public void ExecuteStep(...)
///     {
///         double alpha = _params.RicciFlowAlpha; // From UI!
///         // ... use dynamic parameters
///     }
/// }
/// </code>
/// </summary>
public interface IConfigurablePhysicsModule
{
    /// <summary>
    /// Called before each ExecuteStep with current frame's parameters.
    /// 
    /// IMPORTANT:
    /// - This is called EVERY frame, so keep it lightweight
    /// - Store the parameters in a field for use in ExecuteStep
    /// - Do NOT allocate memory here
    /// </summary>
    /// <param name="parameters">Current physics parameters from UI</param>
    void UpdateParameters(in SimulationParameters parameters);
}

/// <summary>
/// Marker interface for modules that can hot-reload specific parameters.
/// Use when only a subset of parameters affects the module.
/// </summary>
public interface IHotReloadableModule
{
    /// <summary>
    /// Parameters this module cares about.
    /// Used for optimization - only notify when relevant params change.
    /// </summary>
    IReadOnlyList<string> WatchedParameters { get; }
    
    /// <summary>
    /// Called when watched parameters change.
    /// </summary>
    /// <param name="changedParams">Names of changed parameters</param>
    /// <param name="newValues">New parameter values</param>
    void OnParametersChanged(IReadOnlyList<string> changedParams, in SimulationParameters newValues);
}

/// <summary>
/// Context for module execution with full parameter access.
/// 
/// This is the next evolution of SimulationContext - includes both
/// graph state AND dynamic parameters from UI.
/// </summary>
public ref struct ModuleExecutionContext
{
    /// <summary>
    /// Current simulation time.
    /// </summary>
    public double Time;
    
    /// <summary>
    /// Time step for this frame.
    /// </summary>
    public double DeltaTime;
    
    /// <summary>
    /// Frame counter.
    /// </summary>
    public long TickId;
    
    /// <summary>
    /// Number of nodes in graph.
    /// </summary>
    public int NodeCount;
    
    /// <summary>
    /// Number of edges in graph.
    /// </summary>
    public int EdgeCount;
    
    /// <summary>
    /// Full physics parameters from UI.
    /// Contains all configurable values (G, ?, ?, etc.)
    /// </summary>
    public SimulationParameters Params;
    
    /// <summary>
    /// Creates context from simulation context.
    /// </summary>
    public static ModuleExecutionContext FromSimulationContext(in SimulationContext ctx)
    {
        return new ModuleExecutionContext
        {
            Time = ctx.Time,
            DeltaTime = ctx.DeltaTime,
            TickId = ctx.TickId,
            NodeCount = ctx.NodeCount,
            EdgeCount = ctx.EdgeCount,
            Params = ctx.Params
        };
    }
}
