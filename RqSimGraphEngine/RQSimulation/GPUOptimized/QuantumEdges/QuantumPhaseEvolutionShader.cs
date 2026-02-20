using ComputeSharp;

namespace RQSimulation.GPUOptimized.QuantumEdges;

/// <summary>
/// RQG-HYPOTHESIS: Quantum Phase Evolution Shaders
/// 
/// MAIN DRIVER replacing StepPhysics with unitary evolution.
/// 
/// PARADIGM SHIFT:
/// - OLD: Random coin flip + MCMC acceptance
/// - NEW: Pure unitary rotation ? ? exp(-iH·dt)·?
/// 
/// PHYSICS:
/// ?(t+dt) = exp(-i·H·dt_local)·?(t)
/// 
/// where dt_local is computed from Lapse function:
/// dt_local = (N_i + N_j)/2 · d?
/// 
/// STOP-LIST COMPLIANCE:
/// ? NO id.x + seed for pseudo-random
/// ? NO global SimulationStep dt - only LapseBuffer
/// ? NO edge deletion - only weight ? 0
/// 
/// All randomness comes from INITIAL SUPERPOSITION only.
/// </summary>

/// <summary>
/// Primary quantum phase evolution kernel with Lapse modulation.
/// 
/// For each edge e = (i,j):
/// 1. Compute local time: dt_local = (N_i + N_j) / 2 · d?
/// 2. Compute phase rotation: ? = H_e · dt_local
/// 3. Apply unitary: ?_new = ? · exp(-i?) = ? · (cos(?) - i·sin(?))
/// 
/// This is STRICTLY UNITARY - preserves |?|?.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct QuantumPhaseEvolutionShaderDouble : IComputeShader
{
    /// <summary>Edge quantum amplitudes (Real, Imaginary)</summary>
    public readonly ReadWriteBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Edge Hamiltonians (local energy)</summary>
    public readonly ReadOnlyBuffer<double> edgeHamiltonian;
    
    /// <summary>Lapse function at each node</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>Edge source node indices</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination node indices</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Coordinate time step d? (affine parameter)</summary>
    public readonly double deltaLambda;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public QuantumPhaseEvolutionShaderDouble(
        ReadWriteBuffer<Double2> edgeAmplitudes,
        ReadOnlyBuffer<double> edgeHamiltonian,
        ReadOnlyBuffer<double> lapse,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        double deltaLambda,
        int edgeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.edgeHamiltonian = edgeHamiltonian;
        this.lapse = lapse;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.deltaLambda = deltaLambda;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        // Get edge endpoints
        int i = edgeSrc[e];
        int j = edgeDst[e];
        
        // Get Lapse at both endpoints
        double N_i = lapse[i];
        double N_j = lapse[j];
        
        // RQG-HYPOTHESIS: Local time for edge = average Lapse ? coordinate step
        double dt_local = (N_i + N_j) * 0.5 * deltaLambda;
        
        // Get current amplitude and energy
        Double2 psi = edgeAmplitudes[e];
        double energy = edgeHamiltonian[e];
        
        // NUMERICAL GUARD: Check for inf/nan in inputs
        // If H ? ?, the exponential can produce NaN
        if (Hlsl.IsNaN((float)energy) || Hlsl.IsInfinite((float)energy) ||
            Hlsl.IsNaN((float)psi.X) || Hlsl.IsInfinite((float)psi.X) ||
            Hlsl.IsNaN((float)psi.Y) || Hlsl.IsInfinite((float)psi.Y))
        {
            // Reset to zero amplitude if corrupted
            edgeAmplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        // Phase rotation angle
        double theta = energy * dt_local;
        
        // NUMERICAL GUARD: Clamp theta to prevent overflow in trig functions
        // Large phase rotations are effectively random, so clamp to ±2? range
        const double twoPi = 6.283185307179586;
        theta = theta - twoPi * Hlsl.Floor((float)(theta / twoPi));
        
        // Unitary evolution: exp(-i?) = cos(?) - i·sin(?)
        double cosTheta = Hlsl.Cos((float)theta);
        double sinTheta = Hlsl.Sin((float)theta);
        
        // Complex multiplication: (a + bi)·(cos - i·sin) = 
        // (a·cos + b·sin) + i·(b·cos - a·sin)
        double newReal = psi.X * cosTheta + psi.Y * sinTheta;
        double newImag = psi.Y * cosTheta - psi.X * sinTheta;
        
        // NUMERICAL GUARD: Final check for NaN/Inf in output
        if (Hlsl.IsNaN((float)newReal) || Hlsl.IsInfinite((float)newReal) ||
            Hlsl.IsNaN((float)newImag) || Hlsl.IsInfinite((float)newImag))
        {
            // Preserve original amplitude if computation failed
            return;
        }
        
        edgeAmplitudes[e] = new Double2(newReal, newImag);
    }
}

/// <summary>
/// Quantum phase evolution using CSR topology.
/// Infers edge endpoints from CSR structure.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct QuantumPhaseEvolutionCsrShaderDouble : IComputeShader
{
    /// <summary>Edge quantum amplitudes</summary>
    public readonly ReadWriteBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Edge Hamiltonians</summary>
    public readonly ReadOnlyBuffer<double> edgeHamiltonian;
    
    /// <summary>Lapse function at each node</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Coordinate time step</summary>
    public readonly double deltaLambda;
    
    /// <summary>Number of edges (undirected: NNZ/2)</summary>
    public readonly int edgeCount;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public QuantumPhaseEvolutionCsrShaderDouble(
        ReadWriteBuffer<Double2> edgeAmplitudes,
        ReadOnlyBuffer<double> edgeHamiltonian,
        ReadOnlyBuffer<double> lapse,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        double deltaLambda,
        int edgeCount,
        int nodeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.edgeHamiltonian = edgeHamiltonian;
        this.lapse = lapse;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.deltaLambda = deltaLambda;
        this.edgeCount = edgeCount;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        // Find edge endpoints from CSR
        // Map edge index to (i, j) pair
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
                if (i < j) // Upper triangle only for undirected
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
        
        // Compute local time
        double N_A = lapse[nodeA];
        double N_B = lapse[nodeB];
        double dt_local = (N_A + N_B) * 0.5 * deltaLambda;
        
        // Get amplitude and energy
        Double2 psi = edgeAmplitudes[e];
        double energy = edgeHamiltonian[e];
        
        // NUMERICAL GUARD: Check for inf/nan in inputs
        if (Hlsl.IsNaN((float)energy) || Hlsl.IsInfinite((float)energy) ||
            Hlsl.IsNaN((float)psi.X) || Hlsl.IsInfinite((float)psi.X) ||
            Hlsl.IsNaN((float)psi.Y) || Hlsl.IsInfinite((float)psi.Y))
        {
            edgeAmplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        // Unitary rotation
        double theta = energy * dt_local;
        
        // NUMERICAL GUARD: Clamp theta to prevent overflow
        const double twoPi = 6.283185307179586;
        theta = theta - twoPi * Hlsl.Floor((float)(theta / twoPi));
        
        double cosTheta = Hlsl.Cos((float)theta);
        double sinTheta = Hlsl.Sin((float)theta);
        
        double newReal = psi.X * cosTheta + psi.Y * sinTheta;
        double newImag = psi.Y * cosTheta - psi.X * sinTheta;
        
        // NUMERICAL GUARD: Final check for NaN/Inf
        if (Hlsl.IsNaN((float)newReal) || Hlsl.IsInfinite((float)newReal) ||
            Hlsl.IsNaN((float)newImag) || Hlsl.IsInfinite((float)newImag))
        {
            return;
        }
        
        edgeAmplitudes[e] = new Double2(newReal, newImag);
    }
}

/// <summary>
/// Compute edge Hamiltonian from node properties.
/// H_edge = (E_i + E_j)/2 + V_ij
/// 
/// where E_i is node energy and V_ij is interaction potential.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeEdgeHamiltonianShaderDouble : IComputeShader
{
    /// <summary>Node energies</summary>
    public readonly ReadOnlyBuffer<double> nodeEnergies;
    
    /// <summary>Edge interaction potentials</summary>
    public readonly ReadOnlyBuffer<double> edgePotentials;
    
    /// <summary>Edge source nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Output: Edge Hamiltonians</summary>
    public readonly ReadWriteBuffer<double> edgeHamiltonian;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public ComputeEdgeHamiltonianShaderDouble(
        ReadOnlyBuffer<double> nodeEnergies,
        ReadOnlyBuffer<double> edgePotentials,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        ReadWriteBuffer<double> edgeHamiltonian,
        int edgeCount)
    {
        this.nodeEnergies = nodeEnergies;
        this.edgePotentials = edgePotentials;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.edgeHamiltonian = edgeHamiltonian;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int i = edgeSrc[e];
        int j = edgeDst[e];
        
        double E_i = nodeEnergies[i];
        double E_j = nodeEnergies[j];
        double V_ij = edgePotentials[e];
        
        // Edge Hamiltonian: average node energy + interaction
        edgeHamiltonian[e] = (E_i + E_j) * 0.5 + V_ij;
    }
}

/// <summary>
/// Normalize edge amplitudes to preserve total probability.
/// Ensures ?|?_e|? = 1 after numerical drift.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct NormalizeAmplitudesShaderDouble : IComputeShader
{
    /// <summary>Edge amplitudes to normalize</summary>
    public readonly ReadWriteBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Total probability (precomputed: ?|?|?)</summary>
    public readonly double totalProbability;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public NormalizeAmplitudesShaderDouble(
        ReadWriteBuffer<Double2> edgeAmplitudes,
        double totalProbability,
        int edgeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.totalProbability = totalProbability;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        if (totalProbability < 1e-15) return;
        
        double normFactor = 1.0 / Hlsl.Sqrt((float)totalProbability);
        
        Double2 psi = edgeAmplitudes[e];
        edgeAmplitudes[e] = new Double2(psi.X * normFactor, psi.Y * normFactor);
    }
}

/// <summary>
/// Compute existence probabilities from amplitudes.
/// P_e = |?_e|? = Re? + Im?
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeProbabilitiesShaderDouble : IComputeShader
{
    /// <summary>Edge amplitudes</summary>
    public readonly ReadOnlyBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Output: Edge probabilities</summary>
    public readonly ReadWriteBuffer<double> edgeProbabilities;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public ComputeProbabilitiesShaderDouble(
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

/// <summary>
/// Apply gauge phase to edge amplitudes.
/// ?_ij ? exp(i?_i) · ?_ij · exp(-i?_j)
/// 
/// For gauge-invariant evolution.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ApplyGaugePhaseShaderDouble : IComputeShader
{
    /// <summary>Edge amplitudes</summary>
    public readonly ReadWriteBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Gauge phases at each node</summary>
    public readonly ReadOnlyBuffer<double> gaugePhases;
    
    /// <summary>Edge source nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public ApplyGaugePhaseShaderDouble(
        ReadWriteBuffer<Double2> edgeAmplitudes,
        ReadOnlyBuffer<double> gaugePhases,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        int edgeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.gaugePhases = gaugePhases;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int i = edgeSrc[e];
        int j = edgeDst[e];
        
        double theta_i = gaugePhases[i];
        double theta_j = gaugePhases[j];
        
        // Net gauge phase: ?_i - ?_j
        double netPhase = theta_i - theta_j;
        
        double cosP = Hlsl.Cos((float)netPhase);
        double sinP = Hlsl.Sin((float)netPhase);
        
        Double2 psi = edgeAmplitudes[e];
        
        // Complex multiplication: ? · exp(i·netPhase)
        double newReal = psi.X * cosP - psi.Y * sinP;
        double newImag = psi.X * sinP + psi.Y * cosP;
        
        edgeAmplitudes[e] = new Double2(newReal, newImag);
    }
}

/// <summary>
/// Initialize edge amplitudes to uniform superposition.
/// ?_e = 1/?N for all edges (equal probability).
/// 
/// This sets the INITIAL RANDOMNESS for RQG evolution.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct InitializeUniformSuperpositionShaderDouble : IComputeShader
{
    /// <summary>Output: Edge amplitudes</summary>
    public readonly ReadWriteBuffer<Double2> edgeAmplitudes;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public InitializeUniformSuperpositionShaderDouble(
        ReadWriteBuffer<Double2> edgeAmplitudes,
        int edgeCount)
    {
        this.edgeAmplitudes = edgeAmplitudes;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        // Uniform superposition: 1/?N amplitude
        double norm = 1.0 / Hlsl.Sqrt((float)edgeCount);
        
        // Real positive amplitude (pure state)
        edgeAmplitudes[e] = new Double2(norm, 0.0);
    }
}
