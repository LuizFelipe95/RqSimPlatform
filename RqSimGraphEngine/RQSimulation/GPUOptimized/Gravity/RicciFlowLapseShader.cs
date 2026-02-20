using ComputeSharp;

namespace RQSimulation.GPUOptimized.Gravity;

/// <summary>
/// RQG-HYPOTHESIS: Ricci Flow with Lapse Modulation
/// 
/// REPLACES: RewiringManager.cs (random edge switching)
/// 
/// Instead of discrete rewiring with Random, topology evolves
/// continuously through Ricci flow modulated by Lapse function.
/// 
/// PHYSICS:
/// dw/d? = -2 · R_ij · N_i · d?
/// 
/// where:
/// - w = edge weight (metric component)
/// - R_ij = Ollivier-Ricci curvature of edge
/// - N_i = Lapse function (from Hamiltonian constraint)
/// - d? = coordinate time step
/// 
/// KEY INSIGHT:
/// Instead of DELETING edges (expensive CSR rebuild), we set weight ? 0.
/// The edge remains in topology but has no physical effect.
/// 
/// STOP-LIST COMPLIANCE:
/// ? NO edge deletion from CSR arrays
/// ? Set weight = 0 and ignore in physics
/// ? Modulated by Lapse (frozen at singularities)
/// </summary>

/// <summary>
/// Primary Ricci flow kernel with Lapse modulation.
/// 
/// Evolves edge weights according to discrete Ricci flow,
/// with time flow controlled by local Lapse function.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct RicciFlowLapseShaderDouble : IComputeShader
{
    /// <summary>Edge Ricci curvatures (Ollivier-Ricci)</summary>
    public readonly ReadOnlyBuffer<double> ricciCurvature;
    
    /// <summary>Lapse function at source node</summary>
    public readonly ReadOnlyBuffer<double> lapseSrc;
    
    /// <summary>Lapse function at destination node</summary>
    public readonly ReadOnlyBuffer<double> lapseDst;
    
    /// <summary>Current edge weights (input/output)</summary>
    public readonly ReadWriteBuffer<double> edgeWeights;
    
    /// <summary>Coordinate time step d?</summary>
    public readonly double deltaLambda;
    
    /// <summary>Flow rate coefficient</summary>
    public readonly double flowRate;
    
    /// <summary>Minimum weight (floor to prevent negative)</summary>
    public readonly double minWeight;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public RicciFlowLapseShaderDouble(
        ReadOnlyBuffer<double> ricciCurvature,
        ReadOnlyBuffer<double> lapseSrc,
        ReadOnlyBuffer<double> lapseDst,
        ReadWriteBuffer<double> edgeWeights,
        double deltaLambda,
        double flowRate,
        double minWeight,
        int edgeCount)
    {
        this.ricciCurvature = ricciCurvature;
        this.lapseSrc = lapseSrc;
        this.lapseDst = lapseDst;
        this.edgeWeights = edgeWeights;
        this.deltaLambda = deltaLambda;
        this.flowRate = flowRate;
        this.minWeight = minWeight;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double R = ricciCurvature[e];
        double N_src = lapseSrc[e];
        double N_dst = lapseDst[e];
        
        // Average Lapse for this edge
        double N_avg = (N_src + N_dst) * 0.5;
        
        // Ricci flow: dw/d? = -2 · R · N · d?
        // Negative curvature (saddle) ? weight increases
        // Positive curvature (sphere) ? weight decreases
        double flow = -2.0 * R * N_avg * flowRate * deltaLambda;
        
        double w = edgeWeights[e];
        double newWeight = w + flow;
        
        // Floor at minimum weight (instead of deleting edge)
        if (newWeight < minWeight)
        {
            newWeight = minWeight;
        }
        
        edgeWeights[e] = newWeight;
    }
}

/// <summary>
/// Normalized Ricci flow (preserves total volume).
/// 
/// dw/d? = -2(R - R?) · N · d?
/// 
/// where R? is average curvature.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct NormalizedRicciFlowShaderDouble : IComputeShader
{
    /// <summary>Edge Ricci curvatures</summary>
    public readonly ReadOnlyBuffer<double> ricciCurvature;
    
    /// <summary>Average Ricci curvature (precomputed)</summary>
    public readonly double avgCurvature;
    
    /// <summary>Lapse function at each node</summary>
    public readonly ReadOnlyBuffer<double> lapse;
    
    /// <summary>Edge source nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Edge weights (input/output)</summary>
    public readonly ReadWriteBuffer<double> edgeWeights;
    
    /// <summary>Coordinate time step</summary>
    public readonly double deltaLambda;
    
    /// <summary>Flow rate</summary>
    public readonly double flowRate;
    
    /// <summary>Minimum weight</summary>
    public readonly double minWeight;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public NormalizedRicciFlowShaderDouble(
        ReadOnlyBuffer<double> ricciCurvature,
        double avgCurvature,
        ReadOnlyBuffer<double> lapse,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        ReadWriteBuffer<double> edgeWeights,
        double deltaLambda,
        double flowRate,
        double minWeight,
        int edgeCount)
    {
        this.ricciCurvature = ricciCurvature;
        this.avgCurvature = avgCurvature;
        this.lapse = lapse;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.edgeWeights = edgeWeights;
        this.deltaLambda = deltaLambda;
        this.flowRate = flowRate;
        this.minWeight = minWeight;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double R = ricciCurvature[e];
        int src = edgeSrc[e];
        int dst = edgeDst[e];
        
        double N_avg = (lapse[src] + lapse[dst]) * 0.5;
        
        // Normalized flow: subtract average to preserve volume
        double flow = -2.0 * (R - avgCurvature) * N_avg * flowRate * deltaLambda;
        
        double w = edgeWeights[e];
        double newWeight = w + flow;
        
        if (newWeight < minWeight) newWeight = minWeight;
        
        edgeWeights[e] = newWeight;
    }
}

/// <summary>
/// Compute Ollivier-Ricci curvature approximation for Ricci flow.
/// Uses Jaccard-based approximation (fast but approximate).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct OllivierRicciApproxShaderDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR column indices</summary>
    public readonly ReadOnlyBuffer<int> colIdx;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Edge source nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeSrc;
    
    /// <summary>Edge destination nodes</summary>
    public readonly ReadOnlyBuffer<int> edgeDst;
    
    /// <summary>Output: Ollivier-Ricci curvature for each edge</summary>
    public readonly ReadWriteBuffer<double> curvature;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public OllivierRicciApproxShaderDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<int> colIdx,
        ReadOnlyBuffer<double> weights,
        ReadOnlyBuffer<int> edgeSrc,
        ReadOnlyBuffer<int> edgeDst,
        ReadWriteBuffer<double> curvature,
        int edgeCount,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.colIdx = colIdx;
        this.weights = weights;
        this.edgeSrc = edgeSrc;
        this.edgeDst = edgeDst;
        this.curvature = curvature;
        this.edgeCount = edgeCount;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        int u = edgeSrc[e];
        int v = edgeDst[e];
        
        // Get neighborhoods
        int startU = rowPtr[u];
        int endU = rowPtr[u + 1];
        int startV = rowPtr[v];
        int endV = rowPtr[v + 1];
        
        int degU = endU - startU;
        int degV = endV - startV;
        
        if (degU == 0 || degV == 0)
        {
            curvature[e] = 0.0;
            return;
        }
        
        // Count common neighbors (Jaccard numerator)
        int commonNeighbors = 0;
        
        for (int i = startU; i < endU; i++)
        {
            int neighborU = colIdx[i];
            
            for (int j = startV; j < endV; j++)
            {
                int neighborV = colIdx[j];
                if (neighborU == neighborV)
                {
                    commonNeighbors++;
                    break;
                }
            }
        }
        
        // Union size (Jaccard denominator): |N(u) ? N(v)| = |N(u)| + |N(v)| - |N(u) ? N(v)|
        int unionSize = degU + degV - commonNeighbors;
        
        // Jaccard similarity J = |intersection| / |union|
        double jaccard = unionSize > 0 ? (double)commonNeighbors / unionSize : 0.0;
        
        // Ollivier-Ricci approximation: ? ? 2J - 1
        // ? = 1 when perfect overlap (sphere-like)
        // ? = -1 when no overlap (tree-like/hyperbolic)
        curvature[e] = 2.0 * jaccard - 1.0;
    }
}

/// <summary>
/// Apply surgery to edges with weight below threshold.
/// Instead of deleting, mark as "inactive" for physics.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EdgeSurgeryShaderDouble : IComputeShader
{
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Output: Edge active flags (1=active, 0=inactive)</summary>
    public readonly ReadWriteBuffer<int> edgeActive;
    
    /// <summary>Threshold below which edge is deactivated</summary>
    public readonly double threshold;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public EdgeSurgeryShaderDouble(
        ReadOnlyBuffer<double> edgeWeights,
        ReadWriteBuffer<int> edgeActive,
        double threshold,
        int edgeCount)
    {
        this.edgeWeights = edgeWeights;
        this.edgeActive = edgeActive;
        this.threshold = threshold;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double w = edgeWeights[e];
        
        // Mark inactive if below threshold
        edgeActive[e] = (w > threshold) ? 1 : 0;
    }
}

/// <summary>
/// Compute metric tensor components from edge weights.
/// g_ij = w_ij? (weight squared as metric)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct MetricFromWeightsShaderDouble : IComputeShader
{
    /// <summary>Edge weights</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Output: Metric tensor components</summary>
    public readonly ReadWriteBuffer<double> metric;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public MetricFromWeightsShaderDouble(
        ReadOnlyBuffer<double> edgeWeights,
        ReadWriteBuffer<double> metric,
        int edgeCount)
    {
        this.edgeWeights = edgeWeights;
        this.metric = metric;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        double w = edgeWeights[e];
        
        // Metric component: g = w?
        metric[e] = w * w;
    }
}

/// <summary>
/// Volume-preserving constraint for Ricci flow.
/// Rescales weights to maintain total volume.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VolumePreservingRescaleShaderDouble : IComputeShader
{
    /// <summary>Edge weights (input/output)</summary>
    public readonly ReadWriteBuffer<double> edgeWeights;
    
    /// <summary>Current total volume (precomputed)</summary>
    public readonly double currentVolume;
    
    /// <summary>Target volume to preserve</summary>
    public readonly double targetVolume;
    
    /// <summary>Number of edges</summary>
    public readonly int edgeCount;
    
    public VolumePreservingRescaleShaderDouble(
        ReadWriteBuffer<double> edgeWeights,
        double currentVolume,
        double targetVolume,
        int edgeCount)
    {
        this.edgeWeights = edgeWeights;
        this.currentVolume = currentVolume;
        this.targetVolume = targetVolume;
        this.edgeCount = edgeCount;
    }
    
    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;
        
        if (currentVolume < 1e-15) return;
        
        // Rescale factor to preserve volume
        double scale = targetVolume / currentVolume;
        
        double w = edgeWeights[e];
        edgeWeights[e] = w * scale;
    }
}
