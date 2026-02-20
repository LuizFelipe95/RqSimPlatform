using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Causal;

/// <summary>
/// GPU kernel for BFS wavefront expansion step.
/// 
/// ALGORITHM: Parallel Breadth-First Search via CSR
/// Each thread processes one node in the current frontier.
/// If node is in frontier, mark all its neighbors for next frontier.
/// 
/// Uses atomic OR on bitmask for thread-safe frontier updates.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct CausalWavefrontKernel : IComputeShader
{
    /// <summary>CSR row offsets (size = nodeCount + 1).</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;

    /// <summary>CSR column indices (size = nnz).</summary>
    public readonly ReadOnlyBuffer<int> colIndices;

    /// <summary>Current frontier bitmask (1 = node in current frontier).</summary>
    public readonly ReadOnlyBuffer<uint> currentFrontier;

    /// <summary>Next frontier bitmask (output, atomic OR).</summary>
    public readonly ReadWriteBuffer<uint> nextFrontier;

    /// <summary>Visited bitmask (nodes already processed).</summary>
    public readonly ReadWriteBuffer<uint> visited;

    /// <summary>Distance from source (output).</summary>
    public readonly ReadWriteBuffer<int> distance;

    /// <summary>Current BFS depth.</summary>
    public readonly int currentDepth;

    /// <summary>Maximum depth to explore.</summary>
    public readonly int maxDepth;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public CausalWavefrontKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<uint> currentFrontier,
        ReadWriteBuffer<uint> nextFrontier,
        ReadWriteBuffer<uint> visited,
        ReadWriteBuffer<int> distance,
        int currentDepth,
        int maxDepth,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.currentFrontier = currentFrontier;
        this.nextFrontier = nextFrontier;
        this.visited = visited;
        this.distance = distance;
        this.currentDepth = currentDepth;
        this.maxDepth = maxDepth;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        // Check if this node is in current frontier
        int wordIdx = node / 32;
        int bitIdx = node % 32;
        uint mask = 1u << bitIdx;

        if ((currentFrontier[wordIdx] & mask) == 0)
            return;

        // Already at max depth
        if (currentDepth >= maxDepth)
            return;

        // Mark this node as visited
        Hlsl.InterlockedOr(ref visited[wordIdx], mask);

        // Explore all neighbors via CSR
        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            int nWordIdx = neighbor / 32;
            int nBitIdx = neighbor % 32;
            uint nMask = 1u << nBitIdx;

            // Check if already visited
            if ((visited[nWordIdx] & nMask) != 0)
                continue;

            // Add to next frontier (atomic OR for thread safety)
            Hlsl.InterlockedOr(ref nextFrontier[nWordIdx], nMask);

            // Update distance if not set
            Hlsl.InterlockedMin(ref distance[neighbor], currentDepth + 1);
        }
    }
}

/// <summary>
/// GPU kernel for initializing BFS from source node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct CausalInitKernel : IComputeShader
{
    /// <summary>Frontier bitmask to initialize.</summary>
    public readonly ReadWriteBuffer<uint> frontier;

    /// <summary>Visited bitmask to initialize.</summary>
    public readonly ReadWriteBuffer<uint> visited;

    /// <summary>Distance buffer to initialize.</summary>
    public readonly ReadWriteBuffer<int> distance;

    /// <summary>Source node for BFS.</summary>
    public readonly int sourceNode;

    /// <summary>Number of bitmask words.</summary>
    public readonly int wordCount;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public CausalInitKernel(
        ReadWriteBuffer<uint> frontier,
        ReadWriteBuffer<uint> visited,
        ReadWriteBuffer<int> distance,
        int sourceNode,
        int wordCount,
        int nodeCount)
    {
        this.frontier = frontier;
        this.visited = visited;
        this.distance = distance;
        this.sourceNode = sourceNode;
        this.wordCount = wordCount;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;

        // Clear bitmasks
        if (idx < wordCount)
        {
            frontier[idx] = 0;
            visited[idx] = 0;
        }

        // Set max distance for all nodes
        if (idx < nodeCount)
        {
            distance[idx] = int.MaxValue;
        }

        // Set source node
        if (idx == 0)
        {
            int wordIdx = sourceNode / 32;
            int bitIdx = sourceNode % 32;
            frontier[wordIdx] = 1u << bitIdx;
            visited[wordIdx] = 1u << bitIdx;
            distance[sourceNode] = 0;
        }
    }
}

/// <summary>
/// GPU kernel for checking if frontier is empty (reduction).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct FrontierCheckKernel : IComputeShader
{
    /// <summary>Frontier bitmask.</summary>
    public readonly ReadOnlyBuffer<uint> frontier;

    /// <summary>Output: non-zero if frontier has any nodes.</summary>
    public readonly ReadWriteBuffer<int> hasNodes;

    /// <summary>Number of bitmask words.</summary>
    public readonly int wordCount;

    public FrontierCheckKernel(
        ReadOnlyBuffer<uint> frontier,
        ReadWriteBuffer<int> hasNodes,
        int wordCount)
    {
        this.frontier = frontier;
        this.hasNodes = hasNodes;
        this.wordCount = wordCount;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= wordCount) return;

        if (frontier[idx] != 0)
        {
            Hlsl.InterlockedOr(ref hasNodes[0], 1);
        }
    }
}

/// <summary>
/// GPU kernel for extracting causal cone nodes from visited bitmask.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct CausalConeExtractKernel : IComputeShader
{
    /// <summary>Visited bitmask.</summary>
    public readonly ReadOnlyBuffer<uint> visited;

    /// <summary>Output: 1 if node is in causal cone, 0 otherwise.</summary>
    public readonly ReadWriteBuffer<int> inCone;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public CausalConeExtractKernel(
        ReadOnlyBuffer<uint> visited,
        ReadWriteBuffer<int> inCone,
        int nodeCount)
    {
        this.visited = visited;
        this.inCone = inCone;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        int wordIdx = node / 32;
        int bitIdx = node % 32;
        uint mask = 1u << bitIdx;

        inCone[node] = ((visited[wordIdx] & mask) != 0) ? 1 : 0;
    }
}

/// <summary>
/// GPU kernel for multi-source BFS initialization.
/// Sets multiple source nodes at once.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MultiSourceInitKernel : IComputeShader
{
    /// <summary>Frontier bitmask to initialize.</summary>
    public readonly ReadWriteBuffer<uint> frontier;

    /// <summary>Visited bitmask to initialize.</summary>
    public readonly ReadWriteBuffer<uint> visited;

    /// <summary>Distance buffer to initialize.</summary>
    public readonly ReadWriteBuffer<int> distance;

    /// <summary>Source nodes array.</summary>
    public readonly ReadOnlyBuffer<int> sourceNodes;

    /// <summary>Number of source nodes.</summary>
    public readonly int sourceCount;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public MultiSourceInitKernel(
        ReadWriteBuffer<uint> frontier,
        ReadWriteBuffer<uint> visited,
        ReadWriteBuffer<int> distance,
        ReadOnlyBuffer<int> sourceNodes,
        int sourceCount,
        int nodeCount)
    {
        this.frontier = frontier;
        this.visited = visited;
        this.distance = distance;
        this.sourceNodes = sourceNodes;
        this.sourceCount = sourceCount;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= sourceCount) return;

        int source = sourceNodes[idx];
        if (source < 0 || source >= nodeCount) return;

        int wordIdx = source / 32;
        int bitIdx = source % 32;
        uint mask = 1u << bitIdx;

        Hlsl.InterlockedOr(ref frontier[wordIdx], mask);
        Hlsl.InterlockedOr(ref visited[wordIdx], mask);
        distance[source] = 0;
    }
}
