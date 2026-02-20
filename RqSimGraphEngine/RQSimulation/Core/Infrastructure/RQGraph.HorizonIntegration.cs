using RQSimulation.GPUCompressedSparseRow.BlackHole;
using RQSimulation.GPUCompressedSparseRow.Causal;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.Gauge;
using RQSimulation.GPUCompressedSparseRow.HeavyMass;

namespace RQSimulation;

/// <summary>
/// RQGraph extension for GPU module integration.
/// Provides properties for CSR topology, horizon state, causal discovery, gauge checking, and heavy mass.
/// </summary>
public partial class RQGraph
{
    // ============================================================================
    // CSR Topology Integration
    // ============================================================================

    /// <summary>
    /// CSR topology for GPU operations.
    /// Set by CsrUnifiedEngine or manually built.
    /// </summary>
    public CsrTopology? CsrTopology { get; set; }

    /// <summary>
    /// Signature that changes when topology is modified.
    /// Used to detect when GPU buffers need reupload.
    /// </summary>
    public int TopologySignature { get; private set; }

    /// <summary>
    /// Increment topology signature when edges change.
    /// Call after AddEdge/RemoveEdge/Rewire operations.
    /// </summary>
    public void IncrementTopologySignature()
    {
        TopologySignature++;
        TopologyVersion++; // Keep legacy version in sync
    }

    // ============================================================================
    // Horizon Detection Integration
    // ============================================================================

    /// <summary>
    /// GPU Horizon Engine for black hole detection.
    /// </summary>
    public GpuHorizonEngine? GpuHorizonEngine { get; set; }

    /// <summary>
    /// Horizon flags per node (bit field).
    /// </summary>
    public int[]? HorizonFlags { get; set; }

    /// <summary>
    /// Local mass per node (computed from neighbor energies via CSR).
    /// </summary>
    public double[]? LocalMass { get; set; }

    /// <summary>
    /// Number of nodes currently at event horizon.
    /// </summary>
    public int HorizonNodeCount { get; set; }

    /// <summary>
    /// Number of singularity nodes (inside horizon).
    /// </summary>
    public int SingularityNodeCount { get; set; }

    /// <summary>
    /// Check if a node is at an event horizon.
    /// </summary>
    public bool IsNodeAtHorizon(int nodeIndex)
    {
        if (HorizonFlags == null || nodeIndex < 0 || nodeIndex >= HorizonFlags.Length)
            return false;
        return (HorizonFlags[nodeIndex] & 1) != 0;
    }

    /// <summary>
    /// Check if a node is a singularity (inside horizon).
    /// </summary>
    public bool IsNodeSingularity(int nodeIndex)
    {
        if (HorizonFlags == null || nodeIndex < 0 || nodeIndex >= HorizonFlags.Length)
            return false;
        return (HorizonFlags[nodeIndex] & 2) != 0;
    }

    /// <summary>
    /// Get local mass for a node.
    /// </summary>
    public double GetNodeLocalMass(int nodeIndex)
    {
        if (LocalMass == null || nodeIndex < 0 || nodeIndex >= LocalMass.Length)
            return 0.0;
        return LocalMass[nodeIndex];
    }

    // ============================================================================
    // Causal Discovery Integration
    // ============================================================================

    /// <summary>
    /// GPU Causal Engine for parallel BFS causal cone computation.
    /// </summary>
    public GpuCausalEngine? GpuCausalEngine { get; set; }

    /// <summary>
    /// Get causal future of a node using GPU if available, else fallback to CPU.
    /// </summary>
    public List<int> GetCausalFutureGpu(int node, double dt)
    {
        if (GpuCausalEngine != null && GpuCausalEngine.IsInitialized)
        {
            int maxHops = (int)dt;
            GpuCausalEngine.ComputeCausalCone(node, maxHops);
            return GpuCausalEngine.GetCausalConeNodes();
        }

        // Fallback to CPU implementation
        return GetCausalFuture(node, dt);
    }

    /// <summary>
    /// Check causal connection using GPU if available.
    /// </summary>
    public bool IsCausallyConnectedGpu(int nodeA, int nodeB, double dt)
    {
        if (GpuCausalEngine != null && GpuCausalEngine.IsInitialized)
        {
            int maxHops = (int)dt;
            return GpuCausalEngine.AreCausallyConnected(nodeA, nodeB, maxHops);
        }

        // Fallback to CPU implementation
        return IsCausallyConnected(nodeA, nodeB, dt);
    }

    // ============================================================================
    // Gauge Invariant Integration
    // ============================================================================

    /// <summary>
    /// GPU Gauge Engine for Wilson loop computation.
    /// </summary>
    public GpuGaugeEngine? GpuGaugeEngine { get; set; }

    /// <summary>
    /// Total topological charge (Chern number) from GPU computation.
    /// </summary>
    public double TopologicalChargeGpu { get; set; }

    /// <summary>
    /// Maximum gauge violation from GPU computation.
    /// </summary>
    public double GaugeViolationGpu { get; set; }

    /// <summary>
    /// Number of triangles detected by GPU gauge engine.
    /// </summary>
    public int TriangleCountGpu { get; set; }

    /// <summary>
    /// Get topological charge using GPU if available, else compute on CPU.
    /// </summary>
    public int GetChernNumberGpu(List<int> clusterNodes)
    {
        // If GPU engine computed a global charge, return it
        if (GpuGaugeEngine != null && GpuGaugeEngine.IsInitialized)
        {
            return (int)System.Math.Round(GpuGaugeEngine.TotalTopologicalCharge);
        }

        // Fallback to CPU
        return ComputeChernNumber(clusterNodes);
    }

    // ============================================================================
    // Heavy Mass Integration
    // ============================================================================

    /// <summary>
    /// GPU Heavy Mass Engine for correlation mass computation.
    /// </summary>
    public GpuHeavyMassEngine? GpuHeavyMassEngine { get; set; }

    /// <summary>
    /// Total correlation mass from GPU computation.
    /// </summary>
    public double TotalCorrelationMassGpu { get; set; }

    /// <summary>
    /// Number of heavy nodes detected by GPU engine.
    /// </summary>
    public int HeavyNodeCountGpu { get; set; }

    /// <summary>
    /// Get total correlation mass using GPU if available.
    /// </summary>
    public double GetTotalCorrelationMassGpu()
    {
        return GpuHeavyMassEngine?.TotalCorrelationMass ?? TotalCorrelationMassGpu;
    }

    /// <summary>
    /// Get heavy nodes list using GPU if available.
    /// </summary>
    public List<int> GetHeavyNodesGpu()
    {
        return GpuHeavyMassEngine?.GetHeavyNodes() ?? [];
    }
}
