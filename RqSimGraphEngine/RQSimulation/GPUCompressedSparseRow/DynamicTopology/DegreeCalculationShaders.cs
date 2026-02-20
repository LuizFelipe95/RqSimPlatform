using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// DEGREE CALCULATION SHADERS FOR DYNAMIC TOPOLOGY
/// ================================================
/// Compute new node degrees after edge additions and deletions.
/// 
/// Pipeline:
/// 1. ComputeBaseDegrees: Count surviving edges from old CSR (weight >= threshold, not in deletions)
/// 2. AddContributionsFromAdditions: Increment degrees for new edges
/// 3. Result: NewDegrees[i] = final degree of node i after topology changes
/// </summary>

/// <summary>
/// Compute base degrees from current CSR, excluding edges below weight threshold.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeBaseDegreesKernel : IComputeShader
{
    /// <summary>Current CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>Current edge weights.</summary>
    public readonly ReadOnlyBuffer<double> Weights;
    
    /// <summary>Output: base degrees (before additions).</summary>
    public readonly ReadWriteBuffer<int> BaseDegrees;
    
    /// <summary>Weight threshold - edges below this are considered deleted.</summary>
    public readonly double WeightThreshold;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public ComputeBaseDegreesKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<int> baseDegrees,
        double weightThreshold,
        int nodeCount)
    {
        RowOffsets = rowOffsets;
        Weights = weights;
        BaseDegrees = baseDegrees;
        WeightThreshold = weightThreshold;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= NodeCount) return;

        int start = RowOffsets[node];
        int end = RowOffsets[node + 1];
        int degree = 0;

        for (int k = start; k < end; k++)
        {
            if (Weights[k] >= WeightThreshold)
            {
                degree++;
            }
        }

        BaseDegrees[node] = degree;
    }
}

/// <summary>
/// Compute degrees considering explicit deletion list.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeDegreesWithDeletionsKernel : IComputeShader
{
    /// <summary>Current CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>Current edge weights.</summary>
    public readonly ReadOnlyBuffer<double> Weights;
    
    /// <summary>Flags marking edges for deletion (1=delete, 0=keep).</summary>
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    
    /// <summary>Output: degrees after deletions.</summary>
    public readonly ReadWriteBuffer<int> Degrees;
    
    /// <summary>Weight threshold - edges below this are also excluded.</summary>
    public readonly double WeightThreshold;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public ComputeDegreesWithDeletionsKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<int> deletionFlags,
        ReadWriteBuffer<int> degrees,
        double weightThreshold,
        int nodeCount)
    {
        RowOffsets = rowOffsets;
        Weights = weights;
        DeletionFlags = deletionFlags;
        Degrees = degrees;
        WeightThreshold = weightThreshold;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= NodeCount) return;

        int start = RowOffsets[node];
        int end = RowOffsets[node + 1];
        int degree = 0;

        for (int k = start; k < end; k++)
        {
            // Keep edge if: weight >= threshold AND not marked for deletion
            if (Weights[k] >= WeightThreshold && DeletionFlags[k] == 0)
            {
                degree++;
            }
        }

        Degrees[node] = degree;
    }
}

/// <summary>
/// Atomically increment degrees for nodes involved in edge additions.
/// Each addition (nodeA, nodeB) increments both Degrees[nodeA] and Degrees[nodeB].
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct IncrementDegreesFromAdditionsKernel : IComputeShader
{
    /// <summary>Addition candidates (nodeA, nodeB) pairs.</summary>
    public readonly ReadOnlyBuffer<Int2> AdditionCandidates;
    
    /// <summary>Degrees to increment (modified atomically).</summary>
    public readonly ReadWriteBuffer<int> Degrees;
    
    /// <summary>Number of additions to process.</summary>
    public readonly int AdditionCount;

    public IncrementDegreesFromAdditionsKernel(
        ReadOnlyBuffer<Int2> additionCandidates,
        ReadWriteBuffer<int> degrees,
        int additionCount)
    {
        AdditionCandidates = additionCandidates;
        Degrees = degrees;
        AdditionCount = additionCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= AdditionCount) return;

        Int2 edge = AdditionCandidates[i];
        int nodeA = edge.X;
        int nodeB = edge.Y;

        // Atomically increment both endpoints
        int dummy;
        Hlsl.InterlockedAdd(ref Degrees[nodeA], 1, out dummy);
        Hlsl.InterlockedAdd(ref Degrees[nodeB], 1, out dummy);
    }
}

/// <summary>
/// Mark edges for deletion based on deletion candidate list.
/// Sets DeletionFlags[edgeIdx] = 1 for each edge in the list.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct MarkDeletionFlagsKernel : IComputeShader
{
    /// <summary>List of edge indices to delete.</summary>
    public readonly ReadOnlyBuffer<int> DeletionCandidates;
    
    /// <summary>Output flags (0=keep, 1=delete).</summary>
    public readonly ReadWriteBuffer<int> DeletionFlags;
    
    /// <summary>Number of deletions.</summary>
    public readonly int DeletionCount;

    public MarkDeletionFlagsKernel(
        ReadOnlyBuffer<int> deletionCandidates,
        ReadWriteBuffer<int> deletionFlags,
        int deletionCount)
    {
        DeletionCandidates = deletionCandidates;
        DeletionFlags = deletionFlags;
        DeletionCount = deletionCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= DeletionCount) return;

        int edgeIdx = DeletionCandidates[i];
        DeletionFlags[edgeIdx] = 1;
    }
}

/// <summary>
/// Zero-initialize deletion flags before marking.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ZeroDeletionFlagsKernel : IComputeShader
{
    /// <summary>Flags to zero.</summary>
    public readonly ReadWriteBuffer<int> DeletionFlags;
    
    /// <summary>Number of edges.</summary>
    public readonly int EdgeCount;

    public ZeroDeletionFlagsKernel(
        ReadWriteBuffer<int> deletionFlags,
        int edgeCount)
    {
        DeletionFlags = deletionFlags;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= EdgeCount) return;
        DeletionFlags[i] = 0;
    }
}

/// <summary>
/// Compute final degree count after all additions and deletions.
/// Combines base degree computation with atomic increments.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeFinalDegreesKernel : IComputeShader
{
    /// <summary>Current CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>Current edge weights.</summary>
    public readonly ReadOnlyBuffer<double> Weights;
    
    /// <summary>Deletion flags (1=delete).</summary>
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    
    /// <summary>Pre-computed addition contributions per node.</summary>
    public readonly ReadOnlyBuffer<int> AdditionDegrees;
    
    /// <summary>Output final degrees.</summary>
    public readonly ReadWriteBuffer<int> FinalDegrees;
    
    /// <summary>Weight threshold.</summary>
    public readonly double WeightThreshold;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public ComputeFinalDegreesKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> additionDegrees,
        ReadWriteBuffer<int> finalDegrees,
        double weightThreshold,
        int nodeCount)
    {
        RowOffsets = rowOffsets;
        Weights = weights;
        DeletionFlags = deletionFlags;
        AdditionDegrees = additionDegrees;
        FinalDegrees = finalDegrees;
        WeightThreshold = weightThreshold;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= NodeCount) return;

        int start = RowOffsets[node];
        int end = RowOffsets[node + 1];
        int baseDegree = 0;

        // Count surviving edges
        for (int k = start; k < end; k++)
        {
            if (Weights[k] >= WeightThreshold && DeletionFlags[k] == 0)
            {
                baseDegree++;
            }
        }

        // Add contributions from new edges
        FinalDegrees[node] = baseDegree + AdditionDegrees[node];
    }
}
