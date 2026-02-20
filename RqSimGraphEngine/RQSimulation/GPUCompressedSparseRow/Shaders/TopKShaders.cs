using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Shaders;

/// <summary>
/// Compute per-block maximum index kernel.
/// Each invocation handles one block (range of edges) and writes the index of the maximum weight within that block.
/// This is a first-stage GPU-assisted Top-K: reduces nnz -> numBlocks candidates which are reduced on CPU.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct TopBlockMaxKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> BlockMaxIndices;
    public readonly int N;
    public readonly int BlockSize;

    public TopBlockMaxKernel(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> blockMaxIndices,
        int n,
        int blockSize)
    {
        Weights = weights;
        BlockMaxIndices = blockMaxIndices;
        N = n;
        BlockSize = blockSize;
    }

    public void Execute()
    {
        int block = ThreadIds.X;
        int start = block * BlockSize;
        if (start >= N)
        {
            BlockMaxIndices[block] = -1;
            return;
        }

        int end = start + BlockSize;
        if (end > N) end = N;

        double best = double.NegativeInfinity;
        int bestIdx = -1;

        for (int i = start; i < end; i++)
        {
            double w = Weights[i];
            if (w > best)
            {
                best = w;
                bestIdx = i;
            }
        }

        BlockMaxIndices[block] = bestIdx;
    }
}

/// <summary>
/// Compute per-block top-M indices kernel.
/// Finds top-M indices in each block by repeated selection; writes results to BlockTopIndices[block * M + m].
/// This avoids dynamic local allocations by reusing the output buffer to store already-selected indices.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct TopBlockTopMKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> BlockTopIndices; // length = numBlocks * M
    public readonly int N;
    public readonly int BlockSize;
    public readonly int M;

    public TopBlockTopMKernel(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> blockTopIndices,
        int n,
        int blockSize,
        int m)
    {
        Weights = weights;
        BlockTopIndices = blockTopIndices;
        N = n;
        BlockSize = blockSize;
        M = m;
    }

    public void Execute()
    {
        int block = ThreadIds.X;
        int start = block * BlockSize;
        int outOffset = block * M;

        // Initialize output slots to -1
        for (int j = 0; j < M; j++)
        {
            BlockTopIndices[outOffset + j] = -1;
        }

        if (start >= N) return;

        int end = start + BlockSize;
        if (end > N) end = N;

        // Repeated selection: for each rank r find the max not yet selected
        for (int r = 0; r < M; r++)
        {
            double best = double.NegativeInfinity;
            int bestIdx = -1;

            for (int i = start; i < end; i++)
            {
                double w = Weights[i];
                if (w <= best) continue;

                // check if i was already selected
                bool selected = false;
                for (int j = 0; j < r; j++)
                {
                    int prev = BlockTopIndices[outOffset + j];
                    if (prev == i)
                    {
                        selected = true;
                        break;
                    }
                }

                if (selected) continue;

                best = w;
                bestIdx = i;
            }

            BlockTopIndices[outOffset + r] = bestIdx;
            if (bestIdx == -1) break; // no more items
        }
    }
}

/// <summary>
/// Partial block max kernel: each thread scans a subrange of a block and writes its local best index.
/// Use to reduce nnz -> numBlocks * partialPerBlock candidates which are then reduced on CPU.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct PartialBlockMaxKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> PartialMaxIndices; // length = numBlocks * partialPerBlock
    public readonly int N;
    public readonly int BlockSize;
    public readonly int PartialPerBlock;

    public PartialBlockMaxKernel(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> partialMaxIndices,
        int n,
        int blockSize,
        int partialPerBlock)
    {
        Weights = weights;
        PartialMaxIndices = partialMaxIndices;
        N = n;
        BlockSize = blockSize;
        PartialPerBlock = partialPerBlock;
    }

    public void Execute()
    {
        int globalThread = ThreadIds.X;
        int block = globalThread / PartialPerBlock;
        int local = globalThread % PartialPerBlock;

        int startBlock = block * BlockSize;
        if (startBlock >= N)
        {
            PartialMaxIndices[globalThread] = -1;
            return;
        }

        int subSize = (BlockSize + PartialPerBlock - 1) / PartialPerBlock;
        int start = startBlock + local * subSize;
        int end = start + subSize;
        int blockEnd = startBlock + BlockSize;
        if (end > blockEnd) end = blockEnd;
        if (end > N) end = N;
        if (start >= end)
        {
            PartialMaxIndices[globalThread] = -1;
            return;
        }

        double best = double.NegativeInfinity;
        int bestIdx = -1;
        for (int i = start; i < end; i++)
        {
            double w = Weights[i];
            if (w > best)
            {
                best = w;
                bestIdx = i;
            }
        }

        PartialMaxIndices[globalThread] = bestIdx;
    }
}

/// <summary>
/// Partial-block top-M kernel: each thread scans its subrange and computes local top-M indices.
/// Writes M indices per thread into PartialTopIndices[threadId * M + r]
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct PartialBlockTopMKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> PartialTopIndices; // length = numThreads * M
    public readonly int N;
    public readonly int BlockSize;
    public readonly int PartialPerBlock;
    public readonly int M;

    public PartialBlockTopMKernel(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> partialTopIndices,
        int n,
        int blockSize,
        int partialPerBlock,
        int m)
    {
        Weights = weights;
        PartialTopIndices = partialTopIndices;
        N = n;
        BlockSize = blockSize;
        PartialPerBlock = partialPerBlock;
        M = m;
    }





    //!!! TODO: проверить на корректность и работы с M>4
    public void Execute()
    {
        int threadId = ThreadIds.X;
        int block = threadId / PartialPerBlock;
        int local = threadId % PartialPerBlock;
        int outOffset = threadId * M;

        // Initialize outputs
        for (int j = 0; j < M; j++)
            PartialTopIndices[outOffset + j] = -1;

        int startBlock = block * BlockSize;
        if (startBlock >= N) return;

        int subSize = (BlockSize + PartialPerBlock - 1) / PartialPerBlock;
        int start = startBlock + local * subSize;
        int end = start + subSize;
        int blockEnd = startBlock + BlockSize;
        if (end > blockEnd) end = blockEnd;
        if (end > N) end = N;
        if (start >= end) return;

        // Use fixed-size stack variables instead of arrays
        double bestVal0 = double.NegativeInfinity, bestVal1 = double.NegativeInfinity, bestVal2 = double.NegativeInfinity, bestVal3 = double.NegativeInfinity;
        int bestIdx0 = -1, bestIdx1 = -1, bestIdx2 = -1, bestIdx3 = -1;

        for (int i = start; i < end; i++)
        {
            double w = Weights[i];
            if (M > 0 && w > bestVal0)
            {
                if (M > 3)
                {
                    bestVal3 = bestVal2; bestIdx3 = bestIdx2;
                }
                if (M > 2)
                {
                    bestVal2 = bestVal1; bestIdx2 = bestIdx1;
                }
                if (M > 1)
                {
                    bestVal1 = bestVal0; bestIdx1 = bestIdx0;
                }
                bestVal0 = w; bestIdx0 = i;
            }
            else if (M > 1 && w > bestVal1)
            {
                if (M > 3)
                {
                    bestVal3 = bestVal2; bestIdx3 = bestIdx2;
                }
                if (M > 2)
                {
                    bestVal2 = bestVal1; bestIdx2 = bestIdx1;
                }
                bestVal1 = w; bestIdx1 = i;
            }
            else if (M > 2 && w > bestVal2)
            {
                if (M > 3)
                {
                    bestVal3 = bestVal2; bestIdx3 = bestIdx2;
                }
                bestVal2 = w; bestIdx2 = i;
            }
            else if (M > 3 && w > bestVal3)
            {
                bestVal3 = w; bestIdx3 = i;
            }
        }

        if (M > 0) PartialTopIndices[outOffset + 0] = bestIdx0;
        if (M > 1) PartialTopIndices[outOffset + 1] = bestIdx1;
        if (M > 2) PartialTopIndices[outOffset + 2] = bestIdx2;
        if (M > 3) PartialTopIndices[outOffset + 3] = bestIdx3;
    }
}

/// <summary>
/// Parallel block top-M kernel with support for M up to 8.
/// Each thread scans a subrange and maintains top-M using insertion sort approach.
/// This is designed for parallel execution within blocks for improved GPU utilization.
/// 
/// Output layout: PartialTopIndices[threadId * M + r] where r in [0..M-1]
/// 
/// This shader overcomes the M<=4 limitation of PartialBlockTopMKernel by using
/// 8 explicit variables for values/indices and conditional insertion logic.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
internal readonly partial struct ParallelBlockTopMKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Weights;
    public readonly ReadWriteBuffer<int> PartialTopIndices; // length = numThreads * M
    public readonly int N;
    public readonly int BlockSize;
    public readonly int ThreadsPerBlock;
    public readonly int M;

    public ParallelBlockTopMKernel(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> partialTopIndices,
        int n,
        int blockSize,
        int threadsPerBlock,
        int m)
    {
        Weights = weights;
        PartialTopIndices = partialTopIndices;
        N = n;
        BlockSize = blockSize;
        ThreadsPerBlock = threadsPerBlock;
        M = m;
    }

    public void Execute()
    {
        int threadId = ThreadIds.X;
        int block = threadId / ThreadsPerBlock;
        int local = threadId % ThreadsPerBlock;
        int outOffset = threadId * M;

        // Initialize outputs to -1
        for (int j = 0; j < M && j < 8; j++)
            PartialTopIndices[outOffset + j] = -1;

        int startBlock = block * BlockSize;
        if (startBlock >= N) return;

        // Each thread handles a subrange within the block
        int subSize = (BlockSize + ThreadsPerBlock - 1) / ThreadsPerBlock;
        int start = startBlock + local * subSize;
        int end = start + subSize;
        int blockEnd = startBlock + BlockSize;
        if (end > blockEnd) end = blockEnd;
        if (end > N) end = N;
        if (start >= end) return;

        // Use 8 explicit variables for top-M tracking (supports M <= 8)
        double v0 = double.NegativeInfinity, v1 = double.NegativeInfinity;
        double v2 = double.NegativeInfinity, v3 = double.NegativeInfinity;
        double v4 = double.NegativeInfinity, v5 = double.NegativeInfinity;
        double v6 = double.NegativeInfinity, v7 = double.NegativeInfinity;
        int i0 = -1, i1 = -1, i2 = -1, i3 = -1;
        int i4 = -1, i5 = -1, i6 = -1, i7 = -1;

        // Clamp M to max 8
        int mClamped = M;
        if (mClamped > 8) mClamped = 8;

        for (int idx = start; idx < end; idx++)
        {
            double w = Weights[idx];

            // Insertion sort: find position and shift
            if (mClamped >= 1 && w > v0)
            {
                // Shift down from position 0
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                if (mClamped >= 7) { v6 = v5; i6 = i5; }
                if (mClamped >= 6) { v5 = v4; i5 = i4; }
                if (mClamped >= 5) { v4 = v3; i4 = i3; }
                if (mClamped >= 4) { v3 = v2; i3 = i2; }
                if (mClamped >= 3) { v2 = v1; i2 = i1; }
                if (mClamped >= 2) { v1 = v0; i1 = i0; }
                v0 = w; i0 = idx;
            }
            else if (mClamped >= 2 && w > v1)
            {
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                if (mClamped >= 7) { v6 = v5; i6 = i5; }
                if (mClamped >= 6) { v5 = v4; i5 = i4; }
                if (mClamped >= 5) { v4 = v3; i4 = i3; }
                if (mClamped >= 4) { v3 = v2; i3 = i2; }
                if (mClamped >= 3) { v2 = v1; i2 = i1; }
                v1 = w; i1 = idx;
            }
            else if (mClamped >= 3 && w > v2)
            {
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                if (mClamped >= 7) { v6 = v5; i6 = i5; }
                if (mClamped >= 6) { v5 = v4; i5 = i4; }
                if (mClamped >= 5) { v4 = v3; i4 = i3; }
                if (mClamped >= 4) { v3 = v2; i3 = i2; }
                v2 = w; i2 = idx;
            }
            else if (mClamped >= 4 && w > v3)
            {
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                if (mClamped >= 7) { v6 = v5; i6 = i5; }
                if (mClamped >= 6) { v5 = v4; i5 = i4; }
                if (mClamped >= 5) { v4 = v3; i4 = i3; }
                v3 = w; i3 = idx;
            }
            else if (mClamped >= 5 && w > v4)
            {
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                if (mClamped >= 7) { v6 = v5; i6 = i5; }
                if (mClamped >= 6) { v5 = v4; i5 = i4; }
                v4 = w; i4 = idx;
            }
            else if (mClamped >= 6 && w > v5)
            {
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                if (mClamped >= 7) { v6 = v5; i6 = i5; }
                v5 = w; i5 = idx;
            }
            else if (mClamped >= 7 && w > v6)
            {
                if (mClamped >= 8) { v7 = v6; i7 = i6; }
                v6 = w; i6 = idx;
            }
            else if (mClamped >= 8 && w > v7)
            {
                v7 = w; i7 = idx;
            }
        }

        // Write results
        if (mClamped >= 1) PartialTopIndices[outOffset + 0] = i0;
        if (mClamped >= 2) PartialTopIndices[outOffset + 1] = i1;
        if (mClamped >= 3) PartialTopIndices[outOffset + 2] = i2;
        if (mClamped >= 4) PartialTopIndices[outOffset + 3] = i3;
        if (mClamped >= 5) PartialTopIndices[outOffset + 4] = i4;
        if (mClamped >= 6) PartialTopIndices[outOffset + 5] = i5;
        if (mClamped >= 7) PartialTopIndices[outOffset + 6] = i6;
        if (mClamped >= 8) PartialTopIndices[outOffset + 7] = i7;
    }
}
