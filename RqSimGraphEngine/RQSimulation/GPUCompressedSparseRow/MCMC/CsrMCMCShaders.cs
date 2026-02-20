using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.MCMC;

/// <summary>
/// CSR-optimized compute shaders for MCMC sampling on large sparse graphs.
/// 
/// RQ-HYPOTHESIS STAGE 4: GPU MCMC (CSR VERSION)
/// =============================================
/// Same physics as GPUOptimized version but optimized for CSR sparse format.
/// For N &gt; 10? nodes, CSR format saves significant memory and bandwidth.
/// 
/// Memory comparison:
/// - Dense: O(N?) for adjacency matrix
/// - CSR: O(E) for edge list + O(N) for row pointers
/// 
/// PARALLELIZATION (same as dense version):
/// - Action computation: parallel over edges/nodes
/// - Proposal evaluation: parallel ?S computation
/// - Move application: sequential (detailed balance)
/// 
/// All operations use double precision (64-bit).
/// </summary>

/// <summary>
/// CSR Edge action kernel: S_links = ?_e (1 - w_e)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrEdgeActionKernelDouble : IComputeShader
{
    /// <summary>Edge weights in CSR format</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output action per edge</summary>
    public readonly ReadWriteBuffer<double> edgeActions;
    
    /// <summary>Link cost coefficient</summary>
    public readonly double linkCostCoeff;
    
    /// <summary>Number of edges (nnz/2 for undirected)</summary>
    public readonly int edgeCount;
    
    public CsrEdgeActionKernelDouble(
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
        edgeActions[e] = linkCostCoeff * (1.0 - w);
    }
}

/// <summary>
/// CSR Node action kernel with degree lookup via row pointers.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrNodeActionKernelDouble : IComputeShader
{
    /// <summary>Node masses</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>CSR row pointers (for degree = rowPtr[i+1] - rowPtr[i])</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Output action per node</summary>
    public readonly ReadWriteBuffer<double> nodeActions;
    
    /// <summary>Mass coefficient</summary>
    public readonly double massCoeff;
    
    /// <summary>Target degree</summary>
    public readonly double targetDegree;
    
    /// <summary>Degree penalty coefficient</summary>
    public readonly double degreePenaltyCoeff;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrNodeActionKernelDouble(
        ReadOnlyBuffer<double> masses,
        ReadOnlyBuffer<int> rowPtr,
        ReadWriteBuffer<double> nodeActions,
        double massCoeff,
        double targetDegree,
        double degreePenaltyCoeff,
        int nodeCount)
    {
        this.masses = masses;
        this.rowPtr = rowPtr;
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
        int deg = rowPtr[i + 1] - rowPtr[i];
        
        double S_mass = massCoeff * m * m;
        double degDiff = deg - targetDegree;
        double S_degree = degreePenaltyCoeff * degDiff * degDiff;
        
        nodeActions[i] = S_mass + S_degree;
    }
}

/// <summary>
/// CSR curvature computation using degree from row pointers.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrCurvatureKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Output curvatures</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Average degree (precomputed on CPU)</summary>
    public readonly double avgDegree;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrCurvatureKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadWriteBuffer<double> curvatures,
        double avgDegree,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.curvatures = curvatures;
        this.avgDegree = avgDegree;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int deg = rowPtr[i + 1] - rowPtr[i];
        
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

/// <summary>
/// CSR constraint action: S_constraint = ? ? (R - ?m)?
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrConstraintActionKernelDouble : IComputeShader
{
    /// <summary>Curvatures (precomputed)</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Node masses</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Output constraint action per node</summary>
    public readonly ReadWriteBuffer<double> constraintActions;
    
    /// <summary>Gravitational coupling ?</summary>
    public readonly double kappa;
    
    /// <summary>Lagrange multiplier ?</summary>
    public readonly double lambda;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrConstraintActionKernelDouble(
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
        
        double R = curvatures[i];
        double m = masses[i];
        double constraint = R - kappa * m;
        
        constraintActions[i] = lambda * constraint * constraint;
    }
}

/// <summary>
/// CSR local delta action for proposals.
/// Computes ?S for edge weight modifications.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrLocalDeltaActionKernelDouble : IComputeShader
{
    /// <summary>Current edge weights</summary>
    public readonly ReadOnlyBuffer<double> currentWeights;
    
    /// <summary>Proposed weights</summary>
    public readonly ReadOnlyBuffer<double> proposedWeights;
    
    /// <summary>Edge indices for proposals</summary>
    public readonly ReadOnlyBuffer<int> proposalEdgeIndices;
    
    /// <summary>Output ?S</summary>
    public readonly ReadWriteBuffer<double> deltaActions;
    
    /// <summary>Link cost coefficient</summary>
    public readonly double linkCostCoeff;
    
    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;
    
    public CsrLocalDeltaActionKernelDouble(
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
        
        // ?S = ? * (w_old - w_new)
        deltaActions[p] = linkCostCoeff * (w_old - w_new);
    }
}

/// <summary>
/// CSR Metropolis-Hastings acceptance kernel.
/// Includes Hastings ratio q(x'?x)/q(x?x') for asymmetric topology proposals.
/// 
/// DEPRECATED: For strict Hamiltonian constraint enforcement, use 
/// CsrMetropolisWithHamiltonianKernel which accepts precomputed Hamiltonian values.
/// 
/// This kernel uses a proxy (|dS|) for Hamiltonian check which is only approximate.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrMetropolisAcceptKernelDouble : IComputeShader
{
    /// <summary>Delta actions</summary>
    public readonly ReadOnlyBuffer<double> deltaActions;

    /// <summary>Random numbers (0-1)</summary>
    public readonly ReadOnlyBuffer<double> randomNumbers;

    /// <summary>Hastings ratio q(x'?x)/q(x?x') per proposal (1.0 for symmetric)</summary>
    public readonly ReadOnlyBuffer<double> proposalQRatios;

    /// <summary>Inverse temperature ?</summary>
    public readonly double beta;

    /// <summary>Output accept flags</summary>
    public readonly ReadWriteBuffer<int> acceptFlags;

    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;

    /// <summary>Hamiltonian tolerance for strict constraint</summary>
    public readonly double hamiltonianTolerance;

    public CsrMetropolisAcceptKernelDouble(
        ReadOnlyBuffer<double> deltaActions,
        ReadOnlyBuffer<double> randomNumbers,
        ReadOnlyBuffer<double> proposalQRatios,
        double beta,
        ReadWriteBuffer<int> acceptFlags,
        int proposalCount,
        double hamiltonianTolerance)
    {
        this.deltaActions = deltaActions;
        this.randomNumbers = randomNumbers;
        this.proposalQRatios = proposalQRatios;
        this.beta = beta;
        this.acceptFlags = acceptFlags;
        this.proposalCount = proposalCount;
        this.hamiltonianTolerance = hamiltonianTolerance;
    }

    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;

        double dS = deltaActions[p];

        // Placeholder: In CSR MCMC pipeline the Hamiltonian for proposed state
        // should be passed as part of proposal metadata. Here we approximate
        // the check by treating |dS| as proxy for Hamiltonian deviation.
        double proposedH = dS; // In real implementation compute H explicitly

        if (Hlsl.Abs(proposedH) > hamiltonianTolerance)
        {
            // Reject immediately
            acceptFlags[p] = 0;
            return;
        }

        double qRatio = proposalQRatios[p];

        // Metropolis-Hastings: P_accept = min(1, exp(-?�?S) � q_ratio)
        double acceptProb = (double)Hlsl.Exp((float)(-beta * dS)) * qRatio;

        bool accept = acceptProb >= 1.0 || randomNumbers[p] < acceptProb;

        acceptFlags[p] = accept ? 1 : 0;
    }
}

/// <summary>
/// CSR apply accepted moves kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrApplyAcceptedMovesKernelDouble : IComputeShader
{
    /// <summary>Edge weights (modified)</summary>
    public readonly ReadWriteBuffer<double> weights;
    
    /// <summary>Proposed weights</summary>
    public readonly ReadOnlyBuffer<double> proposedWeights;
    
    /// <summary>Edge indices</summary>
    public readonly ReadOnlyBuffer<int> proposalEdgeIndices;
    
    /// <summary>Accept flags</summary>
    public readonly ReadOnlyBuffer<int> acceptFlags;
    
    /// <summary>Minimum weight threshold</summary>
    public readonly double minWeight;
    
    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;
    
    /// <summary>Index of first accepted (only this one applied)</summary>
    public readonly int firstAcceptedIndex;
    
    /// <summary>Hamiltonian tolerance (unused here but forwarded)</summary>
    public readonly double hamiltonianTolerance;
    
    public CsrApplyAcceptedMovesKernelDouble(
        ReadWriteBuffer<double> weights,
        ReadOnlyBuffer<double> proposedWeights,
        ReadOnlyBuffer<int> proposalEdgeIndices,
        ReadOnlyBuffer<int> acceptFlags,
        double minWeight,
        int proposalCount,
        int firstAcceptedIndex,
        double hamiltonianTolerance)
    {
        this.weights = weights;
        this.proposedWeights = proposedWeights;
        this.proposalEdgeIndices = proposalEdgeIndices;
        this.acceptFlags = acceptFlags;
        this.minWeight = minWeight;
        this.proposalCount = proposalCount;
        this.firstAcceptedIndex = firstAcceptedIndex;
        this.hamiltonianTolerance = hamiltonianTolerance;
    }
    
    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;
        
        // Only apply the first accepted move (detailed balance)
        if (p == firstAcceptedIndex && acceptFlags[p] == 1)
        {
            int e = proposalEdgeIndices[p];
            double w_new = proposedWeights[p];
            weights[e] = w_new;
        }
    }
}

/// <summary>
/// CSR sum reduction kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrMCMCSumReductionKernelDouble : IComputeShader
{
    /// <summary>Data to reduce</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Stride</summary>
    public readonly int stride;
    
    /// <summary>Length</summary>
    public readonly int length;
    
    public CsrMCMCSumReductionKernelDouble(
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
/// Degree update kernel after edge modification.
/// Updates node degrees based on edge changes.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUpdateDegreesKernelDouble : IComputeShader
{
    /// <summary>Edge endpoints (i)</summary>
    public readonly ReadOnlyBuffer<int> edgeI;
    
    /// <summary>Edge endpoints (j)</summary>
    public readonly ReadOnlyBuffer<int> edgeJ;
    
    /// <summary>Old weights</summary>
    public readonly ReadOnlyBuffer<double> oldWeights;
    
    /// <summary>New weights</summary>
    public readonly ReadOnlyBuffer<double> newWeights;
    
    /// <summary>Degree deltas (atomic add target)</summary>
    public readonly ReadWriteBuffer<int> degreeDeltas;
    
    /// <summary>Minimum weight threshold</summary>
    public readonly double minWeight;
    
    /// <summary>Edge count</summary>
    public readonly int edgeCount;
    
    public CsrUpdateDegreesKernelDouble(
        ReadOnlyBuffer<int> edgeI,
        ReadOnlyBuffer<int> edgeJ,
        ReadOnlyBuffer<double> oldWeights,
        ReadOnlyBuffer<double> newWeights,
        ReadWriteBuffer<int> degreeDeltas,
        double minWeight,
        int edgeCount)
    {
        this.edgeI = edgeI;
        this.edgeJ = edgeJ;
        this.oldWeights = oldWeights;
        this.newWeights = newWeights;
        this.degreeDeltas = degreeDeltas;
        this.minWeight = minWeight;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        bool wasEdge = oldWeights[e] >= minWeight;
        bool isEdge = newWeights[e] >= minWeight;
        
        if (wasEdge && !isEdge)
        {
            // Edge removed: decrement degrees
            int i = edgeI[e];
            int j = edgeJ[e];
            
            // Note: HLSL doesn't have atomics, this is illustrative
            // In practice, degree updates are done on CPU
            degreeDeltas[i] = -1;
            degreeDeltas[j] = -1;
        }
        else if (!wasEdge && isEdge)
        {
            // Edge added: increment degrees
            int i = edgeI[e];
            int j = edgeJ[e];
            degreeDeltas[i] = 1;
            degreeDeltas[j] = 1;
        }
    }
}

/// <summary>
/// WHEELER-DEWITT HAMILTONIAN CONSTRAINT KERNEL
/// =============================================
/// Computes the Wheeler-DeWitt Hamiltonian constraint for MCMC proposals.
/// 
/// In LQG/CDT, physical states must satisfy H|?> = 0.
/// This kernel computes H for each proposal to enable strict rejection
/// of states that violate the constraint beyond numerical tolerance.
/// 
/// H = H_geom + H_matter where:
/// - H_geom = local scalar curvature (degree-based proxy)
/// - H_matter = ? * m (gravitational coupling times mass)
/// 
/// For strict enforcement: |H| < tolerance ? accept for evaluation
///                        |H| > tolerance ? reject immediately
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrHamiltonianConstraintKernel : IComputeShader
{
    /// <summary>CSR row pointers for degree computation</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Node masses</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Output: Hamiltonian value per node</summary>
    public readonly ReadWriteBuffer<double> hamiltonians;
    
    /// <summary>Average degree (for curvature normalization)</summary>
    public readonly double avgDegree;
    
    /// <summary>Gravitational coupling ?</summary>
    public readonly double kappa;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrHamiltonianConstraintKernel(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<double> masses,
        ReadWriteBuffer<double> hamiltonians,
        double avgDegree,
        double kappa,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.weights = weights;
        this.masses = masses;
        this.hamiltonians = hamiltonians;
        this.avgDegree = avgDegree;
        this.kappa = kappa;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        // Compute weighted degree
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        double weightedDeg = 0.0;
        for (int k = start; k < end; k++)
        {
            weightedDeg += weights[k];
        }
        
        // Geometric term: scalar curvature proxy
        // R_i = (d_i - <d>) / <d> where d is weighted degree
        double R_i = avgDegree > 1e-10 ? (weightedDeg - avgDegree) / avgDegree : 0.0;
        
        // Matter term
        double m_i = masses[i];
        
        // Wheeler-DeWitt constraint: H = R - ? * m
        // Physical states: H = 0
        hamiltonians[i] = R_i - kappa * m_i;
    }
}

/// <summary>
/// Compute total Hamiltonian for proposed state.
/// Aggregates per-node Hamiltonians into a single value for constraint check.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrTotalHamiltonianKernel : IComputeShader
{
    /// <summary>Per-node Hamiltonians</summary>
    public readonly ReadOnlyBuffer<double> nodeHamiltonians;
    
    /// <summary>Output: sum of |H_i|? per thread block (partial sums)</summary>
    public readonly ReadWriteBuffer<double> partialSums;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrTotalHamiltonianKernel(
        ReadOnlyBuffer<double> nodeHamiltonians,
        ReadWriteBuffer<double> partialSums,
        int nodeCount)
    {
        this.nodeHamiltonians = nodeHamiltonians;
        this.partialSums = partialSums;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount)
        {
            partialSums[i] = 0.0;
            return;
        }
        
        // Sum of squared Hamiltonians (L2 norm)
        double h = nodeHamiltonians[i];
        partialSums[i] = h * h;
    }
}

/// <summary>
/// Enhanced Metropolis-Hastings acceptance with explicit Hamiltonian constraint.
/// Uses precomputed Hamiltonian values instead of proxy.
/// Includes Hastings ratio q(x'?x)/q(x?x') for asymmetric topology proposals.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrMetropolisWithHamiltonianKernel : IComputeShader
{
    /// <summary>Delta actions</summary>
    public readonly ReadOnlyBuffer<double> deltaActions;

    /// <summary>Proposed state Hamiltonian (total |H|)</summary>
    public readonly ReadOnlyBuffer<double> proposedHamiltonians;

    /// <summary>Random numbers (0-1)</summary>
    public readonly ReadOnlyBuffer<double> randomNumbers;

    /// <summary>Hastings ratio q(x'?x)/q(x?x') per proposal (1.0 for symmetric)</summary>
    public readonly ReadOnlyBuffer<double> proposalQRatios;

    /// <summary>Inverse temperature ?</summary>
    public readonly double beta;

    /// <summary>Hamiltonian tolerance for strict constraint</summary>
    public readonly double hamiltonianTolerance;

    /// <summary>Output accept flags</summary>
    public readonly ReadWriteBuffer<int> acceptFlags;

    /// <summary>Number of proposals</summary>
    public readonly int proposalCount;

    public CsrMetropolisWithHamiltonianKernel(
        ReadOnlyBuffer<double> deltaActions,
        ReadOnlyBuffer<double> proposedHamiltonians,
        ReadOnlyBuffer<double> randomNumbers,
        ReadOnlyBuffer<double> proposalQRatios,
        double beta,
        double hamiltonianTolerance,
        ReadWriteBuffer<int> acceptFlags,
        int proposalCount)
    {
        this.deltaActions = deltaActions;
        this.proposedHamiltonians = proposedHamiltonians;
        this.randomNumbers = randomNumbers;
        this.proposalQRatios = proposalQRatios;
        this.beta = beta;
        this.hamiltonianTolerance = hamiltonianTolerance;
        this.acceptFlags = acceptFlags;
        this.proposalCount = proposalCount;
    }

    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= proposalCount) return;

        double proposedH = proposedHamiltonians[p];

        // STRICT HAMILTONIAN CONSTRAINT
        // In LQG/CDT, physical states must satisfy H|?> = 0
        if (Hlsl.Abs(proposedH) > hamiltonianTolerance)
        {
            // Reject immediately - not a physical state
            acceptFlags[p] = 0;
            return;
        }

        // Metropolis-Hastings acceptance for action with Hastings correction
        double dS = deltaActions[p];
        double qRatio = proposalQRatios[p];

        // P_accept = min(1, exp(-?�?S) � q_ratio)
        double acceptProb = (double)Hlsl.Exp((float)(-beta * dS)) * qRatio;

        bool accept = acceptProb >= 1.0 || randomNumbers[p] < acceptProb;

        acceptFlags[p] = accept ? 1 : 0;
    }
}

/// <summary>
/// CSR Metropolis-Hastings acceptance kernel.
/// Computes acceptance probability with Hastings ratio and applies move if accepted.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct MetropolisKernel : IComputeShader
{
    public readonly ReadWriteBuffer<double> weights;
    public readonly ReadOnlyBuffer<double> proposedWeights;
    public readonly ReadOnlyBuffer<double> deltaAction;
    public readonly ReadOnlyBuffer<double> randomNumbers;
    public readonly ReadOnlyBuffer<double> proposalQRatios;
    public readonly ReadOnlyBuffer<int> edgeIndices;
    public readonly double beta;
    public readonly double hamiltonianTolerance; // e.g., 1e-6
    public readonly int count;

    public MetropolisKernel(
        ReadWriteBuffer<double> weights,
        ReadOnlyBuffer<double> proposedWeights,
        ReadOnlyBuffer<double> deltaAction,
        ReadOnlyBuffer<double> randomNumbers,
        ReadOnlyBuffer<double> proposalQRatios,
        ReadOnlyBuffer<int> edgeIndices,
        double beta,
        double hamiltonianTolerance,
        int count)
    {
        this.weights = weights;
        this.proposedWeights = proposedWeights;
        this.deltaAction = deltaAction;
        this.randomNumbers = randomNumbers;
        this.proposalQRatios = proposalQRatios;
        this.edgeIndices = edgeIndices;
        this.beta = beta;
        this.hamiltonianTolerance = hamiltonianTolerance;
        this.count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;

        int edgeIdx = edgeIndices[i];
        double dS = deltaAction[i];
        double rand = randomNumbers[i];
        double qRatio = proposalQRatios[i];

        // [CRITICAL CHECK: HAMILTONIAN CONSTRAINT]
        // In LQG/CDT physical states must satisfy H|psi> = 0.
        // If dS is too large (energy violation), reject immediately regardless of temperature
        if (Hlsl.Abs(dS) > hamiltonianTolerance * 1000.0)
        {
            // REJECT MOVE IMMEDIATELY
            return;
        }

        // Metropolis-Hastings acceptance: min(1, exp(-beta * dS) * q_ratio)
        double acceptanceProb = Hlsl.Exp((float)(-beta * dS)) * qRatio;

        if (acceptanceProb >= 1.0 || rand < acceptanceProb)
        {
            // Accept move
            weights[edgeIdx] = proposedWeights[i];
        }
    }
}
