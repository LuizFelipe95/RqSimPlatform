using RQSimulation.Core.Plugins;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.BlackHole;

/// <summary>
/// GPU-accelerated black hole horizon detection module.
/// 
/// Integrates GpuHorizonEngine with the physics pipeline.
/// Detects horizon formation, tracks singularities, and applies
/// Hawking evaporation for black hole mass loss.
/// 
/// PHYSICS (RQ-HYPOTHESIS):
/// - Black holes emerge from high-density graph clusters
/// - Horizon defined by r_eff ? 2M (Schwarzschild criterion)
/// - Lapse freezes at horizon (time stops)
/// - Information trapped behind horizon
/// - Hawking radiation causes slow evaporation
/// 
/// HYBRID PATTERN:
/// - GPU: Detects horizons (read-only topology)
/// - CPU: May act on Recommendations for topology changes
/// </summary>
public sealed class GpuBlackHoleHorizonModule : PhysicsModuleBase, IDisposable
{
    private GpuHorizonEngine? _engine;
    private CsrTopology? _topology;
    private bool _disposed;
    private int _lastTopologySignature;

    public override string Name => "GPU Black Hole Horizon Detection";
    public override string Description => "GPU-accelerated horizon detection using CSR topology with Schwarzschild criterion";
    public override string Category => "Gravity";
    public override ExecutionType ExecutionType => ExecutionType.GPU;

    /// <summary>
    /// Runs in PostProcess stage after physics forces are applied.
    /// This allows horizon detection based on current state.
    /// </summary>
    public override ExecutionStage Stage => ExecutionStage.PostProcess;

    public override int Priority => 10;

    // Configuration properties (exposed to UI)
    
    /// <summary>Density threshold for black hole classification.</summary>
    public double DensityThreshold { get; set; } = 10.0;

    /// <summary>Minimum mass for horizon consideration.</summary>
    public double MinMassThreshold { get; set; } = 0.01;

    /// <summary>Evaporation rate constant for Hawking radiation.</summary>
    public double EvaporationConstant { get; set; } = 1e-4;

    /// <summary>Whether to apply Hawking evaporation each step.</summary>
    public bool EnableEvaporation { get; set; } = true;

    // State accessors

    /// <summary>Number of nodes currently at horizon.</summary>
    public int HorizonCount => _engine?.HorizonCount ?? 0;

    /// <summary>Number of singularity nodes.</summary>
    public int SingularityCount => _engine?.SingularityCount ?? 0;

    /// <summary>Get horizon flags for all nodes (after step).</summary>
    public int[] GetHorizonFlags()
    {
        if (_engine == null) return [];
        var flags = new int[_engine.NodeCount];
        _engine.HorizonFlags.CopyTo(flags);
        return flags;
    }

    /// <summary>Get local mass for all nodes (after step).</summary>
    public double[] GetLocalMass()
    {
        if (_engine == null) return [];
        var mass = new double[_engine.NodeCount];
        _engine.LocalMass.CopyTo(mass);
        return mass;
    }

    public override void Initialize(RQGraph graph)
    {
        _engine?.Dispose();

        // Check if graph has CSR topology
        if (graph.CsrTopology == null)
        {
            // CSR not available - skip GPU module
            return;
        }

        _topology = graph.CsrTopology;

        if (!_topology.IsGpuReady)
        {
            // Topology not on GPU yet
            return;
        }

        // Get node energies from quantum probability or compute from wavefunction
        double[] nodeEnergies = GetNodeEnergies(graph);

        _engine = new GpuHorizonEngine
        {
            DensityThreshold = DensityThreshold,
            MinMassThreshold = MinMassThreshold,
            EvaporationConstant = EvaporationConstant
        };

        _engine.Initialize(_topology, nodeEnergies);
        _lastTopologySignature = graph.TopologySignature;

        // Link to graph for external access
        graph.GpuHorizonEngine = _engine;
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_engine == null || !_engine.IsInitialized)
            return;

        // Check if topology changed (requires reinitialization)
        if (graph.TopologySignature != _lastTopologySignature)
        {
            Initialize(graph);
            if (_engine == null) return;
        }

        // Update node energies from current state
        double[] nodeEnergies = GetNodeEnergies(graph);
        _engine.UpdateNodeEnergies(nodeEnergies);

        // Update parameters
        _engine.DensityThreshold = DensityThreshold;
        _engine.MinMassThreshold = MinMassThreshold;
        _engine.EvaporationConstant = EvaporationConstant;

        // Run GPU computation
        _engine.Step(dt, EnableEvaporation);

        // Sync horizon info back to graph for visualization
        SyncHorizonToGraph(graph);
    }

    private double[] GetNodeEnergies(RQGraph graph)
    {
        int n = graph.N;
        double[] energies = new double[n];

        // Priority: QuantumProbability > CorrelationMass > degree-based estimate
        if (graph.QuantumProbability != null && graph.QuantumProbability.Length == n)
        {
            Array.Copy(graph.QuantumProbability, energies, n);
        }
        else if (graph.CorrelationMass != null && graph.CorrelationMass.Length == n)
        {
            Array.Copy(graph.CorrelationMass, energies, n);
        }
        else
        {
            // Fallback: use weighted degree as energy proxy
            for (int i = 0; i < n; i++)
            {
                double weightedDegree = 0.0;
                foreach (int j in graph.Neighbors(i))
                {
                    weightedDegree += graph.Weights[i, j];
                }
                energies[i] = weightedDegree;
            }
        }

        return energies;
    }

    private void SyncHorizonToGraph(RQGraph graph)
    {
        if (_engine == null) return;

        // Allocate or resize horizon info arrays on graph
        if (graph.HorizonFlags == null || graph.HorizonFlags.Length != graph.N)
        {
            graph.HorizonFlags = new int[graph.N];
        }
        if (graph.LocalMass == null || graph.LocalMass.Length != graph.N)
        {
            graph.LocalMass = new double[graph.N];
        }

        // Copy from engine
        _engine.HorizonFlags.CopyTo(graph.HorizonFlags);
        _engine.LocalMass.CopyTo(graph.LocalMass);

        // Update aggregate stats
        graph.HorizonNodeCount = _engine.HorizonCount;
        graph.SingularityNodeCount = _engine.SingularityCount;
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
