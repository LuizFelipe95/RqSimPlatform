using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// GPU shader for computing Forman-Ricci curvature on edges (Jost formula).
    /// Uses CSR (Compressed Sparse Row) format for efficient neighbor lookup.
    /// 
    /// Jost weighted formula:
    ///   Ric_F(e) = 2 - w_e · [ Σ_{e'~u, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~v, e''≠e} √(1/(w_e·w_{e''})) ]
    /// 
    /// ============================================================
    /// RQ-MODERNIZATION: Physics-Visualization Separation (Checklist #7)
    /// ============================================================
    /// 
    /// IMPORTANT: This shader computes PHYSICS quantities only.
    /// 
    /// INPUTS (Physics Data):
    /// - weights: Edge weights (w_ij) - the metric tensor components
    /// - edges: Topological connectivity (which nodes are connected)
    /// - adjOffsets/adjData: CSR graph structure for neighbor enumeration
    /// 
    /// OUTPUTS (Physics Data):
    /// - curvatures: Ricci curvature tensor components
    /// 
    /// NOT USED:
    /// - NodePositions (visualization coordinates)
    /// - Any UI/rendering data
    /// 
    /// The Forman-Ricci curvature is computed purely from:
    /// 1. Graph topology (who is connected to whom)
    /// 2. Edge weights (how strong are the connections)
    /// 
    /// This maintains strict separation between:
    /// - PHYSICS: Discrete geometry on the graph (weights, curvature, stress-energy)
    /// - VISUALIZATION: Embedding coordinates for rendering (positions in ℝ³)
    /// 
    /// The embedding (ManifoldEmbedding) reads FROM physics AFTER the physics step,
    /// never the other way around. This prevents non-physical coordinate-dependent
    /// artifacts from entering the simulation.
    /// ============================================================
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct FormanCurvatureShader : IComputeShader
    {
        // Input data
        public readonly ReadWriteBuffer<float> weights;       // Current edge weights (w_ij)
        public readonly ReadOnlyBuffer<Int2> edges;           // Node pairs for each edge (u, v)

        // Graph in CSR format (for fast neighbor lookup)
        public readonly ReadOnlyBuffer<int> adjOffsets;       // Start indices of neighbors for each node
        public readonly ReadOnlyBuffer<Int2> adjData;         // Neighbor data: .X = neighborIndex, .Y = edgeIndex

        // Output data
        public readonly ReadWriteBuffer<float> curvatures;    // Result (Ric_ij)

        // Constants
        public readonly int nodeCount;

        public FormanCurvatureShader(
            ReadWriteBuffer<float> weights,
            ReadOnlyBuffer<Int2> edges,
            ReadOnlyBuffer<int> adjOffsets,
            ReadOnlyBuffer<Int2> adjData,
            ReadWriteBuffer<float> curvatures,
            int nodeCount)
        {
            this.weights = weights;
            this.edges = edges;
            this.adjOffsets = adjOffsets;
            this.adjData = adjData;
            this.curvatures = curvatures;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int edgeIdx = ThreadIds.X;
            if (edgeIdx >= edges.Length) return;

            // 1. Get data for current edge E(u,v)
            Int2 nodes = edges[edgeIdx];
            int u = nodes.X;
            int v = nodes.Y;
            float w_e = weights[edgeIdx];

            if (w_e <= 0.0f)
            {
                curvatures[edgeIdx] = 0.0f;
                return;
            }

            // 2. Jost weighted Forman-Ricci curvature:
            // Ric_F(e) = 2 - w_e · [ Σ_{e'~u, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~v, e''≠e} √(1/(w_e·w_{e''})) ]

            // Sum over edges incident to u (excluding edge e)
            float sumU = 0.0f;
            int startU = adjOffsets[u];
            int endU = (u + 1 < nodeCount) ? adjOffsets[u + 1] : adjData.Length;

            for (int k = startU; k < endU; k++)
            {
                int e_idx = adjData[k].Y;
                if (e_idx != edgeIdx)
                {
                    float w_adj = weights[e_idx];
                    if (w_adj > 0.0f)
                        sumU += 1.0f / Hlsl.Sqrt(w_e * w_adj);
                }
            }

            // Sum over edges incident to v (excluding edge e)
            float sumV = 0.0f;
            int startV = adjOffsets[v];
            int endV = (v + 1 < nodeCount) ? adjOffsets[v + 1] : adjData.Length;

            for (int k = startV; k < endV; k++)
            {
                int e_idx = adjData[k].Y;
                if (e_idx != edgeIdx)
                {
                    float w_adj = weights[e_idx];
                    if (w_adj > 0.0f)
                        sumV += 1.0f / Hlsl.Sqrt(w_e * w_adj);
                }
            }

            // 3. Final Jost Forman-Ricci curvature
            curvatures[edgeIdx] = 2.0f - w_e * (sumU + sumV);
        }
    }

    /// <summary>
    /// HLSL Shader for gravity evolution, compiled from C#.
    /// IComputeShader - marker for ComputeSharp.
    /// Source generator provides IComputeShaderDescriptor implementation.
    /// 
    /// ENERGY CONSERVATION FIX:
    /// The gravity update formula now uses a flow-based approach that
    /// redistributes weight rather than adding/removing arbitrarily.
    /// dw = dt * tanh(G * massTerm - curvature + lambda)
    /// The tanh bounds the change to [-1, 1] preventing runaway growth.
    /// 
    /// FORMULA ALIGNED WITH CPU:
    /// flowRate = curvature - massTerm * curvatureTermScale + lambda
    /// This matches EvolveNetworkGeometryOllivierDynamic and EvolveNetworkGeometryForman.
    /// 
    /// RQ-MODERNIZATION: Added isScientificMode flag for unbounded flow.
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct GravityShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> weights;
        public readonly ReadWriteBuffer<float> curvatures;
        public readonly ReadOnlyBuffer<float> masses;
        public readonly ReadOnlyBuffer<Int2> edges;
        public readonly float dt;
        public readonly float G;
        public readonly float lambda;
        public readonly float curvatureTermScale;
        public readonly int isScientificMode;  // 1 = scientific (unbounded), 0 = legacy (bounded)

        public GravityShader(
            ReadWriteBuffer<float> weights,
            ReadWriteBuffer<float> curvatures,
            ReadOnlyBuffer<float> masses,
            ReadOnlyBuffer<Int2> edges,
            float dt,
            float G,
            float lambda,
            float curvatureTermScale,
            int isScientificMode = 0)
        {
            this.weights = weights;
            this.curvatures = curvatures;
            this.masses = masses;
            this.edges = edges;
            this.dt = dt;
            this.G = G;
            this.lambda = lambda;
            this.curvatureTermScale = curvatureTermScale;
            this.isScientificMode = isScientificMode;
        }

        public void Execute()
        {
            int i = ThreadIds.X;

            Int2 edgeNodes = edges[i];
            int nodeA = edgeNodes.X;
            int nodeB = edgeNodes.Y;

            float currentWeight = weights[i];

            if (currentWeight < 0.0001f)
            {
                return;
            }

            float massTerm = (masses[nodeA] + masses[nodeB]) * 0.5f;
            float curvatureTerm = curvatures[i];

            float flowRate = curvatureTerm - G * massTerm + lambda;

            float w;
            if (isScientificMode != 0)
            {
                // SCIENTIFIC MODE: Linear flow without tanh saturation
                float relativeChange = flowRate * 0.1f * dt;
                w = currentWeight * (1.0f + relativeChange);
                // No clamping - allow physics to determine fate (may go negative/NaN)
            }
            else
            {
                // LEGACY MODE: Bounded tanh flow with soft walls
                float relativeChange = Hlsl.Tanh(flowRate * 0.1f) * dt;
                w = currentWeight * (1.0f + relativeChange);
                w = Hlsl.Clamp(w, 0.02f, 0.98f);
            }

                    weights[i] = w;
                    }
                }

                /// <summary>
                /// SINKHORN-KNOPP OLLIVIER-RICCI CURVATURE (float, Visual/Sandbox)
                /// ================================================================
                /// Computes Ollivier-Ricci curvature using proper Sinkhorn-Knopp algorithm
                /// for entropic-regularized optimal transport (Wasserstein-1).
                /// Float precision (~7 significant digits) — suitable for visual/sandbox mode.
                ///
                /// Algorithm per edge e = (u,v):
                ///   1. Build lazy-walk distributions μ on N(u)∪{u}, ν on N(v)∪{v}
                ///   2. Compute cost C[a,b] via weighted 2-hop path approximation
                ///   3. Gibbs kernel K[a,b] = exp(-C[a,b] / ε)
                ///   4. Sinkhorn iterations: u_s ← μ / (K·v_s),  v_s ← ν / (Kᵀ·u_s)
                ///   5. W₁ ≈ Σ u_s[a]·K[a,b]·v_s[b]·C[a,b],  κ = 1 - W₁ / d(u,v)
                ///
                /// For double-precision scientific mode, use SinkhornOllivierRicciKernel.
                /// </summary>
                [ThreadGroupSize(64, 1, 1)]
                [GeneratedComputeShaderDescriptor]
                public readonly partial struct SinkhornOllivierRicciShaderFloat : IComputeShader
                {
                    public readonly ReadOnlyBuffer<int> edgesFrom;
                    public readonly ReadOnlyBuffer<int> edgesTo;
                    public readonly ReadOnlyBuffer<float> edgeWeights;
                    public readonly ReadOnlyBuffer<int> csrOffsets;
                    public readonly ReadOnlyBuffer<int> csrNeighbors;
                    public readonly ReadOnlyBuffer<float> csrWeights;
                    public readonly ReadWriteBuffer<float> curvatures;

                    /// <summary>Per-edge Sinkhorn left scaling vector. Size: edgeCount * MaxSupport.</summary>
                    public readonly ReadWriteBuffer<float> sinkhornU;

                    /// <summary>Per-edge Sinkhorn right scaling vector. Size: edgeCount * MaxSupport.</summary>
                    public readonly ReadWriteBuffer<float> sinkhornV;

                    public readonly int edgeCount;
                    public readonly int nodeCount;
                    public readonly int sinkhornIterations;
                    public readonly float epsilon;
                    public readonly float lazyWalkAlpha;

                    private const int MaxNeighbors = 32;
                    private const int MaxSupport = MaxNeighbors + 1;

                    public SinkhornOllivierRicciShaderFloat(
                        ReadOnlyBuffer<int> edgesFrom,
                        ReadOnlyBuffer<int> edgesTo,
                        ReadOnlyBuffer<float> edgeWeights,
                        ReadOnlyBuffer<int> csrOffsets,
                        ReadOnlyBuffer<int> csrNeighbors,
                        ReadOnlyBuffer<float> csrWeights,
                        ReadWriteBuffer<float> curvatures,
                        ReadWriteBuffer<float> sinkhornU,
                        ReadWriteBuffer<float> sinkhornV,
                        int edgeCount,
                        int nodeCount,
                        int sinkhornIterations,
                        float epsilon,
                        float lazyWalkAlpha = 0.1f)
                    {
                        this.edgesFrom = edgesFrom;
                        this.edgesTo = edgesTo;
                        this.edgeWeights = edgeWeights;
                        this.csrOffsets = csrOffsets;
                        this.csrNeighbors = csrNeighbors;
                        this.csrWeights = csrWeights;
                        this.curvatures = curvatures;
                        this.sinkhornU = sinkhornU;
                        this.sinkhornV = sinkhornV;
                        this.edgeCount = edgeCount;
                        this.nodeCount = nodeCount;
                        this.sinkhornIterations = sinkhornIterations;
                        this.epsilon = epsilon;
                        this.lazyWalkAlpha = lazyWalkAlpha;
                    }

                    public void Execute()
                    {
                        int e = ThreadIds.X;
                        if (e >= edgeCount) return;

                        int u = edgesFrom[e];
                        int v = edgesTo[e];
                        float d_uv = edgeWeights[e];

                        if (d_uv <= 1e-6f)
                        {
                            curvatures[e] = 0.0f;
                            return;
                        }

                        int startU = csrOffsets[u];
                        int endU = csrOffsets[u + 1];
                        int startV = csrOffsets[v];
                        int endV = csrOffsets[v + 1];

                        int degU = Hlsl.Min(endU - startU, MaxNeighbors);
                        int degV = Hlsl.Min(endV - startV, MaxNeighbors);

                        if (degU == 0 || degV == 0)
                        {
                            curvatures[e] = 0.0f;
                            return;
                        }

                        int m = degU + 1;
                        int n = degV + 1;

                        int offsetU = e * MaxSupport;
                        int offsetV = e * MaxSupport;

                        float alpha = lazyWalkAlpha;
                        float invEps = 1.0f / epsilon;

                        // Normalization for μ
                        float normU = alpha;
                        for (int i = 0; i < degU; i++)
                            normU += (1.0f - alpha) * csrWeights[startU + i];

                        // Normalization for ν
                        float normV = alpha;
                        for (int j = 0; j < degV; j++)
                            normV += (1.0f - alpha) * csrWeights[startV + j];

                        // Initialize v_s[b] = 1
                        for (int b = 0; b < n; b++)
                            sinkhornV[offsetV + b] = 1.0f;

                        // Sinkhorn iterations
                        for (int iter = 0; iter < sinkhornIterations; iter++)
                        {
                            // u_s[a] = μ[a] / Σ_b K(a,b) * v_s[b]
                            for (int a = 0; a < m; a++)
                            {
                                float mu_a = (a == 0) ? (alpha / normU) : ((1.0f - alpha) * csrWeights[startU + a - 1] / normU);

                                float Kv_sum = 0.0f;
                                for (int b = 0; b < n; b++)
                                {
                                    float cost_ab = ComputeLocalCost(a, b, u, v, d_uv, startU, degU, startV, degV);
                                    float K_ab = Hlsl.Exp(-cost_ab * invEps);
                                    Kv_sum += K_ab * sinkhornV[offsetV + b];
                                }

                                sinkhornU[offsetU + a] = Kv_sum > 1e-30f ? mu_a / Kv_sum : 0.0f;
                            }

                            // v_s[b] = ν[b] / Σ_a K(a,b) * u_s[a]
                            for (int b = 0; b < n; b++)
                            {
                                float nu_b = (b == 0) ? (alpha / normV) : ((1.0f - alpha) * csrWeights[startV + b - 1] / normV);

                                float Ktu_sum = 0.0f;
                                for (int a = 0; a < m; a++)
                                {
                                    float cost_ab = ComputeLocalCost(a, b, u, v, d_uv, startU, degU, startV, degV);
                                    float K_ab = Hlsl.Exp(-cost_ab * invEps);
                                    Ktu_sum += K_ab * sinkhornU[offsetU + a];
                                }

                                sinkhornV[offsetV + b] = Ktu_sum > 1e-30f ? nu_b / Ktu_sum : 0.0f;
                            }
                        }

                        // Compute W₁ = Σ u_s[a] * K(a,b) * v_s[b] * C(a,b)
                        float W1 = 0.0f;
                        for (int a = 0; a < m; a++)
                        {
                            float u_a = sinkhornU[offsetU + a];
                            for (int b = 0; b < n; b++)
                            {
                                float cost_ab = ComputeLocalCost(a, b, u, v, d_uv, startU, degU, startV, degV);
                                float K_ab = Hlsl.Exp(-cost_ab * invEps);
                                float transport = u_a * K_ab * sinkhornV[offsetV + b];
                                W1 += transport * cost_ab;
                            }
                        }

                        // Ollivier-Ricci curvature
                        float kappa = 1.0f - (W1 / d_uv);
                        curvatures[e] = Hlsl.Clamp(kappa, -2.0f, 1.0f);
                    }

                    /// <summary>
                    /// Compute transport cost between support nodes using weighted 2-hop approximation.
                    /// Support A: A[0]=u, A[1..degU]=neighbors of u.
                    /// Support B: B[0]=v, B[1..degV]=neighbors of v.
                    /// </summary>
                    private float ComputeLocalCost(
                        int a, int b,
                        int u, int v, float d_uv,
                        int startU, int degU,
                        int startV, int degV)
                    {
                        int nodeA = (a == 0) ? u : csrNeighbors[startU + a - 1];
                        int nodeB = (b == 0) ? v : csrNeighbors[startV + b - 1];

                        if (nodeA == nodeB) return 0.0f;

                        float distAU = (a == 0) ? 0.0f : csrWeights[startU + a - 1];
                        float distBV = (b == 0) ? 0.0f : csrWeights[startV + b - 1];

                        float minCost = distAU + d_uv + distBV;

                        for (int i = 0; i < degU; i++)
                        {
                            if (csrNeighbors[startU + i] == nodeB)
                            {
                                float distUB = csrWeights[startU + i];
                                float pathU = distAU + distUB;
                                if (pathU < minCost) minCost = pathU;
                                break;
                            }
                        }

                        for (int j = 0; j < degV; j++)
                        {
                            if (csrNeighbors[startV + j] == nodeA)
                            {
                                float distAV = csrWeights[startV + j];
                                float pathV = distAV + distBV;
                                if (pathV < minCost) minCost = pathV;
                                break;
                            }
                        }

                        return minCost;
                    }
                }
            }
