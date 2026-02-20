using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// TOPOLOGY PROPOSAL SHADERS
/// =========================
/// GPU compute shaders for proposing edge additions and deletions
/// during MCMC-style topology evolution.
/// 
/// Hard Rewiring Pipeline:
/// 1. ProposalKernel: Evaluates action change and proposes new edges via AppendStructuredBuffer pattern
/// 2. AcceptanceKernel: Applies Metropolis acceptance criterion
/// 3. Results collected in EdgeProposalBuffer for later CSR rebuild
/// 
/// Key difference from Soft Rewiring:
/// - Soft: Modify existing weights in fixed CSR structure
/// - Hard: Propose new (nodeA, nodeB) pairs not in current CSR
/// </summary>

/// <summary>
/// Propose edge additions based on MCMC sampling.
/// Each thread evaluates one potential edge and may propose it.
/// Uses atomic counter for thread-safe proposal collection.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeAdditionProposalKernel : IComputeShader
{
    /// <summary>CSR row offsets for current topology.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>CSR column indices for current topology.</summary>
    public readonly ReadOnlyBuffer<int> ColIndices;
    
    /// <summary>Node masses for action computation.</summary>
    public readonly ReadOnlyBuffer<double> Masses;
    
    /// <summary>Random seeds per thread (for Metropolis acceptance).</summary>
    public readonly ReadOnlyBuffer<uint> RandomSeeds;
    
    /// <summary>Atomic counter for additions.</summary>
    public readonly ReadWriteBuffer<int> AdditionCounter;
    
    /// <summary>Output buffer for proposed edges (nodeA, nodeB).</summary>
    public readonly ReadWriteBuffer<Int2> AdditionCandidates;
    
    /// <summary>Output buffer for proposed edge weights.</summary>
    public readonly ReadWriteBuffer<float> AdditionWeights;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;
    
    /// <summary>Maximum edges that can be proposed.</summary>
    public readonly int MaxProposals;
    
    /// <summary>Temperature for Metropolis acceptance.</summary>
    public readonly double Temperature;
    
    /// <summary>Inverse temperature beta = 1/T.</summary>
    public readonly double Beta;
    
    /// <summary>Link cost coefficient in action.</summary>
    public readonly double LinkCostCoeff;
    
    /// <summary>Target degree for degree penalty term.</summary>
    public readonly double TargetDegree;
    
    /// <summary>Degree penalty coefficient.</summary>
    public readonly double DegreePenaltyCoeff;
    
    /// <summary>Initial weight for new edges.</summary>
    public readonly double InitialWeight;

    public EdgeAdditionProposalKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> masses,
        ReadOnlyBuffer<uint> randomSeeds,
        ReadWriteBuffer<int> additionCounter,
        ReadWriteBuffer<Int2> additionCandidates,
        ReadWriteBuffer<float> additionWeights,
        int nodeCount,
        int maxProposals,
        double temperature,
        double linkCostCoeff,
        double targetDegree,
        double degreePenaltyCoeff,
        double initialWeight)
    {
        RowOffsets = rowOffsets;
        ColIndices = colIndices;
        Masses = masses;
        RandomSeeds = randomSeeds;
        AdditionCounter = additionCounter;
        AdditionCandidates = additionCandidates;
        AdditionWeights = additionWeights;
        NodeCount = nodeCount;
        MaxProposals = maxProposals;
        Temperature = temperature;
        Beta = temperature > 1e-10 ? 1.0 / temperature : 1e10;
        LinkCostCoeff = linkCostCoeff;
        TargetDegree = targetDegree;
        DegreePenaltyCoeff = degreePenaltyCoeff;
        InitialWeight = initialWeight;
    }

    public void Execute()
    {
        int threadId = ThreadIds.X;
        if (threadId >= NodeCount) return;

        // Each thread considers adding edges from node 'threadId'
        int nodeA = threadId;
        int degA = RowOffsets[nodeA + 1] - RowOffsets[nodeA];
        
        // Use random seed to pick a candidate target node
        uint seed = RandomSeeds[threadId];
        seed = XorShift32(seed);
        int nodeB = (int)(seed % (uint)NodeCount);
        
        // Skip self-loops and check if edge already exists
        if (nodeA == nodeB) return;
        if (nodeA > nodeB) return; // Only consider (smaller, larger) to avoid duplicates
        if (EdgeExists(nodeA, nodeB)) return;

        // Compute action change for adding this edge
        int degB = RowOffsets[nodeB + 1] - RowOffsets[nodeB];
        
        // Delta S for link cost: adding edge with weight InitialWeight
        double deltaS_link = LinkCostCoeff * (1.0 - InitialWeight);
        
        // Delta S for degree penalty: degrees increase by 1
        double oldPenaltyA = DegreePenaltyCoeff * (degA - TargetDegree) * (degA - TargetDegree);
        double newPenaltyA = DegreePenaltyCoeff * (degA + 1 - TargetDegree) * (degA + 1 - TargetDegree);
        double oldPenaltyB = DegreePenaltyCoeff * (degB - TargetDegree) * (degB - TargetDegree);
        double newPenaltyB = DegreePenaltyCoeff * (degB + 1 - TargetDegree) * (degB + 1 - TargetDegree);
        
        double deltaS_degree = (newPenaltyA - oldPenaltyA) + (newPenaltyB - oldPenaltyB);
        
        double deltaS = deltaS_link + deltaS_degree;

        // Metropolis acceptance
        bool accept = false;
        if (deltaS <= 0)
        {
            accept = true;
        }
        else
        {
            seed = XorShift32(seed);
            double u = (double)(seed & 0x7FFFFFFF) / (double)0x7FFFFFFF;
            double prob = Hlsl.Exp((float)(-Beta * deltaS));
            accept = u < prob;
        }

        if (accept)
        {
            // Atomically append to proposal buffer
            int index = 0;
            Hlsl.InterlockedAdd(ref AdditionCounter[0], 1, out index);
            
            if (index < MaxProposals)
            {
                AdditionCandidates[index] = new Int2(nodeA, nodeB);
                AdditionWeights[index] = (float)InitialWeight;
            }
        }
    }

    private bool EdgeExists(int nodeA, int nodeB)
    {
        int start = RowOffsets[nodeA];
        int end = RowOffsets[nodeA + 1];
        
        for (int k = start; k < end; k++)
        {
            if (ColIndices[k] == nodeB)
                return true;
        }
        return false;
    }

    private static uint XorShift32(uint x)
    {
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return x;
    }
}

/// <summary>
/// Propose edge deletions based on low weight threshold.
/// Edges with weight below threshold are candidates for removal.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeDeletionProposalKernel : IComputeShader
{
    /// <summary>Current edge weights.</summary>
    public readonly ReadOnlyBuffer<double> Weights;
    
    /// <summary>Random seeds per thread.</summary>
    public readonly ReadOnlyBuffer<uint> RandomSeeds;
    
    /// <summary>Atomic counter for deletions.</summary>
    public readonly ReadWriteBuffer<int> DeletionCounter;
    
    /// <summary>Output buffer for edge indices to delete.</summary>
    public readonly ReadWriteBuffer<int> DeletionCandidates;
    
    /// <summary>Number of edges.</summary>
    public readonly int EdgeCount;
    
    /// <summary>Maximum deletions that can be proposed.</summary>
    public readonly int MaxProposals;
    
    /// <summary>Weight threshold for deletion consideration.</summary>
    public readonly double WeightThreshold;
    
    /// <summary>Temperature for probabilistic deletion.</summary>
    public readonly double Temperature;

    public EdgeDeletionProposalKernel(
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<uint> randomSeeds,
        ReadWriteBuffer<int> deletionCounter,
        ReadWriteBuffer<int> deletionCandidates,
        int edgeCount,
        int maxProposals,
        double weightThreshold,
        double temperature)
    {
        Weights = weights;
        RandomSeeds = randomSeeds;
        DeletionCounter = deletionCounter;
        DeletionCandidates = deletionCandidates;
        EdgeCount = edgeCount;
        MaxProposals = maxProposals;
        WeightThreshold = weightThreshold;
        Temperature = temperature;
    }

    public void Execute()
    {
        int edgeIdx = ThreadIds.X;
        if (edgeIdx >= EdgeCount) return;

        double w = Weights[edgeIdx];
        
        // Only consider edges below threshold for deletion
        if (w >= WeightThreshold) return;

        // Probabilistic deletion: lower weight = higher deletion probability
        uint seed = RandomSeeds[edgeIdx];
        seed = XorShift32(seed);
        double u = (double)(seed & 0x7FFFFFFF) / (double)0x7FFFFFFF;
        
        // Deletion probability increases as weight decreases
        // P(delete) = 1 - w/threshold
        double deleteProb = 1.0 - (w / WeightThreshold);
        
        if (u < deleteProb)
        {
            int index = 0;
            Hlsl.InterlockedAdd(ref DeletionCounter[0], 1, out index);
            
            if (index < MaxProposals)
            {
                DeletionCandidates[index] = edgeIdx;
            }
        }
    }

    private static uint XorShift32(uint x)
    {
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return x;
    }
}

/// <summary>
/// Zero the proposal counters before each proposal phase.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ZeroProposalCountersKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> AdditionCounter;
    public readonly ReadWriteBuffer<int> DeletionCounter;

    public ZeroProposalCountersKernel(
        ReadWriteBuffer<int> additionCounter,
        ReadWriteBuffer<int> deletionCounter)
    {
        AdditionCounter = additionCounter;
        DeletionCounter = deletionCounter;
    }

    public void Execute()
    {
        if (ThreadIds.X == 0)
        {
            AdditionCounter[0] = 0;
            DeletionCounter[0] = 0;
        }
    }
}

/// <summary>
/// Combined proposal kernel that evaluates both additions and deletions
/// for MCMC topology evolution with detailed balance.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct MCMCTopologyProposalKernel : IComputeShader
{
    /// <summary>CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> RowOffsets;
    
    /// <summary>CSR column indices.</summary>
    public readonly ReadOnlyBuffer<int> ColIndices;
    
    /// <summary>Current edge weights.</summary>
    public readonly ReadOnlyBuffer<double> Weights;
    
    /// <summary>Node masses.</summary>
    public readonly ReadOnlyBuffer<double> Masses;
    
    /// <summary>Random seeds (updated in-place).</summary>
    public readonly ReadWriteBuffer<uint> RandomSeeds;
    
    /// <summary>Proposal counters [0]=additions, [1]=deletions.</summary>
    public readonly ReadWriteBuffer<int> ProposalCounters;
    
    /// <summary>Addition candidates buffer.</summary>
    public readonly ReadWriteBuffer<Int2> AdditionCandidates;
    
    /// <summary>Addition weights buffer.</summary>
    public readonly ReadWriteBuffer<float> AdditionWeights;
    
    /// <summary>Deletion candidates buffer.</summary>
    public readonly ReadWriteBuffer<int> DeletionCandidates;
    
    /// <summary>Number of nodes.</summary>
    public readonly int NodeCount;
    
    /// <summary>Number of edges.</summary>
    public readonly int EdgeCount;
    
    /// <summary>Max additions per step.</summary>
    public readonly int MaxAdditions;
    
    /// <summary>Max deletions per step.</summary>
    public readonly int MaxDeletions;
    
    /// <summary>Inverse temperature.</summary>
    public readonly double Beta;
    
    /// <summary>Link cost coefficient.</summary>
    public readonly double LinkCostCoeff;
    
    /// <summary>Target degree.</summary>
    public readonly double TargetDegree;
    
    /// <summary>Degree penalty coefficient.</summary>
    public readonly double DegreePenaltyCoeff;
    
    /// <summary>Initial weight for new edges.</summary>
    public readonly double InitialWeight;
    
    /// <summary>Weight threshold for deletion.</summary>
    public readonly double DeletionThreshold;

    public MCMCTopologyProposalKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<double> masses,
        ReadWriteBuffer<uint> randomSeeds,
        ReadWriteBuffer<int> proposalCounters,
        ReadWriteBuffer<Int2> additionCandidates,
        ReadWriteBuffer<float> additionWeights,
        ReadWriteBuffer<int> deletionCandidates,
        int nodeCount,
        int edgeCount,
        int maxAdditions,
        int maxDeletions,
        double beta,
        double linkCostCoeff,
        double targetDegree,
        double degreePenaltyCoeff,
        double initialWeight,
        double deletionThreshold)
    {
        RowOffsets = rowOffsets;
        ColIndices = colIndices;
        Weights = weights;
        Masses = masses;
        RandomSeeds = randomSeeds;
        ProposalCounters = proposalCounters;
        AdditionCandidates = additionCandidates;
        AdditionWeights = additionWeights;
        DeletionCandidates = deletionCandidates;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        MaxAdditions = maxAdditions;
        MaxDeletions = maxDeletions;
        Beta = beta;
        LinkCostCoeff = linkCostCoeff;
        TargetDegree = targetDegree;
        DegreePenaltyCoeff = degreePenaltyCoeff;
        InitialWeight = initialWeight;
        DeletionThreshold = deletionThreshold;
    }

    public void Execute()
    {
        int threadId = ThreadIds.X;
        
        // Thread handles both node-based addition proposals and edge-based deletion proposals
        // First NodeCount threads: propose additions
        // Next EdgeCount threads: propose deletions
        
        if (threadId < NodeCount)
        {
            ProposeAddition(threadId);
        }
        else if (threadId < NodeCount + EdgeCount)
        {
            ProposeDeletion(threadId - NodeCount);
        }
    }

    private void ProposeAddition(int nodeA)
    {
        int degA = RowOffsets[nodeA + 1] - RowOffsets[nodeA];
        
        // Generate random target node
        uint seed = RandomSeeds[nodeA];
        seed = XorShift32(seed);
        RandomSeeds[nodeA] = seed;
        
        int nodeB = (int)(seed % (uint)NodeCount);
        
        if (nodeA == nodeB) return;
        if (nodeA > nodeB) return;
        if (EdgeExistsInRow(nodeA, nodeB)) return;

        int degB = RowOffsets[nodeB + 1] - RowOffsets[nodeB];
        
        // Compute delta S
        double deltaS_link = LinkCostCoeff * (1.0 - InitialWeight);
        double deltaS_degA = DegreePenaltyCoeff * (2 * (degA - TargetDegree) + 1);
        double deltaS_degB = DegreePenaltyCoeff * (2 * (degB - TargetDegree) + 1);
        double deltaS = deltaS_link + deltaS_degA + deltaS_degB;

        // Metropolis
        bool accept = deltaS <= 0;
        if (!accept)
        {
            seed = XorShift32(seed);
            RandomSeeds[nodeA] = seed;
            double u = (double)(seed & 0x7FFFFFFF) / (double)0x7FFFFFFF;
            accept = u < Hlsl.Exp((float)(-Beta * deltaS));
        }

        if (accept)
        {
            int idx = 0;
            Hlsl.InterlockedAdd(ref ProposalCounters[0], 1, out idx);
            if (idx < MaxAdditions)
            {
                AdditionCandidates[idx] = new Int2(nodeA, nodeB);
                AdditionWeights[idx] = (float)InitialWeight;
            }
        }
    }

    private void ProposeDeletion(int edgeIdx)
    {
        if (edgeIdx >= EdgeCount) return;
        
        double w = Weights[edgeIdx];
        if (w >= DeletionThreshold) return;

        uint seed = RandomSeeds[NodeCount + edgeIdx];
        seed = XorShift32(seed);
        RandomSeeds[NodeCount + edgeIdx] = seed;
        
        double u = (double)(seed & 0x7FFFFFFF) / (double)0x7FFFFFFF;
        double deleteProb = 1.0 - (w / DeletionThreshold);
        
        if (u < deleteProb)
        {
            int idx = 0;
            Hlsl.InterlockedAdd(ref ProposalCounters[1], 1, out idx);
            if (idx < MaxDeletions)
            {
                DeletionCandidates[idx] = edgeIdx;
            }
        }
    }

    private bool EdgeExistsInRow(int nodeA, int nodeB)
    {
        int start = RowOffsets[nodeA];
        int end = RowOffsets[nodeA + 1];
        for (int k = start; k < end; k++)
        {
            if (ColIndices[k] == nodeB) return true;
        }
        return false;
    }

    private static uint XorShift32(uint x)
    {
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return x;
    }
}
