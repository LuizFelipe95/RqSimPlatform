using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Shaders;

/// <summary>
/// STREAM COMPACTION KERNELS
/// =========================
/// Implements GPU stream compaction for CSR topology updates.
/// 
/// The compaction process:
/// 1. Mark active edges (weight >= threshold)
/// 2. Compute new row offsets via prefix sum of degrees
/// 3. Scatter active edges to new positions using atomic counters
/// 
/// ATOMICS NOTE:
/// ComputeSharp supports Hlsl.InterlockedAdd for atomic operations.
/// We use per-row atomic counters for write position tracking.
/// </summary>

/// <summary>
/// Compact CSR using pre-computed scatter indices.
/// Each edge knows its destination from prefix sum of flags.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct CompactCsrScatterKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> OldColIndices;
    public readonly ReadOnlyBuffer<double> OldWeights;
    public readonly ReadWriteBuffer<int> Flags;           // 1 if active, 0 otherwise (changed to ReadWrite)
    public readonly ReadWriteBuffer<int> ScatterIndices;  // Exclusive prefix sum of flags (changed to ReadWrite)
    public readonly ReadWriteBuffer<int> NewColIndices;
    public readonly ReadWriteBuffer<double> NewWeights;
    public readonly int EdgeCount;

    public CompactCsrScatterKernel(
        ReadOnlyBuffer<int> oldColIndices,
        ReadOnlyBuffer<double> oldWeights,
        ReadWriteBuffer<int> flags,
        ReadWriteBuffer<int> scatterIndices,
        ReadWriteBuffer<int> newColIndices,
        ReadWriteBuffer<double> newWeights,
        int edgeCount)
    {
        OldColIndices = oldColIndices;
        OldWeights = oldWeights;
        Flags = flags;
        ScatterIndices = scatterIndices;
        NewColIndices = newColIndices;
        NewWeights = newWeights;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= EdgeCount) return;
        
        // Only scatter active edges
        if (Flags[e] == 1)
        {
            int destIdx = ScatterIndices[e];
            NewColIndices[destIdx] = OldColIndices[e];
            NewWeights[destIdx] = OldWeights[e];
        }
    }
}

/// <summary>
/// Per-row compaction with atomic write position tracking.
/// Each thread processes one row and uses atomic counter for writes.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct CompactCsrAtomicKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> OldRowOffsets;
    public readonly ReadOnlyBuffer<int> OldColIndices;
    public readonly ReadOnlyBuffer<double> OldWeights;
    public readonly ReadWriteBuffer<int> NewRowOffsets;  // changed to ReadWrite
    public readonly ReadWriteBuffer<int> NewColIndices;
    public readonly ReadWriteBuffer<double> NewWeights;
    public readonly ReadWriteBuffer<int> WriteCounters;  // Per-row atomic counter
    public readonly double WeightThreshold;
    public readonly int NodeCount;

    public CompactCsrAtomicKernel(
        ReadOnlyBuffer<int> oldRowOffsets,
        ReadOnlyBuffer<int> oldColIndices,
        ReadOnlyBuffer<double> oldWeights,
        ReadWriteBuffer<int> newRowOffsets,
        ReadWriteBuffer<int> newColIndices,
        ReadWriteBuffer<double> newWeights,
        ReadWriteBuffer<int> writeCounters,
        double weightThreshold,
        int nodeCount)
    {
        OldRowOffsets = oldRowOffsets;
        OldColIndices = oldColIndices;
        OldWeights = oldWeights;
        NewRowOffsets = newRowOffsets;
        NewColIndices = newColIndices;
        NewWeights = newWeights;
        WriteCounters = writeCounters;
        WeightThreshold = weightThreshold;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int row = ThreadIds.X;
        if (row >= NodeCount) return;

        int oldStart = OldRowOffsets[row];
        int oldEnd = OldRowOffsets[row + 1];
        int newStart = NewRowOffsets[row];

        // Process each edge in this row
        for (int k = oldStart; k < oldEnd; k++)
        {
            double w = OldWeights[k];
            if (w >= WeightThreshold)
            {
                // Atomically increment write counter for this row
                int localIdx = 0;
                Hlsl.InterlockedAdd(ref WriteCounters[row], 1, out localIdx);
                
                int writePos = newStart + localIdx;
                NewColIndices[writePos] = OldColIndices[k];
                NewWeights[writePos] = w;
            }
        }
    }
}

/// <summary>
/// Original simple per-row compact kernel (kept for compatibility).
/// Deterministic but may have race conditions if rows overlap.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct CompactCsrKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> OldRowOffsets;
    public readonly ReadOnlyBuffer<int> OldColIndices;
    public readonly ReadOnlyBuffer<double> OldWeights;
    
    public readonly ReadWriteBuffer<int> NewRowOffsets;  // changed to ReadWrite
    public readonly ReadWriteBuffer<int> NewColIndices;
    public readonly ReadWriteBuffer<double> NewWeights;

    public readonly double WeightThreshold;
    public readonly int NodeCount;

    public CompactCsrKernel(
        ReadOnlyBuffer<int> oldRowOffsets,
        ReadOnlyBuffer<int> oldColIndices,
        ReadOnlyBuffer<double> oldWeights,
        ReadWriteBuffer<int> newRowOffsets,
        ReadWriteBuffer<int> newColIndices,
        ReadWriteBuffer<double> newWeights,
        double weightThreshold,
        int nodeCount)
    {
        OldRowOffsets = oldRowOffsets;
        OldColIndices = oldColIndices;
        OldWeights = oldWeights;
        NewRowOffsets = newRowOffsets;
        NewColIndices = newColIndices;
        NewWeights = newWeights;
        WeightThreshold = weightThreshold;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int row = ThreadIds.X;
        if (row >= NodeCount) return;

        int oldStart = OldRowOffsets[row];
        int oldEnd = OldRowOffsets[row + 1];
        int writePos = NewRowOffsets[row];

        int localPos = 0;

        for (int k = oldStart; k < oldEnd; k++)
        {
            double w = OldWeights[k];
            if (w >= WeightThreshold)
            {
                int idx = writePos + localPos;
                NewColIndices[idx] = OldColIndices[k];
                NewWeights[idx] = w;
                localPos++;
            }
        }
    }
}

/// <summary>
/// Zero-initialize write counters before atomic compaction.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct ZeroCountersKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Counters;
    public readonly int Count;

    public ZeroCountersKernel(
        ReadWriteBuffer<int> counters,
        int count)
    {
        Counters = counters;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= Count) return;
        Counters[i] = 0;
    }
}
