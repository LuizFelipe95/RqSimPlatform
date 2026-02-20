using ComputeSharp;

namespace RQSimulation.GPUOptimized.SpectralAction;

/// <summary>
/// Double-precision compute shaders for Spectral Action computation.
/// 
/// RQ-HYPOTHESIS STAGE 5: SPECTRAL ACTION PRINCIPLE
/// =================================================
/// The spectral action principle (Chamseddine-Connes) states that the
/// fundamental action is S = Tr(f(D/?)) where D is the Dirac operator
/// and ? is the UV cutoff.
/// 
/// For a graph, we expand this into:
///   S = f???V + f????R + f??C? + S_dimension
/// 
/// where:
/// - V = effective volume (sum of edge weights)
/// - ?R = integrated curvature (Einstein-Hilbert term)
/// - ?C? = Weyl curvature squared (conformal term)
/// - S_dimension = Mexican hat potential for d_S stabilization
/// 
/// GPU Parallelization:
/// - Volume: parallel sum over edges
/// - Curvature average: parallel over nodes + reduction
/// - Weyl squared: variance computation via parallel sum
/// - Spectral dimension: parallel degree computation + reduction
/// 
/// All operations use double precision (64-bit) for physical accuracy.
/// </summary>

/// <summary>
/// Compute effective volume contribution from each edge.
/// Volume = ??? w?? (sum of all edge weights)
/// 
/// Each thread processes one edge.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeVolumeKernelDouble : IComputeShader
{
    /// <summary>Edge weights array</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Output volume contributions (same size as weights)</summary>
    public readonly ReadWriteBuffer<double> volumeContributions;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public EdgeVolumeKernelDouble(
        ReadOnlyBuffer<double> edgeWeights,
        ReadWriteBuffer<double> volumeContributions,
        int edgeCount)
    {
        this.edgeWeights = edgeWeights;
        this.volumeContributions = volumeContributions;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        // Each edge contributes its weight to the volume
        volumeContributions[e] = edgeWeights[e];
    }
}

/// <summary>
/// Compute curvature variance for Weyl squared approximation.
/// 
/// PHYSICS: Weyl tensor C_???? measures traceless Riemann curvature.
/// For a graph, we approximate |C|? ? Var(R) = ?R?? - ?R??
/// 
/// Step 1: Compute R?? for each node (this kernel)
/// Step 2: Reduce to get ?R?? and ?R?
/// Step 3: Compute variance = ?R?? - ?R??
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CurvatureSquaredKernelDouble : IComputeShader
{
    /// <summary>Local curvatures at each node</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Output squared curvatures</summary>
    public readonly ReadWriteBuffer<double> curvaturesSq;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CurvatureSquaredKernelDouble(
        ReadOnlyBuffer<double> curvatures,
        ReadWriteBuffer<double> curvaturesSq,
        int nodeCount)
    {
        this.curvatures = curvatures;
        this.curvaturesSq = curvaturesSq;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double R = curvatures[i];
        curvaturesSq[i] = R * R;
    }
}

/// <summary>
/// Compute curvature deviation from mean for variance calculation.
/// variance[i] = (R[i] - avgR)?
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CurvatureVarianceKernelDouble : IComputeShader
{
    /// <summary>Local curvatures at each node</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Output variance contributions</summary>
    public readonly ReadWriteBuffer<double> variance;
    
    /// <summary>Average curvature (precomputed)</summary>
    public readonly double avgCurvature;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CurvatureVarianceKernelDouble(
        ReadOnlyBuffer<double> curvatures,
        ReadWriteBuffer<double> variance,
        double avgCurvature,
        int nodeCount)
    {
        this.curvatures = curvatures;
        this.variance = variance;
        this.avgCurvature = avgCurvature;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double diff = curvatures[i] - avgCurvature;
        variance[i] = diff * diff;
    }
}

/// <summary>
/// Compute Mexican hat potential for dimension stabilization.
/// 
/// V(d_S) = ? * (d_S - d_target)? * ((d_S - d_target)? - w?)
/// 
/// This potential has:
/// - Global minimum at d_S = d_target (typically 4)
/// - Local barriers at d_S ? d_target ± w
/// - Smooth energy landscape
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DimensionPotentialKernelDouble : IComputeShader
{
    /// <summary>Estimated spectral dimension</summary>
    public readonly double spectralDimension;
    
    /// <summary>Target dimension (typically 4.0)</summary>
    public readonly double targetDimension;
    
    /// <summary>Potential strength ?</summary>
    public readonly double strength;
    
    /// <summary>Potential width w</summary>
    public readonly double width;
    
    /// <summary>Output potential value (single element buffer)</summary>
    public readonly ReadWriteBuffer<double> potential;
    
    public DimensionPotentialKernelDouble(
        double spectralDimension,
        double targetDimension,
        double strength,
        double width,
        ReadWriteBuffer<double> potential)
    {
        this.spectralDimension = spectralDimension;
        this.targetDimension = targetDimension;
        this.strength = strength;
        this.width = width;
        this.potential = potential;
    }
    
    public void Execute()
    {
        // Only thread 0 computes
        if (ThreadIds.X != 0) return;
        
        double deviation = spectralDimension - targetDimension;
        double dev2 = deviation * deviation;
        double w2 = width * width;
        
        // Mexican hat: V = ? * (d - d?)? * ((d - d?)? - w?)
        potential[0] = strength * dev2 * (dev2 - w2);
    }
}

/// <summary>
/// Compute node degrees for spectral dimension estimation.
/// Stores both degree and degree? for variance computation.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct DegreeComputeKernelDouble : IComputeShader
{
    /// <summary>CSR row offsets (degree[i] = rowOffsets[i+1] - rowOffsets[i])</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;
    
    /// <summary>Output degrees</summary>
    public readonly ReadWriteBuffer<double> degrees;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public DegreeComputeKernelDouble(
        ReadOnlyBuffer<int> rowOffsets,
        ReadWriteBuffer<double> degrees,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.degrees = degrees;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int degree = rowOffsets[i + 1] - rowOffsets[i];
        degrees[i] = degree;
    }
}

/// <summary>
/// Compute spectral action term contributions for each node.
/// Combines cosmological, Einstein-Hilbert, and Weyl terms.
/// 
/// S_node[i] = f?*??*v[i] + f?*??*R[i]*v[i] + f?*|C|?*v[i]
/// 
/// where v[i] is the node's share of volume (degree / total_degree).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SpectralActionTermsKernelDouble : IComputeShader
{
    /// <summary>Local curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Node degrees (for volume weighting)</summary>
    public readonly ReadOnlyBuffer<double> degrees;
    
    /// <summary>Output action contributions</summary>
    public readonly ReadWriteBuffer<double> actionContributions;
    
    /// <summary>Total degree (2 * edge count)</summary>
    public readonly double totalDegree;
    
    /// <summary>f? coefficient (cosmological)</summary>
    public readonly double f0;
    
    /// <summary>f? coefficient (Einstein-Hilbert)</summary>
    public readonly double f2;
    
    /// <summary>?? (UV cutoff to 4th power)</summary>
    public readonly double lambda4;
    
    /// <summary>?? (UV cutoff squared)</summary>
    public readonly double lambda2;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public SpectralActionTermsKernelDouble(
        ReadOnlyBuffer<double> curvatures,
        ReadOnlyBuffer<double> degrees,
        ReadWriteBuffer<double> actionContributions,
        double totalDegree,
        double f0,
        double f2,
        double lambda4,
        double lambda2,
        int nodeCount)
    {
        this.curvatures = curvatures;
        this.degrees = degrees;
        this.actionContributions = actionContributions;
        this.totalDegree = totalDegree;
        this.f0 = f0;
        this.f2 = f2;
        this.lambda4 = lambda4;
        this.lambda2 = lambda2;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double R = curvatures[i];
        double deg = degrees[i];
        
        // Node's share of volume
        double volumeFraction = totalDegree > 0 ? deg / totalDegree : 0.0;
        
        // Cosmological term: f? * ?? * v
        double S_cosmo = f0 * lambda4 * volumeFraction;
        
        // Einstein-Hilbert term: f? * ?? * R * v
        double S_eh = f2 * lambda2 * R * volumeFraction;
        
        actionContributions[i] = S_cosmo + S_eh;
    }
}

/// <summary>
/// General parallel reduction kernel for double precision.
/// Tree-based reduction with configurable stride.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ReduceSumKernelDouble : IComputeShader
{
    /// <summary>Data buffer (modified in place)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Current stride</summary>
    public readonly int stride;
    
    /// <summary>Number of elements</summary>
    public readonly int count;
    
    public ReduceSumKernelDouble(
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
        
        if (target < count && source < count)
        {
            data[target] += data[source];
        }
    }
}

/// <summary>
/// Compute curvature integral term: ?R?g = ?? R? * v?
/// where v? is node's volume contribution.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CurvatureIntegralKernelDouble : IComputeShader
{
    /// <summary>Local curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Node degrees (volume proxy)</summary>
    public readonly ReadOnlyBuffer<double> degrees;
    
    /// <summary>Output R*v contributions</summary>
    public readonly ReadWriteBuffer<double> integrals;
    
    /// <summary>Total degree for normalization</summary>
    public readonly double totalDegree;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CurvatureIntegralKernelDouble(
        ReadOnlyBuffer<double> curvatures,
        ReadOnlyBuffer<double> degrees,
        ReadWriteBuffer<double> integrals,
        double totalDegree,
        int nodeCount)
    {
        this.curvatures = curvatures;
        this.degrees = degrees;
        this.integrals = integrals;
        this.totalDegree = totalDegree;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double R = curvatures[i];
        double deg = degrees[i];
        
        // Volume fraction for this node
        double vFrac = totalDegree > 0 ? deg / totalDegree : 0.0;
        
        // Contribution to curvature integral
        integrals[i] = R * vFrac;
    }
}

/// <summary>
/// New kernel: compute local anisotropy (Weyl proxy) using edge-level Ollivier-Ricci curvatures.
/// For each node i, compute the variance of edge curvatures k_ij over its incident edges.
/// This replaces simple averaging of node curvatures with an anisotropy metric derived from edges.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeAnisotropyKernelDouble : IComputeShader
{
    /// <summary>CSR row offsets (rowOffsets[i+1]-rowOffsets[i] = degree)</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;

    /// <summary>Edge-level curvatures (precomputed Ollivier-Ricci per directed edge in CSR order)</summary>
    public readonly ReadOnlyBuffer<double> edgeCurvatures;

    /// <summary>Output per-node anisotropy (weyl proxy)</summary>
    public readonly ReadWriteBuffer<double> weylProxy;

    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;

    public EdgeAnisotropyKernelDouble(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<double> edgeCurvatures,
        ReadWriteBuffer<double> weylProxy,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.edgeCurvatures = edgeCurvatures;
        this.weylProxy = weylProxy;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        int rowStart = rowOffsets[i];
        int rowEnd = rowOffsets[i + 1];

        double sumRicci = 0.0;
        double sumSqRicci = 0.0;
        int degree = 0;

        // Collect edge-level curvatures for incident edges
        for (int idx = rowStart; idx < rowEnd; idx++)
        {
            double kij = edgeCurvatures[idx];
            sumRicci += kij;
            sumSqRicci += kij * kij;
            degree++;
        }

        double denom = degree > 0 ? (double)degree : 1.0;
        double avgR = sumRicci / denom;

        // Anisotropy estimate: variance of edge curvatures around node mean
        double anisotropy = (sumSqRicci / denom) - (avgR * avgR);

        weylProxy[i] = anisotropy;
    }
}
