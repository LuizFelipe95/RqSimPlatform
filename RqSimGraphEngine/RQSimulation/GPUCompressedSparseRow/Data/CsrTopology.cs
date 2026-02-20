using System;
using System.Collections.Generic;
using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Data;

/// <summary>
/// Compressed Sparse Row (CSR) topology for efficient GPU SpMV operations.
/// 
/// CSR Format:
/// - RowOffsets[i] = index of first neighbor of node i in ColIndices/EdgeWeights
/// - RowOffsets[i+1] - RowOffsets[i] = degree of node i
/// - ColIndices[k] = neighbor node ID for edge k
/// - EdgeWeights[k] = weight of edge k
/// 
/// Memory layout optimized for GPU coalesced access in Hamiltonian multiplication.
/// </summary>
public sealed class CsrTopology : IDisposable
{
    // CPU arrays (source data)
    private int[] _rowOffsets = [];
    private int[] _colIndices = [];
    private double[] _edgeWeights = [];
    private double[] _nodePotential = [];

    // GPU buffers (ComputeSharp)
    private ReadOnlyBuffer<int>? _rowOffsetsBuffer;
    private ReadOnlyBuffer<int>? _colIndicesBuffer;
    private ReadOnlyBuffer<double>? _edgeWeightsBuffer;
    private ReadOnlyBuffer<double>? _nodePotentialBuffer;

    private readonly GraphicsDevice _device;
    private bool _disposed;
    private bool _gpuBuffersAllocated;

    /// <summary>
    /// Number of nodes in the graph.
    /// </summary>
    public int NodeCount { get; private set; }

    /// <summary>
    /// Number of non-zero elements (directed edges).
    /// </summary>
    public int Nnz { get; private set; }

    /// <summary>
    /// Whether GPU buffers have been allocated.
    /// </summary>
    public bool IsGpuReady => _gpuBuffersAllocated;

    // ============================================================================
    // CPU Array Accessors (for visualization and debugging)
    // ============================================================================

    /// <summary>
    /// CPU array of row offsets (size = NodeCount + 1).
    /// RowOffsets[i] = index of first neighbor of node i.
    /// </summary>
    public ReadOnlySpan<int> RowOffsets => _rowOffsets;

    /// <summary>
    /// CPU array of column indices (size = Nnz).
    /// ColIndices[k] = neighbor node ID for edge k.
    /// </summary>
    public ReadOnlySpan<int> ColIndices => _colIndices;

    /// <summary>
    /// CPU array of edge weights (size = Nnz).
    /// </summary>
    public ReadOnlySpan<double> EdgeWeights => _edgeWeights;

    /// <summary>
    /// CPU array of node potentials (size = NodeCount).
    /// </summary>
    public ReadOnlySpan<double> NodePotential => _nodePotential;

    // ============================================================================
    // GPU Buffer Accessors
    // ============================================================================

    /// <summary>
    /// GPU buffer for row offsets (size = NodeCount + 1).
    /// </summary>
    public ReadOnlyBuffer<int> RowOffsetsBuffer => _rowOffsetsBuffer 
        ?? throw new InvalidOperationException("GPU buffers not allocated. Call UploadToGpu() first.");

    /// <summary>
    /// GPU buffer for column indices (size = Nnz).
    /// </summary>
    public ReadOnlyBuffer<int> ColIndicesBuffer => _colIndicesBuffer 
        ?? throw new InvalidOperationException("GPU buffers not allocated. Call UploadToGpu() first.");

    /// <summary>
    /// GPU buffer for edge weights (size = Nnz).
    /// </summary>
    public ReadOnlyBuffer<double> EdgeWeightsBuffer => _edgeWeightsBuffer 
        ?? throw new InvalidOperationException("GPU buffers not allocated. Call UploadToGpu() first.");

    /// <summary>
    /// GPU buffer for node potentials (size = NodeCount).
    /// </summary>
    public ReadOnlyBuffer<double> NodePotentialBuffer => _nodePotentialBuffer 
        ?? throw new InvalidOperationException("GPU buffers not allocated. Call UploadToGpu() first.");

    public CsrTopology()
    {
        _device = GraphicsDevice.GetDefault();
    }

    public CsrTopology(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Build CSR topology from dense adjacency matrix and weights.
    /// </summary>
    /// <param name="edges">Dense boolean adjacency matrix [N x N]</param>
    /// <param name="weights">Dense weight matrix [N x N]</param>
    /// <param name="potential">Node potential array [N] (optional, zeros if null)</param>
    public void BuildFromDenseMatrix(bool[,] edges, double[,] weights, double[]? potential = null)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(weights);

        int n = edges.GetLength(0);
        if (edges.GetLength(1) != n || weights.GetLength(0) != n || weights.GetLength(1) != n)
        {
            throw new ArgumentException("Matrix dimensions must be square and equal.");
        }

        NodeCount = n;

        // Count non-zeros
        int nnz = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (edges[i, j])
                    nnz++;
            }
        }
        Nnz = nnz;

        // Allocate arrays
        _rowOffsets = new int[n + 1];
        _colIndices = new int[nnz];
        _edgeWeights = new double[nnz];
        _nodePotential = potential ?? new double[n];

        // Build CSR
        int ptr = 0;
        for (int i = 0; i < n; i++)
        {
            _rowOffsets[i] = ptr;
            for (int j = 0; j < n; j++)
            {
                if (edges[i, j])
                {
                    _colIndices[ptr] = j;
                    _edgeWeights[ptr] = weights[i, j];
                    ptr++;
                }
            }
        }
        _rowOffsets[n] = ptr;

        // Invalidate GPU buffers
        DisposeGpuBuffers();
    }

    /// <summary>
    /// Build CSR topology from edge list representation.
    /// More efficient for sparse graphs.
    /// </summary>
    /// <param name="nodeCount">Number of nodes</param>
    /// <param name="sourceNodes">Source node for each edge</param>
    /// <param name="targetNodes">Target node for each edge</param>
    /// <param name="weights">Weight for each edge</param>
    /// <param name="potential">Node potential array (optional)</param>
    public void BuildFromEdgeList(
        int nodeCount,
        ReadOnlySpan<int> sourceNodes,
        ReadOnlySpan<int> targetNodes,
        ReadOnlySpan<double> weights,
        ReadOnlySpan<double> potential = default)
    {
        if (sourceNodes.Length != targetNodes.Length || sourceNodes.Length != weights.Length)
        {
            throw new ArgumentException("Edge arrays must have equal length.");
        }

        NodeCount = nodeCount;
        Nnz = sourceNodes.Length;

        // Count edges per node
        Span<int> rowCount = stackalloc int[nodeCount];
        rowCount.Clear();
        for (int i = 0; i < sourceNodes.Length; i++)
        {
            rowCount[sourceNodes[i]]++;
        }

        // Build row offsets
        _rowOffsets = new int[nodeCount + 1];
        _rowOffsets[0] = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            _rowOffsets[i + 1] = _rowOffsets[i] + rowCount[i];
        }

        // Allocate column/weight arrays
        _colIndices = new int[Nnz];
        _edgeWeights = new double[Nnz];

        // Current insertion position per row
        Span<int> rowPos = stackalloc int[nodeCount];
        _rowOffsets.AsSpan(0, nodeCount).CopyTo(rowPos);

        // Fill CSR arrays
        for (int i = 0; i < sourceNodes.Length; i++)
        {
            int src = sourceNodes[i];
            int pos = rowPos[src]++;
            _colIndices[pos] = targetNodes[i];
            _edgeWeights[pos] = weights[i];
        }

        // Copy potential
        _nodePotential = potential.IsEmpty 
            ? new double[nodeCount] 
            : potential.ToArray();

        // Invalidate GPU buffers
        DisposeGpuBuffers();
    }

    /// <summary>
    /// Update node potentials without rebuilding topology.
    /// </summary>
    public void UpdatePotential(ReadOnlySpan<double> potential)
    {
        if (potential.Length != NodeCount)
        {
            throw new ArgumentException($"Potential length {potential.Length} != NodeCount {NodeCount}");
        }

        potential.CopyTo(_nodePotential);

        // Update GPU buffer if allocated
        if (_nodePotentialBuffer is not null)
        {
            _nodePotentialBuffer.CopyFrom(potential);
        }
    }

    /// <summary>
    /// Upload CSR data to GPU buffers.
    /// </summary>
    public void UploadToGpu()
    {
        if (_gpuBuffersAllocated)
            return;

        if (NodeCount == 0)
        {
            throw new InvalidOperationException("Topology not initialized. Call BuildFromDenseMatrix or BuildFromEdgeList first.");
        }

        DisposeGpuBuffers();

        _rowOffsetsBuffer = _device.AllocateReadOnlyBuffer(_rowOffsets);
        _colIndicesBuffer = _device.AllocateReadOnlyBuffer(_colIndices);
        _edgeWeightsBuffer = _device.AllocateReadOnlyBuffer(_edgeWeights);
        _nodePotentialBuffer = _device.AllocateReadOnlyBuffer(_nodePotential);

        _gpuBuffersAllocated = true;
    }

    /// <summary>
    /// Get degree of a node (number of outgoing edges).
    /// </summary>
    public int GetDegree(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= NodeCount)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        return _rowOffsets[nodeIndex + 1] - _rowOffsets[nodeIndex];
    }

    /// <summary>
    /// Get neighbors and weights for a node.
    /// </summary>
    public void GetNeighbors(int nodeIndex, out ReadOnlySpan<int> neighbors, out ReadOnlySpan<double> weights)
    {
        if (nodeIndex < 0 || nodeIndex >= NodeCount)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        int start = _rowOffsets[nodeIndex];
        int end = _rowOffsets[nodeIndex + 1];

        neighbors = _colIndices.AsSpan(start, end - start);
        weights = _edgeWeights.AsSpan(start, end - start);
    }

    /// <summary>
    /// Update CSR edge weights from a dense weight matrix without rebuilding CSR structure.
    /// 
    /// Preconditions:
    /// - CSR structure (row offsets / col indices) must correspond to the same adjacency as used to create it.
    /// - Only weights may change.
    /// </summary>
    public void UpdateEdgeWeightsFromDense(double[,] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (NodeCount == 0 || Nnz == 0)
            return;

        if (weights.GetLength(0) != NodeCount || weights.GetLength(1) != NodeCount)
            throw new ArgumentException("Weight matrix dimensions must match NodeCount.");

        for (int i = 0; i < NodeCount; i++)
        {
            int start = _rowOffsets[i];
            int end = _rowOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                int j = _colIndices[k];
                _edgeWeights[k] = weights[i, j];
            }
        }

        if (_edgeWeightsBuffer is not null)
        {
            ReplaceEdgeWeightsBuffer();
        }
    }

    /// <summary>
    /// Update CSR edge weights from a delegate without rebuilding CSR structure.
    /// </summary>
    public void UpdateEdgeWeights(Func<int, int, double> weightProvider)
    {
        ArgumentNullException.ThrowIfNull(weightProvider);

        if (NodeCount == 0 || Nnz == 0)
            return;

        for (int i = 0; i < NodeCount; i++)
        {
            int start = _rowOffsets[i];
            int end = _rowOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                int j = _colIndices[k];
                _edgeWeights[k] = weightProvider(i, j);
            }
        }

        if (_edgeWeightsBuffer is not null)
        {
            ReplaceEdgeWeightsBuffer();
        }
    }

    private void ReplaceEdgeWeightsBuffer()
    {
        // Replace only the edge-weights GPU buffer. Row/col buffers remain valid.
        _edgeWeightsBuffer?.Dispose();
        _edgeWeightsBuffer = _device.AllocateReadOnlyBuffer(_edgeWeights);
    }

    private void DisposeGpuBuffers()
    {
        _rowOffsetsBuffer?.Dispose();
        _colIndicesBuffer?.Dispose();
        _edgeWeightsBuffer?.Dispose();
        _nodePotentialBuffer?.Dispose();

        _rowOffsetsBuffer = null;
        _colIndicesBuffer = null;
        _edgeWeightsBuffer = null;
        _nodePotentialBuffer = null;

        _gpuBuffersAllocated = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeGpuBuffers();
        _disposed = true;
    }
}
