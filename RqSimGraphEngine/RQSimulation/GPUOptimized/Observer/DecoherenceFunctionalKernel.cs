using ComputeSharp;

namespace RQSimulation.GPUOptimized.Observer;

/// <summary>
/// RQG-HYPOTHESIS: Decoherence Functional Kernels
/// 
/// Computes the decoherence functional D(?, ?) which determines
/// when quantum superpositions become classical (decoherent).
/// 
/// PHYSICS:
/// D(?, ?) = ??|P_? P_?|??
/// 
/// where P_?, P_? are projectors onto history branches.
/// 
/// KEY PROPERTIES:
/// - D(?,?) = probability of history ?
/// - |D(?,?)| ? 0 means branches ?,? are decoherent (classical)
/// - |D(?,?)| ~ 1 means quantum interference persists
/// 
/// RQG-HYPOTHESIS: This REPLACES Random in observer.
/// Instead of coin flip, we check if D(?,?) ? 0 (decoherence condition).
/// Classical outcomes emerge when system decoheres with environment.
/// </summary>

/// <summary>
/// Compute decoherence functional for a pair of history branches.
/// D(?,?) = Tr[P_? ? P_?†]
/// 
/// In pure state: D(?,?) = ??|P_? P_?|??
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DecoherenceFunctionalShaderDouble : IComputeShader
{
    /// <summary>Branch ? amplitudes</summary>
    public readonly ReadOnlyBuffer<Double2> branchAlphaAmplitudes;
    
    /// <summary>Branch ? amplitudes</summary>
    public readonly ReadOnlyBuffer<Double2> branchBetaAmplitudes;
    
    /// <summary>Output: D(?,?) for each edge pair (partial sum)</summary>
    public readonly ReadWriteBuffer<Double2> decoherenceContribs;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public DecoherenceFunctionalShaderDouble(
        ReadOnlyBuffer<Double2> branchAlphaAmplitudes,
        ReadOnlyBuffer<Double2> branchBetaAmplitudes,
        ReadWriteBuffer<Double2> decoherenceContribs,
        int edgeCount)
    {
        this.branchAlphaAmplitudes = branchAlphaAmplitudes;
        this.branchBetaAmplitudes = branchBetaAmplitudes;
        this.decoherenceContribs = decoherenceContribs;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 psiAlpha = branchAlphaAmplitudes[e];
        Double2 psiBeta = branchBetaAmplitudes[e];
        
        // D(?,?) contribution = ?_?* · ?_? (complex inner product)
        // (a + bi)* · (c + di) = (a - bi)·(c + di) = (ac + bd) + i(ad - bc)
        double realPart = psiAlpha.X * psiBeta.X + psiAlpha.Y * psiBeta.Y;
        double imagPart = psiAlpha.X * psiBeta.Y - psiAlpha.Y * psiBeta.X;
        
        decoherenceContribs[e] = new Double2(realPart, imagPart);
    }
}

/// <summary>
/// Check decoherence condition: |D(?,?)| < ?
/// 
/// When this is satisfied, branches ? and ? are effectively classical
/// and can be treated as mutually exclusive outcomes.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DecoherenceCheckShaderDouble : IComputeShader
{
    /// <summary>Decoherence functional values (complex)</summary>
    public readonly ReadOnlyBuffer<Double2> decoherenceFunctional;
    
    /// <summary>Output: 1 if decoherent, 0 if coherent</summary>
    public readonly ReadWriteBuffer<int> isDecoherent;
    
    /// <summary>Decoherence threshold ?</summary>
    public readonly double threshold;
    
    /// <summary>Number of branch pairs</summary>
    public readonly int pairCount;
    
    public DecoherenceCheckShaderDouble(
        ReadOnlyBuffer<Double2> decoherenceFunctional,
        ReadWriteBuffer<int> isDecoherent,
        double threshold,
        int pairCount)
    {
        this.decoherenceFunctional = decoherenceFunctional;
        this.isDecoherent = isDecoherent;
        this.threshold = threshold;
        this.pairCount = pairCount;
    }
    
    public void Execute()
    {
        int p = ThreadIds.X;
        if (p >= pairCount) return;
        
        Double2 D = decoherenceFunctional[p];
        
        // |D|? = Re? + Im?
        double magSquared = D.X * D.X + D.Y * D.Y;
        
        // Check if |D| < threshold
        isDecoherent[p] = (magSquared < threshold * threshold) ? 1 : 0;
    }
}

/// <summary>
/// Compute environment-induced decoherence rate.
/// 
/// The rate at which quantum coherence is lost depends on
/// coupling to environmental degrees of freedom.
/// 
/// ?_decoherence = (coupling)? ? (environment spectral density)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DecoherenceRateShaderDouble : IComputeShader
{
    /// <summary>System-environment coupling strength at each node</summary>
    public readonly ReadOnlyBuffer<double> couplingStrength;
    
    /// <summary>Environment temperature (thermal fluctuations)</summary>
    public readonly double temperature;
    
    /// <summary>Output: Decoherence rate ? at each node</summary>
    public readonly ReadWriteBuffer<double> decoherenceRate;
    
    /// <summary>Boltzmann constant (normalized)</summary>
    public readonly double kB;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public DecoherenceRateShaderDouble(
        ReadOnlyBuffer<double> couplingStrength,
        double temperature,
        ReadWriteBuffer<double> decoherenceRate,
        double kB,
        int nodeCount)
    {
        this.couplingStrength = couplingStrength;
        this.temperature = temperature;
        this.decoherenceRate = decoherenceRate;
        this.kB = kB;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double g = couplingStrength[i];
        
        // Decoherence rate: ? ? g? ? k_B ? T
        // High temperature or strong coupling ? fast decoherence
        double rate = g * g * kB * temperature;
        
        decoherenceRate[i] = rate;
    }
}

/// <summary>
/// Apply decoherence damping to off-diagonal density matrix elements.
/// 
/// ?_ij ? ?_ij ? exp(-? ? dt)
/// 
/// This implements the Lindblad master equation approximately.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DecoherenceDampingShaderDouble : IComputeShader
{
    /// <summary>Off-diagonal density matrix elements (coherences)</summary>
    public readonly ReadWriteBuffer<Double2> coherences;
    
    /// <summary>Decoherence rates at each position</summary>
    public readonly ReadOnlyBuffer<double> decoherenceRates;
    
    /// <summary>Time step dt</summary>
    public readonly double dt;
    
    /// <summary>Number of coherence elements</summary>
    public readonly int count;
    
    public DecoherenceDampingShaderDouble(
        ReadWriteBuffer<Double2> coherences,
        ReadOnlyBuffer<double> decoherenceRates,
        double dt,
        int count)
    {
        this.coherences = coherences;
        this.decoherenceRates = decoherenceRates;
        this.dt = dt;
        this.count = count;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;
        
        double rate = decoherenceRates[i];
        
        // Damping factor: exp(-?·dt)
        double damping = Hlsl.Exp((float)(-rate * dt));
        
        Double2 rho = coherences[i];
        
        // Apply damping to off-diagonal element
        coherences[i] = new Double2(rho.X * damping, rho.Y * damping);
    }
}

/// <summary>
/// Compute purity Tr[??] to measure decoherence progress.
/// 
/// - Pure state: Tr[??] = 1
/// - Maximally mixed: Tr[??] = 1/N
/// 
/// Decreasing purity indicates increasing decoherence.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PurityComputeShaderDouble : IComputeShader
{
    /// <summary>Edge probabilities |?_e|?</summary>
    public readonly ReadOnlyBuffer<double> probabilities;
    
    /// <summary>Output: Probability squared (for purity sum)</summary>
    public readonly ReadWriteBuffer<double> probSquared;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public PurityComputeShaderDouble(
        ReadOnlyBuffer<double> probabilities,
        ReadWriteBuffer<double> probSquared,
        int edgeCount)
    {
        this.probabilities = probabilities;
        this.probSquared = probSquared;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double p = probabilities[e];
        
        // p? contribution to purity
        probSquared[e] = p * p;
    }
}

/// <summary>
/// Compute von Neumann entropy S = -Tr[? log ?].
/// 
/// Entropy measures information loss to environment.
/// - S = 0: pure state (no decoherence)
/// - S = log N: maximally mixed (complete decoherence)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VonNeumannEntropyShaderDouble : IComputeShader
{
    /// <summary>Eigenvalues of density matrix (probabilities for diagonal ?)</summary>
    public readonly ReadOnlyBuffer<double> eigenvalues;
    
    /// <summary>Output: -p log(p) contribution</summary>
    public readonly ReadWriteBuffer<double> entropyContribs;
    
    /// <summary>Number of eigenvalues</summary>
    public readonly int count;
    
    /// <summary>Regularization for log(0)</summary>
    public readonly double epsilon;
    
    public VonNeumannEntropyShaderDouble(
        ReadOnlyBuffer<double> eigenvalues,
        ReadWriteBuffer<double> entropyContribs,
        int count,
        double epsilon)
    {
        this.eigenvalues = eigenvalues;
        this.entropyContribs = entropyContribs;
        this.count = count;
        this.epsilon = epsilon;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;
        
        double p = eigenvalues[i];
        
        // S contribution = -p log(p)
        // Use natural log (can convert to log2 later)
        double contrib = 0.0;
        if (p > epsilon)
        {
            contrib = -p * Hlsl.Log((float)p);
        }
        
        entropyContribs[i] = contrib;
    }
}

/// <summary>
/// Einselection (environment-induced superselection).
/// 
/// Identifies pointer states that survive decoherence.
/// These become the classical observables.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EinselectionShaderDouble : IComputeShader
{
    /// <summary>Edge amplitudes</summary>
    public readonly ReadOnlyBuffer<Double2> amplitudes;
    
    /// <summary>Decoherence rates (from environment coupling)</summary>
    public readonly ReadOnlyBuffer<double> decoherenceRates;
    
    /// <summary>Output: Stability measure (high = pointer state)</summary>
    public readonly ReadWriteBuffer<double> pointerStability;
    
    /// <summary>Threshold decoherence rate for pointer state</summary>
    public readonly double rateThreshold;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public EinselectionShaderDouble(
        ReadOnlyBuffer<Double2> amplitudes,
        ReadOnlyBuffer<double> decoherenceRates,
        ReadWriteBuffer<double> pointerStability,
        double rateThreshold,
        int edgeCount)
    {
        this.amplitudes = amplitudes;
        this.decoherenceRates = decoherenceRates;
        this.pointerStability = pointerStability;
        this.rateThreshold = rateThreshold;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 psi = amplitudes[e];
        double rate = decoherenceRates[e];
        
        // Probability
        double prob = psi.X * psi.X + psi.Y * psi.Y;
        
        // Stability: high probability + low decoherence rate = pointer state
        // stability = prob / (1 + rate/threshold)
        double stability = prob / (1.0 + rate / rateThreshold);
        
        pointerStability[e] = stability;
    }
}

/// <summary>
/// Branch weight computation for consistent histories.
/// 
/// W(?) = D(?,?) = |??|P_?|??|?
/// 
/// These weights satisfy probability rules only when
/// decoherence condition D(?,?) ? 0 for ? ? ? is met.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct BranchWeightShaderDouble : IComputeShader
{
    /// <summary>Branch amplitudes ??|P_?|??</summary>
    public readonly ReadOnlyBuffer<Double2> branchAmplitudes;
    
    /// <summary>Output: Branch weights (probabilities)</summary>
    public readonly ReadWriteBuffer<double> branchWeights;
    
    /// <summary>Number of branches</summary>
    public readonly int branchCount;
    
    public BranchWeightShaderDouble(
        ReadOnlyBuffer<Double2> branchAmplitudes,
        ReadWriteBuffer<double> branchWeights,
        int branchCount)
    {
        this.branchAmplitudes = branchAmplitudes;
        this.branchWeights = branchWeights;
        this.branchCount = branchCount;
    }
    
    public void Execute()
    {
        int b = ThreadIds.X;
        if (b >= branchCount) return;
        
        Double2 amp = branchAmplitudes[b];
        
        // W = |amplitude|? = Re? + Im?
        branchWeights[b] = amp.X * amp.X + amp.Y * amp.Y;
    }
}

/// <summary>
/// Branch pruning kernel: zeros out amplitudes below threshold.
/// 
/// MEMORY OPTIMIZATION:
/// "Dead branches" with Amplitude &lt; Epsilon accumulate over time
/// and waste GPU memory. This kernel implements branch pruning:
/// - If |?|? &lt; ??, set ? = 0
/// - This allows GPU memory to be reclaimed for sparse representations
/// - Also prevents numerical underflow from contaminating results
/// 
/// Call periodically (every N decoherence steps) to clean up dead branches.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct BranchPruningShaderDouble : IComputeShader
{
    /// <summary>Branch amplitudes to prune</summary>
    public readonly ReadWriteBuffer<Double2> amplitudes;
    
    /// <summary>Edge/branch weights (set to 0 when pruned)</summary>
    public readonly ReadWriteBuffer<double> weights;
    
    /// <summary>Output: 1 if branch survives, 0 if pruned</summary>
    public readonly ReadWriteBuffer<int> survivedFlags;
    
    /// <summary>Pruning threshold ? (branches with |?| &lt; ? are zeroed)</summary>
    public readonly double epsilon;
    
    /// <summary>Number of branches/edges</summary>
    public readonly int count;
    
    public BranchPruningShaderDouble(
        ReadWriteBuffer<Double2> amplitudes,
        ReadWriteBuffer<double> weights,
        ReadWriteBuffer<int> survivedFlags,
        double epsilon,
        int count)
    {
        this.amplitudes = amplitudes;
        this.weights = weights;
        this.survivedFlags = survivedFlags;
        this.epsilon = epsilon;
        this.count = count;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;
        
        Double2 amp = amplitudes[i];
        
        // NUMERICAL GUARD: Check for NaN/Inf first
        if (Hlsl.IsNaN((float)amp.X) || Hlsl.IsInfinite((float)amp.X) ||
            Hlsl.IsNaN((float)amp.Y) || Hlsl.IsInfinite((float)amp.Y))
        {
            // Corrupted amplitude - prune immediately
            amplitudes[i] = new Double2(0.0, 0.0);
            weights[i] = 0.0;
            survivedFlags[i] = 0;
            return;
        }
        
        // Compute probability |?|?
        double probSq = amp.X * amp.X + amp.Y * amp.Y;
        double epsSq = epsilon * epsilon;
        
        if (probSq < epsSq)
        {
            // BRANCH PRUNING: This branch is "dead" - zero it out
            amplitudes[i] = new Double2(0.0, 0.0);
            weights[i] = 0.0;
            survivedFlags[i] = 0;
        }
        else
        {
            // Branch survives
            survivedFlags[i] = 1;
        }
    }
}

/// <summary>
/// Decoherence-aware weight update kernel.
/// 
/// Applies decoherence damping and prunes branches in one pass:
/// - Damps off-diagonal elements: ?_ij ? ?_ij ? exp(-??dt)
/// - Prunes branches where resulting amplitude &lt; ?
/// 
/// This prevents "dead worlds" from accumulating in memory.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DecoherenceDampingWithPruningShaderDouble : IComputeShader
{
    /// <summary>Coherences (off-diagonal density matrix elements)</summary>
    public readonly ReadWriteBuffer<Double2> coherences;
    
    /// <summary>Decoherence rates at each position</summary>
    public readonly ReadOnlyBuffer<double> decoherenceRates;
    
    /// <summary>Edge weights (set to 0 when pruned)</summary>
    public readonly ReadWriteBuffer<double> weights;
    
    /// <summary>Time step dt</summary>
    public readonly double dt;
    
    /// <summary>Pruning threshold ?</summary>
    public readonly double epsilon;
    
    /// <summary>Number of elements</summary>
    public readonly int count;
    
    public DecoherenceDampingWithPruningShaderDouble(
        ReadWriteBuffer<Double2> coherences,
        ReadOnlyBuffer<double> decoherenceRates,
        ReadWriteBuffer<double> weights,
        double dt,
        double epsilon,
        int count)
    {
        this.coherences = coherences;
        this.decoherenceRates = decoherenceRates;
        this.weights = weights;
        this.dt = dt;
        this.epsilon = epsilon;
        this.count = count;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= count) return;
        
        double rate = decoherenceRates[i];
        Double2 rho = coherences[i];
        
        // NUMERICAL GUARD: Check inputs
        if (Hlsl.IsNaN((float)rate) || Hlsl.IsInfinite((float)rate) ||
            Hlsl.IsNaN((float)rho.X) || Hlsl.IsInfinite((float)rho.X) ||
            Hlsl.IsNaN((float)rho.Y) || Hlsl.IsInfinite((float)rho.Y))
        {
            coherences[i] = new Double2(0.0, 0.0);
            weights[i] = 0.0;
            return;
        }
        
        // Damping factor: exp(-??dt)
        // Clamp rate?dt to prevent underflow in exp()
        double exponent = -rate * dt;
        if (exponent < -50.0) exponent = -50.0; // exp(-50) ? 2e-22
        
        double damping = Hlsl.Exp((float)exponent);
        
        // Apply damping
        double newReal = rho.X * damping;
        double newImag = rho.Y * damping;
        
        // BRANCH PRUNING: Check if result is below threshold
        double probSq = newReal * newReal + newImag * newImag;
        double epsSq = epsilon * epsilon;
        
        if (probSq < epsSq)
        {
            // Branch is effectively dead - prune it
            coherences[i] = new Double2(0.0, 0.0);
            weights[i] = 0.0;
        }
        else
        {
            coherences[i] = new Double2(newReal, newImag);
        }
    }
}
