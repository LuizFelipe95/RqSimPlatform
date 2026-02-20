using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// HIERARCHICAL PREFIX SCAN FOR DYNAMIC TOPOLOGY
/// ==============================================
/// Extended Blelloch scan implementation optimized for dynamic CSR rebuilds.
/// 
/// Features:
/// - Single-pass in-block scan with shared memory (simulated via registers)
/// - Multi-block hierarchical scan for large arrays
/// - Support for computing RowOffsets from Degrees array
/// 
/// Usage:
/// 1. Degrees[] -> ExclusiveScan -> RowOffsets[]
/// 2. RowOffsets[N] contains total edge count (NNZ)
/// </summary>

/// <summary>
/// Efficient single-block exclusive scan using Kogge-Stone algorithm.
/// Suitable for arrays up to ~1024 elements per block.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct KoggeStoneScanKernel : IComputeShader
{
    /// <summary>Input/output data (modified in place).</summary>
    public readonly ReadWriteBuffer<int> Data;
    
    /// <summary>Number of elements.</summary>
    public readonly int N;
    
    /// <summary>Current stride (1, 2, 4, 8, ...).</summary>
    public readonly int Stride;

    public KoggeStoneScanKernel(
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
        if (i >= N) return;

        // Kogge-Stone: each element adds the element Stride positions back
        if (i >= Stride)
        {
            // Read before barrier-like sync (simulated by multiple dispatches)
            int val = Data[i] + Data[i - Stride];
            // Note: In real GPU code we'd need barriers. 
            // Here we use multiple kernel dispatches for each stride.
            Data[i] = val;
        }
    }
}

/// <summary>
/// Convert inclusive scan to exclusive scan by shifting right.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct InclusiveToExclusiveKernel : IComputeShader
{
    /// <summary>Input: inclusive scan result.</summary>
    public readonly ReadOnlyBuffer<int> InclusiveScan;
    
    /// <summary>Output: exclusive scan result.</summary>
    public readonly ReadWriteBuffer<int> ExclusiveScan;
    
    /// <summary>Number of elements.</summary>
    public readonly int N;

    public InclusiveToExclusiveKernel(
        ReadOnlyBuffer<int> inclusiveScan,
        ReadWriteBuffer<int> exclusiveScan,
        int n)
    {
        InclusiveScan = inclusiveScan;
        ExclusiveScan = exclusiveScan;
        N = n;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= N + 1) return;

        if (i == 0)
        {
            ExclusiveScan[0] = 0;
        }
        else
        {
            ExclusiveScan[i] = InclusiveScan[i - 1];
        }
    }
}

/// <summary>
/// Copy degrees to scan buffer with padding for power-of-2 alignment.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct CopyDegreesToScanBufferKernel : IComputeShader
{
    /// <summary>Input degrees.</summary>
    public readonly ReadOnlyBuffer<int> Degrees;
    
    /// <summary>Output scan buffer (padded to power of 2).</summary>
    public readonly ReadWriteBuffer<int> ScanBuffer;
    
    /// <summary>Actual number of nodes.</summary>
    public readonly int NodeCount;
    
    /// <summary>Padded size (power of 2).</summary>
    public readonly int PaddedSize;

    public CopyDegreesToScanBufferKernel(
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<int> scanBuffer,
        int nodeCount,
        int paddedSize)
    {
        Degrees = degrees;
        ScanBuffer = scanBuffer;
        NodeCount = nodeCount;
        PaddedSize = paddedSize;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= PaddedSize) return;

        if (i < NodeCount)
        {
            ScanBuffer[i] = Degrees[i];
        }
        else
        {
            ScanBuffer[i] = 0; // Pad with zeros
        }
    }
}

/// <summary>
/// Extract row offsets from exclusive scan result.
/// RowOffsets[i] = scan[i] for i in [0, NodeCount]
/// RowOffsets[NodeCount] = total NNZ
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ExtractRowOffsetsKernel : IComputeShader
{
    /// <summary>Exclusive scan result.</summary>
    public readonly ReadOnlyBuffer<int> ScanResult;
    
    /// <summary>Original degrees (needed for last element).</summary>
    public readonly ReadOnlyBuffer<int> Degrees;
    
    /// <summary>Output row offsets (size = NodeCount + 1).</summary>
    public readonly ReadWriteBuffer<int> RowOffsets;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public ExtractRowOffsetsKernel(
        ReadOnlyBuffer<int> scanResult,
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<int> rowOffsets,
        int nodeCount)
    {
        ScanResult = scanResult;
        Degrees = degrees;
        RowOffsets = rowOffsets;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i > NodeCount) return;

        if (i < NodeCount)
        {
            RowOffsets[i] = ScanResult[i];
        }
        else
        {
            // RowOffsets[NodeCount] = total NNZ = last exclusive scan + last degree
            RowOffsets[NodeCount] = ScanResult[NodeCount - 1] + Degrees[NodeCount - 1];
        }
    }
}

/// <summary>
/// Two-phase Blelloch scan optimized for computing RowOffsets.
/// This version works in-place and handles non-power-of-2 sizes.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct BlellochUpSweepDynamicKernel : IComputeShader
{
    /// <summary>Data buffer (modified in place).</summary>
    public readonly ReadWriteBuffer<int> Data;
    
    /// <summary>Padded size (power of 2).</summary>
    public readonly int PaddedN;
    
    /// <summary>Current stride (1, 2, 4, ...).</summary>
    public readonly int Stride;

    public BlellochUpSweepDynamicKernel(
        ReadWriteBuffer<int> data,
        int paddedN,
        int stride)
    {
        Data = data;
        PaddedN = paddedN;
        Stride = stride;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        
        // Each thread handles one pair at distance 2*stride
        int idx = (i + 1) * 2 * Stride - 1;
        
        if (idx < PaddedN)
        {
            int leftChild = idx - Stride;
            Data[idx] = Data[idx] + Data[leftChild];
        }
    }
}

/// <summary>
/// Down-sweep phase for Blelloch scan with dynamic sizing.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct BlellochDownSweepDynamicKernel : IComputeShader
{
    /// <summary>Data buffer (modified in place).</summary>
    public readonly ReadWriteBuffer<int> Data;
    
    /// <summary>Padded size (power of 2).</summary>
    public readonly int PaddedN;
    
    /// <summary>Current stride (n/2, n/4, ...).</summary>
    public readonly int Stride;

    public BlellochDownSweepDynamicKernel(
        ReadWriteBuffer<int> data,
        int paddedN,
        int stride)
    {
        Data = data;
        PaddedN = paddedN;
        Stride = stride;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        
        // Each thread handles one node at current level
        int idx = (i + 1) * 2 * Stride - 1;
        
        if (idx < PaddedN)
        {
            int leftChild = idx - Stride;
            int temp = Data[leftChild];
            Data[leftChild] = Data[idx];
            Data[idx] = Data[idx] + temp;
        }
    }
}

/// <summary>
/// Set root element to zero for exclusive scan.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct SetRootZeroDynamicKernel : IComputeShader
{
    /// <summary>Data buffer.</summary>
    public readonly ReadWriteBuffer<int> Data;
    
    /// <summary>Root index (PaddedN - 1).</summary>
    public readonly int RootIndex;

    public SetRootZeroDynamicKernel(
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
