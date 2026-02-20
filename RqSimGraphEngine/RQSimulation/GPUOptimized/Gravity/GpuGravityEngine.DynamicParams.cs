using RQSimulation.Core.Plugins;

namespace RQSimulation.GPUOptimized;

/// <summary>
/// Extension of GpuGravityEngine that supports dynamic physics parameters.
/// 
/// HARD SCIENCE AUDIT v3.1:
/// - Properly recreates kernels when parameters change
/// - Integrated SimulationHealthMonitor for NaN/Inf detection
/// - Scientific mode enables strict validation
/// </summary>
public partial class GpuGravityEngine : IDynamicPhysicsModule
{
    /// <summary>
    /// Current frame's physics parameters.
    /// </summary>
    private DynamicPhysicsParams _params = DynamicPhysicsParams.Default;
    
    /// <summary>
    /// Parameter hash validator for detecting changes.
    /// </summary>
    private readonly ParameterHashValidator _paramValidator = new();
    
    /// <summary>
    /// Snapshot for logging parameter changes.
    /// </summary>
    private ParameterSnapshot _lastSnapshot;
    
    /// <summary>
    /// HARD SCIENCE AUDIT: Health monitor for NaN/Inf detection.
    /// Created lazily when scientific mode is enabled.
    /// </summary>
    private SimulationHealthMonitor? _healthMonitor;
    
    /// <summary>
    /// Last health check result (for UI display).
    /// </summary>
    public HealthCheckResult LastHealthCheck { get; private set; }
    
    /// <summary>
    /// True if simulation is healthy (no NaN/Inf detected).
    /// </summary>
    public bool IsSimulationHealthy => LastHealthCheck.IsHealthy;
    
    /// <summary>
    /// True if simulation has diverged (NaN detected).
    /// </summary>
    public bool HasDiverged => LastHealthCheck.NaNCount > 0;
    
    /// <summary>
    /// Updates parameters from pipeline.
    /// Called before ExecuteStep each frame.
    /// </summary>
    public void UpdateParameters(in DynamicPhysicsParams parameters)
    {
        // Track if parameters changed for logging
        bool gravityChanged = _paramValidator.HasGravityParamsChanged(in parameters);
        bool curvatureChanged = _paramValidator.HasCurvatureParamsChanged(in parameters);
        
        if (gravityChanged || curvatureChanged)
        {
            var oldSnapshot = _lastSnapshot;
            _lastSnapshot = parameters.CreateSnapshot();
        }
        
        // Initialize health monitor in scientific mode
        if (parameters.ScientificMode && _healthMonitor == null && _device != null)
        {
            _healthMonitor = new SimulationHealthMonitor(_device);
        }
        
        _params = parameters;
        _paramValidator.UpdateAllHashes(in parameters);
    }
    
    /// <summary>
    /// Executes gravity evolution using current dynamic parameters.
    /// Creates NEW shader instance each frame to reflect parameter changes.
    /// </summary>
    public void EvolveWithDynamicParams(
        float[] hostWeights,
        float[] hostMasses,
        int[] hostEdgesFrom,
        int[] hostEdgesTo)
    {
        // Extract parameters from current frame
        float dt = (float)_params.DeltaTime;
        float G = (float)_params.GravitationalCoupling;
        float lambda = (float)_params.CosmologicalConstant;

        // Call method that creates fresh shader with current params
        if (_topologyInitialized)
        {
            EvolveFullGpuStep(
                hostWeights,
                hostMasses,
                hostEdgesFrom,
                hostEdgesTo,
                dt,
                G,
                lambda);
            
            // Health check after physics step (scientific mode only)
            if (_params.ScientificMode)
            {
                PerformHealthCheck(hostWeights);
            }
        }
    }
    
    /// <summary>
    /// Performs health check on simulation state using host data.
    /// Detects NaN/Inf values that indicate simulation divergence.
    /// </summary>
    private void PerformHealthCheck(float[] hostWeights)
    {
        try
        {
            // CPU-side NaN/Inf check (faster than GPU roundtrip for small arrays)
            int nanCount = 0;
            int infCount = 0;
            int firstNaN = -1;
            
            for (int i = 0; i < hostWeights.Length; i++)
            {
                float val = hostWeights[i];
                if (float.IsNaN(val))
                {
                    nanCount++;
                    if (firstNaN < 0) firstNaN = i;
                }
                else if (float.IsInfinity(val))
                {
                    infCount++;
                }
            }
            
            LastHealthCheck = new HealthCheckResult
            {
                NaNCount = nanCount,
                InfCount = infCount,
                FirstNaNIndex = firstNaN,
                NegativeCount = 0,
                IsHealthy = nanCount == 0 && infCount == 0,
                SingularityDetected = nanCount > 0,
                Timestamp = DateTime.UtcNow
            };
            
            if (!LastHealthCheck.IsHealthy)
            {
                Console.WriteLine($"[GpuGravityEngine] HEALTH WARNING: NaN={nanCount}, Inf={infCount}");
            }
        }
        catch
        {
            LastHealthCheck = new HealthCheckResult { NaNCount = -1 };
        }
    }
    
    /// <summary>
    /// Executes single step for parameter injection test.
    /// </summary>
    public double EvolveWithOverrideG(
        float[] hostWeights,
        float[] hostMasses,
        int[] hostEdgesFrom,
        int[] hostEdgesTo,
        double overrideG)
    {
        double initialTotalWeight = hostWeights.Sum();
        
        var testParams = _params;
        testParams.GravitationalCoupling = overrideG;
        
        UpdateParameters(in testParams);
        EvolveWithDynamicParams(hostWeights, hostMasses, hostEdgesFrom, hostEdgesTo);
        
        double finalTotalWeight = hostWeights.Sum();
        return finalTotalWeight - initialTotalWeight;
    }
}
