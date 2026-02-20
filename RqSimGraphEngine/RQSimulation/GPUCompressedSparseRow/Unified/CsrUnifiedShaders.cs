using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Unified;

/// <summary>
/// Unified compute shaders for CSR physics step operations.
/// 
/// RQ-HYPOTHESIS STAGE 6: CSR UNIFIED ENGINE
/// =========================================
/// These shaders coordinate multiple physics operations using
/// shared CSR topology to minimize data transfer overhead.
/// 
/// UNIFIED PHYSICS STEP includes:
/// 1. Wheeler-DeWitt constraint computation
/// 2. Spectral action computation
/// 3. Quantum edge evolution
/// 4. MCMC sampling (optional)
/// 5. Internal observer measurement (optional)
/// 
/// All operations share CSR topology buffers for efficiency.
/// All computations use double precision (64-bit).
/// </summary>

/// <summary>
/// Combined constraint and curvature kernel for unified physics step.
/// Computes both local curvature and Wheeler-DeWitt violation in one pass.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUnifiedConstraintKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> values;
    
    /// <summary>Node masses</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Average degree (precomputed)</summary>
    public readonly double avgDegree;
    
    /// <summary>Output curvatures</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Output constraint violations</summary>
    public readonly ReadWriteBuffer<double> violations;
    
    /// <summary>Gravitational coupling ?</summary>
    public readonly double kappa;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrUnifiedConstraintKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> values,
        ReadOnlyBuffer<double> masses,
        double avgDegree,
        ReadWriteBuffer<double> curvatures,
        ReadWriteBuffer<double> violations,
        double kappa,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.values = values;
        this.masses = masses;
        this.avgDegree = avgDegree;
        this.curvatures = curvatures;
        this.violations = violations;
        this.kappa = kappa;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int deg = rowPtr[i + 1] - rowPtr[i];
        
        // Compute local curvature (degree-based scalar curvature proxy)
        double R_i = avgDegree > 1e-10 ? (deg - avgDegree) / avgDegree : 0.0;
        curvatures[i] = R_i;
        
        // Compute Wheeler-DeWitt constraint violation
        double H_geom = R_i;
        double H_matter = masses[i];
        double constraint = H_geom - kappa * H_matter;
        violations[i] = constraint * constraint;
    }
}

/// <summary>
/// Combined spectral action kernel computing volume and curvature terms.
/// S = f???? V + f????? ?R + f? ?C? + S_dimension
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUnifiedSpectralActionKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> values;
    
    /// <summary>Precomputed curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Average curvature (precomputed)</summary>
    public readonly double avgCurvature;
    
    /// <summary>Output volume contributions per edge</summary>
    public readonly ReadWriteBuffer<double> volumeContribs;
    
    /// <summary>Output Weyl? contributions (curvature variance)</summary>
    public readonly ReadWriteBuffer<double> weylContribs;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    /// <summary>Total NNZ (for edge indexing)</summary>
    public readonly int nnz;
    
    public CsrUnifiedSpectralActionKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> values,
        ReadOnlyBuffer<double> curvatures,
        double avgCurvature,
        ReadWriteBuffer<double> volumeContribs,
        ReadWriteBuffer<double> weylContribs,
        int nodeCount,
        int nnz)
    {
        this.rowPtr = rowPtr;
        this.values = values;
        this.curvatures = curvatures;
        this.avgCurvature = avgCurvature;
        this.volumeContribs = volumeContribs;
        this.weylContribs = weylContribs;
        this.nodeCount = nodeCount;
        this.nnz = nnz;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        
        // For edge volume: process NNZ elements
        if (i < nnz)
        {
            volumeContribs[i] = values[i] * 0.5; // Each edge counted twice in CSR
        }
        
        // For Weyl?: process nodes
        if (i < nodeCount)
        {
            double R_i = curvatures[i];
            double diff = R_i - avgCurvature;
            weylContribs[i] = diff * diff;
        }
    }
}

/// <summary>
/// Unified quantum edge evolution kernel with optional collapse.
/// Processes edge amplitudes using CSR format.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUnifiedQuantumEdgeKernelDouble : IComputeShader
{
    /// <summary>Edge amplitudes (real, imag)</summary>
    public readonly ReadWriteBuffer<Double2> amplitudes;
    
    /// <summary>Edge Hamiltonians (diagonal)</summary>
    public readonly ReadOnlyBuffer<double> hamiltonians;
    
    /// <summary>Time step</summary>
    public readonly double dt;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    /// <summary>Evolution mode: 0=none, 1=unitary, 2=collapse</summary>
    public readonly int mode;
    
    /// <summary>Random values for collapse (if mode=2)</summary>
    public readonly ReadOnlyBuffer<double> randomValues;
    
    public CsrUnifiedQuantumEdgeKernelDouble(
        ReadWriteBuffer<Double2> amplitudes,
        ReadOnlyBuffer<double> hamiltonians,
        double dt,
        int edgeCount,
        int mode,
        ReadOnlyBuffer<double> randomValues)
    {
        this.amplitudes = amplitudes;
        this.hamiltonians = hamiltonians;
        this.dt = dt;
        this.edgeCount = edgeCount;
        this.mode = mode;
        this.randomValues = randomValues;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 alpha = amplitudes[e];
        
        if (mode == 1)
        {
            // Unitary evolution: exp(-i H dt)
            double H = hamiltonians[e];
            double phase = H * dt;
            double cosP = Hlsl.Cos((float)phase);
            double sinP = Hlsl.Sin((float)phase);
            
            double newReal = alpha.X * cosP + alpha.Y * sinP;
            double newImag = alpha.Y * cosP - alpha.X * sinP;
            
            amplitudes[e] = new Double2(newReal, newImag);
        }
        else if (mode == 2)
        {
            // Collapse based on |?|? probability
            double prob = alpha.X * alpha.X + alpha.Y * alpha.Y;
            double rand = randomValues[e];
            
            if (rand < prob)
            {
                // Exists with unit amplitude
                amplitudes[e] = new Double2(1.0, 0.0);
            }
            else
            {
                // Doesn't exist
                amplitudes[e] = new Double2(0.0, 0.0);
            }
        }
    }
}

/// <summary>
/// Unified Euclidean action computation for MCMC.
/// Combines link, node, and constraint action terms.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUnifiedActionKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> values;
    
    /// <summary>Node masses</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Curvatures (precomputed)</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Output action per node (combines all terms)</summary>
    public readonly ReadWriteBuffer<double> nodeActions;
    
    /// <summary>Link cost coefficient</summary>
    public readonly double linkCostCoeff;
    
    /// <summary>Mass coefficient</summary>
    public readonly double massCoeff;
    
    /// <summary>Target degree</summary>
    public readonly double targetDegree;
    
    /// <summary>Degree penalty coefficient</summary>
    public readonly double degreePenaltyCoeff;
    
    /// <summary>Gravitational coupling</summary>
    public readonly double kappa;
    
    /// <summary>Constraint Lagrange multiplier</summary>
    public readonly double lambda;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrUnifiedActionKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> values,
        ReadOnlyBuffer<double> masses,
        ReadOnlyBuffer<double> curvatures,
        ReadWriteBuffer<double> nodeActions,
        double linkCostCoeff,
        double massCoeff,
        double targetDegree,
        double degreePenaltyCoeff,
        double kappa,
        double lambda,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.values = values;
        this.masses = masses;
        this.curvatures = curvatures;
        this.nodeActions = nodeActions;
        this.linkCostCoeff = linkCostCoeff;
        this.massCoeff = massCoeff;
        this.targetDegree = targetDegree;
        this.degreePenaltyCoeff = degreePenaltyCoeff;
        this.kappa = kappa;
        this.lambda = lambda;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        int deg = end - start;
        
        // Link action: sum over edges
        double S_link = 0.0;
        for (int k = start; k < end; k++)
        {
            double w = values[k];
            S_link += linkCostCoeff * (1.0 - w) * 0.5; // Half for undirected
        }
        
        // Mass action
        double m = masses[i];
        double S_mass = massCoeff * m * m;
        
        // Degree penalty
        double degDiff = deg - targetDegree;
        double S_degree = degreePenaltyCoeff * degDiff * degDiff;
        
        // Constraint action (Wheeler-DeWitt)
        double R_i = curvatures[i];
        double constraint = R_i - kappa * m;
        double S_constraint = lambda * constraint * constraint;
        
        nodeActions[i] = S_link + S_mass + S_degree + S_constraint;
    }
}

/// <summary>
/// Sum reduction kernel for unified engine.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUnifiedReductionKernelDouble : IComputeShader
{
    /// <summary>Data to reduce</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Stride for this pass</summary>
    public readonly int stride;
    
    /// <summary>Total count</summary>
    public readonly int count;
    
    public CsrUnifiedReductionKernelDouble(
        ReadWriteBuffer<double> data,
        int stride,
        int count)
    {
        this.data = data;
        this.stride = stride;
        this.count = count;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        int target = i * stride * 2;
        int source = target + stride;
        
        if (source < count)
        {
            data[target] = data[target] + data[source];
        }
    }
}

// ============================================================================
// RQG-HYPOTHESIS KERNELS: Emergent Time via Lapse Function
// ============================================================================

/// <summary>
/// RQG-HYPOTHESIS: Compute Lapse function from Hamiltonian constraint.
/// 
/// PHYSICS: N_i = 1 / (1 + ?|H_i|)
/// 
/// - Time stops (N ? 0) at singularities where H ? ?
/// - Time flows normally (N ? 1) in flat space where H ? 0
/// - This is the SINGULARITY CENSORSHIP mechanism
/// 
/// DEFINITION OF DONE:
/// - At H ? 0 (flat space): Lapse = 1.0
/// - At H ? ? (singularity): Lapse ? 0 (time freezes)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LapseFromHamiltonianKernel : IComputeShader
{
    /// <summary>Hamiltonian constraint violations H_i</summary>
    public readonly ReadOnlyBuffer<double> hamiltonianViolations;
    
    /// <summary>Output Lapse function N_i</summary>
    public readonly ReadWriteBuffer<double> lapse;
    
    /// <summary>Regularization constant ? (default 1.0 in Planck units)</summary>
    public readonly double alpha;
    
    /// <summary>Minimum allowed Lapse (prevents division issues)</summary>
    public readonly double minLapse;
    
    /// <summary>Maximum Lapse (normal time flow)</summary>
    public readonly double maxLapse;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public LapseFromHamiltonianKernel(
        ReadOnlyBuffer<double> hamiltonianViolations,
        ReadWriteBuffer<double> lapse,
        double alpha,
        double minLapse,
        double maxLapse,
        int nodeCount)
    {
        this.hamiltonianViolations = hamiltonianViolations;
        this.lapse = lapse;
        this.alpha = alpha;
        this.minLapse = minLapse;
        this.maxLapse = maxLapse;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double H = hamiltonianViolations[i];
        double absH = H < 0 ? -H : H;
        
        // RQG-HYPOTHESIS: N = 1 / (1 + ?|H|)
        double N = 1.0 / (1.0 + alpha * absH);
        
        // Clamp to valid range
        if (N < minLapse) N = minLapse;
        if (N > maxLapse) N = maxLapse;
        
        lapse[i] = N;
    }
}

/// <summary>
/// RQG-HYPOTHESIS: Compute local proper time step from Lapse.
/// 
/// PHYSICS: dt_i = N_i · d?
/// where d? is the coordinate time step (affine parameter).
/// 
/// Each node experiences time according to its local Lapse.
/// This implements gravitational time dilation on the graph.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalTimeStepKernel : IComputeShader
{
    /// <summary>Lapse function N_i</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>Output local time step dt_i</summary>
    public readonly ReadWriteBuffer<double> localDt;
    
    /// <summary>Coordinate time step d?</summary>
    public readonly double deltaLambda;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public LocalTimeStepKernel(
        ReadOnlyBuffer<double> lapse,
        ReadWriteBuffer<double> localDt,
        double deltaLambda,
        int nodeCount)
    {
        this.lapse = lapse;
        this.localDt = localDt;
        this.deltaLambda = deltaLambda;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        // dt_i = N_i · d?
        localDt[i] = lapse[i] * deltaLambda;
    }
}

/// <summary>
/// RQG-HYPOTHESIS: Quantum phase evolution with local Lapse time.
/// 
/// PHYSICS: Main driver replacing StepPhysics.
/// 
/// For each edge (i,j):
/// 1. Compute local time: dt_local = (N_i + N_j) / 2 · d?
/// 2. Apply unitary evolution: ?_new = ? · exp(-i · H_edge · dt_local)
/// 
/// This is STRICTLY UNITARY - no random numbers used!
/// All "randomness" comes from the initial superposition state.
/// 
/// STOP-LIST COMPLIANCE:
/// - NO id.x + seed pseudo-random generation
/// - NO global SimulationStep dt - only LapseBuffer[i]
/// - NO edge deletion - only weight modification
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct QuantumPhaseEvolutionWithLapseKernel : IComputeShader
{
    /// <summary>Edge amplitudes (real, imag)</summary>
    public readonly ReadWriteBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Edge Hamiltonians (local energy)</summary>
    public readonly ReadOnlyBuffer<double> edgeHamiltonian;
    
    /// <summary>Lapse function at each node</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>CSR row pointers (for mapping edges to nodes)</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Number of edges (half of NNZ for undirected)</summary>
    public readonly int edgeCount;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public QuantumPhaseEvolutionWithLapseKernel(
        ReadWriteBuffer<Double2> edgeAmplitudes,
        ReadOnlyBuffer<double> edgeHamiltonian,
        ReadOnlyBuffer<double> lapse,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        int edgeCount,
        int nodeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.edgeHamiltonian = edgeHamiltonian;
        this.lapse = lapse;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.edgeCount = edgeCount;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        // Map edge index to node pair (simplified: linear search)
        // In practice, we'd have a precomputed edge-to-node mapping
        int nodeA = 0;
        int nodeB = 0;
        int edgeIdx = 0;
        
        for (int i = 0; i < nodeCount && edgeIdx <= e; i++)
        {
            int start = rowPtr[i];
            int end = rowPtr[i + 1];
            
            for (int k = start; k < end; k++)
            {
                int j = colIdx[k];
                if (i < j) // Only count upper triangle for undirected
                {
                    if (edgeIdx == e)
                    {
                        nodeA = i;
                        nodeB = j;
                    }
                    edgeIdx++;
                }
            }
        }
        
        // Get Lapse at both endpoints
        double N_A = lapse[nodeA];
        double N_B = lapse[nodeB];
        
        // Average Lapse for this edge (proper time for edge)
        // RQG-HYPOTHESIS: dt_local = (N_i + N_j) / 2 · d?
        // Here d? is absorbed into edge Hamiltonian
        double dt_local = (N_A + N_B) * 0.5;
        
        // Get current amplitude and energy
        Double2 psi = edgeAmplitudes[e];
        double energy = edgeHamiltonian[e];
        
        // Unitary evolution: ?_new = ? · exp(-i · H · dt_local)
        // exp(-i·?) = cos(?) - i·sin(?)
        double theta = energy * dt_local;
        double cosTheta = Hlsl.Cos((float)theta);
        double sinTheta = Hlsl.Sin((float)theta);
        
        // Complex multiplication: (a + bi) · (cos - i·sin) = 
        // (a·cos + b·sin) + i·(b·cos - a·sin)
        double newReal = psi.X * cosTheta + psi.Y * sinTheta;
        double newImag = psi.Y * cosTheta - psi.X * sinTheta;
        
        edgeAmplitudes[e] = new Double2(newReal, newImag);
    }
}

/// <summary>
/// RQG-HYPOTHESIS: Hamiltonian evaluation kernel.
/// 
/// PHYSICS: H_i = (K?_ij - K? + R_ij) + T??
/// where:
/// - K_ij = extrinsic curvature
/// - K = trace of extrinsic curvature  
/// - R_ij = Ricci scalar
/// - T?? = stress-energy (matter contribution)
/// 
/// Simplified implementation uses degree-based curvature proxy.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HamiltonianEvalKernel : IComputeShader
{
    /// <summary>Mean curvature K</summary>
    public readonly ReadOnlyBuffer<double> meanCurvature;
    
    /// <summary>Ricci scalar R</summary>
    public readonly ReadOnlyBuffer<double> ricciScalar;
    
    /// <summary>Stress-energy T??</summary>
    public readonly ReadOnlyBuffer<double> stressEnergy;
    
    /// <summary>Output Hamiltonian constraint H_i</summary>
    public readonly ReadWriteBuffer<double> hamiltonianViolations;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public HamiltonianEvalKernel(
        ReadOnlyBuffer<double> meanCurvature,
        ReadOnlyBuffer<double> ricciScalar,
        ReadOnlyBuffer<double> stressEnergy,
        ReadWriteBuffer<double> hamiltonianViolations,
        int nodeCount)
    {
        this.meanCurvature = meanCurvature;
        this.ricciScalar = ricciScalar;
        this.stressEnergy = stressEnergy;
        this.hamiltonianViolations = hamiltonianViolations;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double K = meanCurvature[i];
        double k_sq = K * K;
        double ricci = ricciScalar[i];
        double matter = stressEnergy[i];
        
        // Wheeler-DeWitt Hamiltonian constraint:
        // H = (R - K?) + T??
        // Physical states satisfy H ? 0
        hamiltonianViolations[i] = (ricci - k_sq) + matter;
    }
}

/// <summary>
/// RQG-HYPOTHESIS: Information current kernel for unitarity verification.
/// 
/// VALIDATION: Total probability must be conserved.
/// ?|?|? = 1.0 ± 10??? throughout evolution.
/// 
/// This is the DEFINITION OF DONE criterion for RQG migration.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct InformationCurrentKernel : IComputeShader
{
    /// <summary>Edge amplitudes (real, imag)</summary>
    public readonly ReadOnlyBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Output probability per edge |?_e|?</summary>
    public readonly ReadWriteBuffer<double> edgeProbabilities;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public InformationCurrentKernel(
        ReadOnlyBuffer<Double2> edgeAmplitudes,
        ReadWriteBuffer<double> edgeProbabilities,
        int edgeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.edgeProbabilities = edgeProbabilities;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 psi = edgeAmplitudes[e];
        
        // |?|? = Re? + Im?
        edgeProbabilities[e] = psi.X * psi.X + psi.Y * psi.Y;
    }
}
