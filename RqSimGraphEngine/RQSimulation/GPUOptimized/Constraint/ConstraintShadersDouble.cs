using ComputeSharp;

namespace RQSimulation.GPUOptimized.Constraint;

/// <summary>
/// Double-precision compute shaders for Wheeler-DeWitt constraint computation.
/// 
/// RQ-HYPOTHESIS STAGE 2: WHEELER-DEWITT CONSTRAINT
/// ================================================
/// The Wheeler-DeWitt equation is the quantum gravity analog of the
/// Schr?dinger equation: H|?? = 0 (frozen time formalism).
/// 
/// For a graph, we compute the local constraint at each node:
/// H_local = H_geometry - ? * H_matter ? 0
/// 
/// GPU Parallelization:
/// - Each node computes its local curvature independently
/// - Each node computes its constraint violation independently
/// - Final reduction sums all violations (parallel reduction)
/// 
/// All operations use double precision for physical accuracy.
/// </summary>

/// <summary>
/// Compute local curvature (degree deviation) at each node.
/// 
/// PHYSICS: Local curvature R_i = (k_i - ?k?) / ?k?
/// where k_i is node degree and ?k? is average degree.
/// 
/// This is a simplified Forman-Ricci-like curvature that measures
/// how much a node deviates from the "average" connectivity.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalCurvatureKernelDouble : IComputeShader
{
    /// <summary>CSR row offsets for adjacency (size N+1)</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;
    
    /// <summary>Node degrees (size N)</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Output curvatures (size N)</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Average degree of the graph</summary>
    public readonly double avgDegree;
    
    /// <summary>Total number of nodes</summary>
    public readonly int nodeCount;
    
    public LocalCurvatureKernelDouble(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<double> curvatures,
        double avgDegree,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.degrees = degrees;
        this.curvatures = curvatures;
        this.avgDegree = avgDegree;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double localDegree = degrees[i];
        
        // Curvature = (local - avg) / avg
        // Zero curvature means node has exactly average connectivity
        // Positive = more connected, Negative = less connected
        if (avgDegree > 1e-10)
        {
            curvatures[i] = (localDegree - avgDegree) / avgDegree;
        }
        else
        {
            curvatures[i] = 0.0;
        }
    }
}

/// <summary>
/// Compute Wheeler-DeWitt constraint violation at each node.
/// 
/// PHYSICS:
/// H_constraint = (H_geometry - ? * H_matter)?
/// 
/// The constraint should be zero for physical configurations.
/// We compute the squared violation for use in the Hamiltonian.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct WheelerDeWittConstraintKernelDouble : IComputeShader
{
    /// <summary>Local curvatures (H_geometry) at each node</summary>
    public readonly ReadOnlyBuffer<double> curvatures;
    
    /// <summary>Correlation mass (H_matter) at each node</summary>
    public readonly ReadOnlyBuffer<double> mass;
    
    /// <summary>Output squared constraint violation at each node</summary>
    public readonly ReadWriteBuffer<double> violations;
    
    /// <summary>Gravitational coupling ? (kappa)</summary>
    public readonly double kappa;
    
    /// <summary>Total number of nodes</summary>
    public readonly int nodeCount;
    
    public WheelerDeWittConstraintKernelDouble(
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
        
        // Wheeler-DeWitt: geometry = ? * matter
        // Constraint = H_geom - ? * H_matter should be zero
        double constraint = H_geom - kappa * H_matter;
        
        // Store squared violation (always non-negative)
        violations[i] = constraint * constraint;
    }
}

/// <summary>
/// Parallel reduction kernel for summing an array.
/// Uses tree-based reduction for O(log N) depth.
/// 
/// Call this kernel multiple times with increasing stride
/// until the sum is in element 0.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SumReductionKernelDouble : IComputeShader
{
    /// <summary>Data buffer to reduce (modified in place)</summary>
    public readonly ReadWriteBuffer<double> data;
    
    /// <summary>Current stride (1, 2, 4, 8, ...)</summary>
    public readonly int stride;
    
    /// <summary>Number of active elements</summary>
    public readonly int count;
    
    public SumReductionKernelDouble(
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
/// Advanced local curvature kernel using Forman-Ricci approximation.
/// 
/// PHYSICS: Forman-Ricci curvature for edge (i,j):
/// R_ij = w_ij * (deg_i + deg_j - 2 - #triangles(i,j))
/// 
/// For node i, we average curvatures of incident edges.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct FormanRicciCurvatureKernelDouble : IComputeShader
{
    /// <summary>CSR row offsets</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;
    
    /// <summary>CSR column indices (neighbors)</summary>
    public readonly ReadOnlyBuffer<int> colIndices;
    
    /// <summary>Edge weights in CSR format</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Node degrees</summary>
    public readonly ReadOnlyBuffer<int> degrees;
    
    /// <summary>Output curvatures (size N)</summary>
    public readonly ReadWriteBuffer<double> curvatures;
    
    /// <summary>Total number of nodes</summary>
    public readonly int nodeCount;
    
    /// <summary>Bonus for triangles (typically 1.0)</summary>
    public readonly double triangleBonus;
    
    public FormanRicciCurvatureKernelDouble(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> degrees,
        ReadWriteBuffer<double> curvatures,
        int nodeCount,
        double triangleBonus)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.degrees = degrees;
        this.curvatures = curvatures;
        this.nodeCount = nodeCount;
        this.triangleBonus = triangleBonus;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int start = rowOffsets[i];
        int end = rowOffsets[i + 1];
        int degI = degrees[i];
        
        if (end <= start)
        {
            curvatures[i] = 0.0;
            return;
        }
        
        double totalCurvature = 0.0;
        int edgeCount = 0;
        
        // Iterate over neighbors of i
        for (int k = start; k < end; k++)
        {
            int j = colIndices[k];
            int degJ = degrees[j];
            double w = edgeWeights[k];
            
            // Count common neighbors (triangles through edge i-j)
            int triangles = CountCommonNeighbors(i, j);
            
            // Forman-Ricci curvature for edge (i,j)
            // R = w * (deg_i + deg_j - 2 - bonus * triangles)
            double R_edge = w * (degI + degJ - 2.0 - triangleBonus * triangles);
            
            totalCurvature += R_edge;
            edgeCount++;
        }
        
        // Average over incident edges
        curvatures[i] = edgeCount > 0 ? totalCurvature / edgeCount : 0.0;
    }
    
    /// <summary>
    /// Count common neighbors between nodes i and j.
    /// Uses linear search (O(deg_i * deg_j)) - acceptable for sparse graphs.
    /// </summary>
    private int CountCommonNeighbors(int i, int j)
    {
        int startI = rowOffsets[i];
        int endI = rowOffsets[i + 1];
        int startJ = rowOffsets[j];
        int endJ = rowOffsets[j + 1];
        
        int count = 0;
        
        for (int ki = startI; ki < endI; ki++)
        {
            int nbI = colIndices[ki];
            for (int kj = startJ; kj < endJ; kj++)
            {
                if (colIndices[kj] == nbI)
                {
                    count++;
                    break;
                }
            }
        }
        
        return count;
    }
}
