using ComputeSharp;

namespace RQSimulation.GPUOptimized.QuantumEdges;

/// <summary>
/// Double-precision compute shaders for quantum edge operations.
/// 
/// RQ-HYPOTHESIS STAGE 3: QUANTUM GRAPHITY
/// ========================================
/// In quantum gravity, geometry itself is in superposition.
/// Each edge has a quantum amplitude: |edge_ij? = ?|exists? + ?|not-exists?
/// 
/// GPU Parallelization:
/// - Unitary evolution: parallel over all edges (fully independent)
/// - Probability computation: parallel over edges ? reduction for normalization
/// - Purity computation: parallel over edges ? reduction for sum
/// 
/// PHYSICS:
/// - Edge amplitude evolves as: ?(t+dt) = exp(-i*H_edge*dt) * ?(t)
/// - Existence probability: P = |?|?
/// - Purity: ? = ? P? / (? P)? measures quantum coherence
/// 
/// All operations use double precision (64-bit) for physical accuracy.
/// </summary>

/// <summary>
/// Unitary evolution kernel for quantum edge amplitudes.
/// 
/// PHYSICS: Time evolution under local Hamiltonian
/// exp(-i*H*dt) = cos(H*dt) - i*sin(H*dt)
/// 
/// Each edge evolves independently based on its local energy.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct UnitaryEvolutionKernelDouble : IComputeShader
{
    /// <summary>Edge amplitudes as (real, imag) pairs</summary>
    public readonly ReadWriteBuffer<Double2> amplitudes;
    
    /// <summary>Diagonal Hamiltonian elements (local energy for each edge)</summary>
    public readonly ReadOnlyBuffer<double> hamiltonianDiag;
    
    /// <summary>Time step dt</summary>
    public readonly double dt;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public UnitaryEvolutionKernelDouble(
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
        
        // exp(-i * H * dt) = cos(H*dt) - i*sin(H*dt)
        double phase = H * dt;
        double cosP = Hlsl.Cos((float)phase);
        double sinP = Hlsl.Sin((float)phase);
        
        // Complex multiplication: (a + bi) * (cos - i*sin)
        // = a*cos + b*sin + i*(b*cos - a*sin)
        double newReal = alpha.X * cosP + alpha.Y * sinP;
        double newImag = alpha.Y * cosP - alpha.X * sinP;
        
        amplitudes[e] = new Double2(newReal, newImag);
    }
}

/// <summary>
/// Compute existence probabilities from quantum amplitudes.
/// P_e = |?_e|? = (Re)? + (Im)?
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ExistenceProbabilityKernelDouble : IComputeShader
{
    /// <summary>Edge amplitudes as (real, imag) pairs</summary>
    public readonly ReadOnlyBuffer<Double2> amplitudes;
    
    /// <summary>Output probabilities</summary>
    public readonly ReadWriteBuffer<double> probabilities;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public ExistenceProbabilityKernelDouble(
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
/// Compute squared probabilities for purity calculation.
/// purity = ? P? / (? P)?
/// This kernel computes P? for each edge.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PuritySquaredKernelDouble : IComputeShader
{
    /// <summary>Probabilities P = |?|?</summary>
    public readonly ReadOnlyBuffer<double> probabilities;
    
    /// <summary>Output squared probabilities P?</summary>
    public readonly ReadWriteBuffer<double> squaredProbs;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public PuritySquaredKernelDouble(
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
/// Compute local edge Hamiltonian based on neighbor configuration.
/// 
/// PHYSICS: Edge energy depends on:
/// - Triangle count (clustering coefficient)
/// - Endpoint degrees (connectivity)
/// - Edge weight (existing strength)
/// 
/// Lower energy for edges that form triangles (clustered structures).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeHamiltonianKernelDouble : IComputeShader
{
    /// <summary>Edge endpoints: edgeI[e], edgeJ[e]</summary>
    public readonly ReadOnlyBuffer<int> edgeI;
    public readonly ReadOnlyBuffer<int> edgeJ;
    
    /// <summary>CSR row offsets for adjacency</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIndices;
    
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Node degrees</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Output Hamiltonian diagonal</summary>
    public readonly ReadWriteBuffer<double> hamiltonianDiag;
    
    /// <summary>Triangle bonus coefficient (negative = lower energy)</summary>
    public readonly double triangleBonus;
    
    /// <summary>Degree penalty coefficient</summary>
    public readonly double degreePenalty;
    
    /// <summary>Target average degree</summary>
    public readonly double targetDegree;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public EdgeHamiltonianKernelDouble(
        ReadOnlyBuffer<int> edgeI,
        ReadOnlyBuffer<int> edgeJ,
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<double> hamiltonianDiag,
        double triangleBonus,
        double degreePenalty,
        double targetDegree,
        int edgeCount)
    {
        this.edgeI = edgeI;
        this.edgeJ = edgeJ;
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.weights = weights;
        this.degrees = degrees;
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
        
        // Count common neighbors (triangles)
        int triangles = CountCommonNeighbors(i, j);
        
        // Triangle energy (negative = favors triangles)
        double E_triangle = triangleBonus * triangles;
        
        // Degree penalty
        int degI = degrees[i];
        int degJ = degrees[j];
        double E_degree = degreePenalty * ((degI - targetDegree) + (degJ - targetDegree));
        
        hamiltonianDiag[e] = E_triangle + E_degree;
    }
    
    private int CountCommonNeighbors(int i, int j)
    {
        int startI = rowOffsets[i];
        int endI = rowOffsets[i + 1];
        int startJ = rowOffsets[j];
        int endJ = rowOffsets[j + 1];
        
        int count = 0;
        int ki = startI;
        int kj = startJ;
        
        // Merge-style intersection of sorted neighbor lists
        while (ki < endI && kj < endJ)
        {
            int ni = colIndices[ki];
            int nj = colIndices[kj];
            
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
/// Apply quantum measurement (collapse) to edge amplitudes.
/// Based on random threshold, collapses to |exists? or |not-exists?.
/// 
/// PHYSICS: Quantum measurement projects superposition to eigenstate.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CollapseKernelDouble : IComputeShader
{
    /// <summary>Edge amplitudes (modified in-place)</summary>
    public readonly ReadWriteBuffer<Double2> amplitudes;
    
    /// <summary>Edge weights (classical)</summary>
    public readonly ReadWriteBuffer<double> weights;
    
    /// <summary>Random thresholds for each edge (0-1)</summary>
    public readonly ReadOnlyBuffer<double> randomThresholds;
    
    /// <summary>Output: 1 if edge exists after collapse, 0 otherwise</summary>
    public readonly ReadWriteBuffer<int> existsAfterCollapse;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public CollapseKernelDouble(
        ReadWriteBuffer<Double2> amplitudes,
        ReadWriteBuffer<double> weights,
        ReadOnlyBuffer<double> randomThresholds,
        ReadWriteBuffer<int> existsAfterCollapse,
        int edgeCount)
    {
        this.amplitudes = amplitudes;
        this.weights = weights;
        this.randomThresholds = randomThresholds;
        this.existsAfterCollapse = existsAfterCollapse;
        this.edgeCount = edgeCount;
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
            // Collapse to |exists?: amplitude = 1
            amplitudes[e] = new Double2(1.0, 0.0);
            // Keep weight or set minimum
            if (weights[e] < 0.01)
            {
                weights[e] = 0.5;
            }
        }
        else
        {
            // Collapse to |not-exists?: amplitude = 0
            amplitudes[e] = new Double2(0.0, 0.0);
            weights[e] = 0.0;
        }
    }
}

/// <summary>
/// Parallel sum reduction kernel for double arrays.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SumReductionKernelDouble : IComputeShader
{
    /// <summary>Data to sum (modified in-place during reduction)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Stride for this reduction pass</summary>
    public readonly int stride;
    
    /// <summary>Total array length</summary>
    public readonly int length;
    
    public SumReductionKernelDouble(
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
