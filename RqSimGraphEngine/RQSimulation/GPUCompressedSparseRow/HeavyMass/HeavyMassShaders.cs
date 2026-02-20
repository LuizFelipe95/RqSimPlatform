using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.HeavyMass;

/// <summary>
/// GPU kernel for computing correlation mass per node via CSR.
/// 
/// PHYSICS: Correlation mass M_i = ?_j w_ij
/// This is the sum of edge weights to all neighbors.
/// High correlation mass indicates strongly connected nodes.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CorrelationMassKernel : IComputeShader
{
    /// <summary>CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;

    /// <summary>CSR column indices.</summary>
    public readonly ReadOnlyBuffer<int> colIndices;

    /// <summary>CSR edge weights.</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;

    /// <summary>Output: correlation mass per node.</summary>
    public readonly ReadWriteBuffer<double> correlationMass;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public CorrelationMassKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadWriteBuffer<double> correlationMass,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.correlationMass = correlationMass;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        double mass = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            mass += edgeWeights[k];
        }

        correlationMass[node] = mass;
    }
}

/// <summary>
/// GPU kernel for computing cluster correlation energy.
/// 
/// PHYSICS: E_cluster = ?_{i,j ? cluster} w_ij
/// Sum of all internal edge weights within a cluster.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ClusterEnergyKernel : IComputeShader
{
    /// <summary>CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;

    /// <summary>CSR column indices.</summary>
    public readonly ReadOnlyBuffer<int> colIndices;

    /// <summary>CSR edge weights.</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;

    /// <summary>Cluster membership: clusterMembership[node] = clusterId, -1 if not in cluster.</summary>
    public readonly ReadOnlyBuffer<int> clusterMembership;

    /// <summary>Output: partial energy sums per node.</summary>
    public readonly ReadWriteBuffer<double> nodeEnergyContrib;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public ClusterEnergyKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> clusterMembership,
        ReadWriteBuffer<double> nodeEnergyContrib,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.clusterMembership = clusterMembership;
        this.nodeEnergyContrib = nodeEnergyContrib;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        int myCluster = clusterMembership[node];
        if (myCluster < 0)
        {
            nodeEnergyContrib[node] = 0.0;
            return;
        }

        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        double energy = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            int neighborCluster = clusterMembership[neighbor];
            
            // Only count edges within same cluster
            if (neighborCluster == myCluster)
            {
                // Count each edge once (when node < neighbor)
                if (node < neighbor)
                {
                    energy += edgeWeights[k];
                }
            }
        }

        nodeEnergyContrib[node] = energy;
    }
}

/// <summary>
/// GPU kernel for computing geometry inertia (resistance to change).
/// 
/// PHYSICS: Inertia_i = |dM_i/dt| where M is correlation mass.
/// Computed from difference between current and previous mass.
/// High inertia indicates stable, "heavy" clusters.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct GeometryInertiaKernel : IComputeShader
{
    /// <summary>Current correlation mass.</summary>
    public readonly ReadOnlyBuffer<double> currentMass;

    /// <summary>Previous correlation mass.</summary>
    public readonly ReadOnlyBuffer<double> previousMass;

    /// <summary>Output: geometry inertia per node.</summary>
    public readonly ReadWriteBuffer<double> inertia;

    /// <summary>Time step for derivative.</summary>
    public readonly double dt;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public GeometryInertiaKernel(
        ReadOnlyBuffer<double> currentMass,
        ReadOnlyBuffer<double> previousMass,
        ReadWriteBuffer<double> inertia,
        double dt,
        int nodeCount)
    {
        this.currentMass = currentMass;
        this.previousMass = previousMass;
        this.inertia = inertia;
        this.dt = dt;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        double current = currentMass[node];
        double previous = previousMass[node];

        // Inertia = |dM/dt|
        double dMdt = (current - previous) / dt;
        inertia[node] = (dMdt >= 0) ? dMdt : -dMdt;
    }
}

/// <summary>
/// GPU kernel for computing center of mass using spectral coordinates.
/// 
/// PHYSICS: CoM = ?_i m_i * x_i / ? m_i
/// Uses spectral coordinates (Laplacian eigenvectors) for background independence.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CenterOfMassKernel : IComputeShader
{
    /// <summary>Correlation mass per node.</summary>
    public readonly ReadOnlyBuffer<double> mass;

    /// <summary>Spectral X coordinates.</summary>
    public readonly ReadOnlyBuffer<double> spectralX;

    /// <summary>Spectral Y coordinates.</summary>
    public readonly ReadOnlyBuffer<double> spectralY;

    /// <summary>Cluster membership.</summary>
    public readonly ReadOnlyBuffer<int> clusterMembership;

    /// <summary>Output: partial sums [totalMass, sumMX, sumMY] per block.</summary>
    public readonly ReadWriteBuffer<double> partialSums;

    /// <summary>Target cluster ID.</summary>
    public readonly int targetCluster;

    /// <summary>Block size for reduction.</summary>
    public readonly int blockSize;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public CenterOfMassKernel(
        ReadOnlyBuffer<double> mass,
        ReadOnlyBuffer<double> spectralX,
        ReadOnlyBuffer<double> spectralY,
        ReadOnlyBuffer<int> clusterMembership,
        ReadWriteBuffer<double> partialSums,
        int targetCluster,
        int blockSize,
        int nodeCount)
    {
        this.mass = mass;
        this.spectralX = spectralX;
        this.spectralY = spectralY;
        this.clusterMembership = clusterMembership;
        this.partialSums = partialSums;
        this.targetCluster = targetCluster;
        this.blockSize = blockSize;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / blockSize;
        int localId = idx % blockSize;

        if (localId == 0)
        {
            int start = blockId * blockSize;
            int end = start + blockSize;
            if (end > nodeCount) end = nodeCount;

            double totalMass = 0.0;
            double sumMX = 0.0;
            double sumMY = 0.0;

            for (int i = start; i < end; i++)
            {
                if (clusterMembership[i] == targetCluster)
                {
                    double m = mass[i];
                    totalMass += m;
                    sumMX += m * spectralX[i];
                    sumMY += m * spectralY[i];
                }
            }

            int baseIdx = blockId * 3;
            partialSums[baseIdx] = totalMass;
            partialSums[baseIdx + 1] = sumMX;
            partialSums[baseIdx + 2] = sumMY;
        }
    }
}

/// <summary>
/// GPU kernel for heavy cluster detection based on mass threshold.
/// 
/// PHYSICS: Heavy cluster = connected component with total mass > threshold.
/// These represent stable, massive structures in the graph.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HeavyClusterDetectionKernel : IComputeShader
{
    /// <summary>Correlation mass per node.</summary>
    public readonly ReadOnlyBuffer<double> correlationMass;

    /// <summary>Mass threshold for heavy classification.</summary>
    public readonly double massThreshold;

    /// <summary>Output: heavy flag per node.</summary>
    public readonly ReadWriteBuffer<int> isHeavy;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public HeavyClusterDetectionKernel(
        ReadOnlyBuffer<double> correlationMass,
        double massThreshold,
        ReadWriteBuffer<int> isHeavy,
        int nodeCount)
    {
        this.correlationMass = correlationMass;
        this.massThreshold = massThreshold;
        this.isHeavy = isHeavy;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        isHeavy[node] = (correlationMass[node] >= massThreshold) ? 1 : 0;
    }
}
