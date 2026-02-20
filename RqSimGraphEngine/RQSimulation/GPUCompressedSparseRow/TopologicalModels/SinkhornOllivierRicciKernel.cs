using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.TopologicalModels;

/// <summary>
/// SPLIT-KERNEL SINKHORN OLLIVIER-RICCI CURVATURE (CSR Pipeline)
/// ==============================================================
/// Computes Ollivier-Ricci curvature via entropic-regularized optimal transport
/// (Sinkhorn-Knopp algorithm) using CSR sparse graph format.
///
/// ARCHITECTURE: Split-kernel design for TDR safety
/// ================================================
/// Unlike the monolithic kernels in GravityShadersDouble.cs, this implementation
/// splits the Sinkhorn iteration into separate GPU dispatches:
///
///   CPU dispatcher loop (50–100 iterations):
///     1. Dispatch CsrSinkhornUpdateUKernel  (u ← μ / (K·v))
///     2. Dispatch CsrSinkhornUpdateVKernel  (v ← ν / (Kᵀ·u))
///   Then:
///     3. Dispatch CsrSinkhornTransportCostKernel  (W₁, κ)
///
/// Each dispatch is O(maxSupport²) per edge — well under TDR limits.
/// The CPU controls iteration count and can check convergence between dispatches.
///
/// MATH:
/// =====
/// For edge e = (u,v) with weight d(u,v):
///   μ = lazy random walk on N(u) ∪ {u}
///   ν = lazy random walk on N(v) ∪ {v}
///   K[a,b] = exp(-C[a,b] / ε)  (Gibbs kernel)
///   C[a,b] = weighted 2-hop path approximation
///   Sinkhorn: u_s ← μ / (K·v_s),  v_s ← ν / (Kᵀ·u_s)
///   Transport plan: T[a,b] = u_s[a]·K[a,b]·v_s[b]
///   W₁ ≈ ⟨T, C⟩ = Σ T[a,b]·C[a,b]
///   κ(u,v) = 1 - W₁ / d(u,v)
///
/// BUFFER LAYOUT:
/// ==============
/// Per-edge scratch buffers are indexed as: edge_index * maxSupport + local_index
/// maxSupport = maxNeighbors + 1 (neighbors + self-loop node)
///
/// Required GPU buffers (allocated by CPU dispatcher):
///   sinkhornU:  ReadWriteBuffer&lt;double&gt;  size = edgeCount × maxSupport
///   sinkhornV:  ReadWriteBuffer&lt;double&gt;  size = edgeCount × maxSupport
///   curvatures: ReadWriteBuffer&lt;double&gt;  size = edgeCount
///
/// PARAMETERS:
/// ===========
/// epsilon (ε): Entropic regularization. Range 0.01–1.0. Default 0.1.
///   Smaller → more accurate W₁ but slower convergence and numerical instability.
///   Larger → faster convergence but over-regularized (biased toward uniform transport).
///   Recommended: 10.0–50.0 for lambda = 1/ε convention. Here we use ε directly.
///
/// lazyWalkAlpha (α): Probability of staying at current node. Range 0.0–1.0. Default 0.1.
///
/// maxNeighbors: Maximum neighborhood size per node. Default 64.
///   Capped at 128 for TDR safety. Nodes with higher degree are truncated.
///
/// PRECISION: double (64-bit). Requires [RequiresDoublePrecisionSupport].
/// </summary>

// ============================================================
// KERNEL 1: Initialize Sinkhorn scaling vectors
// ============================================================

/// <summary>
/// Initializes Sinkhorn scaling vectors for all edges.
/// Sets v[b] = 1.0 for all support indices (standard Sinkhorn initialization).
/// Must be dispatched once before the U/V iteration loop.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSinkhornInitKernel : IComputeShader
{
    /// <summary>Per-edge Sinkhorn left scaling vector. Size: edgeCount × maxSupport.</summary>
    public readonly ReadWriteBuffer<double> sinkhornU;

    /// <summary>Per-edge Sinkhorn right scaling vector. Size: edgeCount × maxSupport.</summary>
    public readonly ReadWriteBuffer<double> sinkhornV;

    /// <summary>CSR row offsets for degree lookup.</summary>
    public readonly ReadOnlyBuffer<int> csrOffsets;

    /// <summary>Edge source nodes.</summary>
    public readonly ReadOnlyBuffer<int> edgesFrom;

    /// <summary>Edge target nodes.</summary>
    public readonly ReadOnlyBuffer<int> edgesTo;

    public readonly int edgeCount;
    public readonly int maxSupport;

    public CsrSinkhornInitKernel(
        ReadWriteBuffer<double> sinkhornU,
        ReadWriteBuffer<double> sinkhornV,
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> edgesFrom,
        ReadOnlyBuffer<int> edgesTo,
        int edgeCount,
        int maxSupport)
    {
        this.sinkhornU = sinkhornU;
        this.sinkhornV = sinkhornV;
        this.csrOffsets = csrOffsets;
        this.edgesFrom = edgesFrom;
        this.edgesTo = edgesTo;
        this.edgeCount = edgeCount;
        this.maxSupport = maxSupport;
    }

    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;

        int offset = e * maxSupport;

        // Initialize v = 1.0, u = 0.0
        for (int i = 0; i < maxSupport; i++)
        {
            sinkhornU[offset + i] = 0.0;
            sinkhornV[offset + i] = 1.0;
        }
    }
}

// ============================================================
// KERNEL 2: Sinkhorn Update U
// ============================================================

/// <summary>
/// Sinkhorn left scaling vector update: u[a] = μ[a] / Σ_b K(a,b)·v[b]
///
/// Dispatched iteratively from CPU, alternating with CsrSinkhornUpdateVKernel.
/// Each dispatch is a single Sinkhorn half-iteration across all edges.
///
/// Cost matrix C and Gibbs kernel K are computed on-the-fly using
/// weighted 2-hop path approximation through edge endpoints.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSinkhornUpdateUKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> edgesFrom;
    public readonly ReadOnlyBuffer<int> edgesTo;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> csrWeights;

    /// <summary>Per-edge left scaling vector (output). Size: edgeCount × maxSupport.</summary>
    public readonly ReadWriteBuffer<double> sinkhornU;

    /// <summary>Per-edge right scaling vector (input, from previous V update). Size: edgeCount × maxSupport.</summary>
    public readonly ReadWriteBuffer<double> sinkhornV;

    public readonly int edgeCount;
    public readonly int maxNeighbors;
    public readonly double epsilon;
    public readonly double lazyWalkAlpha;

    public CsrSinkhornUpdateUKernel(
        ReadOnlyBuffer<int> edgesFrom,
        ReadOnlyBuffer<int> edgesTo,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> csrWeights,
        ReadWriteBuffer<double> sinkhornU,
        ReadWriteBuffer<double> sinkhornV,
        int edgeCount,
        int maxNeighbors,
        double epsilon,
        double lazyWalkAlpha)
    {
        this.edgesFrom = edgesFrom;
        this.edgesTo = edgesTo;
        this.edgeWeights = edgeWeights;
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.csrWeights = csrWeights;
        this.sinkhornU = sinkhornU;
        this.sinkhornV = sinkhornV;
        this.edgeCount = edgeCount;
        this.maxNeighbors = maxNeighbors;
        this.epsilon = epsilon;
        this.lazyWalkAlpha = lazyWalkAlpha;
    }

    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;

        int u = edgesFrom[e];
        int v = edgesTo[e];
        double d_uv = edgeWeights[e];

        if (d_uv <= 1e-10) return;

        int startU = csrOffsets[u];
        int endU = csrOffsets[u + 1];
        int startV = csrOffsets[v];
        int endV = csrOffsets[v + 1];

        int effectiveMax = maxNeighbors;
        if (effectiveMax > 128) effectiveMax = 128;

        int degU = endU - startU;
        if (degU > effectiveMax) degU = effectiveMax;
        int degV = endV - startV;
        if (degV > effectiveMax) degV = effectiveMax;

        if (degU == 0 || degV == 0) return;

        int m = degU + 1; // support size for μ
        int n = degV + 1; // support size for ν

        int maxSupport = effectiveMax + 1;
        int offsetU = e * maxSupport;
        int offsetV = e * maxSupport;

        double alpha = lazyWalkAlpha;
        double invEps = 1.0 / epsilon;

        // Normalization for μ: Z_μ = α + (1-α) · Σ w(u, nb_k)
        double normU = alpha;
        for (int i = 0; i < degU; i++)
            normU += (1.0 - alpha) * csrWeights[startU + i];

        // u[a] = μ[a] / Σ_b K(a,b) · v[b]
        for (int a = 0; a < m; a++)
        {
            double mu_a = (a == 0)
                ? (alpha / normU)
                : ((1.0 - alpha) * csrWeights[startU + a - 1] / normU);

            double Kv_sum = 0.0;
            for (int b = 0; b < n; b++)
            {
                double cost_ab = ComputeLocalCost(
                    a, b, u, v, d_uv, startU, degU, startV, degV);
                double K_ab = (double)Hlsl.Exp((float)(-cost_ab * invEps));
                Kv_sum += K_ab * sinkhornV[offsetV + b];
            }

            sinkhornU[offsetU + a] = Kv_sum > 1e-300 ? mu_a / Kv_sum : 0.0;
        }
    }

    /// <summary>
    /// Compute transport cost between support node a of μ and support node b of ν.
    /// Uses weighted 2-hop path approximation through endpoints u and v.
    ///
    /// Support A: A[0]=u, A[1..degU]=neighbors of u.
    /// Support B: B[0]=v, B[1..degV]=neighbors of v.
    ///
    /// Paths considered:
    ///   1. Identity: cost = 0 if A[a] == B[b]
    ///   2. Through u: cost = d(A[a],u) + d(u,B[b]) if B[b] ∈ N(u)
    ///   3. Through v: cost = d(A[a],v) + d(v,B[b]) if A[a] ∈ N(v)
    ///   4. Through u→v: cost = d(A[a],u) + d_uv + d(v,B[b])
    /// Returns minimum of all available paths.
    /// </summary>
    private double ComputeLocalCost(
        int a, int b,
        int u, int v, double d_uv,
        int startU, int degU,
        int startV, int degV)
    {
        int nodeA = (a == 0) ? u : csrNeighbors[startU + a - 1];
        int nodeB = (b == 0) ? v : csrNeighbors[startV + b - 1];

        if (nodeA == nodeB) return 0.0;

        double distAU = (a == 0) ? 0.0 : csrWeights[startU + a - 1];
        double distBV = (b == 0) ? 0.0 : csrWeights[startV + b - 1];

        // Default path: A[a] → u → v → B[b]
        double minCost = distAU + d_uv + distBV;

        // Check if B[b] is a neighbor of u → path A[a] → u → B[b]
        for (int i = 0; i < degU; i++)
        {
            if (csrNeighbors[startU + i] == nodeB)
            {
                double distUB = csrWeights[startU + i];
                double pathU = distAU + distUB;
                if (pathU < minCost) minCost = pathU;
                break;
            }
        }

        // Check if A[a] is a neighbor of v → path A[a] → v → B[b]
        for (int j = 0; j < degV; j++)
        {
            if (csrNeighbors[startV + j] == nodeA)
            {
                double distAV = csrWeights[startV + j];
                double pathV = distAV + distBV;
                if (pathV < minCost) minCost = pathV;
                break;
            }
        }

        return minCost;
    }
}

// ============================================================
// KERNEL 3: Sinkhorn Update V
// ============================================================

/// <summary>
/// Sinkhorn right scaling vector update: v[b] = ν[b] / Σ_a K(a,b)·u[a]
///
/// Dispatched iteratively from CPU, alternating with CsrSinkhornUpdateUKernel.
/// Each dispatch is a single Sinkhorn half-iteration across all edges.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSinkhornUpdateVKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> edgesFrom;
    public readonly ReadOnlyBuffer<int> edgesTo;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> csrWeights;

    /// <summary>Per-edge left scaling vector (input, from previous U update). Size: edgeCount × maxSupport.</summary>
    public readonly ReadWriteBuffer<double> sinkhornU;

    /// <summary>Per-edge right scaling vector (output). Size: edgeCount × maxSupport.</summary>
    public readonly ReadWriteBuffer<double> sinkhornV;

    public readonly int edgeCount;
    public readonly int maxNeighbors;
    public readonly double epsilon;
    public readonly double lazyWalkAlpha;

    public CsrSinkhornUpdateVKernel(
        ReadOnlyBuffer<int> edgesFrom,
        ReadOnlyBuffer<int> edgesTo,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> csrWeights,
        ReadWriteBuffer<double> sinkhornU,
        ReadWriteBuffer<double> sinkhornV,
        int edgeCount,
        int maxNeighbors,
        double epsilon,
        double lazyWalkAlpha)
    {
        this.edgesFrom = edgesFrom;
        this.edgesTo = edgesTo;
        this.edgeWeights = edgeWeights;
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.csrWeights = csrWeights;
        this.sinkhornU = sinkhornU;
        this.sinkhornV = sinkhornV;
        this.edgeCount = edgeCount;
        this.maxNeighbors = maxNeighbors;
        this.epsilon = epsilon;
        this.lazyWalkAlpha = lazyWalkAlpha;
    }

    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;

        int u = edgesFrom[e];
        int v = edgesTo[e];
        double d_uv = edgeWeights[e];

        if (d_uv <= 1e-10) return;

        int startU = csrOffsets[u];
        int endU = csrOffsets[u + 1];
        int startV = csrOffsets[v];
        int endV = csrOffsets[v + 1];

        int effectiveMax = maxNeighbors;
        if (effectiveMax > 128) effectiveMax = 128;

        int degU = endU - startU;
        if (degU > effectiveMax) degU = effectiveMax;
        int degV = endV - startV;
        if (degV > effectiveMax) degV = effectiveMax;

        if (degU == 0 || degV == 0) return;

        int m = degU + 1; // support size for μ
        int n = degV + 1; // support size for ν

        int maxSupport = effectiveMax + 1;
        int offsetU = e * maxSupport;
        int offsetV = e * maxSupport;

        double alpha = lazyWalkAlpha;
        double invEps = 1.0 / epsilon;

        // Normalization for ν: Z_ν = α + (1-α) · Σ w(v, nb_k)
        double normV = alpha;
        for (int j = 0; j < degV; j++)
            normV += (1.0 - alpha) * csrWeights[startV + j];

        // v[b] = ν[b] / Σ_a K(a,b) · u[a]
        for (int b = 0; b < n; b++)
        {
            double nu_b = (b == 0)
                ? (alpha / normV)
                : ((1.0 - alpha) * csrWeights[startV + b - 1] / normV);

            double Ktu_sum = 0.0;
            for (int a = 0; a < m; a++)
            {
                double cost_ab = ComputeLocalCost(
                    a, b, u, v, d_uv, startU, degU, startV, degV);
                double K_ab = (double)Hlsl.Exp((float)(-cost_ab * invEps));
                Ktu_sum += K_ab * sinkhornU[offsetU + a];
            }

            sinkhornV[offsetV + b] = Ktu_sum > 1e-300 ? nu_b / Ktu_sum : 0.0;
        }
    }

    /// <summary>
    /// Compute transport cost between support node a of μ and support node b of ν.
    /// Uses weighted 2-hop path approximation (same logic as UpdateU kernel).
    /// </summary>
    private double ComputeLocalCost(
        int a, int b,
        int u, int v, double d_uv,
        int startU, int degU,
        int startV, int degV)
    {
        int nodeA = (a == 0) ? u : csrNeighbors[startU + a - 1];
        int nodeB = (b == 0) ? v : csrNeighbors[startV + b - 1];

        if (nodeA == nodeB) return 0.0;

        double distAU = (a == 0) ? 0.0 : csrWeights[startU + a - 1];
        double distBV = (b == 0) ? 0.0 : csrWeights[startV + b - 1];

        double minCost = distAU + d_uv + distBV;

        for (int i = 0; i < degU; i++)
        {
            if (csrNeighbors[startU + i] == nodeB)
            {
                double distUB = csrWeights[startU + i];
                double pathU = distAU + distUB;
                if (pathU < minCost) minCost = pathU;
                break;
            }
        }

        for (int j = 0; j < degV; j++)
        {
            if (csrNeighbors[startV + j] == nodeA)
            {
                double distAV = csrWeights[startV + j];
                double pathV = distAV + distBV;
                if (pathV < minCost) minCost = pathV;
                break;
            }
        }

        return minCost;
    }
}

// ============================================================
// KERNEL 4: Transport Cost and Curvature
// ============================================================

/// <summary>
/// Computes the final Wasserstein-1 distance and Ollivier-Ricci curvature
/// from converged Sinkhorn scaling vectors.
///
/// Dispatched once after the U/V iteration loop completes.
///
/// Formula:
///   T[a,b] = u[a] · K(a,b) · v[b]            (transport plan)
///   W₁ = Σ_{a,b} T[a,b] · C[a,b]             (Earth Mover's Distance)
///   κ(u,v) = 1 - W₁ / d(u,v)                 (Ollivier-Ricci curvature)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct CsrSinkhornTransportCostKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> edgesFrom;
    public readonly ReadOnlyBuffer<int> edgesTo;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> csrWeights;

    /// <summary>Converged left scaling vector from Sinkhorn iterations.</summary>
    public readonly ReadWriteBuffer<double> sinkhornU;

    /// <summary>Converged right scaling vector from Sinkhorn iterations.</summary>
    public readonly ReadWriteBuffer<double> sinkhornV;

    /// <summary>Output: Ollivier-Ricci curvature per edge.</summary>
    public readonly ReadWriteBuffer<double> curvatures;

    public readonly int edgeCount;
    public readonly int maxNeighbors;
    public readonly double epsilon;

    public CsrSinkhornTransportCostKernel(
        ReadOnlyBuffer<int> edgesFrom,
        ReadOnlyBuffer<int> edgesTo,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> csrWeights,
        ReadWriteBuffer<double> sinkhornU,
        ReadWriteBuffer<double> sinkhornV,
        ReadWriteBuffer<double> curvatures,
        int edgeCount,
        int maxNeighbors,
        double epsilon)
    {
        this.edgesFrom = edgesFrom;
        this.edgesTo = edgesTo;
        this.edgeWeights = edgeWeights;
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.csrWeights = csrWeights;
        this.sinkhornU = sinkhornU;
        this.sinkhornV = sinkhornV;
        this.curvatures = curvatures;
        this.edgeCount = edgeCount;
        this.maxNeighbors = maxNeighbors;
        this.epsilon = epsilon;
    }

    public void Execute()
    {
        int e = ThreadIds.X;
        if (e >= edgeCount) return;

        int u = edgesFrom[e];
        int v = edgesTo[e];
        double d_uv = edgeWeights[e];

        if (d_uv <= 1e-10)
        {
            curvatures[e] = 0.0;
            return;
        }

        int startU = csrOffsets[u];
        int endU = csrOffsets[u + 1];
        int startV = csrOffsets[v];
        int endV = csrOffsets[v + 1];

        int effectiveMax = maxNeighbors;
        if (effectiveMax > 128) effectiveMax = 128;

        int degU = endU - startU;
        if (degU > effectiveMax) degU = effectiveMax;
        int degV = endV - startV;
        if (degV > effectiveMax) degV = effectiveMax;

        if (degU == 0 || degV == 0)
        {
            curvatures[e] = 0.0;
            return;
        }

        int m = degU + 1;
        int n = degV + 1;

        int maxSupport = effectiveMax + 1;
        int offsetU = e * maxSupport;
        int offsetV = e * maxSupport;

        double invEps = 1.0 / epsilon;

        // W₁ = Σ_{a,b} u[a] · K(a,b) · v[b] · C(a,b)
        double W1 = 0.0;
        for (int a = 0; a < m; a++)
        {
            double u_a = sinkhornU[offsetU + a];
            for (int b = 0; b < n; b++)
            {
                double cost_ab = ComputeLocalCost(
                    a, b, u, v, d_uv, startU, degU, startV, degV);
                double K_ab = (double)Hlsl.Exp((float)(-cost_ab * invEps));
                double transport = u_a * K_ab * sinkhornV[offsetV + b];
                W1 += transport * cost_ab;
            }
        }

        // κ(u,v) = 1 - W₁ / d(u,v)
        curvatures[e] = 1.0 - (W1 / d_uv);
    }

    /// <summary>
    /// Compute transport cost between support node a of μ and support node b of ν.
    /// Uses weighted 2-hop path approximation (same logic as U/V kernels).
    /// </summary>
    private double ComputeLocalCost(
        int a, int b,
        int u, int v, double d_uv,
        int startU, int degU,
        int startV, int degV)
    {
        int nodeA = (a == 0) ? u : csrNeighbors[startU + a - 1];
        int nodeB = (b == 0) ? v : csrNeighbors[startV + b - 1];

        if (nodeA == nodeB) return 0.0;

        double distAU = (a == 0) ? 0.0 : csrWeights[startU + a - 1];
        double distBV = (b == 0) ? 0.0 : csrWeights[startV + b - 1];

        double minCost = distAU + d_uv + distBV;

        for (int i = 0; i < degU; i++)
        {
            if (csrNeighbors[startU + i] == nodeB)
            {
                double distUB = csrWeights[startU + i];
                double pathU = distAU + distUB;
                if (pathU < minCost) minCost = pathU;
                break;
            }
        }

        for (int j = 0; j < degV; j++)
        {
            if (csrNeighbors[startV + j] == nodeA)
            {
                double distAV = csrWeights[startV + j];
                double pathV = distAV + distBV;
                if (pathV < minCost) minCost = pathV;
                break;
            }
        }

        return minCost;
    }
}
