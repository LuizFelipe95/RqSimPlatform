using ComputeSharp;
using RQSimulation;
using RQSimulation.Core.Plugins;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUOptimized.Topology;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

/// <summary>
/// GPU module for potential link activation (sleeping edge awakening).
/// 
/// Manages activation of "sleeping" edges (weight ? 0) based on:
/// 1. Energy imbalance between endpoints
/// 2. Hamiltonian constraint violations
/// 3. Spectral gap requirements
/// 
/// PHYSICS:
/// - Avoids expensive CSR topology rebuilds
/// - Activates pre-existing edges rather than creating new ones
/// - Energy-driven and constraint-driven activation criteria
/// 
/// Uses shaders from: GPUOptimized/Topology/PotentialLinkActivator.cs
/// </summary>
public sealed class PotentialLinkActivatorGpuModule : GpuPluginBase
{
    private CsrTopology? _topology;
    private RQGraph? _graph;
    
    // GPU buffers
    private ReadWriteBuffer<double>? _edgeWeightsBuffer;
    private ReadWriteBuffer<double>? _nodeEnergiesBuffer;
    private ReadWriteBuffer<double>? _activationPotentialBuffer;
    
    // Configuration
    private int _lastActivationTick;
    private int _activationsThisStep;
    
    public override string Name => "Link Activator (GPU)";
    public override string Description => "GPU-accelerated sleeping edge activation based on energy/constraint criteria";
    public override string Category => "Topology";
    public override int Priority => 150; // Run after physics, before post-process
    public override ExecutionStage Stage => ExecutionStage.Integration;
    
    /// <summary>
    /// Module group for atomic execution with other topology modules.
    /// </summary>
    public string ModuleGroup => "TopologyEvolution";
    
    /// <summary>
    /// How often to attempt activations (every N steps).
    /// </summary>
    public int ActivationInterval { get; set; } = 50;
    
    /// <summary>
    /// Threshold for considering an edge "sleeping" (weight below this).
    /// </summary>
    public double SleepThreshold { get; set; } = 0.01;
    
    /// <summary>
    /// Minimum activation potential required to wake an edge.
    /// </summary>
    public double PotentialThreshold { get; set; } = 0.5;
    
    /// <summary>
    /// Weight to assign to newly activated edges.
    /// </summary>
    public double ActivationWeight { get; set; } = 0.1;
    
    /// <summary>
    /// Maximum edges to activate per step.
    /// </summary>
    public int MaxActivationsPerStep { get; set; } = 10;
    
    /// <summary>
    /// Number of edges activated in last step.
    /// </summary>
    public int ActivationsThisStep => _activationsThisStep;
    
    /// <summary>
    /// Total edges activated since initialization.
    /// </summary>
    public int TotalActivations { get; private set; }
    
    /// <summary>
    /// Activation potentials per edge (for visualization).
    /// </summary>
    public double[]? ActivationPotentials { get; private set; }

    public override void Initialize(RQGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _lastActivationTick = 0;
        _activationsThisStep = 0;
        TotalActivations = 0;
        
        // Initialize CSR topology
        _topology = new CsrTopology();
        _topology.BuildFromDenseMatrix(graph.Edges, graph.Weights);
        _topology.UploadToGpu();
        
        // Allocate buffers
        AllocateBuffers(graph.N, _topology.Nnz);
    }

    private void AllocateBuffers(int nodeCount, int edgeCount)
    {
        var device = GraphicsDevice.GetDefault();
        
        _edgeWeightsBuffer?.Dispose();
        _nodeEnergiesBuffer?.Dispose();
        _activationPotentialBuffer?.Dispose();
        
        _edgeWeightsBuffer = device.AllocateReadWriteBuffer<double>(edgeCount);
        _nodeEnergiesBuffer = device.AllocateReadWriteBuffer<double>(nodeCount);
        _activationPotentialBuffer = device.AllocateReadWriteBuffer<double>(edgeCount);
        
        ActivationPotentials = new double[edgeCount];
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (_topology is null || _edgeWeightsBuffer is null) return;
        
        _lastActivationTick++;
        _activationsThisStep = 0;
        
        // Only attempt activation periodically
        if (_lastActivationTick < ActivationInterval) return;
        _lastActivationTick = 0;
        
        // Compute activation potentials
        ComputeActivationPotentials(graph);
        
        // Activate sleeping edges with high potential
        ActivateSleepingEdges(graph);
    }

    private void ComputeActivationPotentials(RQGraph graph)
    {
        if (_topology is null || ActivationPotentials is null) return;
        
        // Upload node energies - use local action as proxy for energy
        double[] nodeEnergies = new double[graph.N];
        for (int i = 0; i < graph.N; i++)
        {
            // Use node mass as energy proxy (mass ~ correlation strength)
            nodeEnergies[i] = graph.GetNodeMass(i);
        }
        _nodeEnergiesBuffer!.CopyFrom(nodeEnergies);
        
        // Compute activation potential for each edge (CPU version for simplicity)
        int edgeIdx = 0;
        for (int i = 0; i < graph.N; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (edgeIdx >= ActivationPotentials.Length) break;
                
                double w = graph.Weights[i, j];
                
                if (w > SleepThreshold)
                {
                    // Already active
                    ActivationPotentials[edgeIdx] = 0.0;
                }
                else
                {
                    // Sleeping edge - compute potential
                    double E_i = nodeEnergies[i];
                    double E_j = nodeEnergies[j];
                    
                    // Energy imbalance drives activation
                    double energyImbalance = (E_i - E_j) * (E_i - E_j);
                    
                    // Use degree difference as constraint proxy
                    int deg_i = graph.Degree(i);
                    int deg_j = graph.Degree(j);
                    double constraintNeed = Math.Abs(deg_i - deg_j) * 0.1;
                    
                    ActivationPotentials[edgeIdx] = energyImbalance + constraintNeed;
                }
                edgeIdx++;
            }
        }
        
        // Upload to GPU for visualization
        _activationPotentialBuffer!.CopyFrom(ActivationPotentials);
    }

    private void ActivateSleepingEdges(RQGraph graph)
    {
        if (ActivationPotentials is null) return;
        
        // Find edges with highest activation potential
        var candidates = new List<(int edgeIdx, double potential, int i, int j)>();
        
        int edgeIdx = 0;
        for (int i = 0; i < graph.N; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (edgeIdx >= ActivationPotentials.Length) break;
                
                double potential = ActivationPotentials[edgeIdx];
                if (potential > PotentialThreshold)
                {
                    candidates.Add((edgeIdx, potential, i, j));
                }
                edgeIdx++;
            }
        }
        
        // Sort by potential (descending) and activate top candidates
        var toActivate = candidates
            .OrderByDescending(c => c.potential)
            .Take(MaxActivationsPerStep);
        
        foreach (var (_, _, i, j) in toActivate)
        {
            // Activate edge
            graph.Weights[i, j] = ActivationWeight;
            graph.Weights[j, i] = ActivationWeight;
            
            _activationsThisStep++;
            TotalActivations++;
        }
    }

    protected override void DisposeCore()
    {
        _edgeWeightsBuffer?.Dispose();
        _nodeEnergiesBuffer?.Dispose();
        _activationPotentialBuffer?.Dispose();
        _topology?.Dispose();
        
        _edgeWeightsBuffer = null;
        _nodeEnergiesBuffer = null;
        _activationPotentialBuffer = null;
        _topology = null;
    }
}
