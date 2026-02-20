using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Shaders;

/// <summary>
/// CSR-format Hamiltonian SpMV (Sparse Matrix-Vector Multiplication) kernel.
/// 
/// Computes y = A * x where A is the Cayley operator (1 + i*H*dt/2) in CSR format.
/// This is the core operation for BiCGStab solver in Cayley evolution.
/// 
/// PHYSICS: H = -? + V (graph Laplacian plus potential)
/// Graph Laplacian: (??)_i = ?_j w_ij (?_j - ?_i) = ?_j w_ij ?_j - d_i ?_i
/// where d_i = ?_j w_ij is the weighted degree.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrHamiltonianKernel : IComputeShader
{
    /// <summary>Input wavefunction vector.</summary>
    public readonly ReadWriteBuffer<Double2> psi;
    
    /// <summary>Output: H*psi result.</summary>
    public readonly ReadWriteBuffer<Double2> hPsi;
    
    /// <summary>CSR row offsets (size = nodeCount + 1).</summary>
    public readonly ReadOnlyBuffer<int> rowOffsets;
    
    /// <summary>CSR column indices (size = nnz).</summary>
    public readonly ReadOnlyBuffer<int> colIndices;
    
    /// <summary>CSR edge weights (size = nnz).</summary>
    public readonly ReadOnlyBuffer<double> edgeWeights;
    
    /// <summary>Node potentials V(x) (size = nodeCount).</summary>
    public readonly ReadOnlyBuffer<double> potential;
    
    /// <summary>Number of nodes in graph.</summary>
    public readonly int nodeCount;
    
    /// <summary>Gauge dimension (components per node).</summary>
    public readonly int gaugeDim;

    public CsrHamiltonianKernel(
        ReadWriteBuffer<Double2> psi,
        ReadWriteBuffer<Double2> hPsi,
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<double> potential,
        int nodeCount,
        int gaugeDim)
    {
        this.psi = psi;
        this.hPsi = hPsi;
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.potential = potential;
        this.nodeCount = nodeCount;
        this.gaugeDim = gaugeDim;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int dim = nodeCount * gaugeDim;
        if (idx >= dim) return;

        int node = idx / gaugeDim;
        int comp = idx % gaugeDim;

        // Start with potential term: V_i * ?_i
        Double2 sum = new Double2(
            potential[node] * psi[idx].X,
            potential[node] * psi[idx].Y
        );

        // CSR row bounds for this node
        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        // Compute weighted degree and neighbor contributions
        double degree = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            double weight = edgeWeights[k];
            degree += weight;

            // Off-diagonal: -w_ij * ?_j (Laplacian)
            int neighborIdx = neighbor * gaugeDim + comp;
            sum.X -= weight * psi[neighborIdx].X;
            sum.Y -= weight * psi[neighborIdx].Y;
        }

        // Diagonal: d_i * ?_i (Laplacian degree term)
        sum.X += degree * psi[idx].X;
        sum.Y += degree * psi[idx].Y;

        hPsi[idx] = sum;
    }
}

/// <summary>
/// Cayley operator SpMV: y = (1 + i*?*H) * x
/// where ? = dt/2 for Cayley transform.
/// 
/// Used for both RHS computation (1 - i*?*H)*? and
/// matrix-vector product in BiCGStab iterations (1 + i*?*H)*x.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrCayleySpMVKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> y;
    public readonly ReadOnlyBuffer<int> rowOffsets;
    public readonly ReadOnlyBuffer<int> colIndices;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<double> potential;
    
    /// <summary>Coefficient ? = dt/2. Sign determines (1+i?H) vs (1-i?H).</summary>
    public readonly double alpha;
    public readonly int nodeCount;
    public readonly int gaugeDim;

    public CsrCayleySpMVKernel(
        ReadWriteBuffer<Double2> x,
        ReadWriteBuffer<Double2> y,
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<double> potential,
        double alpha,
        int nodeCount,
        int gaugeDim)
    {
        this.x = x;
        this.y = y;
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.potential = potential;
        this.alpha = alpha;
        this.nodeCount = nodeCount;
        this.gaugeDim = gaugeDim;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int dim = nodeCount * gaugeDim;
        if (idx >= dim) return;

        int node = idx / gaugeDim;
        int comp = idx % gaugeDim;

        // Compute H*x
        Double2 Hx = new Double2(
            potential[node] * x[idx].X,
            potential[node] * x[idx].Y
        );

        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        double degree = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            double weight = edgeWeights[k];
            degree += weight;

            int neighborIdx = neighbor * gaugeDim + comp;
            Hx.X -= weight * x[neighborIdx].X;
            Hx.Y -= weight * x[neighborIdx].Y;
        }

        Hx.X += degree * x[idx].X;
        Hx.Y += degree * x[idx].Y;

        // y = x + i*?*H*x = (x.r - ?*Hx.i, x.i + ?*Hx.r)
        y[idx] = new Double2(
            x[idx].X - alpha * Hx.Y,
            x[idx].Y + alpha * Hx.X
        );
    }
}

/// <summary>
/// Compute RHS for Cayley evolution: b = (1 - i*?*H) * ?
/// This is the right-hand side of the linear system Ax = b.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrComputeRhsKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> psi;
    public readonly ReadWriteBuffer<Double2> rhs;
    public readonly ReadOnlyBuffer<int> rowOffsets;
    public readonly ReadOnlyBuffer<int> colIndices;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<double> potential;
    public readonly double alpha;
    public readonly int nodeCount;
    public readonly int gaugeDim;

    public CsrComputeRhsKernel(
        ReadWriteBuffer<Double2> psi,
        ReadWriteBuffer<Double2> rhs,
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<double> potential,
        double alpha,
        int nodeCount,
        int gaugeDim)
    {
        this.psi = psi;
        this.rhs = rhs;
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.potential = potential;
        this.alpha = alpha;
        this.nodeCount = nodeCount;
        this.gaugeDim = gaugeDim;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int dim = nodeCount * gaugeDim;
        if (idx >= dim) return;

        int node = idx / gaugeDim;
        int comp = idx % gaugeDim;

        // Compute H*?
        Double2 Hpsi = new Double2(
            potential[node] * psi[idx].X,
            potential[node] * psi[idx].Y
        );

        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        double degree = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            double weight = edgeWeights[k];
            degree += weight;

            int neighborIdx = neighbor * gaugeDim + comp;
            Hpsi.X -= weight * psi[neighborIdx].X;
            Hpsi.Y -= weight * psi[neighborIdx].Y;
        }

        Hpsi.X += degree * psi[idx].X;
        Hpsi.Y += degree * psi[idx].Y;

        // rhs = ? - i*?*H*? = (?.r + ?*H?.i, ?.i - ?*H?.r)
        // Note: opposite sign from SpMV kernel
        rhs[idx] = new Double2(
            psi[idx].X + alpha * Hpsi.Y,
            psi[idx].Y - alpha * Hpsi.X
        );
    }
}

/// <summary>
/// Compute initial residual: r = b - A*x
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrComputeResidualKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> b;
    public readonly ReadWriteBuffer<Double2> r;
    public readonly ReadOnlyBuffer<int> rowOffsets;
    public readonly ReadOnlyBuffer<int> colIndices;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<double> potential;
    public readonly double alpha;
    public readonly int nodeCount;
    public readonly int gaugeDim;

    public CsrComputeResidualKernel(
        ReadWriteBuffer<Double2> x,
        ReadWriteBuffer<Double2> b,
        ReadWriteBuffer<Double2> r,
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<double> potential,
        double alpha,
        int nodeCount,
        int gaugeDim)
    {
        this.x = x;
        this.b = b;
        this.r = r;
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.potential = potential;
        this.alpha = alpha;
        this.nodeCount = nodeCount;
        this.gaugeDim = gaugeDim;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int dim = nodeCount * gaugeDim;
        if (idx >= dim) return;

        int node = idx / gaugeDim;
        int comp = idx % gaugeDim;

        // Compute H*x
        Double2 Hx = new Double2(
            potential[node] * x[idx].X,
            potential[node] * x[idx].Y
        );

        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        double degree = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            double weight = edgeWeights[k];
            degree += weight;

            int neighborIdx = neighbor * gaugeDim + comp;
            Hx.X -= weight * x[neighborIdx].X;
            Hx.Y -= weight * x[neighborIdx].Y;
        }

        Hx.X += degree * x[idx].X;
        Hx.Y += degree * x[idx].Y;

        // A*x = (1 + i*?*H)*x
        Double2 Ax = new Double2(
            x[idx].X - alpha * Hx.Y,
            x[idx].Y + alpha * Hx.X
        );

        // r = b - A*x
        r[idx] = new Double2(b[idx].X - Ax.X, b[idx].Y - Ax.Y);
    }
}
