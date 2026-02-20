using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Shaders;

/// <summary>
/// GPU reduction kernels for computing dot products and norms.
/// Uses multi-stage reduction with block-level accumulation.
/// 
/// ALGORITHM:
/// 1. Each thread block (256 threads) accumulates a partial sum
/// 2. Partial sums are written to an intermediate buffer
/// 3. If many blocks, repeat reduction on partial sums
/// 4. Final small sum is computed on CPU or single-block GPU
/// </summary>

/// <summary>
/// Complex Hermitian inner product: ?a, b? = ? conj(a_i) * b_i
/// Result is complex. Computes partial sums per thread block.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComplexDotProductKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> a;
    public readonly ReadWriteBuffer<Double2> b;
    public readonly ReadWriteBuffer<Double2> partialSums;
    public readonly int length;
    public readonly int blockSize;

    public ComplexDotProductKernel(ReadWriteBuffer<Double2> a, ReadWriteBuffer<Double2> b,
                                    ReadWriteBuffer<Double2> partialSums, int length, int blockSize)
    {
        this.a = a;
        this.b = b;
        this.partialSums = partialSums;
        this.length = length;
        this.blockSize = blockSize;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / blockSize;
        int localId = idx % blockSize;

        // Only first thread in each block computes the sum
        // This is a simple (not optimal) approach that avoids shared memory issues
        if (localId == 0)
        {
            int start = blockId * blockSize;
            int end = start + blockSize;
            if (end > length) end = length;

            Double2 blockSum = new Double2(0, 0);
            for (int i = start; i < end; i++)
            {
                // Hermitian: conj(a) * b = (a.r*b.r + a.i*b.i) + i*(a.r*b.i - a.i*b.r)
                blockSum.X += a[i].X * b[i].X + a[i].Y * b[i].Y;
                blockSum.Y += a[i].X * b[i].Y - a[i].Y * b[i].X;
            }
            partialSums[blockId] = blockSum;
        }
    }
}

/// <summary>
/// Squared norm: ||v||? = ? |v_i|? = ? (v_i.r? + v_i.i?)
/// Result is real. Computes partial sums per thread block.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SquaredNormKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> v;
    public readonly ReadWriteBuffer<double> partialSums;
    public readonly int length;
    public readonly int blockSize;

    public SquaredNormKernel(ReadWriteBuffer<Double2> v, ReadWriteBuffer<double> partialSums,
                             int length, int blockSize)
    {
        this.v = v;
        this.partialSums = partialSums;
        this.length = length;
        this.blockSize = blockSize;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / blockSize;
        int localId = idx % blockSize;

        if (localId == 0)
        {
            int start = blockId * blockSize;
            int end = start + blockSize;
            if (end > length) end = length;

            double blockSum = 0;
            for (int i = start; i < end; i++)
            {
                blockSum += v[i].X * v[i].X + v[i].Y * v[i].Y;
            }
            partialSums[blockId] = blockSum;
        }
    }
}

/// <summary>
/// Real dot product for reduction: ? a_i * b_i
/// Both a and b are real (double) arrays.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct RealDotProductKernel : IComputeShader
{
    public readonly ReadWriteBuffer<double> a;
    public readonly ReadWriteBuffer<double> b;
    public readonly ReadWriteBuffer<double> partialSums;
    public readonly int length;
    public readonly int blockSize;

    public RealDotProductKernel(ReadWriteBuffer<double> a, ReadWriteBuffer<double> b,
                                 ReadWriteBuffer<double> partialSums, int length, int blockSize)
    {
        this.a = a;
        this.b = b;
        this.partialSums = partialSums;
        this.length = length;
        this.blockSize = blockSize;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / blockSize;
        int localId = idx % blockSize;

        if (localId == 0)
        {
            int start = blockId * blockSize;
            int end = start + blockSize;
            if (end > length) end = length;

            double blockSum = 0;
            for (int i = start; i < end; i++)
            {
                blockSum += a[i] * b[i];
            }
            partialSums[blockId] = blockSum;
        }
    }
}

/// <summary>
/// Sum reduction for Double2 array (second stage).
/// Sums partial sums from previous stage.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SumReduceDouble2Kernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> input;
    public readonly ReadWriteBuffer<Double2> output;
    public readonly int length;
    public readonly int blockSize;

    public SumReduceDouble2Kernel(ReadWriteBuffer<Double2> input, ReadWriteBuffer<Double2> output,
                                   int length, int blockSize)
    {
        this.input = input;
        this.output = output;
        this.length = length;
        this.blockSize = blockSize;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / blockSize;
        int localId = idx % blockSize;

        if (localId == 0)
        {
            int start = blockId * blockSize;
            int end = start + blockSize;
            if (end > length) end = length;

            Double2 blockSum = new Double2(0, 0);
            for (int i = start; i < end; i++)
            {
                blockSum.X += input[i].X;
                blockSum.Y += input[i].Y;
            }
            output[blockId] = blockSum;
        }
    }
}

/// <summary>
/// Sum reduction for double array (second stage).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SumReduceDoubleKernel : IComputeShader
{
    public readonly ReadWriteBuffer<double> input;
    public readonly ReadWriteBuffer<double> output;
    public readonly int length;
    public readonly int blockSize;

    public SumReduceDoubleKernel(ReadWriteBuffer<double> input, ReadWriteBuffer<double> output,
                                  int length, int blockSize)
    {
        this.input = input;
        this.output = output;
        this.length = length;
        this.blockSize = blockSize;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / blockSize;
        int localId = idx % blockSize;

        if (localId == 0)
        {
            int start = blockId * blockSize;
            int end = start + blockSize;
            if (end > length) end = length;

            double blockSum = 0;
            for (int i = start; i < end; i++)
            {
                blockSum += input[i];
            }
            output[blockId] = blockSum;
        }
    }
}

/// <summary>
/// Optimized parallel reduction for squared norm.
/// Uses block-level reduction without wave intrinsics for compatibility.
/// </summary>
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ParallelReduceNormKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> v;
    public readonly ReadWriteBuffer<double> partialSums;
    public readonly int length;
    public readonly int numBlocks;

    public ParallelReduceNormKernel(ReadWriteBuffer<Double2> v, ReadWriteBuffer<double> partialSums, 
                                     int length, int numBlocks)
    {
        this.v = v;
        this.partialSums = partialSums;
        this.length = length;
        this.numBlocks = numBlocks;
    }

    public void Execute()
    {
        int globalIdx = ThreadIds.X;
        int blockIdx = globalIdx / 64;
        int localIdx = globalIdx % 64;

        if (blockIdx >= numBlocks) return;

        // Each block handles a portion of the array
        int elementsPerBlock = (length + numBlocks - 1) / numBlocks;
        int blockStart = blockIdx * elementsPerBlock;
        int blockEnd = blockStart + elementsPerBlock;
        if (blockEnd > length) blockEnd = length;

        // Only first thread in block computes the sum
        if (localIdx == 0)
        {
            double blockSum = 0;
            for (int i = blockStart; i < blockEnd; i++)
            {
                blockSum += v[i].X * v[i].X + v[i].Y * v[i].Y;
            }
            partialSums[blockIdx] = blockSum;
        }
    }
}
