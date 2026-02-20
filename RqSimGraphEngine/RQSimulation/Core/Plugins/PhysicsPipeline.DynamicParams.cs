namespace RQSimulation.Core.Plugins;

/// <summary>
/// PhysicsPipeline extension for dynamic physics configuration.
/// 
/// This partial class adds support for passing DynamicPhysicsParams
/// to physics modules each frame.
/// 
/// INTEGRATION WITH UI:
/// - UI layer (RqSimEngineApi) has SimulationParameters
/// - At pipeline entry, convert SimulationParameters ? DynamicPhysicsParams
/// - Pipeline and modules use DynamicPhysicsParams internally
/// </summary>
public partial class PhysicsPipeline
{
    /// <summary>
    /// Current physics parameters for this frame.
    /// </summary>
    private DynamicPhysicsParams _currentParams = DynamicPhysicsParams.Default;
    
    /// <summary>
    /// Lock for thread-safe parameter updates.
    /// </summary>
    private readonly object _paramsLock = new();
    
    /// <summary>
    /// Gets the current physics parameters.
    /// </summary>
    public DynamicPhysicsParams CurrentParameters
    {
        get
        {
            lock (_paramsLock)
            {
                return _currentParams;
            }
        }
    }

    /// <summary>
    /// Updates physics parameters directly.
    /// Thread-safe: can be called from UI thread.
    /// </summary>
    /// <param name="parameters">New physics parameters</param>
    public void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        lock (_paramsLock)
        {
            _currentParams = parameters;
        }
    }

    /// <summary>
    /// Executes all enabled modules using current parameters.
    /// Call UpdateParameters() first if you need to change settings.
    /// </summary>
    /// <param name="graph">Graph to process</param>
    /// <param name="dt">Time step</param>
    public void ExecuteFrameWithParams(RQGraph graph, double dt)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Get current params (thread-safe copy)
        DynamicPhysicsParams currentParams;
        lock (_paramsLock)
        {
            currentParams = _currentParams;
        }
        
        // Update dt from params if needed
        double effectiveDt = currentParams.DeltaTime > 0 ? currentParams.DeltaTime : dt;

        // Update configurable modules with current parameters
        foreach (var module in _modules.Where(m => m.IsEnabled))
        {
            if (module is IDynamicPhysicsModule configurable)
            {
                try
                {
                    configurable.UpdateParameters(in currentParams);
                }
                catch (Exception ex)
                {
                    RaiseError(module, ex, "UpdateParameters");
                }
            }
        }

        // Execute standard frame logic
        ExecuteFrame(graph, effectiveDt);
    }

    /// <summary>
    /// Applies a preset configuration.
    /// </summary>
    /// <param name="preset">Preset name: "Default", "FastPreview", or "Scientific"</param>
    public void ApplyPreset(string preset)
    {
        var parameters = preset.ToLowerInvariant() switch
        {
            "default" => DynamicPhysicsParams.Default,
            "fastpreview" or "fast" => CreateFastPreviewParams(),
            "scientific" or "science" => CreateScientificParams(),
            _ => DynamicPhysicsParams.Default
        };
        
        UpdateParameters(in parameters);
        RaiseLog($"Applied physics preset: {preset}");
    }
    
    private static DynamicPhysicsParams CreateFastPreviewParams()
    {
        var p = DynamicPhysicsParams.Default;
        p.DeltaTime = 0.02;
        p.RicciFlowAlpha = 0.3;
        p.SinkhornIterations = 20;
        p.SinkhornEpsilon = 0.1;
        p.ConvergenceThreshold = 1e-4;
        p.McmcBeta = 0.5;
        p.McmcStepsPerCall = 5;
        return p;
    }

    private static DynamicPhysicsParams CreateScientificParams()
    {
        var p = DynamicPhysicsParams.Default;
        p.DeltaTime = 0.001;
        p.SinkhornIterations = 100;
        p.SinkhornEpsilon = 0.001;
        p.ConvergenceThreshold = 1e-8;
        p.ScientificMode = true;
        p.McmcBeta = 2.0;
        p.McmcStepsPerCall = 50;
        return p;
    }
}
