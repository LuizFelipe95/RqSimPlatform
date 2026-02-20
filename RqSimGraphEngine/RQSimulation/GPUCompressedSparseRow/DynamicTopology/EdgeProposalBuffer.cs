using System;
using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// Edge proposal for dynamic topology changes.
/// Represents a proposed edge addition or deletion.
/// </summary>
public readonly struct EdgeProposal
{
    /// <summary>Source node of the proposed edge.</summary>
    public readonly int NodeA;
    
    /// <summary>Target node of the proposed edge.</summary>
    public readonly int NodeB;
    
    /// <summary>Initial weight for added edges (ignored for deletions).</summary>
    public readonly float Weight;
    
    /// <summary>1 for addition, 0 for deletion.</summary>
    public readonly int IsAddition;

    public EdgeProposal(int nodeA, int nodeB, float weight, bool isAddition)
    {
        NodeA = nodeA;
        NodeB = nodeB;
        Weight = weight;
        IsAddition = isAddition ? 1 : 0;
    }
}

/// <summary>
/// GPU buffer for collecting edge proposals during MCMC steps.
/// 
/// Usage pattern:
/// 1. Reset counters before each topology proposal phase
/// 2. Shaders atomically add proposals via AppendProposal
/// 3. Host reads back proposal counts and data
/// 4. DynamicCsrTopology applies accepted proposals
/// 
/// Buffer layout:
/// - AdditionCounter[0]: Number of edge additions proposed
/// - DeletionCounter[0]: Number of edge deletions proposed
/// - AdditionCandidates[]: (nodeA, nodeB) pairs for new edges
/// - DeletionCandidates[]: Edge indices in current CSR to remove
/// </summary>
public sealed class EdgeProposalBuffer : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;

    // Atomic counters for proposal tracking
    private ReadWriteBuffer<int>? _additionCounterBuffer;
    private ReadWriteBuffer<int>? _deletionCounterBuffer;
    
    // Proposal data buffers
    private ReadWriteBuffer<Int2>? _additionCandidatesBuffer;  // (nodeA, nodeB) pairs
    private ReadWriteBuffer<float>? _additionWeightsBuffer;    // Initial weights
    private ReadWriteBuffer<int>? _deletionCandidatesBuffer;   // Edge indices to delete
    
    // Capacity tracking
    private int _additionCapacity;
    private int _deletionCapacity;

    /// <summary>
    /// Maximum number of edge additions per topology step.
    /// Default allows for ~1% of edges to be added per step.
    /// </summary>
    public int AdditionCapacity => _additionCapacity;

    /// <summary>
    /// Maximum number of edge deletions per topology step.
    /// </summary>
    public int DeletionCapacity => _deletionCapacity;

    /// <summary>
    /// GPU buffer for atomic addition counter.
    /// </summary>
    public ReadWriteBuffer<int> AdditionCounter => _additionCounterBuffer 
        ?? throw new InvalidOperationException("Buffer not allocated");

    /// <summary>
    /// GPU buffer for atomic deletion counter.
    /// </summary>
    public ReadWriteBuffer<int> DeletionCounter => _deletionCounterBuffer
        ?? throw new InvalidOperationException("Buffer not allocated");

    /// <summary>
    /// GPU buffer for addition candidates (Int2: nodeA, nodeB).
    /// </summary>
    public ReadWriteBuffer<Int2> AdditionCandidates => _additionCandidatesBuffer
        ?? throw new InvalidOperationException("Buffer not allocated");

    /// <summary>
    /// GPU buffer for addition weights.
    /// </summary>
    public ReadWriteBuffer<float> AdditionWeights => _additionWeightsBuffer
        ?? throw new InvalidOperationException("Buffer not allocated");

    /// <summary>
    /// GPU buffer for deletion candidates (edge indices).
    /// </summary>
    public ReadWriteBuffer<int> DeletionCandidates => _deletionCandidatesBuffer
        ?? throw new InvalidOperationException("Buffer not allocated");

    /// <summary>
    /// Whether buffers are allocated and ready.
    /// </summary>
    public bool IsAllocated => _additionCounterBuffer is not null;

    public EdgeProposalBuffer(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Allocate proposal buffers with specified capacities.
    /// </summary>
    /// <param name="additionCapacity">Max edge additions per step</param>
    /// <param name="deletionCapacity">Max edge deletions per step</param>
    public void Allocate(int additionCapacity, int deletionCapacity)
    {
        if (additionCapacity <= 0) 
            throw new ArgumentOutOfRangeException(nameof(additionCapacity));
        if (deletionCapacity <= 0) 
            throw new ArgumentOutOfRangeException(nameof(deletionCapacity));

        DisposeBuffers();

        _additionCapacity = additionCapacity;
        _deletionCapacity = deletionCapacity;

        _additionCounterBuffer = _device.AllocateReadWriteBuffer<int>(1);
        _deletionCounterBuffer = _device.AllocateReadWriteBuffer<int>(1);
        _additionCandidatesBuffer = _device.AllocateReadWriteBuffer<Int2>(additionCapacity);
        _additionWeightsBuffer = _device.AllocateReadWriteBuffer<float>(additionCapacity);
        _deletionCandidatesBuffer = _device.AllocateReadWriteBuffer<int>(deletionCapacity);
    }

    /// <summary>
    /// Reallocate buffers if capacity needs to grow.
    /// </summary>
    public void EnsureCapacity(int additionCapacity, int deletionCapacity)
    {
        if (additionCapacity > _additionCapacity || deletionCapacity > _deletionCapacity)
        {
            Allocate(
                System.Math.Max(additionCapacity, _additionCapacity * 2),
                System.Math.Max(deletionCapacity, _deletionCapacity * 2));
        }
    }

    /// <summary>
    /// Reset counters to zero before new proposal phase.
    /// Must be called before each topology evolution step.
    /// </summary>
    public void Reset()
    {
        if (!IsAllocated)
            throw new InvalidOperationException("Buffer not allocated");

        // Zero the counters
        _additionCounterBuffer!.CopyFrom([0]);
        _deletionCounterBuffer!.CopyFrom([0]);
    }

    /// <summary>
    /// Read back the number of proposed additions.
    /// </summary>
    public int GetAdditionCount()
    {
        if (!IsAllocated)
            return 0;

        int[] count = new int[1];
        _additionCounterBuffer!.CopyTo(count);
        return System.Math.Min(count[0], _additionCapacity);
    }

    /// <summary>
    /// Read back the number of proposed deletions.
    /// </summary>
    public int GetDeletionCount()
    {
        if (!IsAllocated)
            return 0;

        int[] count = new int[1];
        _deletionCounterBuffer!.CopyTo(count);
        return System.Math.Min(count[0], _deletionCapacity);
    }

    /// <summary>
    /// Download all addition proposals to host.
    /// </summary>
    /// <returns>Arrays of (nodeA, nodeB) pairs and weights</returns>
    public (Int2[] candidates, float[] weights, int count) DownloadAdditions()
    {
        int count = GetAdditionCount();
        if (count == 0)
            return ([], [], 0);

        Int2[] candidates = new Int2[count];
        float[] weights = new float[count];

        _additionCandidatesBuffer!.CopyTo(candidates.AsSpan());
        _additionWeightsBuffer!.CopyTo(weights.AsSpan());

        return (candidates, weights, count);
    }

    /// <summary>
    /// Download all deletion proposals to host.
    /// </summary>
    /// <returns>Array of edge indices to delete and count</returns>
    public (int[] edgeIndices, int count) DownloadDeletions()
    {
        int count = GetDeletionCount();
        if (count == 0)
            return ([], 0);

        int[] indices = new int[count];
        _deletionCandidatesBuffer!.CopyTo(indices.AsSpan());

        return (indices, count);
    }

    private void DisposeBuffers()
    {
        _additionCounterBuffer?.Dispose();
        _deletionCounterBuffer?.Dispose();
        _additionCandidatesBuffer?.Dispose();
        _additionWeightsBuffer?.Dispose();
        _deletionCandidatesBuffer?.Dispose();

        _additionCounterBuffer = null;
        _deletionCounterBuffer = null;
        _additionCandidatesBuffer = null;
        _additionWeightsBuffer = null;
        _deletionCandidatesBuffer = null;

        _additionCapacity = 0;
        _deletionCapacity = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}
