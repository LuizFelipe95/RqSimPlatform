using RQSimulation.Core.Plugins;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.Causal;

/// <summary>
/// GPU-accelerated causal structure discovery module.
/// 
/// Provides parallel BFS-based causal cone computation using CSR topology.
/// This enables fast causality checks and light cone calculations.
/// 
/// PHYSICS (RQ-HYPOTHESIS):
/// - Causality emerges from graph connectivity
/// - Light cone = all reachable nodes within proper time
/// - Speed of light = 1 edge per time unit (configurable)
/// - Causal structure determines what can influence what
/// 
/// HYBRID PATTERN:
/// - GPU: Computes causal cones (read-only topology)
/// - CPU: Uses results for physics decisions
/// </summary>
public sealed class GpuCausalDiscoveryModule : PhysicsModuleBase, IDisposable
{
    private GpuCausalEngine? _engine;
    private CsrTopology? _topology;
    private bool _disposed;
    private int _lastTopologySignature;

    public override string Name => "GPU Causal Discovery";
    public override string Description => "GPU-accelerated causal structure discovery using parallel BFS on CSR topology";
    public override string Category => "Topology";
    public override ExecutionType ExecutionType => ExecutionType.GPU;

    /// <summary>
    /// Runs in Preparation stage to compute causal structure before physics.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.Preparation;

    public override int Priority => 20;

    // Configuration

    /// <summary>Maximum BFS depth for causal cone computation.</summary>
    public int MaxCausalDepth { get; set; } = 10;

    /// <summary>Speed of light in edges per time unit.</summary>
    public double SpeedOfLight { get; set; } = 1.0;

    /// <summary>Whether to compute causal cones each step.</summary>
    public bool ComputeConesEachStep { get; set; } = false;

    /// <summary>Sample rate for causal cone computation (1 = every step).</summary>
    public int ConeComputeSampleRate { get; set; } = 10;

    private int _stepCounter;

    // State accessors

    /// <summary>Access to underlying engine for direct queries.</summary>
    public GpuCausalEngine? Engine => _engine;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();

        if (graph.CsrTopology == null)
            return;

        _topology = graph.CsrTopology;

        if (!_topology.IsGpuReady)
            return;

        _engine = new GpuCausalEngine
        {
            MaxDepth = MaxCausalDepth
        };

        _engine.Initialize(_topology);
        _lastTopologySignature = graph.TopologySignature;

        // Link to graph
        graph.GpuCausalEngine = _engine;
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

        // Optional: compute sample causal cones periodically
        if (ComputeConesEachStep && (_stepCounter % ConeComputeSampleRate == 0))
        {
            // Compute causal cone from a sample node (e.g., first active node)
            // This is for diagnostics/visualization
            int sampleNode = FindSampleNode(graph);
            if (sampleNode >= 0)
            {
                int maxDepthFromDt = (int)(SpeedOfLight * dt * 10); // 10 time units lookahead
                _engine.ComputeCausalCone(sampleNode, System.Math.Min(maxDepthFromDt, MaxCausalDepth));
            }
        }
    }

    private int FindSampleNode(RQGraph graph)
    {
        // Find a node with activity (e.g., high energy or excited state)
        for (int i = 0; i < graph.N; i++)
        {
            if (graph.State[i] != NodeState.Rest)
                return i;
        }
        // Fallback: first node
        return graph.N > 0 ? 0 : -1;
    }

    /// <summary>
    /// Compute causal cone from a specific node.
    /// </summary>
    public int ComputeCausalCone(int sourceNode, int maxDepth)
    {
        return _engine?.ComputeCausalCone(sourceNode, maxDepth) ?? 0;
    }

    /// <summary>
    /// Check if two nodes are causally connected.
    /// </summary>
    public bool AreCausallyConnected(int nodeA, int nodeB, double dt)
    {
        if (_engine == null) return false;
        int maxHops = (int)(SpeedOfLight * dt);
        return _engine.AreCausallyConnected(nodeA, nodeB, maxHops);
    }

    /// <summary>
    /// Get causal future of a node within time dt.
    /// </summary>
    public List<int> GetCausalFuture(int node, double dt)
    {
        if (_engine == null) return [];
        int maxHops = (int)(SpeedOfLight * dt);
        _engine.ComputeCausalCone(node, maxHops);
        return _engine.GetCausalConeNodes();
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
