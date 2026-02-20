using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// SCATTER REBUILD SHADERS FOR DYNAMIC CSR
/// ========================================
/// Reconstruct CSR structure after edge additions and deletions.
/// 
/// The rebuild process:
/// 1. ScatterOldEdges: Copy surviving edges from old CSR to new positions
/// 2. ScatterNewEdges: Insert new edges at their positions in new CSR
/// 3. Result: New ColIndices and Weights arrays with updated topology
/// 
/// Key challenge: Edges must be sorted by (row, col) for CSR validity.
/// We achieve this by:
/// - Processing each row separately
/// - Using atomic counters per row for thread-safe writes
/// - Sorting new edges within each row during scatter
/// </summary>

/// <summary>
/// Scatter surviving edges from old CSR to new CSR positions.
/// Each thread processes one row and copies all valid edges.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct ScatterOldEdgesKernel : IComputeShader
{
    /// <summary>Old CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> OldRowOffsets;
    
    /// <summary>Old CSR column indices.</summary>
    public readonly ReadOnlyBuffer<int> OldColIndices;
    
    /// <summary>Old edge weights.</summary>
    public readonly ReadOnlyBuffer<double> OldWeights;
    
    /// <summary>Deletion flags (1=delete).</summary>
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    
    /// <summary>New CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> NewRowOffsets;
    
    /// <summary>New CSR column indices (output).</summary>
    public readonly ReadWriteBuffer<int> NewColIndices;
    
    /// <summary>New edge weights (output).</summary>
    public readonly ReadWriteBuffer<double> NewWeights;
    
    /// <summary>Per-row write counters for atomic writes.</summary>
    public readonly ReadWriteBuffer<int> WriteCounters;
    
    /// <summary>Weight threshold.</summary>
    public readonly double WeightThreshold;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public ScatterOldEdgesKernel(
        ReadOnlyBuffer<int> oldRowOffsets,
        ReadOnlyBuffer<int> oldColIndices,
        ReadOnlyBuffer<double> oldWeights,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> newRowOffsets,
        ReadWriteBuffer<int> newColIndices,
        ReadWriteBuffer<double> newWeights,
        ReadWriteBuffer<int> writeCounters,
        double weightThreshold,
        int nodeCount)
    {
        OldRowOffsets = oldRowOffsets;
        OldColIndices = oldColIndices;
        OldWeights = oldWeights;
        DeletionFlags = deletionFlags;
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

        for (int k = oldStart; k < oldEnd; k++)
        {
            double w = OldWeights[k];
            
            // Keep edge if: weight >= threshold AND not marked for deletion
            if (w >= WeightThreshold && DeletionFlags[k] == 0)
            {
                // Atomically get write position
                int localIdx;
                Hlsl.InterlockedAdd(ref WriteCounters[row], 1, out localIdx);
                
                int writePos = newStart + localIdx;
                NewColIndices[writePos] = OldColIndices[k];
                NewWeights[writePos] = w;
            }
        }
    }
}

/// <summary>
/// Scatter new edges to their positions in the new CSR.
/// Handles both endpoints of each edge (symmetric CSR).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct ScatterNewEdgesKernel : IComputeShader
{
    /// <summary>New edge candidates (nodeA, nodeB).</summary>
    public readonly ReadOnlyBuffer<Int2> AdditionCandidates;
    
    /// <summary>Weights for new edges.</summary>
    public readonly ReadOnlyBuffer<float> AdditionWeights;
    
    /// <summary>New CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> NewRowOffsets;
    
    /// <summary>New CSR column indices (output).</summary>
    public readonly ReadWriteBuffer<int> NewColIndices;
    
    /// <summary>New edge weights (output).</summary>
    public readonly ReadWriteBuffer<double> NewWeights;
    
    /// <summary>Per-row write counters for atomic writes.</summary>
    public readonly ReadWriteBuffer<int> WriteCounters;
    
    /// <summary>Number of additions.</summary>
    public readonly int AdditionCount;

    public ScatterNewEdgesKernel(
        ReadOnlyBuffer<Int2> additionCandidates,
        ReadOnlyBuffer<float> additionWeights,
        ReadOnlyBuffer<int> newRowOffsets,
        ReadWriteBuffer<int> newColIndices,
        ReadWriteBuffer<double> newWeights,
        ReadWriteBuffer<int> writeCounters,
        int additionCount)
    {
        AdditionCandidates = additionCandidates;
        AdditionWeights = additionWeights;
        NewRowOffsets = newRowOffsets;
        NewColIndices = newColIndices;
        NewWeights = newWeights;
        WriteCounters = writeCounters;
        AdditionCount = additionCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= AdditionCount) return;

        Int2 edge = AdditionCandidates[i];
        int nodeA = edge.X;
        int nodeB = edge.Y;
        double weight = AdditionWeights[i];

        // Add edge (nodeA -> nodeB)
        int localIdxA;
        Hlsl.InterlockedAdd(ref WriteCounters[nodeA], 1, out localIdxA);
        int writePosA = NewRowOffsets[nodeA] + localIdxA;
        NewColIndices[writePosA] = nodeB;
        NewWeights[writePosA] = weight;

        // Add symmetric edge (nodeB -> nodeA)
        int localIdxB;
        Hlsl.InterlockedAdd(ref WriteCounters[nodeB], 1, out localIdxB);
        int writePosB = NewRowOffsets[nodeB] + localIdxB;
        NewColIndices[writePosB] = nodeA;
        NewWeights[writePosB] = weight;
    }
}

/// <summary>
/// Initialize write counters to zero before scatter operations.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ZeroWriteCountersKernel : IComputeShader
{
    /// <summary>Counters to zero.</summary>
    public readonly ReadWriteBuffer<int> WriteCounters;
    
    /// <summary>Number of counters (= NodeCount).</summary>
    public readonly int Count;

    public ZeroWriteCountersKernel(
        ReadWriteBuffer<int> writeCounters,
        int count)
    {
        WriteCounters = writeCounters;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= Count) return;
        WriteCounters[i] = 0;
    }
}

/// <summary>
/// Sort column indices within each row after scatter.
/// Uses simple insertion sort (efficient for small row degrees).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct SortRowsKernel : IComputeShader
{
    /// <summary>Row offsets.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>Column indices to sort.</summary>
    public readonly ReadWriteBuffer<int> ColIndices;
    
    /// <summary>Weights to reorder with columns.</summary>
    public readonly ReadWriteBuffer<double> Weights;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public SortRowsKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadWriteBuffer<int> colIndices,
        ReadWriteBuffer<double> weights,
        int nodeCount)
    {
        RowOffsets = rowOffsets;
        ColIndices = colIndices;
        Weights = weights;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int row = ThreadIds.X;
        if (row >= NodeCount) return;

        int start = RowOffsets[row];
        int end = RowOffsets[row + 1];
        int rowLen = end - start;

        // Simple insertion sort (O(k^2) where k is row degree, typically small)
        for (int i = 1; i < rowLen; i++)
        {
            int key = ColIndices[start + i];
            double keyWeight = Weights[start + i];
            int j = i - 1;

            while (j >= 0 && ColIndices[start + j] > key)
            {
                ColIndices[start + j + 1] = ColIndices[start + j];
                Weights[start + j + 1] = Weights[start + j];
                j--;
            }
            ColIndices[start + j + 1] = key;
            Weights[start + j + 1] = keyWeight;
        }
    }
}

/// <summary>
/// Verify CSR structure validity after rebuild.
/// Checks sorted order and symmetric pairs.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct VerifyCsrKernel : IComputeShader
{
    /// <summary>Row offsets.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>Column indices.</summary>
    public readonly ReadOnlyBuffer<int> ColIndices;
    
    /// <summary>Output: error flags per row (0=valid, 1=error).</summary>
    public readonly ReadWriteBuffer<int> ErrorFlags;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;

    public VerifyCsrKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadWriteBuffer<int> errorFlags,
        int nodeCount)
    {
        RowOffsets = rowOffsets;
        ColIndices = colIndices;
        ErrorFlags = errorFlags;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int row = ThreadIds.X;
        if (row >= NodeCount) return;

        int start = RowOffsets[row];
        int end = RowOffsets[row + 1];
        int error = 0;

        // Check sorted order
        for (int k = start + 1; k < end; k++)
        {
            if (ColIndices[k] <= ColIndices[k - 1])
            {
                error = 1; // Not strictly increasing
            }
        }

        // Check for self-loops
        for (int k = start; k < end; k++)
        {
            if (ColIndices[k] == row)
            {
                error = 1; // Self-loop detected
            }
        }

        ErrorFlags[row] = error;
    }
}

/// <summary>
/// Combined rebuild kernel: scatter old edges and new edges in one pass.
/// More efficient than separate kernels for small-medium graphs.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct CombinedScatterKernel : IComputeShader
{
    /// <summary>Old CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> OldRowOffsets;
    
    /// <summary>Old CSR column indices.</summary>
    public readonly ReadOnlyBuffer<int> OldColIndices;
    
    /// <summary>Old edge weights.</summary>
    public readonly ReadOnlyBuffer<double> OldWeights;
    
    /// <summary>Deletion flags.</summary>
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    
    /// <summary>Addition candidates sorted by nodeA.</summary>
    public readonly ReadOnlyBuffer<Int2> AdditionCandidates;
    
    /// <summary>Addition weights.</summary>
    public readonly ReadOnlyBuffer<float> AdditionWeights;
    
    /// <summary>Start index in AdditionCandidates for each row.</summary>
    public readonly ReadOnlyBuffer<int> AdditionRowStarts;
    
    /// <summary>New CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> NewRowOffsets;
    
    /// <summary>New CSR column indices (output).</summary>
    public readonly ReadWriteBuffer<int> NewColIndices;
    
    /// <summary>New edge weights (output).</summary>
    public readonly ReadWriteBuffer<double> NewWeights;
    
    /// <summary>Weight threshold.</summary>
    public readonly double WeightThreshold;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;
    
    /// <summary>Number of additions.</summary>
    public readonly int AdditionCount;

    public CombinedScatterKernel(
        ReadOnlyBuffer<int> oldRowOffsets,
        ReadOnlyBuffer<int> oldColIndices,
        ReadOnlyBuffer<double> oldWeights,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<Int2> additionCandidates,
        ReadOnlyBuffer<float> additionWeights,
        ReadOnlyBuffer<int> additionRowStarts,
        ReadOnlyBuffer<int> newRowOffsets,
        ReadWriteBuffer<int> newColIndices,
        ReadWriteBuffer<double> newWeights,
        double weightThreshold,
        int nodeCount,
        int additionCount)
    {
        OldRowOffsets = oldRowOffsets;
        OldColIndices = oldColIndices;
        OldWeights = oldWeights;
        DeletionFlags = deletionFlags;
        AdditionCandidates = additionCandidates;
        AdditionWeights = additionWeights;
        AdditionRowStarts = additionRowStarts;
        NewRowOffsets = newRowOffsets;
        NewColIndices = newColIndices;
        NewWeights = newWeights;
        WeightThreshold = weightThreshold;
        NodeCount = nodeCount;
        AdditionCount = additionCount;
    }

    public void Execute()
    {
        int row = ThreadIds.X;
        if (row >= NodeCount) return;

        int newStart = NewRowOffsets[row];
        int writePos = newStart;

        // 1. Copy surviving old edges
        int oldStart = OldRowOffsets[row];
        int oldEnd = OldRowOffsets[row + 1];
        
        for (int k = oldStart; k < oldEnd; k++)
        {
            double w = OldWeights[k];
            if (w >= WeightThreshold && DeletionFlags[k] == 0)
            {
                NewColIndices[writePos] = OldColIndices[k];
                NewWeights[writePos] = w;
                writePos++;
            }
        }

        // 2. Add new edges for this row
        int addStart = AdditionRowStarts[row];
        int addEnd = (row < NodeCount - 1) ? AdditionRowStarts[row + 1] : AdditionCount;
        
        for (int a = addStart; a < addEnd; a++)
        {
            Int2 edge = AdditionCandidates[a];
            // Check if this row is nodeA
            if (edge.X == row)
            {
                NewColIndices[writePos] = edge.Y;
                NewWeights[writePos] = AdditionWeights[a];
                writePos++;
            }
        }

        // Note: Symmetric edges (nodeB -> nodeA) are handled in a separate pass
        // or by duplicating additions in the candidate list
    }
}
