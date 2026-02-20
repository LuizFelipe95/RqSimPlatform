namespace RQSimulation.Core.Plugins;

/// <summary>
/// Adapter class that wraps Action/Func delegates into IPhysicsModule.
/// Allows registering physics logic without creating dedicated classes.
/// 
/// Supports dynamic physics parameters via IDynamicPhysicsModule interface.
/// 
/// Usage example:
/// <code>
/// var module = new FunctionalPhysicsModule(
///     "Custom Gravity",
///     init: g => g.InitNetworkGravity(),
///     step: (g, dt) => g.EvolveNetworkGravity(dt),
///     category: "Gravity",
///     stage: ExecutionStage.Forces
/// );
/// pipeline.RegisterModule(module);
/// 
/// // With dynamic parameters:
/// var paramModule = new FunctionalPhysicsModule(
///     "Adaptive Gravity",
///     step: (g, dt) => g.EvolveNetworkGravity(dt),
///     onParamsUpdate: (ref DynamicPhysicsParams p) => 
///         _localCoupling = p.GravitationalCoupling
/// );
/// </code>
/// </summary>
public class FunctionalPhysicsModule : IPhysicsModule, IDynamicPhysicsModule
{
    private readonly Action<RQGraph>? _initAction;
    private readonly Action<RQGraph, double>? _stepAction;
    private readonly Action? _cleanupAction;
    
    /// <summary>
    /// Delegate for handling parameter updates.
    /// </summary>
    public delegate void ParamsUpdateHandler(in DynamicPhysicsParams parameters);
    
    private readonly ParamsUpdateHandler? _onParamsUpdate;
    
    /// <summary>
    /// Current physics parameters, updated each frame before ExecuteStep.
    /// </summary>
    protected DynamicPhysicsParams CurrentParams;

    public string Name { get; }
    public string Description { get; }
    public bool IsEnabled { get; set; } = true;
    public ExecutionType ExecutionType { get; }
    
    /// <summary>
    /// Execution stage within the pipeline.
    /// </summary>
    public ExecutionStage Stage { get; }
    
    public int Priority { get; }
    public string Category { get; }

    /// <summary>
    /// Creates a functional physics module with lambda callbacks.
    /// </summary>
    /// <param name="name">Display name</param>
    /// <param name="init">Called once at simulation start (optional)</param>
    /// <param name="step">Called each simulation step (optional)</param>
    /// <param name="cleanup">Called when simulation stops (optional)</param>
    /// <param name="executionType">How the module executes</param>
    /// <param name="stage">Execution stage (Preparation, Forces, Integration, PostProcess)</param>
    /// <param name="priority">Execution order within stage (lower = earlier)</param>
    /// <param name="category">UI grouping category</param>
    /// <param name="description">Tooltip description</param>
    /// <param name="onParamsUpdate">Called when physics parameters change (optional)</param>
    public FunctionalPhysicsModule(
        string name,
        Action<RQGraph>? init = null,
        Action<RQGraph, double>? step = null,
        Action? cleanup = null,
        ExecutionType executionType = ExecutionType.SynchronousCPU,
        ExecutionStage stage = ExecutionStage.Forces,
        int priority = 100,
        string category = "General",
        string description = "",
        ParamsUpdateHandler? onParamsUpdate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = string.IsNullOrEmpty(description) ? name : description;
        _initAction = init;
        _stepAction = step;
        _cleanupAction = cleanup;
        _onParamsUpdate = onParamsUpdate;
        ExecutionType = executionType;
        Stage = stage;
        Priority = priority;
        Category = category;
        CurrentParams = DynamicPhysicsParams.Default;
    }

    public void Initialize(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _initAction?.Invoke(graph);
    }

    public void ExecuteStep(RQGraph graph, double dt)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _stepAction?.Invoke(graph, dt);
    }

    public void Cleanup()
    {
        _cleanupAction?.Invoke();
    }
    
    /// <summary>
    /// Updates physics parameters for this frame.
    /// Called by PhysicsPipeline before ExecuteStep.
    /// </summary>
    public void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        CurrentParams = parameters;
        _onParamsUpdate?.Invoke(in parameters);
    }
}

/// <summary>
/// Base class for physics modules providing common functionality.
/// Extend this instead of implementing IPhysicsModule directly.
/// 
/// Supports dynamic physics parameters via IDynamicPhysicsModule interface.
/// Override UpdateParameters to react to parameter changes.
/// </summary>
public abstract class PhysicsModuleBase : IPhysicsModule, IDynamicPhysicsModule
{
    /// <summary>
    /// Current physics parameters, updated each frame before ExecuteStep.
    /// Access in ExecuteStep to use dynamic values from UI.
    /// </summary>
    protected DynamicPhysicsParams CurrentParams = DynamicPhysicsParams.Default;
    
    public abstract string Name { get; }
    public virtual string Description => Name;
    public bool IsEnabled { get; set; } = true;
    public virtual ExecutionType ExecutionType => ExecutionType.SynchronousCPU;
    
    /// <summary>
    /// Execution stage within the pipeline.
    /// Override to specify when this module runs relative to others.
    /// Default is Forces (main physics computation stage).
    /// </summary>
    public virtual ExecutionStage Stage => ExecutionStage.Forces;
    
    public virtual int Priority => 100;
    public virtual string Category => "General";

    public abstract void Initialize(RQGraph graph);
    public abstract void ExecuteStep(RQGraph graph, double dt);
    public virtual void Cleanup() { }
    
    /// <summary>
    /// Updates physics parameters for this frame.
    /// Override to react to parameter changes (e.g., reconfigure internal state).
    /// Base implementation stores parameters in CurrentParams.
    /// </summary>
    public virtual void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        CurrentParams = parameters;
    }
}
