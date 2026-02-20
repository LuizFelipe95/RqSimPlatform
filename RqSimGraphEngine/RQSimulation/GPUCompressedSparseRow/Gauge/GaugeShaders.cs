using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Gauge;

/// <summary>
/// GPU kernel for detecting triangles (plaquettes) in CSR graph.
/// 
/// ALGORITHM: For each edge (i,j), find common neighbors to form triangles.
/// Uses CSR intersection: neighbors(i) ? neighbors(j) = triangle vertices.
/// 
/// Output: List of triangles as (i, j, k) with i < j < k.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct TriangleDetectionKernel : IComputeShader
{
    /// <summary>CSR row offsets.</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;

    /// <summary>CSR column indices.</summary>
    public readonly ReadOnlyBuffer<int> colIndices;

    /// <summary>Output: triangle count per edge (for prefix sum).</summary>
    public readonly ReadWriteBuffer<int> triangleCount;

    /// <summary>Output: triangle vertex k for each edge (i,j) forming triangle (i,j,k).</summary>
    public readonly ReadWriteBuffer<int> triangleVertices;

    /// <summary>Maximum triangles per edge.</summary>
    public readonly int maxTrianglesPerEdge;

    /// <summary>Number of edges (nnz).</summary>
    public readonly int edgeCount;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public TriangleDetectionKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadWriteBuffer<int> triangleCount,
        ReadWriteBuffer<int> triangleVertices,
        int maxTrianglesPerEdge,
        int edgeCount,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.triangleCount = triangleCount;
        this.triangleVertices = triangleVertices;
        this.maxTrianglesPerEdge = maxTrianglesPerEdge;
        this.edgeCount = edgeCount;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int edgeIdx = ThreadIds.X;
        if (edgeIdx >= edgeCount) return;

        // Find edge endpoints by scanning rowOffsets
        int i = 0;
        for (int n = 0; n < nodeCount; n++)
        {
            if (edgeIdx >= rowOffsets[n] && edgeIdx < rowOffsets[n + 1])
            {
                i = n;
                break;
            }
        }
        int j = colIndices[edgeIdx];

        // Only process edges where i < j to avoid duplicates
        if (i >= j)
        {
            triangleCount[edgeIdx] = 0;
            return;
        }

        // Find intersection of neighbors(i) and neighbors(j)
        int iStart = rowOffsets[i];
        int iEnd = rowOffsets[i + 1];
        int jStart = rowOffsets[j];
        int jEnd = rowOffsets[j + 1];

        int count = 0;
        int outputBase = edgeIdx * maxTrianglesPerEdge;

        // Two-pointer intersection (sorted CSR)
        int pi = iStart;
        int pj = jStart;

        while (pi < iEnd && pj < jEnd && count < maxTrianglesPerEdge)
        {
            int ni = colIndices[pi];
            int nj = colIndices[pj];

            if (ni == nj)
            {
                // Common neighbor k = ni forms triangle (i, j, k)
                if (ni > j) // Ensure i < j < k
                {
                    triangleVertices[outputBase + count] = ni;
                    count++;
                }
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

        triangleCount[edgeIdx] = count;
    }
}

/// <summary>
/// GPU kernel for computing Wilson loop around a triangle.
/// 
/// PHYSICS: Wilson loop W = exp(i(?_ij + ?_jk + ?_ki))
/// where ?_ij is the U(1) gauge phase on edge (i,j).
/// 
/// For U(1) gauge: W = exp(i * flux) where flux = ? phases around loop.
/// Gauge invariant quantity: |W| = 1, arg(W) = magnetic flux.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct WilsonLoopKernel : IComputeShader
{
    /// <summary>Triangle data: 3 vertices per triangle.</summary>
    public readonly ReadOnlyBuffer<int> triangleData;

    /// <summary>Edge phases ?_ij stored as flattened N?N matrix.</summary>
    public readonly ReadOnlyBuffer<double> edgePhases;

    /// <summary>Output: Wilson loop real part (cos(flux)).</summary>
    public readonly ReadWriteBuffer<double> wilsonReal;

    /// <summary>Output: Wilson loop imaginary part (sin(flux)).</summary>
    public readonly ReadWriteBuffer<double> wilsonImag;

    /// <summary>Number of triangles.</summary>
    public readonly int triangleCount;

    /// <summary>Number of nodes (for phase matrix indexing).</summary>
    public readonly int nodeCount;

    public WilsonLoopKernel(
        ReadOnlyBuffer<int> triangleData,
        ReadOnlyBuffer<double> edgePhases,
        ReadWriteBuffer<double> wilsonReal,
        ReadWriteBuffer<double> wilsonImag,
        int triangleCount,
        int nodeCount)
    {
        this.triangleData = triangleData;
        this.edgePhases = edgePhases;
        this.wilsonReal = wilsonReal;
        this.wilsonImag = wilsonImag;
        this.triangleCount = triangleCount;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int triIdx = ThreadIds.X;
        if (triIdx >= triangleCount) return;

        // Get triangle vertices
        int i = triangleData[triIdx * 3];
        int j = triangleData[triIdx * 3 + 1];
        int k = triangleData[triIdx * 3 + 2];

        // Get phases (antisymmetric: ?_ij = -?_ji)
        double theta_ij = edgePhases[i * nodeCount + j];
        double theta_jk = edgePhases[j * nodeCount + k];
        double theta_ki = edgePhases[k * nodeCount + i];

        // Total flux around triangle
        double flux = theta_ij + theta_jk + theta_ki;

        // Wilson loop = exp(i * flux)
        wilsonReal[triIdx] = Hlsl.Cos((float)flux);
        wilsonImag[triIdx] = Hlsl.Sin((float)flux);
    }
}

/// <summary>
/// GPU kernel for computing topological charge from Wilson loops.
/// 
/// PHYSICS: Chern number C = (1/2?) ?_triangles arg(W)
/// where W is the Wilson loop around each triangle.
/// 
/// For U(1) gauge theory, this counts magnetic monopoles.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct TopologicalChargeKernel : IComputeShader
{
    /// <summary>Wilson loop real parts.</summary>
    public readonly ReadOnlyBuffer<double> wilsonReal;

    /// <summary>Wilson loop imaginary parts.</summary>
    public readonly ReadOnlyBuffer<double> wilsonImag;

    /// <summary>Output: flux contribution per triangle (in [-?, ?]).</summary>
    public readonly ReadWriteBuffer<double> fluxContribution;

    /// <summary>Number of triangles.</summary>
    public readonly int triangleCount;

    public TopologicalChargeKernel(
        ReadOnlyBuffer<double> wilsonReal,
        ReadOnlyBuffer<double> wilsonImag,
        ReadWriteBuffer<double> fluxContribution,
        int triangleCount)
    {
        this.wilsonReal = wilsonReal;
        this.wilsonImag = wilsonImag;
        this.fluxContribution = fluxContribution;
        this.triangleCount = triangleCount;
    }

    public void Execute()
    {
        int triIdx = ThreadIds.X;
        if (triIdx >= triangleCount) return;

        double re = wilsonReal[triIdx];
        double im = wilsonImag[triIdx];

        // arg(W) = atan2(im, re) in [-?, ?]
        double flux = Hlsl.Atan2((float)im, (float)re);

        fluxContribution[triIdx] = flux;
    }
}

/// <summary>
/// GPU kernel for node-local gauge invariant check.
/// 
/// PHYSICS: For each node, sum Wilson loops of adjacent triangles.
/// Local gauge invariance requires these to sum to multiples of 2?.
/// 
/// Output: Gauge violation measure per node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalGaugeViolationKernel : IComputeShader
{
    /// <summary>Triangle membership: which triangles contain each node.</summary>
    public readonly ReadOnlyBuffer<int> nodeTriangleOffsets;

    /// <summary>Triangle indices for each node.</summary>
    public readonly ReadOnlyBuffer<int> nodeTriangles;

    /// <summary>Flux contribution per triangle.</summary>
    public readonly ReadOnlyBuffer<double> fluxContribution;

    /// <summary>Output: gauge violation per node.</summary>
    public readonly ReadWriteBuffer<double> gaugeViolation;

    /// <summary>Number of nodes.</summary>
    public readonly int nodeCount;

    public LocalGaugeViolationKernel(
        ReadOnlyBuffer<int> nodeTriangleOffsets,
        ReadOnlyBuffer<int> nodeTriangles,
        ReadOnlyBuffer<double> fluxContribution,
        ReadWriteBuffer<double> gaugeViolation,
        int nodeCount)
    {
        this.nodeTriangleOffsets = nodeTriangleOffsets;
        this.nodeTriangles = nodeTriangles;
        this.fluxContribution = fluxContribution;
        this.gaugeViolation = gaugeViolation;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        int start = nodeTriangleOffsets[node];
        int end = nodeTriangleOffsets[node + 1];

        double totalFlux = 0.0;
        for (int k = start; k < end; k++)
        {
            int triIdx = nodeTriangles[k];
            totalFlux += fluxContribution[triIdx];
        }

        // Gauge violation = deviation from 2? multiples
        const double twoPi = 2.0 * 3.14159265358979323846;
        double normalized = totalFlux / twoPi;
        double violation = normalized - Hlsl.Round((float)normalized);

        gaugeViolation[node] = violation * twoPi; // In radians
    }
}

/// <summary>
/// GPU kernel for computing Berry phase around cluster boundary.
/// 
/// PHYSICS: Berry phase = ? A·dl around closed path
/// For discrete graph: ?_path ?_ij over boundary edges.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct BerryPhaseKernel : IComputeShader
{
    /// <summary>Boundary path: ordered list of node pairs.</summary>
    public readonly ReadOnlyBuffer<int> boundaryPath;

    /// <summary>Edge phases ?_ij.</summary>
    public readonly ReadOnlyBuffer<double> edgePhases;

    /// <summary>Output: partial sums for reduction.</summary>
    public readonly ReadWriteBuffer<double> partialSums;

    /// <summary>Number of edges in boundary path.</summary>
    public readonly int pathLength;

    /// <summary>Number of nodes (for phase indexing).</summary>
    public readonly int nodeCount;

    /// <summary>Block size for reduction.</summary>
    public readonly int blockSize;

    public BerryPhaseKernel(
        ReadOnlyBuffer<int> boundaryPath,
        ReadOnlyBuffer<double> edgePhases,
        ReadWriteBuffer<double> partialSums,
        int pathLength,
        int nodeCount,
        int blockSize)
    {
        this.boundaryPath = boundaryPath;
        this.edgePhases = edgePhases;
        this.partialSums = partialSums;
        this.pathLength = pathLength;
        this.nodeCount = nodeCount;
        this.blockSize = blockSize;
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
            if (end > pathLength) end = pathLength;

            double sum = 0.0;
            for (int k = start; k < end; k++)
            {
                int i = boundaryPath[k * 2];
                int j = boundaryPath[k * 2 + 1];
                sum += edgePhases[i * nodeCount + j];
            }
            partialSums[blockId] = sum;
        }
    }
}
