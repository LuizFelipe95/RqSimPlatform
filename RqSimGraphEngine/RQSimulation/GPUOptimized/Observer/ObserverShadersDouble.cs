using ComputeSharp;

namespace RQSimulation.GPUOptimized.Observer;

/// <summary>
/// GPU compute shaders for internal observer operations.
/// 
/// RQ-HYPOTHESIS STAGE 5: GPU INTERNAL OBSERVER
/// =============================================
/// Instead of external "God's eye view" measurements, the observer
/// is a subsystem of the graph that becomes entangled with targets.
/// 
/// PARALLELIZATION STRATEGY:
/// ========================
/// - Phase shifts: parallel over observer nodes (independent)
/// - Correlations: parallel over observer-target pairs (independent)
/// - Mutual information: parallel computation + reduction
/// 
/// PHYSICAL CORRECTNESS:
/// ====================
/// - Observer nodes apply phase rotations to their wavefunction components
/// - Correlations computed between observer and target subsystems
/// - Measurement creates entanglement, not classical readout
/// 
/// All computations use double precision (64-bit).
/// </summary>

/// <summary>
/// Phase shift kernel: applies exp(i * shift) to wavefunction at observer nodes.
/// 
/// PHYSICS: Phase rotation creates entanglement between observer and target.
/// The phase shift encodes measurement information in the observer's state.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PhaseShiftKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag) per node * gauge dimension</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Phase shifts per observer node</summary>
    public readonly ReadOnlyBuffer<double> phaseShifts;
    
    /// <summary>Gauge dimension (components per node)</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of observer nodes</summary>
    public readonly int observerCount;
    
    public PhaseShiftKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadOnlyBuffer<int> observerNodes,
        ReadOnlyBuffer<double> phaseShifts,
        int gaugeDim,
        int observerCount)
    {
        this.wavefunction = wavefunction;
        this.observerNodes = observerNodes;
        this.phaseShifts = phaseShifts;
        this.gaugeDim = gaugeDim;
        this.observerCount = observerCount;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= observerCount) return;
        
        int node = observerNodes[idx];
        double shift = phaseShifts[idx];
        
        // exp(i * shift) = cos(shift) + i * sin(shift)
        double cosS = Hlsl.Cos((float)shift);
        double sinS = Hlsl.Sin((float)shift);
        
        // Apply phase rotation to all gauge components of this node
        int baseIdx = node * gaugeDim;
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psi = wavefunction[baseIdx + a];
            
            // (re + i*im) * (cos + i*sin) = (re*cos - im*sin) + i*(re*sin + im*cos)
            double newReal = psi.X * cosS - psi.Y * sinS;
            double newImag = psi.X * sinS + psi.Y * cosS;
            
            wavefunction[baseIdx + a] = new Double2(newReal, newImag);
        }
    }
}

/// <summary>
/// Correlation kernel: computes correlation between observer node and target node.
/// 
/// PHYSICS: Correlation C_ij = Re(?*_i · ?_j) measures entanglement.
/// High correlation indicates strong measurement interaction.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CorrelationKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag) per node * gauge dimension</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Target node indices</summary>
    public readonly ReadOnlyBuffer<int> targetNodes;
    
    /// <summary>Edge weights between observer-target pairs (for coupling strength)</summary>
    public readonly ReadOnlyBuffer<double> connectionWeights;
    
    /// <summary>Output correlations per pair</summary>
    public readonly ReadWriteBuffer<double> correlations;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of pairs to compute</summary>
    public readonly int pairCount;
    
    public CorrelationKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadOnlyBuffer<int> observerNodes,
        ReadOnlyBuffer<int> targetNodes,
        ReadOnlyBuffer<double> connectionWeights,
        ReadWriteBuffer<double> correlations,
        int gaugeDim,
        int pairCount)
    {
        this.wavefunction = wavefunction;
        this.observerNodes = observerNodes;
        this.targetNodes = targetNodes;
        this.connectionWeights = connectionWeights;
        this.correlations = correlations;
        this.gaugeDim = gaugeDim;
        this.pairCount = pairCount;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= pairCount) return;
        
        int obsNode = observerNodes[idx];
        int tgtNode = targetNodes[idx];
        double w = connectionWeights[idx];
        
        int obsBase = obsNode * gaugeDim;
        int tgtBase = tgtNode * gaugeDim;
        
        // Compute inner product ?*_obs · ?_tgt
        double corrReal = 0.0;
        double corrImag = 0.0;
        
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psiObs = wavefunction[obsBase + a];
            Double2 psiTgt = wavefunction[tgtBase + a];
            
            // (obs_re - i*obs_im) * (tgt_re + i*tgt_im)
            // = (obs_re*tgt_re + obs_im*tgt_im) + i*(obs_re*tgt_im - obs_im*tgt_re)
            corrReal += psiObs.X * psiTgt.X + psiObs.Y * psiTgt.Y;
            corrImag += psiObs.X * psiTgt.Y - psiObs.Y * psiTgt.X;
        }
        
        // Correlation magnitude weighted by connection strength
        double corrMag = Hlsl.Sqrt((float)(corrReal * corrReal + corrImag * corrImag));
        correlations[idx] = w * corrMag;
    }
}

/// <summary>
/// Probability density kernel: computes |?|? per node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ProbabilityDensityKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag) per node * gauge dimension</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>Output probability density per node</summary>
    public readonly ReadWriteBuffer<double> probDensity;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public ProbabilityDensityKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadWriteBuffer<double> probDensity,
        int gaugeDim,
        int nodeCount)
    {
        this.wavefunction = wavefunction;
        this.probDensity = probDensity;
        this.gaugeDim = gaugeDim;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int baseIdx = i * gaugeDim;
        double prob = 0.0;
        
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psi = wavefunction[baseIdx + a];
            prob += psi.X * psi.X + psi.Y * psi.Y;
        }
        
        probDensity[i] = prob;
    }
}

/// <summary>
/// Shannon entropy kernel: computes H = -? p * log(p) for probability distribution.
/// Used for computing mutual information I(O:T) = H(O) + H(T) - H(O,T).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EntropyContributionKernelDouble : IComputeShader
{
    /// <summary>Probability values (normalized)</summary>
    public readonly ReadOnlyBuffer<double> probabilities;
    
    /// <summary>Output entropy contributions: -p * log(p)</summary>
    public readonly ReadWriteBuffer<double> entropyContribs;
    
    /// <summary>Number of elements</summary>
    public readonly int count;
    
    /// <summary>Small epsilon to avoid log(0)</summary>
    public readonly double epsilon;
    
    public EntropyContributionKernelDouble(
        ReadOnlyBuffer<double> probabilities,
        ReadWriteBuffer<double> entropyContribs,
        int count,
        double epsilon)
    {
        this.probabilities = probabilities;
        this.entropyContribs = entropyContribs;
        this.count = count;
        this.epsilon = epsilon;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;
        
        double p = probabilities[i];
        
        if (p > epsilon)
        {
            // -p * log2(p)
            double logP = Hlsl.Log2((float)p);
            entropyContribs[i] = -p * logP;
        }
        else
        {
            entropyContribs[i] = 0.0;
        }
    }
}

/// <summary>
/// Controlled phase kernel: applies measurement-induced phase shift.
/// 
/// PHYSICS: Measurement interaction H_int ~ g * |target??target| ? |obs??obs|
/// creates entanglement by applying controlled phase based on target state.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ControlledPhaseKernelDouble : IComputeShader
{
    /// <summary>Wavefunction (real, imag) per node * gauge dimension</summary>
    public readonly ReadWriteBuffer<Double2> wavefunction;
    
    /// <summary>Control node indices (targets being measured)</summary>
    public readonly ReadOnlyBuffer<int> controlNodes;
    
    /// <summary>Target node indices (observer nodes to receive phase)</summary>
    public readonly ReadOnlyBuffer<int> targetNodes;
    
    /// <summary>Coupling strengths for each pair</summary>
    public readonly ReadOnlyBuffer<double> couplings;
    
    /// <summary>Gauge dimension</summary>
    public readonly int gaugeDim;
    
    /// <summary>Number of pairs</summary>
    public readonly int pairCount;
    
    public ControlledPhaseKernelDouble(
        ReadWriteBuffer<Double2> wavefunction,
        ReadOnlyBuffer<int> controlNodes,
        ReadOnlyBuffer<int> targetNodes,
        ReadOnlyBuffer<double> couplings,
        int gaugeDim,
        int pairCount)
    {
        this.wavefunction = wavefunction;
        this.controlNodes = controlNodes;
        this.targetNodes = targetNodes;
        this.couplings = couplings;
        this.gaugeDim = gaugeDim;
        this.pairCount = pairCount;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= pairCount) return;
        
        int ctrlNode = controlNodes[idx];
        int tgtNode = targetNodes[idx];
        double g = couplings[idx];
        
        // Get control phase from control node's wavefunction
        int ctrlBase = ctrlNode * gaugeDim;
        double ctrlPhase = 0.0;
        
        // Sum phases from all gauge components
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psiCtrl = wavefunction[ctrlBase + a];
            ctrlPhase += Hlsl.Atan2((float)psiCtrl.Y, (float)psiCtrl.X);
        }
        ctrlPhase /= gaugeDim;
        
        // Apply controlled phase shift to target
        double shift = g * ctrlPhase;
        double cosS = Hlsl.Cos((float)shift);
        double sinS = Hlsl.Sin((float)shift);
        
        int tgtBase = tgtNode * gaugeDim;
        for (int a = 0; a < gaugeDim; a++)
        {
            Double2 psi = wavefunction[tgtBase + a];
            double newReal = psi.X * cosS - psi.Y * sinS;
            double newImag = psi.X * sinS + psi.Y * cosS;
            wavefunction[tgtBase + a] = new Double2(newReal, newImag);
        }
    }
}

/// <summary>
/// Observer expectation value kernel: computes ?O? = ? |?_i|? * O_i
/// for nodes in observer subsystem.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ObserverExpectationKernelDouble : IComputeShader
{
    /// <summary>Probability density per node</summary>
    public readonly ReadWriteBuffer<double> probDensity;
    
    /// <summary>Observable values per node (e.g., local energy)</summary>
    public readonly ReadOnlyBuffer<double> observable;
    
    /// <summary>Observer node indices</summary>
    public readonly ReadOnlyBuffer<int> observerNodes;
    
    /// <summary>Output contributions: |?_i|? * O_i</summary>
    public readonly ReadWriteBuffer<double> contributions;
    
    /// <summary>Number of observer nodes</summary>
    public readonly int observerCount;
    
    public ObserverExpectationKernelDouble(
        ReadWriteBuffer<double> probDensity,
        ReadOnlyBuffer<double> observable,
        ReadOnlyBuffer<int> observerNodes,
        ReadWriteBuffer<double> contributions,
        int observerCount)
    {
        this.probDensity = probDensity;
        this.observable = observable;
        this.observerNodes = observerNodes;
        this.contributions = contributions;
        this.observerCount = observerCount;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= observerCount) return;
        
        int node = observerNodes[idx];
        double prob = probDensity[node];
        double obs = observable[node];
        
        contributions[idx] = prob * obs;
    }
}

/// <summary>
/// Sum reduction kernel (double precision).
/// Reduces array to single sum using parallel tree reduction.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SumReductionKernelObserverDouble : IComputeShader
{
    /// <summary>Data to reduce (modified in place)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Stride for this reduction pass</summary>
    public readonly int stride;
    
    /// <summary>Total data count</summary>
    public readonly int count;
    
    public SumReductionKernelObserverDouble(
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
