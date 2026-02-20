using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.SpectralAction;

/// <summary>
/// CSR-optimized compute shaders for Spectral Action on large sparse graphs.
/// 
/// PHYSICS:
/// ========
/// Same spectral action principle as GPUOptimized but optimized for CSR sparse format.
/// For graphs with N > 10? nodes, CSR provides significant memory savings.
/// 
/// Memory comparison for typical sparse graphs (E ? 4N):
/// - Dense adjacency: O(N?) ? 1M nodes = 8TB
/// - CSR format: O(N + E) ? 1M nodes = 40MB
/// 
/// All operations use double precision (64-bit) for physical accuracy.
/// </summary>

/// <summary>
/// Compute effective volume from CSR edge weights.
/// Sum all weights and divide by 2 (since CSR stores directed edges).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrVolumeKernelDouble : IComputeShader
{
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> values;
    
    /// <summary>Output volume contributions</summary>
    public readonly ReadWriteBuffer<double> volumeContribs;
    
    /// <summary>Number of non-zero elements</summary>
    public readonly int nnz;
    
    public CsrVolumeKernelDouble(
        ReadOnlyBuffer<double> values,
        ReadWriteBuffer<double> volumeContribs,
        int nnz)
    {
        this.values = values;
        this.volumeContribs = volumeContribs;
        this.nnz = nnz;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= nnz) return;
        
        // Each directed edge contributes half its weight to total volume
        volumeContribs[e] = values[e] * 0.5;
    }
}

/// <summary>
/// Compute local curvature from CSR topology.
/// Uses degree-based curvature: R[i] = (deg[i] - avgDeg) / avgDeg
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrLocalCurvatureKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Output curvatures</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Average degree</summary>
    public readonly double avgDegree;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrLocalCurvatureKernelDouble(
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
        
        int degree = rowPtr[i + 1] - rowPtr[i];
        
        if (avgDegree > 1e-10)
        {
            curvatures[i] = (degree - avgDegree) / avgDegree;
        }
        else
        {
            curvatures[i] = 0.0;
        }
    }
}

/// <summary>
/// Compute Weyl squared (variance) contributions from CSR curvatures.
/// variance[i] = (R[i] - avgR)?
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrCurvatureVarianceKernelDouble : IComputeShader
{
    /// <summary>Node curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Output variance contributions</summary>
    public readonly ReadWriteBuffer<double> variance;
    
    /// <summary>Average curvature</summary>
    public readonly double avgCurvature;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrCurvatureVarianceKernelDouble(
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
/// Combined spectral action terms computation.
/// Computes S_node[i] = f???v[i] + f???R[i]v[i] for each node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSpectralActionTermsKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers (for degree computation)</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Node curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Output action contributions</summary>
    public readonly ReadWriteBuffer<double> actionContribs;
    
    /// <summary>Total degree (2 * edge_count)</summary>
    public readonly double totalDegree;
    
    /// <summary>f? coefficient</summary>
    public readonly double f0;
    
    /// <summary>f? coefficient</summary>
    public readonly double f2;
    
    /// <summary>??</summary>
    public readonly double lambda4;
    
    /// <summary>??</summary>
    public readonly double lambda2;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrSpectralActionTermsKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> curvatures,
        ReadWriteBuffer<double> actionContribs,
        double totalDegree,
        double f0,
        double f2,
        double lambda4,
        double lambda2,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.curvatures = curvatures;
        this.actionContribs = actionContribs;
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
        
        int degree = rowPtr[i + 1] - rowPtr[i];
        double R = curvatures[i];
        
        // Volume fraction for this node
        double vFrac = totalDegree > 0 ? degree / totalDegree : 0.0;
        
        // Cosmological: f???v
        double S_cosmo = f0 * lambda4 * vFrac;
        
        // Einstein-Hilbert: f???Rv
        double S_eh = f2 * lambda2 * R * vFrac;
        
        actionContribs[i] = S_cosmo + S_eh;
    }
}

/// <summary>
/// Segmented parallel reduction for CSR matrices.
/// Reduces values within each row segment defined by rowPtr.
/// Useful for per-node aggregations.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSegmentedReduceKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Values to reduce (size = nnz)</summary>
    public readonly ReadOnlyBuffer<double> values;
    
    /// <summary>Output per-row sums (size = N)</summary>
    public readonly ReadWriteBuffer<double> rowSums;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrSegmentedReduceKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> values,
        ReadWriteBuffer<double> rowSums,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.values = values;
        this.rowSums = rowSums;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double sum = 0.0;
        for (int k = start; k < end; k++)
        {
            sum += values[k];
        }
        
        rowSums[i] = sum;
    }
}

/// <summary>
/// Global sum reduction for double precision.
/// Uses tree-based parallel reduction.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrGlobalReduceKernelDouble : IComputeShader
{
    /// <summary>Data to reduce (modified in place)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Current stride</summary>
    public readonly int stride;
    
    /// <summary>Number of elements</summary>
    public readonly int count;
    
    public CsrGlobalReduceKernelDouble(
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
/// Compute integrated curvature ?R?g using CSR format.
/// Each node contributes R[i] * (deg[i] / totalDeg).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrCurvatureIntegralKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Node curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Output integral contributions</summary>
    public readonly ReadWriteBuffer<double> integrals;
    
    /// <summary>Total degree</summary>
    public readonly double totalDegree;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrCurvatureIntegralKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> curvatures,
        ReadWriteBuffer<double> integrals,
        double totalDegree,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.curvatures = curvatures;
        this.integrals = integrals;
        this.totalDegree = totalDegree;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int degree = rowPtr[i + 1] - rowPtr[i];
        double R = curvatures[i];
        
        double vFrac = totalDegree > 0 ? degree / totalDegree : 0.0;
        integrals[i] = R * vFrac;
    }
}

/// <summary>
/// Compute spectral dimension contribution from degree distribution.
/// Outputs degree and degree log for d_S estimation.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrDegreeStatsKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Output degrees as doubles</summary>
    public readonly ReadWriteBuffer<double> degreesOut;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrDegreeStatsKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadWriteBuffer<double> degreesOut,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.degreesOut = degreesOut;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int degree = rowPtr[i + 1] - rowPtr[i];
        degreesOut[i] = degree;
    }
}
