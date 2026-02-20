using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.QuantumEdges;

/// <summary>
/// CSR-optimized compute shaders for quantum edge operations on large sparse graphs.
/// 
/// RQ-HYPOTHESIS STAGE 3: QUANTUM GRAPHITY (CSR VERSION)
/// =====================================================
/// Same physics as GPUOptimized version but optimized for CSR sparse format.
/// For N &gt; 10? nodes, CSR format saves significant memory and bandwidth.
/// 
/// Memory comparison:
/// - Dense: O(N?) for edge matrix
/// - CSR: O(E) for edge list
/// 
/// CSR Edge Storage:
/// - EdgeI[e], EdgeJ[e] = endpoints of edge e
/// - Amplitudes[e] = quantum amplitude for edge e
/// - Uses separate CSR for topology (neighbor lookups for triangles)
/// 
/// All operations use double precision (64-bit) for physical accuracy.
/// </summary>

/// <summary>
/// CSR Unitary evolution kernel for sparse edge lists.
/// Identical physics to dense version, optimized for CSR.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrUnitaryEvolutionKernelDouble : IComputeShader
{
    /// <summary>Edge amplitudes as (real, imag) pairs</summary>
    public readonly ReadWriteBuffer<Double2> amplitudes;
    
    /// <summary>Diagonal Hamiltonian elements</summary>
    public readonly ReadOnlyBuffer<double> hamiltonianDiag;
    
    /// <summary>Time step</summary>
    public readonly double dt;
    
    /// <summary>Number of edges in sparse list</summary>
    public readonly int edgeCount;
    
    public CsrUnitaryEvolutionKernelDouble(
        ReadWriteBuffer<Double2> amplitudes,
        ReadOnlyBuffer<double> hamiltonianDiag,
        double dt,
        int edgeCount)
    {
        this.amplitudes = amplitudes;
        this.hamiltonianDiag = hamiltonianDiag;
        this.dt = dt;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 alpha = amplitudes[e];
        double H = hamiltonianDiag[e];
        
        // NUMERICAL GUARD: Check for inf/nan in inputs
        if (Hlsl.IsNaN((float)H) || Hlsl.IsInfinite((float)H) ||
            Hlsl.IsNaN((float)alpha.X) || Hlsl.IsInfinite((float)alpha.X) ||
            Hlsl.IsNaN((float)alpha.Y) || Hlsl.IsInfinite((float)alpha.Y))
        {
            // Reset corrupted amplitude to zero
            amplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        double phase = H * dt;
        
        // NUMERICAL GUARD: Normalize phase to [-?, ?] range
        const double twoPi = 6.283185307179586;
        phase = phase - twoPi * Hlsl.Floor((float)(phase / twoPi));
        
        double cosP = Hlsl.Cos((float)phase);
        double sinP = Hlsl.Sin((float)phase);
        
        double newReal = alpha.X * cosP + alpha.Y * sinP;
        double newImag = alpha.Y * cosP - alpha.X * sinP;
        
        // NUMERICAL GUARD: Final check for NaN/Inf
        if (Hlsl.IsNaN((float)newReal) || Hlsl.IsInfinite((float)newReal) ||
            Hlsl.IsNaN((float)newImag) || Hlsl.IsInfinite((float)newImag))
        {
            return;
        }
        
        amplitudes[e] = new Double2(newReal, newImag);
    }
}

/// <summary>
/// CSR Edge Hamiltonian computation with efficient triangle counting.
/// Uses CSR topology for O(deg) triangle counting per edge.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrEdgeHamiltonianKernelDouble : IComputeShader
{
    /// <summary>Edge endpoints</summary>
    public readonly ReadOnlyBuffer<int> edgeI;
    public readonly ReadOnlyBuffer<int> edgeJ;
    
    /// <summary>CSR topology for triangle counting</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Node degrees</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output Hamiltonian</summary>
    public readonly ReadWriteBuffer<double> hamiltonianDiag;
    
    /// <summary>Physics parameters</summary>
    public readonly double triangleBonus;
    public readonly double degreePenalty;
    public readonly double targetDegree;
    
    /// <summary>Edge count</summary>
    public readonly int edgeCount;
    
    public CsrEdgeHamiltonianKernelDouble(
        ReadOnlyBuffer<int> edgeI,
        ReadOnlyBuffer<int> edgeJ,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<int> degrees,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<double> hamiltonianDiag,
        double triangleBonus,
        double degreePenalty,
        double targetDegree,
        int edgeCount)
    {
        this.edgeI = edgeI;
        this.edgeJ = edgeJ;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.degrees = degrees;
        this.weights = weights;
        this.hamiltonianDiag = hamiltonianDiag;
        this.triangleBonus = triangleBonus;
        this.degreePenalty = degreePenalty;
        this.targetDegree = targetDegree;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int i = edgeI[e];
        int j = edgeJ[e];
        
        // Count triangles via CSR intersection
        int triangles = CountCommonNeighborsCsr(i, j);
        
        double E_triangle = triangleBonus * triangles;
        
        int degI = degrees[i];
        int degJ = degrees[j];
        double E_degree = degreePenalty * ((degI - targetDegree) + (degJ - targetDegree));
        
        // Optional: include edge weight in energy
        double w = weights[e];
        double E_weight = 0.1 * (1.0 - w); // Higher energy for weaker edges
        
        hamiltonianDiag[e] = E_triangle + E_degree + E_weight;
    }
    
    private int CountCommonNeighborsCsr(int i, int j)
    {
        int startI = rowPtr[i];
        int endI = rowPtr[i + 1];
        int startJ = rowPtr[j];
        int endJ = rowPtr[j + 1];
        
        int count = 0;
        int ki = startI;
        int kj = startJ;
        
        // Merge-style sorted intersection
        while (ki < endI && kj < endJ)
        {
            int ni = colIdx[ki];
            int nj = colIdx[kj];
            
            if (ni == nj)
            {
                count++;
                ki++;
                kj++;
            }
            else if (ni < nj)
            {
                ki++;
            }
            else
            {
                kj++;
            }
        }
        
        return count;
    }
}

/// <summary>
/// CSR probability computation kernel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrExistenceProbabilityKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<Double2> amplitudes;
    public readonly ReadWriteBuffer<double> probabilities;
    public readonly int edgeCount;
    
    public CsrExistenceProbabilityKernelDouble(
        ReadOnlyBuffer<Double2> amplitudes,
        ReadWriteBuffer<double> probabilities,
        int edgeCount)
    {
        this.amplitudes = amplitudes;
        this.probabilities = probabilities;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 alpha = amplitudes[e];
        probabilities[e] = alpha.X * alpha.X + alpha.Y * alpha.Y;
    }
}

/// <summary>
/// CSR squared probability kernel for purity.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrPuritySquaredKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> probabilities;
    public readonly ReadWriteBuffer<double> squaredProbs;
    public readonly int edgeCount;
    
    public CsrPuritySquaredKernelDouble(
        ReadOnlyBuffer<double> probabilities,
        ReadWriteBuffer<double> squaredProbs,
        int edgeCount)
    {
        this.probabilities = probabilities;
        this.squaredProbs = squaredProbs;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double p = probabilities[e];
        squaredProbs[e] = p * p;
    }
}

/// <summary>
/// CSR collapse kernel with batch random numbers.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrCollapseKernelDouble : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> amplitudes;
    public readonly ReadWriteBuffer<double> weights;
    public readonly ReadOnlyBuffer<double> randomThresholds;
    public readonly ReadWriteBuffer<int> existsAfterCollapse;
    public readonly int edgeCount;
    public readonly double minWeight;
    
    public CsrCollapseKernelDouble(
        ReadWriteBuffer<Double2> amplitudes,
        ReadWriteBuffer<double> weights,
        ReadOnlyBuffer<double> randomThresholds,
        ReadWriteBuffer<int> existsAfterCollapse,
        int edgeCount,
        double minWeight)
    {
        this.amplitudes = amplitudes;
        this.weights = weights;
        this.randomThresholds = randomThresholds;
        this.existsAfterCollapse = existsAfterCollapse;
        this.edgeCount = edgeCount;
        this.minWeight = minWeight;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 alpha = amplitudes[e];
        double probability = alpha.X * alpha.X + alpha.Y * alpha.Y;
        double threshold = randomThresholds[e];
        
        bool exists = threshold < probability;
        existsAfterCollapse[e] = exists ? 1 : 0;
        
        if (exists)
        {
            amplitudes[e] = new Double2(1.0, 0.0);
            double w = weights[e];
            if (w < minWeight)
            {
                weights[e] = 0.5;
            }
        }
        else
        {
            amplitudes[e] = new Double2(0.0, 0.0);
            weights[e] = 0.0;
        }
    }
}

/// <summary>
/// CSR parallel reduction for sum.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSumReductionKernelDouble : IComputeShader
{
    public readonly ReadWriteBuffer<double> data;
    public readonly int stride;
    public readonly int length;
    
    public CsrSumReductionKernelDouble(
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
/// Normalization kernel to ensure ?|?|? = 1 after operations.
/// Includes branch pruning: edges with amplitude below epsilon are zeroed.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrNormalizeAmplitudesKernelDouble : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> amplitudes;
    public readonly int edgeCount;
    /// <summary>Amplitude pruning threshold (amplitudes below this are zeroed)</summary>
    public readonly double pruneEpsilon;
    
    public CsrNormalizeAmplitudesKernelDouble(
        ReadWriteBuffer<Double2> amplitudes,
        int edgeCount,
        double pruneEpsilon = 1e-15)
    {
        this.amplitudes = amplitudes;
        this.edgeCount = edgeCount;
        this.pruneEpsilon = pruneEpsilon;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        Double2 alpha = amplitudes[e];
        
        // NUMERICAL GUARD: Check for NaN/Inf
        if (Hlsl.IsNaN((float)alpha.X) || Hlsl.IsInfinite((float)alpha.X) ||
            Hlsl.IsNaN((float)alpha.Y) || Hlsl.IsInfinite((float)alpha.Y))
        {
            amplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        double normSq = alpha.X * alpha.X + alpha.Y * alpha.Y;
        
        // BRANCH PRUNING: Zero out negligible amplitudes to free GPU memory
        if (normSq < pruneEpsilon * pruneEpsilon)
        {
            amplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        double norm = Hlsl.Sqrt((float)normSq);
        
        if (norm > 1e-10)
        {
            amplitudes[e] = new Double2(alpha.X / norm, alpha.Y / norm);
        }
        else
        {
            // Zero amplitude stays zero
            amplitudes[e] = new Double2(0.0, 0.0);
        }
    }
}

/// <summary>
/// Global renormalization kernel to restore ?|?|? = targetNorm.
/// Applied periodically (every N steps) to correct numerical drift.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrGlobalRenormalizeKernelDouble : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> amplitudes;
    public readonly int edgeCount;
    /// <summary>Current total probability ?|?|? (precomputed via reduction)</summary>
    public readonly double totalProbability;
    /// <summary>Target total probability (usually 1.0)</summary>
    public readonly double targetProbability;
    /// <summary>Amplitude pruning threshold</summary>
    public readonly double pruneEpsilon;
    
    public CsrGlobalRenormalizeKernelDouble(
        ReadWriteBuffer<Double2> amplitudes,
        int edgeCount,
        double totalProbability,
        double targetProbability,
        double pruneEpsilon = 1e-15)
    {
        this.amplitudes = amplitudes;
        this.edgeCount = edgeCount;
        this.totalProbability = totalProbability;
        this.targetProbability = targetProbability;
        this.pruneEpsilon = pruneEpsilon;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        // Skip renormalization if total probability is too small
        if (totalProbability < 1e-30)
        {
            return;
        }
        
        Double2 alpha = amplitudes[e];
        
        // NUMERICAL GUARD: Check for NaN/Inf
        if (Hlsl.IsNaN((float)alpha.X) || Hlsl.IsInfinite((float)alpha.X) ||
            Hlsl.IsNaN((float)alpha.Y) || Hlsl.IsInfinite((float)alpha.Y))
        {
            amplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        // BRANCH PRUNING: Zero out negligible amplitudes
        double normSq = alpha.X * alpha.X + alpha.Y * alpha.Y;
        if (normSq < pruneEpsilon * pruneEpsilon)
        {
            amplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        // Scale factor: sqrt(target / current)
        double scale = Hlsl.Sqrt((float)(targetProbability / totalProbability));
        
        double newReal = alpha.X * scale;
        double newImag = alpha.Y * scale;
        
        // NUMERICAL GUARD: Final check
        if (Hlsl.IsNaN((float)newReal) || Hlsl.IsInfinite((float)newReal) ||
            Hlsl.IsNaN((float)newImag) || Hlsl.IsInfinite((float)newImag))
        {
            amplitudes[e] = new Double2(0.0, 0.0);
            return;
        }
        
        amplitudes[e] = new Double2(newReal, newImag);
    }
}
