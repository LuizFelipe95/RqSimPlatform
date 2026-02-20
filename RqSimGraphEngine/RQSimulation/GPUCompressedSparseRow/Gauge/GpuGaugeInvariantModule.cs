using RQSimulation.Core.Plugins;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.Gauge;

/// <summary>
/// GPU-accelerated gauge invariant checking module.
/// 
/// Validates gauge symmetry through Wilson loop computation and
/// detects topological charges (monopoles) in the field configuration.
/// 
/// PHYSICS (RQ-HYPOTHESIS):
/// - Gauge invariance is fundamental symmetry of field interactions
/// - Wilson loops W = Tr[P exp(i?A·dl)] probe field strength
/// - Topological charge = (1/2?) ? arg(W) counts monopoles
/// - Violations indicate physical or numerical issues
/// 
/// APPLICATIONS:
/// - Validate SU(N) gauge field configurations
/// - Detect topological defects (monopoles, instantons)
/// - Monitor gauge constraint violation during evolution
/// - Compute Berry phases for topological protection
/// </summary>
public sealed class GpuGaugeInvariantModule : PhysicsModuleBase, IDisposable
{
    private GpuGaugeEngine? _engine;
    private CsrTopology? _topology;
    private bool _disposed;
    private int _lastTopologySignature;

    public override string Name => "GPU Gauge Invariant Checker";
    public override string Description => "GPU-accelerated gauge invariance validation using Wilson loops";
    public override string Category => "Gauge";
    public override ExecutionType ExecutionType => ExecutionType.GPU;

    /// <summary>
    /// Runs in PostProcess stage to validate gauge field after physics.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.PostProcess;

    public override int Priority => 80;

    // Configuration

    /// <summary>Whether to refresh triangle detection each step (expensive).</summary>
    public bool RefreshTrianglesEachStep { get; set; } = false;

    /// <summary>Gauge violation threshold for warnings.</summary>
    public double ViolationWarningThreshold { get; set; } = 0.01;

    /// <summary>Sample rate for gauge checking (1 = every step).</summary>
    public int GaugeCheckSampleRate { get; set; } = 1;

    private int _stepCounter;

    // Results

    /// <summary>Total topological charge (Chern number) from last check.</summary>
    public double TotalTopologicalCharge => _engine?.TotalTopologicalCharge ?? 0;

    /// <summary>Mean Wilson loop magnitude (should be ~1).</summary>
    public double MeanWilsonMagnitude => _engine?.MeanWilsonMagnitude ?? 0;

    /// <summary>Maximum gauge violation from last check.</summary>
    public double MaxGaugeViolation => _engine?.MaxGaugeViolation ?? 0;

    /// <summary>Number of triangles (plaquettes) detected.</summary>
    public int TriangleCount => _engine?.TriangleCount ?? 0;

    /// <summary>Whether gauge is valid (violation below threshold).</summary>
    public bool IsGaugeValid => MaxGaugeViolation < ViolationWarningThreshold;

    /// <summary>Access to underlying engine for direct queries.</summary>
    public GpuGaugeEngine? Engine => _engine;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();

        if (graph.CsrTopology == null)
            return;

        _topology = graph.CsrTopology;

        if (!_topology.IsGpuReady)
            return;

        // Get edge phases from graph
        double[,] edgePhases = GetEdgePhases(graph);

        _engine = new GpuGaugeEngine
        {
            RefreshTrianglesEachStep = RefreshTrianglesEachStep
        };

        _engine.Initialize(_topology, edgePhases);
        _lastTopologySignature = graph.TopologySignature;

        // Link to graph
        graph.GpuGaugeEngine = _engine;
        _stepCounter = 0;
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_engine == null || !_engine.IsInitialized)
            return;

        // Check topology change
        if (graph.TopologySignature != _lastTopologySignature)
        {
            Initialize(graph);
            if (_engine == null) return;
        }

        _stepCounter++;

        // Only check gauge at sample rate
        if (_stepCounter % GaugeCheckSampleRate != 0)
            return;

        // Update edge phases and compute invariants
        double[,] edgePhases = GetEdgePhases(graph);
        _engine.Step(edgePhases);

        // Update engine setting
        _engine.RefreshTrianglesEachStep = RefreshTrianglesEachStep;

        // Sync results back to graph
        SyncToGraph(graph);
    }

    private double[,] GetEdgePhases(RQGraph graph)
    {
        // Use graph's edge phase array if available
        if (graph.EdgePhaseU1 != null)
        {
            return graph.EdgePhaseU1;
        }

        // Fallback: return zero phases
        return new double[graph.N, graph.N];
    }

    private void SyncToGraph(RQGraph graph)
    {
        if (_engine == null) return;

        // Store key metrics on graph for visualization
        graph.TopologicalChargeGpu = _engine.TotalTopologicalCharge;
        graph.GaugeViolationGpu = _engine.MaxGaugeViolation;
        graph.TriangleCountGpu = _engine.TriangleCount;
    }

    /// <summary>
    /// Get integer Chern number (rounded topological charge).
    /// </summary>
    public int GetChernNumber()
    {
        return (int)System.Math.Round(TotalTopologicalCharge);
    }

    /// <summary>
    /// Get Wilson loop for a specific triangle.
    /// </summary>
    public (double real, double imag) GetWilsonLoop(int triangleIndex)
    {
        return _engine?.GetWilsonLoop(triangleIndex) ?? (0, 0);
    }

    /// <summary>
    /// Get all triangles as (i, j, k) tuples.
    /// </summary>
    public List<(int i, int j, int k)> GetTriangles()
    {
        return _engine?.GetTriangles() ?? [];
    }

    public override void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _engine = null;
        _disposed = true;
    }
}
