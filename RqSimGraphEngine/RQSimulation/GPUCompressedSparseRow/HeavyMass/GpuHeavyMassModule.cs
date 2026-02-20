using RQSimulation.Core.Plugins;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.HeavyMass;

/// <summary>
/// GPU-accelerated heavy mass analysis module.
/// 
/// Analyzes cluster mass distribution and geometry inertia to identify
/// stable "heavy" structures in the graph (analog of baryonic matter).
/// 
/// PHYSICS (RQ-HYPOTHESIS):
/// - Correlation mass = sum of edge weights (connectivity)
/// - Heavy clusters resist topology changes (high inertia)
/// - Stable structures emerge from high correlation mass
/// - Mass conservation tracked through evolution
/// 
/// APPLICATIONS:
/// - Identify particle-like stable structures
/// - Track mass distribution over time
/// - Detect cluster formation and dissolution
/// - Monitor energy/mass conservation
/// </summary>
public sealed class GpuHeavyMassModule : PhysicsModuleBase, IDisposable
{
    private GpuHeavyMassEngine? _engine;
    private CsrTopology? _topology;
    private bool _disposed;
    private int _lastTopologySignature;

    public override string Name => "GPU Heavy Mass Analyzer";
    public override string Description => "GPU-accelerated heavy cluster detection and mass analysis";
    public override string Category => "Topology";
    public override ExecutionType ExecutionType => ExecutionType.GPU;
    public override ExecutionStage Stage => ExecutionStage.PostProcess;
    public override int Priority => 90;

    // Configuration
    public double HeavyMassThreshold { get; set; } = 1.0;
    public int MassComputeSampleRate { get; set; } = 1;

    private int _stepCounter;

    // Results
    public double TotalCorrelationMass => _engine?.TotalCorrelationMass ?? 0;
    public double MeanCorrelationMass => _engine?.MeanCorrelationMass ?? 0;
    public double MaxCorrelationMass => _engine?.MaxCorrelationMass ?? 0;
    public int HeavyNodeCount => _engine?.HeavyNodeCount ?? 0;
    public double TotalInertia => _engine?.TotalInertia ?? 0;
    public GpuHeavyMassEngine? Engine => _engine;

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();

        if (graph.CsrTopology == null)
            return;

        _topology = graph.CsrTopology;

        if (!_topology.IsGpuReady)
            return;

        _engine = new GpuHeavyMassEngine
        {
            HeavyMassThreshold = HeavyMassThreshold
        };

        _engine.Initialize(_topology);
        _lastTopologySignature = graph.TopologySignature;

        graph.GpuHeavyMassEngine = _engine;
        _stepCounter = 0;
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_engine == null || !_engine.IsInitialized)
            return;

        if (graph.TopologySignature != _lastTopologySignature)
        {
            Initialize(graph);
            if (_engine == null) return;
        }

        _stepCounter++;

        if (_stepCounter % MassComputeSampleRate != 0)
            return;

        _engine.HeavyMassThreshold = HeavyMassThreshold;
        _engine.Step(dt);

        SyncToGraph(graph);
    }

    private void SyncToGraph(RQGraph graph)
    {
        if (_engine == null) return;

        // Update graph with mass statistics
        graph.TotalCorrelationMassGpu = _engine.TotalCorrelationMass;
        graph.HeavyNodeCountGpu = _engine.HeavyNodeCount;

        // Update LocalMass array (used for visualization and other modules)
        if (graph.LocalMass == null || graph.LocalMass.Length != graph.N)
        {
            graph.LocalMass = new double[graph.N];
        }
        _engine.CorrelationMass.CopyTo(graph.LocalMass);
    }

    public List<int> GetHeavyNodes()
    {
        return _engine?.GetHeavyNodes() ?? [];
    }

    public double GetNodeMass(int nodeIndex)
    {
        return _engine?.GetNodeMass(nodeIndex) ?? 0;
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
