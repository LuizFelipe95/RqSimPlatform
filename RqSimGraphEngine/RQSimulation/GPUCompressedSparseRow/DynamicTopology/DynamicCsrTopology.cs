using System;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// Dynamic CSR Topology
/// ====================
/// Extended CsrTopology with support for dynamic topology changes.
/// 
/// Features:
/// - Version tracking for change detection
/// - Capacity-based buffer allocation with growth
/// - Swap-buffer pattern for efficient updates
/// - Direct CSR array initialization
/// </summary>
public sealed class DynamicCsrTopology : IDisposable
{
    // CPU arrays
    private int[] _rowOffsets = [];
    private int[] _colIndices = [];
    private double[] _edgeWeights = [];
    private double[] _nodePotential = [];

    // GPU buffers
    private ReadOnlyBuffer<int>? _rowOffsetsBuffer;
    private ReadOnlyBuffer<int>? _colIndicesBuffer;
    private ReadOnlyBuffer<double>? _edgeWeightsBuffer;
    private ReadOnlyBuffer<double>? _nodePotentialBuffer;

    // ReadWrite buffers for in-place updates
    private ReadWriteBuffer<double>? _edgeWeightsRwBuffer;

    private readonly GraphicsDevice _device;
    private bool _disposed;
    private bool _gpuBuffersAllocated;

    // Capacity tracking for buffer reuse
    private int _rowOffsetsCapacity;
    private int _colIndicesCapacity;

    /// <summary>
    /// Number of nodes in the graph.
    /// </summary>
    public int NodeCount { get; private set; }

    /// <summary>
    /// Number of non-zero elements (edges).
    /// </summary>
    public int Nnz { get; private set; }

    /// <summary>
    /// Topology version, increments on each change.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Whether GPU buffers are allocated.
    /// </summary>
    public bool IsGpuReady => _gpuBuffersAllocated;

    /// <summary>
    /// Current edge capacity (can be larger than Nnz).
    /// </summary>
    public int EdgeCapacity => _colIndicesCapacity;

    // CPU array accessors
    public ReadOnlySpan<int> RowOffsets => _rowOffsets;
    public ReadOnlySpan<int> ColIndices => _colIndices.AsSpan(0, Nnz);
    public ReadOnlySpan<double> EdgeWeights => _edgeWeights.AsSpan(0, Nnz);
    public ReadOnlySpan<double> NodePotential => _nodePotential;

    // GPU buffer accessors
    public ReadOnlyBuffer<int> RowOffsetsBuffer => _rowOffsetsBuffer
        ?? throw new InvalidOperationException("GPU buffers not allocated");
    
    public ReadOnlyBuffer<int> ColIndicesBuffer => _colIndicesBuffer
        ?? throw new InvalidOperationException("GPU buffers not allocated");
    
    public ReadOnlyBuffer<double> EdgeWeightsBuffer => _edgeWeightsBuffer
        ?? throw new InvalidOperationException("GPU buffers not allocated");
    
    public ReadOnlyBuffer<double> NodePotentialBuffer => _nodePotentialBuffer
        ?? throw new InvalidOperationException("GPU buffers not allocated");

    /// <summary>
    /// ReadWrite buffer for edge weights (for soft rewiring).
    /// </summary>
    public ReadWriteBuffer<double> EdgeWeightsRwBuffer
    {
        get
        {
            if (_edgeWeightsRwBuffer is null)
            {
                if (Nnz == 0)
                    throw new InvalidOperationException("No edges in topology");
                
                _edgeWeightsRwBuffer = _device.AllocateReadWriteBuffer<double>(Nnz);
                _edgeWeightsRwBuffer.CopyFrom(_edgeWeights.AsSpan(0, Nnz));
            }
            return _edgeWeightsRwBuffer;
        }
    }

    public DynamicCsrTopology(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Build from pre-computed CSR arrays.
    /// Used by GpuDynamicTopologyEngine after topology evolution.
    /// </summary>
    public void BuildFromCsrArrays(
        int nodeCount,
        int nnz,
        int[] rowOffsets,
        int[] colIndices,
        double[] edgeWeights,
        double[]? potential = null)
    {
        ArgumentNullException.ThrowIfNull(rowOffsets);
        ArgumentNullException.ThrowIfNull(colIndices);
        ArgumentNullException.ThrowIfNull(edgeWeights);

        if (rowOffsets.Length != nodeCount + 1)
            throw new ArgumentException($"rowOffsets length {rowOffsets.Length} != {nodeCount + 1}");
        if (colIndices.Length < nnz)
            throw new ArgumentException($"colIndices length {colIndices.Length} < nnz {nnz}");
        if (edgeWeights.Length < nnz)
            throw new ArgumentException($"edgeWeights length {edgeWeights.Length} < nnz {nnz}");

        NodeCount = nodeCount;
        Nnz = nnz;

        // Allocate or reuse arrays
        EnsureCapacity(nodeCount, nnz);

        // Copy data
        rowOffsets.CopyTo(_rowOffsets, 0);
        colIndices.AsSpan(0, nnz).CopyTo(_colIndices);
        edgeWeights.AsSpan(0, nnz).CopyTo(_edgeWeights);

        if (potential is not null && potential.Length == nodeCount)
        {
            potential.CopyTo(_nodePotential, 0);
        }
        else
        {
            Array.Clear(_nodePotential, 0, nodeCount);
        }

        // Invalidate GPU buffers
        DisposeGpuBuffers();
        Version++;
    }

    /// <summary>
    /// Build from standard CsrTopology.
    /// </summary>
    public void BuildFromCsrTopology(CsrTopology source)
    {
        ArgumentNullException.ThrowIfNull(source);

        NodeCount = source.NodeCount;
        Nnz = source.Nnz;

        EnsureCapacity(NodeCount, Nnz);

        source.RowOffsets.CopyTo(_rowOffsets);
        source.ColIndices.CopyTo(_colIndices);
        source.EdgeWeights.CopyTo(_edgeWeights);
        source.NodePotential.CopyTo(_nodePotential);

        DisposeGpuBuffers();
        Version++;
    }

    /// <summary>
    /// Ensure arrays have sufficient capacity.
    /// </summary>
    private void EnsureCapacity(int nodeCount, int nnz)
    {
        if (_rowOffsetsCapacity < nodeCount + 1)
        {
            int newCapacity = System.Math.Max(nodeCount + 1, (int)(_rowOffsetsCapacity * 1.5));
            _rowOffsets = new int[newCapacity];
            _nodePotential = new double[newCapacity - 1];
            _rowOffsetsCapacity = newCapacity;
        }

        if (_colIndicesCapacity < nnz)
        {
            int newCapacity = System.Math.Max(nnz, (int)(_colIndicesCapacity * 1.5));
            _colIndices = new int[newCapacity];
            _edgeWeights = new double[newCapacity];
            _colIndicesCapacity = newCapacity;
        }
    }

    /// <summary>
    /// Upload to GPU.
    /// </summary>
    public void UploadToGpu()
    {
        if (_gpuBuffersAllocated) return;
        if (NodeCount == 0)
            throw new InvalidOperationException("Topology not initialized");

        DisposeGpuBuffers();

        _rowOffsetsBuffer = _device.AllocateReadOnlyBuffer(_rowOffsets.AsSpan(0, NodeCount + 1));
        _colIndicesBuffer = _device.AllocateReadOnlyBuffer(_colIndices.AsSpan(0, Nnz));
        _edgeWeightsBuffer = _device.AllocateReadOnlyBuffer(_edgeWeights.AsSpan(0, Nnz));
        _nodePotentialBuffer = _device.AllocateReadOnlyBuffer(_nodePotential.AsSpan(0, NodeCount));

        _gpuBuffersAllocated = true;
    }

    /// <summary>
    /// Sync edge weights from GPU to CPU.
    /// </summary>
    public void SyncWeightsFromGpu()
    {
        if (_edgeWeightsRwBuffer is not null && Nnz > 0)
        {
            _edgeWeightsRwBuffer.CopyTo(_edgeWeights.AsSpan(0, Nnz));
        }
    }

    /// <summary>
    /// Sync edge weights from CPU to GPU.
    /// </summary>
    public void SyncWeightsToGpu()
    {
        if (_edgeWeightsBuffer is not null && Nnz > 0)
        {
            // Need to recreate readonly buffer
            _edgeWeightsBuffer.Dispose();
            _edgeWeightsBuffer = _device.AllocateReadOnlyBuffer(_edgeWeights.AsSpan(0, Nnz));
        }

        if (_edgeWeightsRwBuffer is not null && Nnz > 0)
        {
            _edgeWeightsRwBuffer.CopyFrom(_edgeWeights.AsSpan(0, Nnz));
        }
    }

    /// <summary>
    /// Update node potentials.
    /// </summary>
    public void UpdatePotential(ReadOnlySpan<double> potential)
    {
        if (potential.Length != NodeCount)
            throw new ArgumentException($"Potential length {potential.Length} != NodeCount {NodeCount}");

        potential.CopyTo(_nodePotential);

        if (_nodePotentialBuffer is not null)
        {
            _nodePotentialBuffer.Dispose();
            _nodePotentialBuffer = _device.AllocateReadOnlyBuffer(potential);
        }
    }

    /// <summary>
    /// Convert to standard CsrTopology.
    /// </summary>
    public CsrTopology ToStandardTopology()
    {
        var topology = new CsrTopology(_device);
        
        // Build source nodes from CSR
        int[] sourceNodes = new int[Nnz];
        for (int i = 0; i < NodeCount; i++)
        {
            int start = _rowOffsets[i];
            int end = _rowOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                sourceNodes[k] = i;
            }
        }

        topology.BuildFromEdgeList(
            NodeCount,
            sourceNodes,
            _colIndices.AsSpan(0, Nnz),
            _edgeWeights.AsSpan(0, Nnz),
            _nodePotential.AsSpan(0, NodeCount));

        return topology;
    }

    /// <summary>
    /// Get edge weight by CSR index.
    /// </summary>
    public double GetWeight(int edgeIndex)
    {
        if (edgeIndex < 0 || edgeIndex >= Nnz)
            throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        return _edgeWeights[edgeIndex];
    }

    /// <summary>
    /// Set edge weight by CSR index.
    /// </summary>
    public void SetWeight(int edgeIndex, double weight)
    {
        if (edgeIndex < 0 || edgeIndex >= Nnz)
            throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        _edgeWeights[edgeIndex] = weight;
    }

    /// <summary>
    /// Find edge index for (source, target) pair.
    /// Returns -1 if not found.
    /// </summary>
    public int FindEdge(int source, int target)
    {
        if (source < 0 || source >= NodeCount)
            return -1;

        int start = _rowOffsets[source];
        int end = _rowOffsets[source + 1];

        for (int k = start; k < end; k++)
        {
            if (_colIndices[k] == target)
                return k;
        }
        return -1;
    }

    /// <summary>
    /// Get degree of a node.
    /// </summary>
    public int GetDegree(int node)
    {
        if (node < 0 || node >= NodeCount)
            throw new ArgumentOutOfRangeException(nameof(node));
        return _rowOffsets[node + 1] - _rowOffsets[node];
    }

    /// <summary>
    /// Get neighbors of a node.
    /// </summary>
    public ReadOnlySpan<int> GetNeighbors(int node)
    {
        if (node < 0 || node >= NodeCount)
            throw new ArgumentOutOfRangeException(nameof(node));
        
        int start = _rowOffsets[node];
        int end = _rowOffsets[node + 1];
        return _colIndices.AsSpan(start, end - start);
    }

    private void DisposeGpuBuffers()
    {
        _rowOffsetsBuffer?.Dispose();
        _colIndicesBuffer?.Dispose();
        _edgeWeightsBuffer?.Dispose();
        _nodePotentialBuffer?.Dispose();
        _edgeWeightsRwBuffer?.Dispose();

        _rowOffsetsBuffer = null;
        _colIndicesBuffer = null;
        _edgeWeightsBuffer = null;
        _nodePotentialBuffer = null;
        _edgeWeightsRwBuffer = null;

        _gpuBuffersAllocated = false;
    }

    /// <summary>
    /// Replace GPU buffers with externally produced ReadOnlyBuffers.
    /// This performs a shallow swap of the GPU buffers without copying
    /// CPU-side arrays. Caller must ensure that NodeCount and Nnz are
    /// set appropriately and that the provided buffers are valid for
    /// the current topology sizes.
    /// </summary>
    /// <param name="rowOffsetsBuffer">GPU buffer for row offsets (size = NodeCount + 1)</param>
    /// <param name="colIndicesBuffer">GPU buffer for column indices (size = Nnz)</param>
    /// <param name="edgeWeightsBuffer">GPU buffer for edge weights (size = Nnz)</param>
    public void ReplaceGpuBuffers(
        ReadOnlyBuffer<int> rowOffsetsBuffer,
        ReadOnlyBuffer<int> colIndicesBuffer,
        ReadOnlyBuffer<double> edgeWeightsBuffer)
    {
        if (rowOffsetsBuffer is null) throw new ArgumentNullException(nameof(rowOffsetsBuffer));
        if (colIndicesBuffer is null) throw new ArgumentNullException(nameof(colIndicesBuffer));
        if (edgeWeightsBuffer is null) throw new ArgumentNullException(nameof(edgeWeightsBuffer));

        // Dispose previous GPU buffers to avoid leaks
        _rowOffsetsBuffer?.Dispose();
        _colIndicesBuffer?.Dispose();
        _edgeWeightsBuffer?.Dispose();

        // Shallow assign new buffers (ownership handed to this object)
        _rowOffsetsBuffer = rowOffsetsBuffer;
        _colIndicesBuffer = colIndicesBuffer;
        _edgeWeightsBuffer = edgeWeightsBuffer;

        _gpuBuffersAllocated = true;

        // Bump version to indicate topology change
        Version++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeGpuBuffers();
        _disposed = true;
    }
}
