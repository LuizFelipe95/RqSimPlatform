using ComputeSharp;

namespace RQSimulation.GPUOptimized.QuantumEdges;

/// <summary>
/// RQG-HYPOTHESIS: Information Current and Unitarity Check
/// 
/// PHYSICS:
/// Information current is the quantum probability current between nodes.
/// For unitary evolution, the total probability must be conserved:
/// ?|?|? = const
/// 
/// This shader computes:
/// 1. Local probability density |?|? at each node
/// 2. Information current J_ij between connected nodes
/// 3. Divergence of current ?·J (should be zero for unitary)
/// 4. Global unitarity check ?|?|?
/// 
/// NON-UNITARY SOURCES:
/// - Numerical drift (accumulation errors)
/// - Decoherence (environmental entanglement)
/// - Measurement-like events (state collapse)
/// 
/// STOP-LIST COMPLIANCE:
/// ? All computations through weighted CSR edges
/// ? No Random usage
/// </summary>

/// <summary>
/// Compute probability density |?|? at each node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ProbabilityDensityShaderDouble : IComputeShader
{
    /// <summary>Amplitude real part</summary>
    public readonly ReadOnlyBuffer<double> amplitudeReal;
    
    /// <summary>Amplitude imaginary part</summary>
    public readonly ReadOnlyBuffer<double> amplitudeImag;
    
    /// <summary>Output: Probability density |?|?</summary>
    public readonly ReadWriteBuffer<double> probability;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public ProbabilityDensityShaderDouble(
        ReadOnlyBuffer<double> amplitudeReal,
        ReadOnlyBuffer<double> amplitudeImag,
        ReadWriteBuffer<double> probability,
        int nodeCount)
    {
        this.amplitudeReal = amplitudeReal;
        this.amplitudeImag = amplitudeImag;
        this.probability = probability;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double re = amplitudeReal[i];
        double im = amplitudeImag[i];
        
        // |?|? = Re? + Im?
        probability[i] = re * re + im * im;
    }
}

/// <summary>
/// Compute information current J_ij between connected nodes.
/// 
/// J_ij = (?/m) · Im(??* ???) · w_ij
/// 
/// For graph: J_ij ? Im(??* (??·e^{i?} - ??)) · w_ij
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct InformationCurrentShaderDouble : IComputeShader
{
    /// <summary>Amplitude real part</summary>
    public readonly ReadOnlyBuffer<double> amplitudeReal;
    
    /// <summary>Amplitude imaginary part</summary>
    public readonly ReadOnlyBuffer<double> amplitudeImag;
    
    /// <summary>Edge phases</summary>
    public readonly ReadOnlyBuffer<double> edgePhases;
    
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Edge source nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Output: Information current for each edge</summary>
    public readonly ReadWriteBuffer<double> current;
    
    /// <summary>Reduced Planck constant times speed factor</summary>
    public readonly double hbarOverM;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public InformationCurrentShaderDouble(
        ReadOnlyBuffer<double> amplitudeReal,
        ReadOnlyBuffer<double> amplitudeImag,
        ReadOnlyBuffer<double> edgePhases,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        ReadWriteBuffer<double> current,
        double hbarOverM,
        int edgeCount)
    {
        this.amplitudeReal = amplitudeReal;
        this.amplitudeImag = amplitudeImag;
        this.edgePhases = edgePhases;
        this.edgeWeights = edgeWeights;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.current = current;
        this.hbarOverM = hbarOverM;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int i = edgeSrc[e];
        int j = edgeDst[e];
        
        double re_i = amplitudeReal[i];
        double im_i = amplitudeImag[i];
        double re_j = amplitudeReal[j];
        double im_j = amplitudeImag[j];
        
        // NUMERICAL GUARD: Check for NaN/Inf in inputs
        if (Hlsl.IsNaN((float)re_i) || Hlsl.IsInfinite((float)re_i) ||
            Hlsl.IsNaN((float)im_i) || Hlsl.IsInfinite((float)im_i) ||
            Hlsl.IsNaN((float)re_j) || Hlsl.IsInfinite((float)re_j) ||
            Hlsl.IsNaN((float)im_j) || Hlsl.IsInfinite((float)im_j))
        {
            current[e] = 0.0;
            return;
        }
        
        double phase = edgePhases[e];
        double w = edgeWeights[e];
        
        // NUMERICAL GUARD: Check phase and weight
        if (Hlsl.IsNaN((float)phase) || Hlsl.IsInfinite((float)phase) ||
            Hlsl.IsNaN((float)w) || Hlsl.IsInfinite((float)w))
        {
            current[e] = 0.0;
            return;
        }
        
        // Normalize phase to prevent overflow in trig functions
        const double twoPi = 6.283185307179586;
        phase = phase - twoPi * Hlsl.Floor((float)(phase / twoPi));
        
        // Taylor approximation for cos/sin (GPU double precision)
        double p2 = phase * phase;
        double cosP = 1.0 - p2 * 0.5 + p2 * p2 / 24.0;
        double sinP = phase * (1.0 - p2 / 6.0 + p2 * p2 / 120.0);
        
        // ?? ? e^{i?}
        double gauged_re = re_j * cosP - im_j * sinP;
        double gauged_im = re_j * sinP + im_j * cosP;
        
        // Gradient: (???e^{i?} - ??)
        double grad_re = gauged_re - re_i;
        double grad_im = gauged_im - im_i;
        
        // ??*: (re_i, -im_i)
        // ??* ? ?? = (re_i ? grad_re + im_i ? grad_im) + i(re_i ? grad_im - im_i ? grad_re)
        // Im(??* ? ??) = re_i ? grad_im - im_i ? grad_re
        double imPart = re_i * grad_im - im_i * grad_re;
        
        // Current: J = (?/m) ? Im(?* ??) ? w
        double result = hbarOverM * imPart * w;
        
        // NUMERICAL GUARD: Final check for NaN/Inf
        if (Hlsl.IsNaN((float)result) || Hlsl.IsInfinite((float)result))
        {
            current[e] = 0.0;
            return;
        }
        
        current[e] = result;
    }
}

/// <summary>
/// Compute divergence of information current at each node.
/// For unitary evolution: ?·J + ??/?t = 0
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CurrentDivergenceShaderDouble : IComputeShader
{
    /// <summary>Information currents on edges</summary>
    public readonly ReadOnlyBuffer<double> edgeCurrents;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Edge indices for each CSR entry</summary>
    public readonly ReadOnlyBuffer<int> csrEdgeIdx;
    
    /// <summary>Direction flags (-1 for outgoing, +1 for incoming)</summary>
    public readonly ReadOnlyBuffer<int> edgeDirection;
    
    /// <summary>Output: Divergence at each node</summary>
    public readonly ReadWriteBuffer<double> divergence;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CurrentDivergenceShaderDouble(
        ReadOnlyBuffer<double> edgeCurrents,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<int> csrEdgeIdx,
        ReadOnlyBuffer<int> edgeDirection,
        ReadWriteBuffer<double> divergence,
        int nodeCount)
    {
        this.edgeCurrents = edgeCurrents;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.csrEdgeIdx = csrEdgeIdx;
        this.edgeDirection = edgeDirection;
        this.divergence = divergence;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double div = 0.0;
        
        for (int k = start; k < end; k++)
        {
            int edgeIdx = csrEdgeIdx[k];
            int dir = edgeDirection[k];
            
            // Sum incoming minus outgoing currents
            div += dir * edgeCurrents[edgeIdx];
        }
        
        divergence[i] = div;
    }
}

/// <summary>
/// Compute unitarity violation measure.
/// Compares total probability at current step vs initial.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct UnitarityCheckShaderDouble : IComputeShader
{
    /// <summary>Current probability densities</summary>
    public readonly ReadOnlyBuffer<double> probability;
    
    /// <summary>Output: Per-node contribution to total probability</summary>
    public readonly ReadWriteBuffer<double> totalContribution;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public UnitarityCheckShaderDouble(
        ReadOnlyBuffer<double> probability,
        ReadWriteBuffer<double> totalContribution,
        int nodeCount)
    {
        this.probability = probability;
        this.totalContribution = totalContribution;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        // Just copy for reduction (could optimize with shared memory)
        totalContribution[i] = probability[i];
    }
}

/// <summary>
/// Renormalize amplitudes to restore unitarity.
/// Applied periodically to correct numerical drift.
/// 
/// ENHANCED VERSION with:
/// - NaN/Inf validation
/// - Branch pruning for dead branches
/// - Safe epsilon handling
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct RenormalizeAmplitudesShaderDouble : IComputeShader
{
    /// <summary>Amplitude real parts (input/output)</summary>
    public readonly ReadWriteBuffer<double> amplitudeReal;
    
    /// <summary>Amplitude imaginary parts (input/output)</summary>
    public readonly ReadWriteBuffer<double> amplitudeImag;
    
    /// <summary>Current total probability (precomputed)</summary>
    public readonly double totalProbability;
    
    /// <summary>Target total probability (usually 1.0)</summary>
    public readonly double targetProbability;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    /// <summary>Pruning threshold for dead branches</summary>
    public readonly double pruneEpsilon;
    
    public RenormalizeAmplitudesShaderDouble(
        ReadWriteBuffer<double> amplitudeReal,
        ReadWriteBuffer<double> amplitudeImag,
        double totalProbability,
        double targetProbability,
        int nodeCount,
        double pruneEpsilon = 1e-15)
    {
        this.amplitudeReal = amplitudeReal;
        this.amplitudeImag = amplitudeImag;
        this.totalProbability = totalProbability;
        this.targetProbability = targetProbability;
        this.nodeCount = nodeCount;
        this.pruneEpsilon = pruneEpsilon;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double re = amplitudeReal[i];
        double im = amplitudeImag[i];
        
        // NUMERICAL GUARD: Check for NaN/Inf in inputs
        if (Hlsl.IsNaN((float)re) || Hlsl.IsInfinite((float)re) ||
            Hlsl.IsNaN((float)im) || Hlsl.IsInfinite((float)im))
        {
            // Reset corrupted amplitude to zero
            amplitudeReal[i] = 0.0;
            amplitudeImag[i] = 0.0;
            return;
        }
        
        // BRANCH PRUNING: Check if amplitude is below threshold
        double localProbSq = re * re + im * im;
        if (localProbSq < pruneEpsilon * pruneEpsilon)
        {
            // Dead branch - zero it out
            amplitudeReal[i] = 0.0;
            amplitudeImag[i] = 0.0;
            return;
        }
        
        // Skip renormalization if total probability is too small
        if (totalProbability < 1e-30)
        {
            return;
        }
        
        // Scale factor to restore total probability
        // |?_new|? = |?_old|? ? (target/current)
        // ?_new = ?_old ? sqrt(target/current)
        double scale = targetProbability / totalProbability;
        
        // Use Hlsl.Sqrt for float (then back to double)
        double sqrtScale = Hlsl.Sqrt((float)scale);
        
        double newRe = re * sqrtScale;
        double newIm = im * sqrtScale;
        
        // NUMERICAL GUARD: Final check for NaN/Inf
        if (Hlsl.IsNaN((float)newRe) || Hlsl.IsInfinite((float)newRe) ||
            Hlsl.IsNaN((float)newIm) || Hlsl.IsInfinite((float)newIm))
        {
            // Preserve original if computation failed
            return;
        }
        
        amplitudeReal[i] = newRe;
        amplitudeImag[i] = newIm;
    }
}

/// <summary>
/// Compute local conservation violation.
/// |??/?t + ?·J| should be zero for unitary evolution.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ConservationViolationShaderDouble : IComputeShader
{
    /// <summary>Current probability densities</summary>
    public readonly ReadOnlyBuffer<double> probability;
    
    /// <summary>Previous probability densities</summary>
    public readonly ReadOnlyBuffer<double> prevProbability;
    
    /// <summary>Current divergence at each node</summary>
    public readonly ReadOnlyBuffer<double> divergence;
    
    /// <summary>Time step</summary>
    public readonly double dt;
    
    /// <summary>Output: Conservation violation at each node</summary>
    public readonly ReadWriteBuffer<double> violation;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public ConservationViolationShaderDouble(
        ReadOnlyBuffer<double> probability,
        ReadOnlyBuffer<double> prevProbability,
        ReadOnlyBuffer<double> divergence,
        double dt,
        ReadWriteBuffer<double> violation,
        int nodeCount)
    {
        this.probability = probability;
        this.prevProbability = prevProbability;
        this.divergence = divergence;
        this.dt = dt;
        this.violation = violation;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        // ??/?t ? (?_new - ?_old) / dt
        double drhodt = (probability[i] - prevProbability[i]) / dt;
        
        double divJ = divergence[i];
        
        // Conservation: ??/?t + ?·J = 0
        // Violation = |??/?t + ?·J|
        double sum = drhodt + divJ;
        violation[i] = sum > 0 ? sum : -sum;
    }
}

/// <summary>
/// Compute phase coherence between neighboring nodes.
/// High coherence = quantum; low coherence = classical.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PhaseCoherenceShaderDouble : IComputeShader
{
    /// <summary>Amplitude real parts</summary>
    public readonly ReadOnlyBuffer<double> amplitudeReal;
    
    /// <summary>Amplitude imaginary parts</summary>
    public readonly ReadOnlyBuffer<double> amplitudeImag;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Output: Phase coherence at each node</summary>
    public readonly ReadWriteBuffer<double> coherence;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public PhaseCoherenceShaderDouble(
        ReadOnlyBuffer<double> amplitudeReal,
        ReadOnlyBuffer<double> amplitudeImag,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> edgeWeights,
        ReadWriteBuffer<double> coherence,
        int nodeCount)
    {
        this.amplitudeReal = amplitudeReal;
        this.amplitudeImag = amplitudeImag;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.edgeWeights = edgeWeights;
        this.coherence = coherence;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double re_i = amplitudeReal[i];
        double im_i = amplitudeImag[i];
        
        // Norm of ??
        double norm_i = Hlsl.Sqrt((float)(re_i * re_i + im_i * im_i));
        if (norm_i < 1e-15)
        {
            coherence[i] = 0.0;
            return;
        }
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double coherenceSum = 0.0;
        double weightSum = 0.0;
        
        for (int k = start; k < end; k++)
        {
            int j = colIdx[k];
            double w = edgeWeights[k];
            
            if (w < 1e-15) continue; // Skip sleeping edges
            
            double re_j = amplitudeReal[j];
            double im_j = amplitudeImag[j];
            
            double norm_j = Hlsl.Sqrt((float)(re_j * re_j + im_j * im_j));
            if (norm_j < 1e-15) continue;
            
            // Phase alignment: |??* · ??| / (|??| · |??|)
            // ??* · ?? = (re_i · re_j + im_i · im_j) + i(re_i · im_j - im_i · re_j)
            double innerRe = re_i * re_j + im_i * im_j;
            double innerIm = re_i * im_j - im_i * re_j;
            double innerNorm = Hlsl.Sqrt((float)(innerRe * innerRe + innerIm * innerIm));
            
            double alignment = innerNorm / (norm_i * norm_j);
            
            coherenceSum += alignment * w;
            weightSum += w;
        }
        
        coherence[i] = weightSum > 1e-15 ? coherenceSum / weightSum : 0.0;
    }
}
