using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Shaders;

/// <summary>
/// BLELLOCH SCAN KERNELS
/// =====================
/// Implements work-efficient parallel prefix sum (Blelloch scan) on GPU.
/// 
/// The Blelloch algorithm has two phases:
/// 1. Up-sweep (reduce): Build partial sums bottom-up
/// 2. Down-sweep: Propagate prefix sums top-down
/// 
/// For arrays larger than a single thread group, we use a hierarchical approach:
/// - Scan each block, store block sums
/// - Recursively scan block sums
/// - Add block sums back to each element
/// </summary>

/// <summary>
/// Up-sweep (reduce) phase of Blelloch scan.
/// Each step doubles the stride and adds elements.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct BlellochUpSweepKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Data;
    public readonly int N;
    public readonly int Stride;    // Current stride (1, 2, 4, 8, ...)
    public readonly int Offset;    // Offset = 2 * stride - 1

    public BlellochUpSweepKernel(
        ReadWriteBuffer<int> data,
        int n,
        int stride,
        int offset)
    {
        Data = data;
        N = n;
        Stride = stride;
        Offset = offset;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        
        // Each thread handles one pair at distance 2*stride
        int idx = (i + 1) * 2 * Stride - 1;
        
        if (idx < N)
        {
            int leftChild = idx - Stride;
            Data[idx] = Data[idx] + Data[leftChild];
        }
    }
}

/// <summary>
/// Down-sweep phase of Blelloch scan.
/// Propagates prefix sums from root to leaves.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct BlellochDownSweepKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Data;
    public readonly int N;
    public readonly int Stride;

    public BlellochDownSweepKernel(
        ReadWriteBuffer<int> data,
        int n,
        int stride)
    {
        Data = data;
        N = n;
        Stride = stride;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        
        // Each thread handles one node at current level
        int idx = (i + 1) * 2 * Stride - 1;
        
        if (idx < N)
        {
            int leftChild = idx - Stride;
            int temp = Data[leftChild];
            Data[leftChild] = Data[idx];
            Data[idx] = Data[idx] + temp;
        }
    }
}

/// <summary>
/// Set root to zero before down-sweep (for exclusive scan).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct BlellochSetRootZeroKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Data;
    public readonly int RootIndex;

    public BlellochSetRootZeroKernel(
        ReadWriteBuffer<int> data,
        int rootIndex)
    {
        Data = data;
        RootIndex = rootIndex;
    }

    public void Execute()
    {
        if (ThreadIds.X == 0)
        {
            Data[RootIndex] = 0;
        }
    }
}

/// <summary>
/// Add block sums to elements after hierarchical scan.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct BlellochAddBlockSumsKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Data;
    public readonly ReadOnlyBuffer<int> BlockSums;
    public readonly int BlockSize;
    public readonly int N;

    public BlellochAddBlockSumsKernel(
        ReadWriteBuffer<int> data,
        ReadOnlyBuffer<int> blockSums,
        int blockSize,
        int n)
    {
        Data = data;
        BlockSums = blockSums;
        BlockSize = blockSize;
        N = n;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= N) return;
        
        int blockIdx = i / BlockSize;
        if (blockIdx > 0)
        {
            Data[i] = Data[i] + BlockSums[blockIdx];
        }
    }
}

/// <summary>
/// Extract block sums (last element of each block after up-sweep).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct BlellochExtractBlockSumsKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> Data;
    public readonly ReadWriteBuffer<int> BlockSums;
    public readonly int BlockSize;
    public readonly int NumBlocks;

    public BlellochExtractBlockSumsKernel(
        ReadOnlyBuffer<int> data,
        ReadWriteBuffer<int> blockSums,
        int blockSize,
        int numBlocks)
    {
        Data = data;
        BlockSums = blockSums;
        BlockSize = blockSize;
        NumBlocks = numBlocks;
    }

    public void Execute()
    {
        int blockIdx = ThreadIds.X;
        if (blockIdx >= NumBlocks) return;
        
        // Last element of block holds the block sum after up-sweep
        int lastElementIdx = (blockIdx + 1) * BlockSize - 1;
        BlockSums[blockIdx] = Data[lastElementIdx];
    }
}

/// <summary>
/// Simple Hillis-Steele style inclusive scan step (kept for compatibility).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct PrefixSumKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> InputValues;
    public readonly ReadWriteBuffer<int> OutputOffsets;
    public readonly int N;
    public readonly int Step;

    public PrefixSumKernel(
        ReadWriteBuffer<int> inputValues,
        ReadWriteBuffer<int> outputOffsets,
        int n,
        int step)
    {
        InputValues = inputValues;
        OutputOffsets = outputOffsets;
        N = n;
        Step = step;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= N) return;

        if (i >= Step)
            OutputOffsets[i] = InputValues[i] + InputValues[i - Step];
        else
            OutputOffsets[i] = InputValues[i];
    }
}

/// <summary>
/// Mark edges for compaction: 1 if weight >= threshold, 0 otherwise.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct MarkActiveEdgesKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> Flags;
    public readonly double Threshold;
    public readonly int EdgeCount;

    public MarkActiveEdgesKernel(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> flags,
        double threshold,
        int edgeCount)
    {
        Weights = weights;
        Flags = flags;
        Threshold = threshold;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= EdgeCount) return;
        
        Flags[e] = Weights[e] >= Threshold ? 1 : 0;
    }
}

/// <summary>
/// Compute new degree per node by counting active edges in old CSR.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct ComputeNewDegreesKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> OldRowOffsets;
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> NewDegrees;
    public readonly double Threshold;
    public readonly int NodeCount;

    public ComputeNewDegreesKernel(
        ReadOnlyBuffer<int> oldRowOffsets,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> newDegrees,
        double threshold,
        int nodeCount)
    {
        OldRowOffsets = oldRowOffsets;
        Weights = weights;
        NewDegrees = newDegrees;
        Threshold = threshold;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= NodeCount) return;
        
        int start = OldRowOffsets[node];
        int end = OldRowOffsets[node + 1];
        int count = 0;
        
        for (int k = start; k < end; k++)
        {
            if (Weights[k] >= Threshold)
                count++;
        }
        
        NewDegrees[node] = count;
    }
}
