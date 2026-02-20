using ComputeSharp;

namespace RQSimulation.GPUOptimized.RelationalTime;

/// <summary>
/// RQG-HYPOTHESIS: Lapse Field Shaders for Emergent Time
/// 
/// The Lapse function N controls how fast proper time flows at each point.
/// In General Relativity: d? = N · dt
/// 
/// RQG-HYPOTHESIS FORMULA:
/// N_i = 1 / (1 + ?|H_i|)
/// 
/// where:
/// - N_i = Lapse at node i
/// - ? = regularization constant (~1.0 in Planck units)
/// - H_i = Hamiltonian constraint violation
/// 
/// PHYSICS:
/// - At H ? 0 (flat space): N ? 1 (normal time flow)
/// - At H ? ? (singularity): N ? 0 (time freezes)
/// 
/// This is the SINGULARITY CENSORSHIP mechanism in RQG.
/// Time stops at singularities, preventing infinite evolution.
/// 
/// DEFINITION OF DONE:
/// 1. When H=0: Lapse = 1.0 exactly
/// 2. When H??: Lapse ? 0 (time freeze)
/// 3. System reproduces standard QM when space is flat
/// </summary>

/// <summary>
/// Primary Lapse field shader.
/// Computes N = 1 / (1 + ?|H|) from Hamiltonian constraint.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LapseFieldShaderDouble : IComputeShader
{
    /// <summary>Hamiltonian constraint violations H_i</summary>
    public readonly ReadOnlyBuffer<double> hamiltonianViolations;
    
    /// <summary>Output: Lapse function N_i</summary>
    public readonly ReadWriteBuffer<double> lapse;
    
    /// <summary>Regularization constant ? (Planck units: ~1.0)</summary>
    public readonly double alpha;
    
    /// <summary>Minimum Lapse (prevents division issues)</summary>
    public readonly double minLapse;
    
    /// <summary>Maximum Lapse (flat space limit)</summary>
    public readonly double maxLapse;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public LapseFieldShaderDouble(
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
        // Time stops where constraint is maximally violated (singularity)
        double N = 1.0 / (1.0 + alpha * absH);
        
        // Clamp to valid range
        if (N < minLapse) N = minLapse;
        if (N > maxLapse) N = maxLapse;
        
        lapse[i] = N;
    }
}

/// <summary>
/// Lapse field with exponential suppression.
/// N = exp(-?|H|) - faster decay near singularities.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LapseExponentialShaderDouble : IComputeShader
{
    /// <summary>Hamiltonian constraint violations H_i</summary>
    public readonly ReadOnlyBuffer<double> hamiltonianViolations;
    
    /// <summary>Output: Lapse function N_i</summary>
    public readonly ReadWriteBuffer<double> lapse;
    
    /// <summary>Suppression scale ?</summary>
    public readonly double alpha;
    
    /// <summary>Minimum Lapse</summary>
    public readonly double minLapse;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public LapseExponentialShaderDouble(
        ReadOnlyBuffer<double> hamiltonianViolations,
        ReadWriteBuffer<double> lapse,
        double alpha,
        double minLapse,
        int nodeCount)
    {
        this.hamiltonianViolations = hamiltonianViolations;
        this.lapse = lapse;
        this.alpha = alpha;
        this.minLapse = minLapse;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double H = hamiltonianViolations[i];
        double absH = H < 0 ? -H : H;
        
        // Exponential suppression: N = exp(-?|H|)
        double N = Hlsl.Exp((float)(-alpha * absH));
        
        if (N < minLapse) N = minLapse;
        
        lapse[i] = N;
    }
}

/// <summary>
/// Lapse field with entropy correction.
/// N = 1 / (1 + ?|H| + ?·S) where S is entanglement entropy.
/// 
/// Combines geometric (Hamiltonian) and quantum (entropy) time dilation.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LapseEntropicShaderDouble : IComputeShader
{
    /// <summary>Hamiltonian constraint violations H_i</summary>
    public readonly ReadOnlyBuffer<double> hamiltonianViolations;
    
    /// <summary>Entanglement entropy S_i at each node</summary>
    public readonly ReadOnlyBuffer<double> entropy;
    
    /// <summary>Output: Lapse function N_i</summary>
    public readonly ReadWriteBuffer<double> lapse;
    
    /// <summary>Hamiltonian coupling ?</summary>
    public readonly double alpha;
    
    /// <summary>Entropy coupling ?</summary>
    public readonly double beta;
    
    /// <summary>Minimum Lapse</summary>
    public readonly double minLapse;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public LapseEntropicShaderDouble(
        ReadOnlyBuffer<double> hamiltonianViolations,
        ReadOnlyBuffer<double> entropy,
        ReadWriteBuffer<double> lapse,
        double alpha,
        double beta,
        double minLapse,
        int nodeCount)
    {
        this.hamiltonianViolations = hamiltonianViolations;
        this.entropy = entropy;
        this.lapse = lapse;
        this.alpha = alpha;
        this.beta = beta;
        this.minLapse = minLapse;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double H = hamiltonianViolations[i];
        double absH = H < 0 ? -H : H;
        double S = entropy[i];
        
        // Combined Lapse: N = 1 / (1 + ?|H| + ?·S)
        // High entropy (many entangled states) slows time
        double N = 1.0 / (1.0 + alpha * absH + beta * S);
        
        if (N < minLapse) N = minLapse;
        
        lapse[i] = N;
    }
}

/// <summary>
/// Smoothed Lapse with spatial averaging.
/// Prevents discontinuous time flow between neighbors.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LapseSmoothingShaderDouble : IComputeShader
{
    /// <summary>Input Lapse values (unsmoothed)</summary>
    public readonly ReadOnlyBuffer<double> lapseIn;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output: Smoothed Lapse</summary>
    public readonly ReadWriteBuffer<double> lapseOut;
    
    /// <summary>Smoothing factor (0=none, 1=full average)</summary>
    public readonly double smoothingFactor;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public LapseSmoothingShaderDouble(
        ReadOnlyBuffer<double> lapseIn,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<double> lapseOut,
        double smoothingFactor,
        int nodeCount)
    {
        this.lapseIn = lapseIn;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.weights = weights;
        this.lapseOut = lapseOut;
        this.smoothingFactor = smoothingFactor;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double N_i = lapseIn[i];
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double sumWeightedLapse = 0.0;
        double totalWeight = 0.0;
        
        for (int k = start; k < end; k++)
        {
            int j = colIdx[k];
            double w = weights[k];
            
            sumWeightedLapse += w * lapseIn[j];
            totalWeight += w;
        }
        
        double avgLapse = totalWeight > 1e-10 ? sumWeightedLapse / totalWeight : N_i;
        
        // Blend: N_smooth = (1-s)·N_i + s·avg
        lapseOut[i] = (1.0 - smoothingFactor) * N_i + smoothingFactor * avgLapse;
    }
}

/// <summary>
/// Compute proper time step from Lapse.
/// d?_i = N_i · d?
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ProperTimeStepShaderDouble : IComputeShader
{
    /// <summary>Lapse function N_i</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>Output: Local proper time step d?_i</summary>
    public readonly ReadWriteBuffer<double> properTimeStep;
    
    /// <summary>Coordinate time step d? (affine parameter)</summary>
    public readonly double deltaLambda;
    
    /// <summary>Minimum time step (prevents zero evolution)</summary>
    public readonly double minDt;
    
    /// <summary>Maximum time step (stability)</summary>
    public readonly double maxDt;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public ProperTimeStepShaderDouble(
        ReadOnlyBuffer<double> lapse,
        ReadWriteBuffer<double> properTimeStep,
        double deltaLambda,
        double minDt,
        double maxDt,
        int nodeCount)
    {
        this.lapse = lapse;
        this.properTimeStep = properTimeStep;
        this.deltaLambda = deltaLambda;
        this.minDt = minDt;
        this.maxDt = maxDt;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        // d? = N · d?
        double dt = lapse[i] * deltaLambda;
        
        // Clamp to valid range
        if (dt < minDt) dt = minDt;
        if (dt > maxDt) dt = maxDt;
        
        properTimeStep[i] = dt;
    }
}

/// <summary>
/// Compute edge-local time step from node Lapses.
/// dt_edge = (N_i + N_j) / 2 · d?
/// 
/// Used in quantum edge evolution where edge time
/// is average of endpoint times.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeTimeStepShaderDouble : IComputeShader
{
    /// <summary>Lapse function at each node</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>Edge source nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Output: Local time step for each edge</summary>
    public readonly ReadWriteBuffer<double> edgeTimeStep;
    
    /// <summary>Coordinate time step d?</summary>
    public readonly double deltaLambda;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public EdgeTimeStepShaderDouble(
        ReadOnlyBuffer<double> lapse,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        ReadWriteBuffer<double> edgeTimeStep,
        double deltaLambda,
        int edgeCount)
    {
        this.lapse = lapse;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.edgeTimeStep = edgeTimeStep;
        this.deltaLambda = deltaLambda;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int i = edgeSrc[e];
        int j = edgeDst[e];
        
        double N_i = lapse[i];
        double N_j = lapse[j];
        
        // Average Lapse at edge endpoints
        double N_edge = (N_i + N_j) * 0.5;
        
        // Edge time step
        edgeTimeStep[e] = N_edge * deltaLambda;
    }
}
