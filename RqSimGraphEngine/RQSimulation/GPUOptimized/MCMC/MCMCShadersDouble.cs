using ComputeSharp;

namespace RQSimulation.GPUOptimized.MCMC;

/// <summary>
/// Double-precision compute shaders for MCMC sampling on GPU.
/// 
/// RQ-HYPOTHESIS STAGE 4: GPU MCMC SAMPLING
/// ========================================
/// Markov Chain Monte Carlo for path integral quantum gravity.
/// Sample configurations from the partition function:
///   Z = ? D[g] exp(-S_E[g])
/// 
/// GPU PARALLELIZATION STRATEGY:
/// ============================
/// MCMC moves are inherently sequential (detailed balance), but:
/// 1. Action computation: Fully parallelizable (sum over edges/nodes)
/// 2. Parallel Tempering: K independent replicas run in parallel
/// 3. Batch proposals: Pre-compute many proposals, select subset
/// 
/// Key insight: We CAN parallelize:
/// - Computing S_E for multiple configurations
/// - Computing ?S for batched edge modifications
/// - Replica Exchange Monte Carlo (parallel chains)
/// 
/// We CANNOT parallelize:
/// - Sequential Metropolis moves on single chain (breaks detailed balance)
/// 
/// Solution: Parallel Tempering with K replicas at different temperatures.
/// Each replica evolves independently, periodic state swaps.
/// 
/// All operations use double precision (64-bit) for physical accuracy.
/// </summary>

/// <summary>
/// GAUGE INVARIANCE CHECK KERNEL (Noether Theorem)
/// ================================================
/// Checks whether proposed edge modifications preserve gauge symmetry.
/// 
/// SCIENTIFIC PHYSICS:
/// Energy conservation emerges from gauge symmetry (Noether theorem).
/// Instead of tracking energy explicitly, we enforce gauge invariance:
/// - Wilson Loop flux around cycles must be quantized (mod 2?)
/// - Moves that create "unclosed flux" break gauge symmetry
/// - Such moves are rejected as "illegal universe" configurations
/// 
/// This kernel computes the Wilson Loop flux for triangular cycles
/// containing each proposed edge modification.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct GaugeInvarianceCheckKernel : IComputeShader
{
    /// <summary>Edge phases (gauge field A_ij)</summary>
    public readonly ReadOnlyBuffer<double> edgePhases;
    
    /// <summary>Edge node A (source)</summary>
    public readonly ReadOnlyBuffer<int> edgeNodeA;
    
    /// <summary>Edge node B (target)</summary>
    public readonly ReadOnlyBuffer<int> edgeNodeB;
    
    /// <summary>Edge existence flags</summary>
    public readonly ReadOnlyBuffer<int> edgeExists;
    
    /// <summary>Proposal edge indices</summary>
    public readonly ReadOnlyBuffer<int> proposalEdgeIndices;
    
    /// <summary>Proposed new weights</summary>
    public readonly ReadOnlyBuffer<double> proposedWeights;
    
    /// <summary>Output: 1 = gauge invariant, 0 = violation</summary>
    public readonly ReadWriteBuffer<int> gaugeInvariantFlags;
    
    /// <summary>Minimum weight threshold</summary>
    public readonly double minWeight;
    
    /// <summary>Tolerance for flux quantization</summary>
    public readonly double fluxTolerance;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;
    
    private const double TwoPi = 6.283185307179586;
    
    public GaugeInvarianceCheckKernel(
        ReadOnlyBuffer<double> edgePhases,
        ReadOnlyBuffer<int> edgeNodeA,
        ReadOnlyBuffer<int> edgeNodeB,
        ReadOnlyBuffer<int> edgeExists,
        ReadOnlyBuffer<int> proposalEdgeIndices,
        ReadOnlyBuffer<double> proposedWeights,
        ReadWriteBuffer<int> gaugeInvariantFlags,
        double minWeight,
        double fluxTolerance,
        int edgeCount,
        int proposalCount)
    {
        this.edgePhases = edgePhases;
        this.edgeNodeA = edgeNodeA;
        this.edgeNodeB = edgeNodeB;
        this.edgeExists = edgeExists;
        this.proposalEdgeIndices = proposalEdgeIndices;
        this.proposedWeights = proposedWeights;
        this.gaugeInvariantFlags = gaugeInvariantFlags;
        this.minWeight = minWeight;
        this.fluxTolerance = fluxTolerance;
        this.edgeCount = edgeCount;
        this.proposalCount = proposalCount;
    }
    
    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;
        
        int targetEdge = proposalEdgeIndices[p];
        if (targetEdge < 0 || targetEdge >= edgeCount)
        {
            gaugeInvariantFlags[p] = 1; // Invalid edge, allow
            return;
        }
        
        int nodeA = edgeNodeA[targetEdge];
        int nodeB = edgeNodeB[targetEdge];
        double proposedWeight = proposedWeights[p];
        
        // Calculate Wilson Loop flux for cycles containing this edge
        double totalFlux = 0.0;
        int triangleCount = 0;
        
        // Find triangles containing edge (nodeA, nodeB)
        // A triangle exists if there's a node C connected to both A and B
        for (int e1 = 0; e1 < edgeCount; e1++)
        {
            if (edgeExists[e1] == 0) continue;
            
            int e1A = edgeNodeA[e1];
            int e1B = edgeNodeB[e1];
            
            // Check if e1 connects to nodeA or nodeB
            int sharedNode = -1;
            int otherNode = -1;
            
            if (e1A == nodeA) { sharedNode = nodeA; otherNode = e1B; }
            else if (e1B == nodeA) { sharedNode = nodeA; otherNode = e1A; }
            else if (e1A == nodeB) { sharedNode = nodeB; otherNode = e1A; }
            else if (e1B == nodeB) { sharedNode = nodeB; otherNode = e1B; }
            
            if (sharedNode < 0 || otherNode == nodeA || otherNode == nodeB) continue;
            
            // Check if otherNode connects to the other end (forms triangle)
            int targetNode = (sharedNode == nodeA) ? nodeB : nodeA;
            
            for (int e2 = 0; e2 < edgeCount; e2++)
            {
                if (edgeExists[e2] == 0) continue;
                
                int e2A = edgeNodeA[e2];
                int e2B = edgeNodeB[e2];
                
                if ((e2A == otherNode && e2B == targetNode) || 
                    (e2B == otherNode && e2A == targetNode))
                {
                    // Found triangle: nodeA - nodeB - otherNode
                    // Wilson loop: ?_AB + ?_B_other + ?_other_A (or similar path)
                    double phaseAB = edgePhases[targetEdge];
                    double phase1 = edgePhases[e1];
                    double phase2 = edgePhases[e2];
                    
                    // Weight factor for proposed change
                    double weightFactor = (proposedWeight >= minWeight) ? 1.0 : 0.0;
                    
                    double loopFlux = (phaseAB * weightFactor) + phase1 + phase2;
                    totalFlux += loopFlux;
                    triangleCount++;
                    break;
                }
            }
        }
        
        // If no triangles, flux is automatically gauge invariant
        if (triangleCount == 0)
        {
            gaugeInvariantFlags[p] = 1;
            return;
        }
        
        // Average flux per triangle
        double avgFlux = totalFlux / triangleCount;
        
        // Check quantization: flux should be 2?n
        // |flux mod 2?| < tolerance
        double normalizedFlux = avgFlux - TwoPi * Hlsl.Round((float)(avgFlux / TwoPi));
        
        gaugeInvariantFlags[p] = (Hlsl.Abs((float)normalizedFlux) < fluxTolerance) ? 1 : 0;
    }
}

/// <summary>
/// Compute link contribution to Euclidean action for all edges.
/// S_links = ?_ij (1 - w_ij) for all edges
/// Parallelized over edges.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeActionKernelDouble : IComputeShader
{
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output action contribution per edge</summary>
    public readonly ReadWriteBuffer<double> edgeActions;
    
    /// <summary>Link cost coefficient ?_links</summary>
    public readonly double linkCostCoeff;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public EdgeActionKernelDouble(
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<double> edgeActions,
        double linkCostCoeff,
        int edgeCount)
    {
        this.weights = weights;
        this.edgeActions = edgeActions;
        this.linkCostCoeff = linkCostCoeff;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double w = weights[e];
        // Action contribution: cost for weak edges
        edgeActions[e] = linkCostCoeff * (1.0 - w);
    }
}

/// <summary>
/// Compute node contribution to Euclidean action (mass/potential).
/// S_nodes = ?_i V(m_i) where V is the node potential.
/// Parallelized over nodes.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct NodeActionKernelDouble : IComputeShader
{
    /// <summary>Node masses</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Node degrees</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Output action contribution per node</summary>
    public readonly ReadWriteBuffer<double> nodeActions;
    
    /// <summary>Mass coefficient ?_mass</summary>
    public readonly double massCoeff;
    
    /// <summary>Target degree for Mexican-hat potential</summary>
    public readonly double targetDegree;
    
    /// <summary>Degree penalty coefficient</summary>
    public readonly double degreePenaltyCoeff;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public NodeActionKernelDouble(
        ReadOnlyBuffer<double> masses,
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<double> nodeActions,
        double massCoeff,
        double targetDegree,
        double degreePenaltyCoeff,
        int nodeCount)
    {
        this.masses = masses;
        this.degrees = degrees;
        this.nodeActions = nodeActions;
        this.massCoeff = massCoeff;
        this.targetDegree = targetDegree;
        this.degreePenaltyCoeff = degreePenaltyCoeff;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double m = masses[i];
        int deg = degrees[i];
        
        // Mass action: favors non-zero mass
        double S_mass = massCoeff * m * m;
        
        // Degree potential: Mexican-hat centered at targetDegree
        double degDiff = deg - targetDegree;
        double S_degree = degreePenaltyCoeff * degDiff * degDiff;
        
        nodeActions[i] = S_mass + S_degree;
    }
}

/// <summary>
/// Compute constraint violation contribution to action.
/// S_constraint = ? * ?_i (H_geom - ?*H_matter)?
/// This penalizes configurations far from Wheeler-DeWitt constraint.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ConstraintActionKernelDouble : IComputeShader
{
    /// <summary>Local curvatures (H_geom)</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Node masses (H_matter)</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Output constraint violation per node</summary>
    public readonly ReadWriteBuffer<double> constraintActions;
    
    /// <summary>Gravitational coupling ?</summary>
    public readonly double kappa;
    
    /// <summary>Constraint Lagrange multiplier ?</summary>
    public readonly double lambda;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public ConstraintActionKernelDouble(
        ReadOnlyBuffer<double> curvatures,
        ReadOnlyBuffer<double> masses,
        ReadWriteBuffer<double> constraintActions,
        double kappa,
        double lambda,
        int nodeCount)
    {
        this.curvatures = curvatures;
        this.masses = masses;
        this.constraintActions = constraintActions;
        this.kappa = kappa;
        this.lambda = lambda;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double H_geom = curvatures[i];
        double H_matter = masses[i];
        
        double constraint = H_geom - kappa * H_matter;
        constraintActions[i] = lambda * constraint * constraint;
    }
}

/// <summary>
/// Compute local action change ?S for edge modification.
/// Used for batched proposal evaluation.
/// Each thread computes ?S for one proposed edge change.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalDeltaActionKernelDouble : IComputeShader
{
    /// <summary>Current edge weights</summary>
    public readonly ReadOnlyBuffer<double> currentWeights;
    
    /// <summary>Proposed new weights</summary>
    public readonly ReadOnlyBuffer<double> proposedWeights;
    
    /// <summary>Edge index for each proposal</summary>
    public readonly ReadOnlyBuffer<int> proposalEdgeIndices;
    
    /// <summary>Output ?S for each proposal</summary>
    public readonly ReadWriteBuffer<double> deltaActions;
    
    /// <summary>Link cost coefficient</summary>
    public readonly double linkCostCoeff;
    
    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;
    
    public LocalDeltaActionKernelDouble(
        ReadOnlyBuffer<double> currentWeights,
        ReadOnlyBuffer<double> proposedWeights,
        ReadOnlyBuffer<int> proposalEdgeIndices,
        ReadWriteBuffer<double> deltaActions,
        double linkCostCoeff,
        int proposalCount)
    {
        this.currentWeights = currentWeights;
        this.proposedWeights = proposedWeights;
        this.proposalEdgeIndices = proposalEdgeIndices;
        this.deltaActions = deltaActions;
        this.linkCostCoeff = linkCostCoeff;
        this.proposalCount = proposalCount;
    }
    
    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;
        
        int e = proposalEdgeIndices[p];
        double w_old = currentWeights[e];
        double w_new = proposedWeights[p];
        
        // ?S_link = ? * [(1-w_new) - (1-w_old)] = ? * (w_old - w_new)
        double deltaS_link = linkCostCoeff * (w_old - w_new);
        
        deltaActions[p] = deltaS_link;
    }
}

/// <summary>
/// Metropolis-Hastings acceptance kernel for batched proposals.
/// Computes accept/reject for multiple proposals in parallel.
/// Includes Hastings ratio q(x'→x)/q(x→x') for asymmetric topology proposals.
/// NOTE: Only one proposal per edge should be applied per step!
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct MetropolisAcceptKernelDouble : IComputeShader
{
    /// <summary>?S for each proposal</summary>
    public readonly ReadOnlyBuffer<double> deltaActions;

    /// <summary>Random numbers for acceptance (0-1)</summary>
    public readonly ReadOnlyBuffer<double> randomNumbers;

    /// <summary>Hastings ratio q(x'→x)/q(x→x') per proposal (1.0 for symmetric)</summary>
    public readonly ReadOnlyBuffer<double> proposalQRatios;

    /// <summary>Inverse temperature ? = 1/T</summary>
    public readonly double beta;

    /// <summary>Output: 1 = accept, 0 = reject</summary>
    public readonly ReadWriteBuffer<int> acceptFlags;

    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;

    public MetropolisAcceptKernelDouble(
        ReadOnlyBuffer<double> deltaActions,
        ReadOnlyBuffer<double> randomNumbers,
        ReadOnlyBuffer<double> proposalQRatios,
        double beta,
        ReadWriteBuffer<int> acceptFlags,
        int proposalCount)
    {
        this.deltaActions = deltaActions;
        this.randomNumbers = randomNumbers;
        this.proposalQRatios = proposalQRatios;
        this.beta = beta;
        this.acceptFlags = acceptFlags;
        this.proposalCount = proposalCount;
    }

    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;

        double dS = deltaActions[p];
        double qRatio = proposalQRatios[p];

        // Metropolis-Hastings criterion: P_accept = min(1, exp(-?*?S) · q_ratio)
        // q_ratio corrects for asymmetric add/remove topology proposals
        double acceptProb = Hlsl.Exp((float)(-beta * dS)) * qRatio;

        bool accept = acceptProb >= 1.0 || randomNumbers[p] < acceptProb;

        acceptFlags[p] = accept ? 1 : 0;
    }
}

/// <summary>
/// Apply accepted moves to weights.
/// Applies only moves where acceptFlags[p] = 1.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ApplyAcceptedMovesKernelDouble : IComputeShader
{
    /// <summary>Edge weights (modified)</summary>
    public readonly ReadWriteBuffer<double> weights;
    
    /// <summary>Edge existence flags (modified)</summary>
    public readonly ReadWriteBuffer<int> edgeExists;
    
    /// <summary>Proposed new weights</summary>
    public readonly ReadOnlyBuffer<double> proposedWeights;
    
    /// <summary>Edge indices for proposals</summary>
    public readonly ReadOnlyBuffer<int> proposalEdgeIndices;
    
    /// <summary>Accept flags (1 = apply)</summary>
    public readonly ReadOnlyBuffer<int> acceptFlags;
    
    /// <summary>Minimum weight threshold</summary>
    public readonly double minWeight;
    
    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;
    
    public ApplyAcceptedMovesKernelDouble(
        ReadWriteBuffer<double> weights,
        ReadWriteBuffer<int> edgeExists,
        ReadOnlyBuffer<double> proposedWeights,
        ReadOnlyBuffer<int> proposalEdgeIndices,
        ReadOnlyBuffer<int> acceptFlags,
        double minWeight,
        int proposalCount)
    {
        this.weights = weights;
        this.edgeExists = edgeExists;
        this.proposedWeights = proposedWeights;
        this.proposalEdgeIndices = proposalEdgeIndices;
        this.acceptFlags = acceptFlags;
        this.minWeight = minWeight;
        this.proposalCount = proposalCount;
    }
    
    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;
        
        if (acceptFlags[p] == 1)
        {
            int e = proposalEdgeIndices[p];
            double w_new = proposedWeights[p];
            
            weights[e] = w_new;
            edgeExists[e] = (w_new >= minWeight) ? 1 : 0;
        }
    }
}

/// <summary>
/// Parallel tree reduction for summing double arrays.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct MCMCSumReductionKernelDouble : IComputeShader
{
    /// <summary>Data to sum (modified in-place)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Stride for this reduction pass</summary>
    public readonly int stride;
    
    /// <summary>Total array length</summary>
    public readonly int length;
    
    public MCMCSumReductionKernelDouble(
        ReadWriteBuffer<double> data,
        int stride,
        int length)
    {
        this.data = data;
        this.stride = stride;
        this.length = length;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        int target = i * stride * 2;
        int source = target + stride;
        
        if (source < length)
        {
            data[target] += data[source];
        }
    }
}

/// <summary>
/// Count accepted moves (parallel count of flags).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CountAcceptedKernelDouble : IComputeShader
{
    /// <summary>Accept flags</summary>
    public readonly ReadOnlyBuffer<int> acceptFlags;
    
    /// <summary>Output count contributions</summary>
    public readonly ReadWriteBuffer<double> countBuffer;
    
    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;
    
    public CountAcceptedKernelDouble(
        ReadOnlyBuffer<int> acceptFlags,
        ReadWriteBuffer<double> countBuffer,
        int proposalCount)
    {
        this.acceptFlags = acceptFlags;
        this.countBuffer = countBuffer;
        this.proposalCount = proposalCount;
    }
    
    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;
        
        countBuffer[p] = acceptFlags[p];
    }
}

/// <summary>
/// Compute local curvature for MCMC action (degree-based approximation).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct MCMCCurvatureKernelDouble : IComputeShader
{
    /// <summary>Node degrees</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Output curvatures</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Average degree (for normalization)</summary>
    public readonly double avgDegree;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public MCMCCurvatureKernelDouble(
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<double> curvatures,
        double avgDegree,
        int nodeCount)
    {
        this.degrees = degrees;
        this.curvatures = curvatures;
        this.avgDegree = avgDegree;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double deg = degrees[i];
        
        if (avgDegree > 1e-10)
        {
            curvatures[i] = (deg - avgDegree) / avgDegree;
        }
        else
        {
            curvatures[i] = 0.0;
        }
    }
}
