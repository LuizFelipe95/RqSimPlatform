using System;

namespace RQSimulation.Core.Infrastructure;

/// <summary>
/// Data Transfer Object (DTO) for graph topology snapshots.
/// Lives in CPU RAM for efficient distribution to worker GPUs.
/// 
/// PURPOSE:
/// ========
/// Captures a point-in-time snapshot of the graph from the Physics GPU (Device 0)
/// for distribution to Analysis/Sampling worker GPUs without blocking physics simulation.
/// 
/// DATA FLOW:
/// ==========
/// 1. Physics GPU (Device 0) ? ToArray() ? GraphSnapshot (CPU RAM)
/// 2. GraphSnapshot ? Worker GPU (Device N) ? CopyFrom()
/// 
/// IMPORTANT: ComputeSharp buffers are device-bound. Cannot transfer directly between GPUs.
/// Must go through CPU arrays.
/// 
/// All arrays use CSR (Compressed Sparse Row) format for memory efficiency.
/// </summary>
public sealed class GraphSnapshot
{
    /// <summary>
    /// CSR row offsets. Length = NodeCount + 1.
    /// RowOffsets[i] to RowOffsets[i+1] defines the neighbor range for node i.
    /// </summary>
    public int[] RowOffsets { get; set; } = [];

    /// <summary>
    /// CSR column indices (neighbor node IDs). Length = Nnz (number of non-zeros).
    /// </summary>
    public int[] ColIndices { get; set; } = [];

    /// <summary>
    /// Edge weights in CSR order. Length = Nnz.
    /// </summary>
    public double[] EdgeWeights { get; set; } = [];

    /// <summary>
    /// Node masses. Length = NodeCount.
    /// Computed as m_i = sqrt(sum_j w_ij^2) (correlation mass).
    /// </summary>
    public double[] NodeMasses { get; set; } = [];

    /// <summary>
    /// Scalar field values at each node. Length = NodeCount.
    /// </summary>
    public double[] ScalarField { get; set; } = [];

    /// <summary>
    /// Node curvatures (Forman-Ricci or Ollivier). Length = NodeCount.
    /// </summary>
    public double[] Curvatures { get; set; } = [];

    /// <summary>
    /// Simulation tick at which snapshot was taken.
    /// Used for temporal ordering and staleness detection.
    /// </summary>
    public long TickId { get; set; }

    /// <summary>
    /// Timestamp when snapshot was created.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Topology version from the source graph.
    /// Incremented when edges are added/removed.
    /// </summary>
    public int TopologyVersion { get; set; }

    /// <summary>
    /// Number of nodes in the graph.
    /// </summary>
    public int NodeCount => RowOffsets.Length > 0 ? RowOffsets.Length - 1 : 0;

    /// <summary>
    /// Number of non-zero entries (directed edges) in CSR.
    /// </summary>
    public int Nnz => ColIndices.Length;

    /// <summary>
    /// Number of undirected edges (Nnz / 2 for symmetric adjacency).
    /// </summary>
    public int EdgeCount => Nnz / 2;

    /// <summary>
    /// Average node degree.
    /// </summary>
    public double AverageDegree => NodeCount > 0 ? (double)Nnz / NodeCount : 0.0;

    /// <summary>
    /// Total graph weight (sum of all edge weights).
    /// </summary>
    public double TotalWeight { get; set; }

    /// <summary>
    /// Total action/energy at snapshot time.
    /// </summary>
    public double TotalAction { get; set; }

    /// <summary>
    /// Create empty snapshot.
    /// </summary>
    public GraphSnapshot()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Create snapshot from RQGraph.
    /// </summary>
    /// <param name="graph">Source graph</param>
    /// <param name="tickId">Current simulation tick</param>
    public static GraphSnapshot FromGraph(RQGraph graph, long tickId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Build CSR if not already built
        graph.BuildSoAViews();

        int nodeCount = graph.N;
        int nnz = graph.CsrIndices.Length;

        // Copy CSR topology (defensive copies)
        int[] offsets = new int[graph.CsrOffsets.Length];
        Array.Copy(graph.CsrOffsets, offsets, offsets.Length);

        int[] indices = new int[nnz];
        Array.Copy(graph.CsrIndices, indices, indices.Length);

        // Build weights array in CSR order
        double[] weights = new double[nnz];
        for (int i = 0; i < nodeCount; i++)
        {
            int start = offsets[i];
            int end = offsets[i + 1];
            for (int k = start; k < end; k++)
            {
                int j = indices[k];
                weights[k] = graph.Weights[i, j];
            }
        }

        // Node masses
        double[] masses = new double[nodeCount];
        var correlationMass = graph.CorrelationMass;
        if (correlationMass != null && correlationMass.Length >= nodeCount)
        {
            Array.Copy(correlationMass, masses, nodeCount);
        }
        else
        {
            for (int i = 0; i < nodeCount; i++)
            {
                masses[i] = graph.GetNodeMass(i);
            }
        }

        // Scalar field
        double[] scalarField = new double[nodeCount];
        if (graph.ScalarField != null && graph.ScalarField.Length >= nodeCount)
        {
            Array.Copy(graph.ScalarField, scalarField, nodeCount);
        }

        // Total weight
        double totalWeight = 0.0;
        for (int i = 0; i < nnz; i++)
        {
            totalWeight += weights[i];
        }
        totalWeight /= 2.0; // Each edge counted twice in CSR

        return new GraphSnapshot
        {
            RowOffsets = offsets,
            ColIndices = indices,
            EdgeWeights = weights,
            NodeMasses = masses,
            ScalarField = scalarField,
            Curvatures = new double[nodeCount], // Filled separately if needed
            TickId = tickId,
            Timestamp = DateTime.UtcNow,
            TopologyVersion = graph.TopologyVersion,
            TotalWeight = totalWeight
        };
    }

    /// <summary>
    /// Validate snapshot integrity.
    /// </summary>
    /// <returns>True if snapshot is valid</returns>
    public bool Validate()
    {
        if (RowOffsets.Length == 0)
            return false;

        int n = NodeCount;

        // Check array lengths
        if (NodeMasses.Length != n)
            return false;

        if (EdgeWeights.Length != Nnz)
            return false;

        if (ColIndices.Length != Nnz)
            return false;

        // Check CSR monotonicity
        for (int i = 0; i < n; i++)
        {
            if (RowOffsets[i] > RowOffsets[i + 1])
                return false;
        }

        // Check column indices bounds
        for (int k = 0; k < Nnz; k++)
        {
            if (ColIndices[k] < 0 || ColIndices[k] >= n)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Get approximate memory footprint in bytes.
    /// </summary>
    public long ApproximateSizeBytes
    {
        get
        {
            long size = 0;
            size += RowOffsets.Length * sizeof(int);
            size += ColIndices.Length * sizeof(int);
            size += EdgeWeights.Length * sizeof(double);
            size += NodeMasses.Length * sizeof(double);
            size += ScalarField.Length * sizeof(double);
            size += Curvatures.Length * sizeof(double);
            size += 64; // Object overhead + primitive fields
            return size;
        }
    }

    /// <summary>
    /// Create a lightweight copy with only topology (no physics state).
    /// </summary>
    public GraphSnapshot CloneTopologyOnly()
    {
        return new GraphSnapshot
        {
            RowOffsets = (int[])RowOffsets.Clone(),
            ColIndices = (int[])ColIndices.Clone(),
            EdgeWeights = (double[])EdgeWeights.Clone(),
            NodeMasses = [],
            ScalarField = [],
            Curvatures = [],
            TickId = TickId,
            Timestamp = Timestamp,
            TopologyVersion = TopologyVersion,
            TotalWeight = TotalWeight
        };
    }

    /// <summary>
    /// Create a full deep copy.
    /// </summary>
    public GraphSnapshot Clone()
    {
        return new GraphSnapshot
        {
            RowOffsets = (int[])RowOffsets.Clone(),
            ColIndices = (int[])ColIndices.Clone(),
            EdgeWeights = (double[])EdgeWeights.Clone(),
            NodeMasses = (double[])NodeMasses.Clone(),
            ScalarField = (double[])ScalarField.Clone(),
            Curvatures = (double[])Curvatures.Clone(),
            TickId = TickId,
            Timestamp = Timestamp,
            TopologyVersion = TopologyVersion,
            TotalWeight = TotalWeight,
            TotalAction = TotalAction
        };
    }
}
