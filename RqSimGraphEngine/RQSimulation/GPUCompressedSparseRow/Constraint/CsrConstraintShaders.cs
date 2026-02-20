using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Constraint;

/// <summary>
/// CSR-optimized compute shaders for Wheeler-DeWitt constraint on large sparse graphs.
/// 
/// PHYSICS:
/// ========
/// Same as GPUOptimized version but optimized for CSR sparse format.
/// For N > 10? nodes, CSR format saves significant memory and bandwidth.
/// 
/// Memory comparison:
/// - Dense: O(N?) for adjacency matrix
/// - CSR: O(N + E) for sparse representation
/// 
/// All operations use double precision (64-bit) for physical accuracy.
/// </summary>

/// <summary>
/// Compute local Forman-Ricci curvature using CSR format.
/// More accurate than degree-based curvature for sparse graphs.
/// 
/// PHYSICS: Forman-Ricci for node i:
/// R_i = (1/deg_i) * ???N(i) [w_ij * (deg_i + deg_j - 2 - #triangles_ij)]
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrFormanRicciKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers (size N+1)</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> values;
    
    /// <summary>Node degrees (precomputed)</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Output curvatures</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    /// <summary>Triangle bonus coefficient</summary>
    public readonly double triangleBonus;
    
    public CsrFormanRicciKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> values,
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<double> curvatures,
        int nodeCount,
        double triangleBonus)
    {
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.values = values;
        this.degrees = degrees;
        this.curvatures = curvatures;
        this.nodeCount = nodeCount;
        this.triangleBonus = triangleBonus;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int startI = rowPtr[i];
        int endI = rowPtr[i + 1];
        int degI = degrees[i];
        
        if (degI == 0)
        {
            curvatures[i] = 0.0;
            return;
        }
        
        double totalCurvature = 0.0;
        
        // Iterate over neighbors of i
        for (int ki = startI; ki < endI; ki++)
        {
            int j = colIdx[ki];
            double w = values[ki];
            int degJ = degrees[j];
            
            // Count triangles through edge (i,j)
            int triangles = CountTriangles(i, j);
            
            // Forman-Ricci for edge (i,j)
            double R_edge = w * (degI + degJ - 2.0 - triangleBonus * triangles);
            totalCurvature += R_edge;
        }
        
        // Average over edges
        curvatures[i] = totalCurvature / degI;
    }
    
    /// <summary>
    /// Count common neighbors between i and j (triangles through edge i-j).
    /// Uses merge-like intersection for sorted neighbor lists.
    /// </summary>
    private int CountTriangles(int i, int j)
    {
        int startI = rowPtr[i];
        int endI = rowPtr[i + 1];
        int startJ = rowPtr[j];
        int endJ = rowPtr[j + 1];
        
        int count = 0;
        int pi = startI;
        int pj = startJ;
        
        // Merge intersection (assumes sorted colIdx)
        while (pi < endI && pj < endJ)
        {
            int ni = colIdx[pi];
            int nj = colIdx[pj];
            
            if (ni == nj)
            {
                count++;
                pi++;
                pj++;
            }
            else if (ni < nj)
            {
                pi++;
            }
            else
            {
                pj++;
            }
        }
        
        return count;
    }
}

/// <summary>
/// CSR-optimized Wheeler-DeWitt constraint kernel.
/// Identical to GPUOptimized version but explicitly marked for CSR context.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrWheelerDeWittKernelDouble : IComputeShader
{
    /// <summary>Local curvatures</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Correlation mass</summary>
    public readonly ReadOnlyBuffer<double> mass;
    
    /// <summary>Output violations</summary>
    public readonly ReadWriteBuffer<double> violations;
    
    /// <summary>Gravitational coupling ?</summary>
    public readonly double kappa;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrWheelerDeWittKernelDouble(
        ReadOnlyBuffer<double> curvatures,
        ReadOnlyBuffer<double> mass,
        ReadWriteBuffer<double> violations,
        double kappa,
        int nodeCount)
    {
        this.curvatures = curvatures;
        this.mass = mass;
        this.violations = violations;
        this.kappa = kappa;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double H_geom = curvatures[i];
        double H_matter = mass[i];
        double constraint = H_geom - kappa * H_matter;
        
        violations[i] = constraint * constraint;
    }
}

/// <summary>
/// Warp-level reduction for partial sums.
/// Each thread group reduces its local values.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrWarpReduceKernelDouble : IComputeShader
{
    /// <summary>Input data</summary>
    public readonly ReadOnlyBuffer<double> input;
    
    /// <summary>Output partial sums (one per thread group)</summary>
    public readonly ReadWriteBuffer<double> partialSums;
    
    /// <summary>Number of elements</summary>
    public readonly int count;
    
    /// <summary>Stride for reduction</summary>
    public readonly int stride;
    
    public CsrWarpReduceKernelDouble(
        ReadOnlyBuffer<double> input,
        ReadWriteBuffer<double> partialSums,
        int count,
        int stride)
    {
        this.input = input;
        this.partialSums = partialSums;
        this.count = count;
        this.stride = stride;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        int target = i * stride * 2;
        int source = target + stride;
        
        if (target < count && source < count)
        {
            partialSums[target] = input[target] + input[source];
        }
        else if (target < count)
        {
            partialSums[target] = input[target];
        }
    }
}

/// <summary>
/// Ollivier-Ricci curvature for CSR format (Jaccard approximation).
/// Computes curvature for each edge in parallel.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrOllivierRicciEdgeKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Edge source nodes (for each edge index)</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Output edge curvatures</summary>
    public readonly ReadWriteBuffer<double> edgeCurvatures;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public CsrOllivierRicciEdgeKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        ReadWriteBuffer<double> edgeCurvatures,
        int edgeCount)
    {
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.edgeCurvatures = edgeCurvatures;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int u = edgeSrc[e];
        int v = edgeDst[e];
        
        int startU = rowPtr[u];
        int endU = rowPtr[u + 1];
        int startV = rowPtr[v];
        int endV = rowPtr[v + 1];
        
        int degU = endU - startU;
        int degV = endV - startV;
        
        // Count intersection using merge (sorted lists)
        int intersection = 0;
        int pu = startU;
        int pv = startV;
        
        while (pu < endU && pv < endV)
        {
            int nu = colIdx[pu];
            int nv = colIdx[pv];
            
            if (nu == nv)
            {
                intersection++;
                pu++;
                pv++;
            }
            else if (nu < nv)
            {
                pu++;
            }
            else
            {
                pv++;
            }
        }
        
        // Jaccard similarity
        int unionSize = degU + degV - intersection;
        double jaccard = unionSize > 0 ? (double)intersection / unionSize : 0.0;
        
        // Ollivier-Ricci: ? = 2J - 1
        edgeCurvatures[e] = 2.0 * jaccard - 1.0;
    }
}

/// <summary>
/// Degree computation from CSR format.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrDegreeKernelDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Output degrees</summary>
    public readonly ReadWriteBuffer<int> degrees;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public CsrDegreeKernelDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadWriteBuffer<int> degrees,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.degrees = degrees;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        degrees[i] = rowPtr[i + 1] - rowPtr[i];
    }
}
