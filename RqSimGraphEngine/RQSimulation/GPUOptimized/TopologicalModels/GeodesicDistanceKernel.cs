using ComputeSharp;

namespace RQSimulation.GPUOptimized.TopologicalModels;

/// <summary>
/// RQG-HYPOTHESIS: Geodesic Distance Kernels for GPU
/// 
/// Computes graph geodesic distances for:
/// - k-th order neighbor finding (nonlocality)
/// - Ollivier-Ricci curvature computation
/// - Wasserstein distance in optimal transport
/// - Causal structure analysis
/// 
/// ALGORITHMS:
/// 1. BFS-based shortest path (exact, O(V+E) per source)
/// 2. Heat Method approximation (fast, parallel)
/// 3. Multi-source distance propagation
/// 
/// These distances are essential for nonlocal quantum effects
/// where particles can "hop" through k-neighbors.
/// </summary>

/// <summary>
/// BFS distance propagation kernel - single step.
/// Run iteratively until convergence (distance stabilizes).
/// 
/// ALGORITHM:
/// For each node with distance d, set neighbors with distance > d+1 to d+1.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct BfsDistancePropagationKernel : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>Current distances (input/output)</summary>
    public readonly ReadWriteBuffer<int> distances;
    
    /// <summary>Flag: whether any update occurred (for convergence check)</summary>
    public readonly ReadWriteBuffer<int> updated;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    /// <summary>Maximum distance (infinity proxy)</summary>
    public readonly int infinity;
    
    public BfsDistancePropagationKernel(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadWriteBuffer<int> distances,
        ReadWriteBuffer<int> updated,
        int nodeCount,
        int infinity)
    {
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.distances = distances;
        this.updated = updated;
        this.nodeCount = nodeCount;
        this.infinity = infinity;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int d_i = distances[i];
        if (d_i >= infinity) return; // Node not yet reached
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        for (int k = start; k < end; k++)
        {
            int j = colIdx[k];
            int d_j = distances[j];
            
            int newDist = d_i + 1;
            if (d_j > newDist)
            {
                // Atomic min would be ideal, but we'll use simple write
                // (may need multiple iterations to converge)
                distances[j] = newDist;
                updated[0] = 1; // Signal that we made an update
            }
        }
    }
}

/// <summary>
/// Initialize distance array for BFS from source node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct InitDistancesKernel : IComputeShader
{
    /// <summary>Output distances</summary>
    public readonly ReadWriteBuffer<int> distances;
    
    /// <summary>Source node index</summary>
    public readonly int source;
    
    /// <summary>Infinity value</summary>
    public readonly int infinity;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public InitDistancesKernel(
        ReadWriteBuffer<int> distances,
        int source,
        int infinity,
        int nodeCount)
    {
        this.distances = distances;
        this.source = source;
        this.infinity = infinity;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        distances[i] = (i == source) ? 0 : infinity;
    }
}

/// <summary>
/// Find k-th order neighbors (nodes at distance exactly k).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct FindKthNeighborsKernel : IComputeShader
{
    /// <summary>Precomputed distances from source</summary>
    public readonly ReadOnlyBuffer<int> distances;
    
    /// <summary>Output: 1 if node is at distance k, 0 otherwise</summary>
    public readonly ReadWriteBuffer<int> isKthNeighbor;
    
    /// <summary>Target distance k</summary>
    public readonly int k;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public FindKthNeighborsKernel(
        ReadOnlyBuffer<int> distances,
        ReadWriteBuffer<int> isKthNeighbor,
        int k,
        int nodeCount)
    {
        this.distances = distances;
        this.isKthNeighbor = isKthNeighbor;
        this.k = k;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        isKthNeighbor[i] = (distances[i] == k) ? 1 : 0;
    }
}

/// <summary>
/// Heat Method for geodesic distance approximation.
/// Step 1: Solve heat equation ?u/?t = ?u with initial condition at source.
/// 
/// This is a diffusion step - run multiple iterations.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HeatDiffusionKernel : IComputeShader
{
    /// <summary>Current heat values</summary>
    public readonly ReadOnlyBuffer<double> heatIn;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output: Updated heat values</summary>
    public readonly ReadWriteBuffer<double> heatOut;
    
    /// <summary>Diffusion coefficient</summary>
    public readonly double diffusionCoeff;
    
    /// <summary>Time step dt</summary>
    public readonly double dt;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public HeatDiffusionKernel(
        ReadOnlyBuffer<double> heatIn,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<double> heatOut,
        double diffusionCoeff,
        double dt,
        int nodeCount)
    {
        this.heatIn = heatIn;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.weights = weights;
        this.heatOut = heatOut;
        this.diffusionCoeff = diffusionCoeff;
        this.dt = dt;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double u_i = heatIn[i];
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double laplacian = 0.0;
        double totalWeight = 0.0;
        
        for (int k = start; k < end; k++)
        {
            int j = colIdx[k];
            double w = weights[k];
            
            laplacian += w * (heatIn[j] - u_i);
            totalWeight += w;
        }
        
        if (totalWeight > 1e-10)
        {
            laplacian /= totalWeight;
        }
        
        // Forward Euler: u_new = u + dt * D * ?u
        heatOut[i] = u_i + dt * diffusionCoeff * laplacian;
    }
}

/// <summary>
/// Heat Method Step 2: Compute normalized gradient.
/// X = -?u / |?u|
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HeatGradientKernel : IComputeShader
{
    /// <summary>Heat values after diffusion</summary>
    public readonly ReadOnlyBuffer<double> heat;
    
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output: Gradient magnitude for Poisson solve</summary>
    public readonly ReadWriteBuffer<double> gradMagnitude;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public HeatGradientKernel(
        ReadOnlyBuffer<double> heat,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<double> gradMagnitude,
        int nodeCount)
    {
        this.heat = heat;
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.weights = weights;
        this.gradMagnitude = gradMagnitude;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double u_i = heat[i];
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double gradSq = 0.0;
        
        for (int k = start; k < end; k++)
        {
            int j = colIdx[k];
            double w = weights[k];
            
            double diff = heat[j] - u_i;
            gradSq += w * diff * diff;
        }
        
        // |?u| = sqrt(? w_ij (u_j - u_i)?)
        double absGrad = Hlsl.Sqrt((float)gradSq);
        
        gradMagnitude[i] = absGrad > 1e-10 ? absGrad : 1e-10;
    }
}

/// <summary>
/// Weighted distance computation for Ollivier-Ricci.
/// Computes d(i,j) as shortest weighted path length.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct WeightedDistanceKernel : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights (as distances: smaller = closer)</summary>
    public readonly ReadOnlyBuffer<double> distances;
    
    /// <summary>Current shortest distances from source</summary>
    public readonly ReadWriteBuffer<double> shortestDist;
    
    /// <summary>Infinity value</summary>
    public readonly double infinity;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public WeightedDistanceKernel(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> distances,
        ReadWriteBuffer<double> shortestDist,
        double infinity,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.distances = distances;
        this.shortestDist = shortestDist;
        this.infinity = infinity;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double d_i = shortestDist[i];
        if (d_i >= infinity) return;
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        for (int k = start; k < end; k++)
        {
            int j = colIdx[k];
            double edgeDist = distances[k];
            
            double newDist = d_i + edgeDist;
            double d_j = shortestDist[j];
            
            if (newDist < d_j)
            {
                shortestDist[j] = newDist;
            }
        }
    }
}

/// <summary>
/// Compute all-pairs shortest path matrix for small subgraphs.
/// Used in local Wasserstein distance computation.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalDistanceMatrixKernel : IComputeShader
{
    /// <summary>Local adjacency (flattened n?n matrix)</summary>
    public readonly ReadOnlyBuffer<double> localAdj;
    
    /// <summary>Output: Distance matrix (flattened n?n)</summary>
    public readonly ReadWriteBuffer<double> distMatrix;
    
    /// <summary>Local subgraph size n</summary>
    public readonly int localSize;
    
    /// <summary>Infinity value</summary>
    public readonly double infinity;
    
    public LocalDistanceMatrixKernel(
        ReadOnlyBuffer<double> localAdj,
        ReadWriteBuffer<double> distMatrix,
        int localSize,
        double infinity)
    {
        this.localAdj = localAdj;
        this.distMatrix = distMatrix;
        this.localSize = localSize;
        this.infinity = infinity;
    }
    
    public void Execute()
    {
        int idx = ThreadIds.X;
        int n = localSize;
        if (idx >= n * n) return;
        
        int i = idx / n;
        int j = idx % n;
        
        double adj = localAdj[idx];
        
        if (i == j)
        {
            distMatrix[idx] = 0.0;
        }
        else if (adj > 1e-10)
        {
            // Edge weight as distance (inverse of strength)
            distMatrix[idx] = 1.0 / adj;
        }
        else
        {
            distMatrix[idx] = infinity;
        }
    }
}
