using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// GPU shader that computes Jost Forman-Ricci curvature AND updates weights in one pass (fused).
    /// Reduces kernel launch overhead by 50%.
    /// 
    /// Jost weighted formula:
    ///   Ric_F(e) = 2 - w_e · [ Σ_{e'~u, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~v, e''≠e} √(1/(w_e·w_{e''})) ]
    /// 
    /// ENERGY CONSERVATION: Uses multiplicative update with tanh-bounded flow rate.
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct FusedCurvatureGravityShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> weights;
        public readonly ReadOnlyBuffer<Int2> edges;
        public readonly ReadOnlyBuffer<int> adjOffsets;
        public readonly ReadOnlyBuffer<Int2> adjData;
        public readonly ReadOnlyBuffer<float> masses;
        public readonly float dt;
        public readonly float G;
        public readonly float lambda;
        public readonly int nodeCount;

        public FusedCurvatureGravityShader(
            ReadWriteBuffer<float> weights,
            ReadOnlyBuffer<Int2> edges,
            ReadOnlyBuffer<int> adjOffsets,
            ReadOnlyBuffer<Int2> adjData,
            ReadOnlyBuffer<float> masses,
            float dt, float G, float lambda, int nodeCount)
        {
            this.weights = weights;
            this.edges = edges;
            this.adjOffsets = adjOffsets;
            this.adjData = adjData;
            this.masses = masses;
            this.dt = dt;
            this.G = G;
            this.lambda = lambda;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int edgeIdx = ThreadIds.X;
            if (edgeIdx >= edges.Length) return;

            Int2 nodes = edges[edgeIdx];
            int u = nodes.X;
            int v = nodes.Y;
            float w_e = weights[edgeIdx];

            // Skip very weak edges - use consistent threshold
            if (w_e <= 0.02f)
            {
                return;
            }

            // === CURVATURE COMPUTATION (Jost Forman-Ricci) ===
            // Ric_F(e) = 2 - w_e · [ Σ_{e'~u, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~v, e''≠e} √(1/(w_e·w_{e''})) ]

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

            float curvature = 2.0f - w_e * (sumU + sumV);

            // === GRAVITY UPDATE (ENERGY-CONSERVING) ===
            float massTerm = (masses[u] + masses[v]) * 0.5f;

            // Flow rate based on Einstein equation
            // Matches CPU: flowRate = curvature - effectiveG * massTerm + lambda
            float flowRate = curvature - G * massTerm + lambda;

            // ENERGY CONSERVATION: Multiplicative update with bounded rate
            float relativeChange = Hlsl.Tanh(flowRate * 0.1f) * dt;
            float w = w_e * (1.0f + relativeChange);

            // Clamp to match PhysicsConstants: WeightLowerSoftWall=0.02, WeightUpperSoftWall=0.98
            weights[edgeIdx] = Hlsl.Clamp(w, 0.02f, 0.98f);
        }
    }

    /// <summary>
    /// GPU shader for U(1) gauge phase evolution.
    /// 
    /// Phase evolution: d?/dt = -g * (J + curl)
    /// where:
    /// - J = current from matter fields (density difference)
    /// - curl = plaquette contribution (field strength)
    /// 
    /// PHYSICS: Preserves gauge invariance via compact U(1) (mod 2?)
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct GaugePhaseEvolutionShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> phases;           // Edge phases ?_ij
        public readonly ReadOnlyBuffer<Int2> edges;              // Edge endpoints
        public readonly ReadOnlyBuffer<float> weights;           // Edge weights w_ij
        public readonly ReadOnlyBuffer<float> scalarField;       // ?_i scalar field
        public readonly ReadOnlyBuffer<int> nodeStates;          // 1=excited, 0=rest
        public readonly ReadOnlyBuffer<int> adjOffsets;          // CSR offsets
        public readonly ReadOnlyBuffer<Int2> adjData;            // (neighbor, edgeIdx) pairs
        public readonly float dt;
        public readonly float gaugeCoupling;                     // g coupling constant
        public readonly float plaquetteWeight;                   // Weight of curl term
        public readonly int nodeCount;

        public GaugePhaseEvolutionShader(
            ReadWriteBuffer<float> phases,
            ReadOnlyBuffer<Int2> edges,
            ReadOnlyBuffer<float> weights,
            ReadOnlyBuffer<float> scalarField,
            ReadOnlyBuffer<int> nodeStates,
            ReadOnlyBuffer<int> adjOffsets,
            ReadOnlyBuffer<Int2> adjData,
            float dt, float gaugeCoupling, float plaquetteWeight, int nodeCount)
        {
            this.phases = phases;
            this.edges = edges;
            this.weights = weights;
            this.scalarField = scalarField;
            this.nodeStates = nodeStates;
            this.adjOffsets = adjOffsets;
            this.adjData = adjData;
            this.dt = dt;
            this.gaugeCoupling = gaugeCoupling;
            this.plaquetteWeight = plaquetteWeight;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int edgeIdx = ThreadIds.X;
            if (edgeIdx >= edges.Length) return;

            Int2 ep = edges[edgeIdx];
            int i = ep.X;
            int j = ep.Y;
            float w_ij = weights[edgeIdx];

            if (w_ij <= 0.02f) return; // Skip weak edges

            // === CURRENT COMPUTATION ===
            // J = (?_i - ?_j) * w_ij where ? is density from node state
            float density_i = nodeStates[i] == 1 ? 1.0f : 0.0f;
            float density_j = nodeStates[j] == 1 ? 1.0f : 0.0f;
            float fermionCurrent = (density_i - density_j) * w_ij;

            // Scalar field back-reaction: J_scalar = g * ?_i * ?_j * sin(?_ij)
            float phi_i = scalarField[i];
            float phi_j = scalarField[j];
            float theta_ij = phases[edgeIdx];
            float scalarCurrent = 0.1f * phi_i * phi_j * Hlsl.Sin(theta_ij);

            float totalCurrent = fermionCurrent + scalarCurrent;

            // === PLAQUETTE (CURL) CONTRIBUTION ===
            // Find triangles through this edge: i-j-k
            float curvatureSum = 0.0f;
            int plaquetteCount = 0;

            int startI = adjOffsets[i];
            int endI = (i + 1 < nodeCount) ? adjOffsets[i + 1] : adjData.Length;
            int startJ = adjOffsets[j];
            int endJ = (j + 1 < nodeCount) ? adjOffsets[j + 1] : adjData.Length;

            // Look for common neighbors (triangles)
            for (int ki = startI; ki < endI; ki++)
            {
                int k = adjData[ki].X;
                if (k == j) continue;
                int edge_ik = adjData[ki].Y;

                // Check if k is also neighbor of j
                for (int kj = startJ; kj < endJ; kj++)
                {
                    if (adjData[kj].X == k)
                    {
                        int edge_jk = adjData[kj].Y;

                        // Plaquette phase sum: ?_ij + ?_jk + ?_ki
                        // Note: edge storage may have opposite sign, use carefully
                        float phase_ij = phases[edgeIdx];
                        float phase_jk = phases[edge_jk];
                        float phase_ki = -phases[edge_ik]; // k->i is opposite of i->k

                        float phaseSum = phase_ij + phase_jk + phase_ki;
                        curvatureSum += Hlsl.Sin(phaseSum);
                        plaquetteCount++;
                        break;
                    }
                }
            }

            float avgCurvature = plaquetteCount > 0 ? curvatureSum / plaquetteCount : 0.0f;

            // === PHASE UPDATE ===
            // d?/dt = -g * (J + w_plaq * curl)
            float dPhase = -gaugeCoupling * (totalCurrent + plaquetteWeight * avgCurvature) * dt;

            float newPhase = phases[edgeIdx] + dPhase;

            // Wrap to [-?, ?] (compact U(1))
            const float PI = 3.14159265f;
            while (newPhase > PI) newPhase -= 2.0f * PI;
            while (newPhase < -PI) newPhase += 2.0f * PI;

            phases[edgeIdx] = newPhase;
        }
    }

    /// <summary>
    /// GPU shader for computing time dilation factors (lapse function).
    /// 
    /// N_i = 1 / sqrt(1 + ?*m_i + ?*|R_i|)
    /// 
    /// Higher mass/curvature ? slower local time (gravitational time dilation)
    /// This is used for asynchronous (event-driven) physics updates.
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct TimeDilationShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> timeDilation;    // N_i output
        public readonly ReadOnlyBuffer<float> nodeMasses;       // m_i correlation mass
        public readonly ReadOnlyBuffer<float> edgeCurvatures;   // R_e curvatures
        public readonly ReadOnlyBuffer<int> adjOffsets;         // CSR offsets
        public readonly ReadOnlyBuffer<Int2> adjData;           // (neighbor, edgeIdx) pairs
        public readonly float massScale;                        // Normalization for mass
        public readonly float curvatureScale;                   // Normalization for curvature
        public readonly int nodeCount;

        public TimeDilationShader(
            ReadWriteBuffer<float> timeDilation,
            ReadOnlyBuffer<float> nodeMasses,
            ReadOnlyBuffer<float> edgeCurvatures,
            ReadOnlyBuffer<int> adjOffsets,
            ReadOnlyBuffer<Int2> adjData,
            float massScale, float curvatureScale, int nodeCount)
        {
            this.timeDilation = timeDilation;
            this.nodeMasses = nodeMasses;
            this.edgeCurvatures = edgeCurvatures;
            this.adjOffsets = adjOffsets;
            this.adjData = adjData;
            this.massScale = massScale;
            this.curvatureScale = curvatureScale;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;

            // Get mass contribution
            float mass = nodeMasses[i];

            // Compute average incident edge curvature
            float avgCurvature = 0.0f;
            int edgeCount = 0;

            int start = adjOffsets[i];
            int end = (i + 1 < nodeCount) ? adjOffsets[i + 1] : adjData.Length;

            for (int k = start; k < end; k++)
            {
                int edgeIdx = adjData[k].Y;
                avgCurvature += Hlsl.Abs(edgeCurvatures[edgeIdx]);
                edgeCount++;
            }

            if (edgeCount > 0)
                avgCurvature /= edgeCount;

            // Time dilation factor: N = 1/sqrt(1 + ?*m + ?*|R|)
            float factor = 1.0f + mass / massScale + avgCurvature / curvatureScale;
            factor = Hlsl.Max(factor, 0.01f); // Prevent division by zero

            float N = 1.0f / Hlsl.Sqrt(factor);

            // Clamp to reasonable range [0.1, 1.0]
            timeDilation[i] = Hlsl.Clamp(N, 0.1f, 1.0f);
        }
    }

    /// <summary>
    /// GPU shader for node excitation state updates (parallel-safe).
    /// 
    /// Uses graph coloring to ensure no race conditions:
    /// - Only nodes of the same color are updated in parallel
    /// - Neighbors are guaranteed to have different colors
    /// 
    /// PHYSICS: Implements local excitation dynamics with proper time
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct NodeStateUpdateShader : IComputeShader
    {
        public readonly ReadWriteBuffer<int> nodeStates;        // 0=rest, 1=excited, 2=refractory
        public readonly ReadWriteBuffer<int> refractoryCounts;  // Steps remaining in refractory
        public readonly ReadOnlyBuffer<float> weights;          // Edge weights
        public readonly ReadOnlyBuffer<int> csrOffsets;         // CSR offsets
        public readonly ReadOnlyBuffer<int> csrNeighbors;       // CSR neighbor indices
        public readonly ReadOnlyBuffer<int> csrEdgeIndices;     // CSR to edge index mapping
        public readonly ReadOnlyBuffer<float> timeDilation;     // Lapse function N_i
        public readonly ReadOnlyBuffer<int> colorMask;          // 1 if node should update this round
        public readonly float excitationThreshold;              // Threshold for state transition
        public readonly int refractoryDuration;                 // Base refractory steps
        public readonly int nodeCount;

        public NodeStateUpdateShader(
            ReadWriteBuffer<int> nodeStates,
            ReadWriteBuffer<int> refractoryCounts,
            ReadOnlyBuffer<float> weights,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            ReadOnlyBuffer<int> csrEdgeIndices,
            ReadOnlyBuffer<float> timeDilation,
            ReadOnlyBuffer<int> colorMask,
            float excitationThreshold, int refractoryDuration, int nodeCount)
        {
            this.nodeStates = nodeStates;
            this.refractoryCounts = refractoryCounts;
            this.weights = weights;
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.csrEdgeIndices = csrEdgeIndices;
            this.timeDilation = timeDilation;
            this.colorMask = colorMask;
            this.excitationThreshold = excitationThreshold;
            this.refractoryDuration = refractoryDuration;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;

            // Only update nodes of current color
            if (colorMask[i] != 1) return;

            int state = nodeStates[i];

            // Handle refractory state
            if (state == 2) // Refractory
            {
                int remaining = refractoryCounts[i] - 1;
                if (remaining <= 0)
                {
                    nodeStates[i] = 0; // Return to rest
                    refractoryCounts[i] = 0;
                }
                else
                {
                    refractoryCounts[i] = remaining;
                }
                return;
            }

            // Handle excited state -> transition to refractory
            if (state == 1) // Excited
            {
                nodeStates[i] = 2; // Become refractory
                // Scale refractory duration by time dilation (slower time = longer refractory)
                float N = timeDilation[i];
                int scaledDuration = (int)(refractoryDuration / N + 0.5f);
                refractoryCounts[i] = Hlsl.Max(scaledDuration, 1);
                return;
            }

            // Handle rest state -> check for excitation
            // Count weighted excitation from neighbors
            float weightedExcitation = 0.0f;

            int start = csrOffsets[i];
            int end = (i + 1 < nodeCount) ? csrOffsets[i + 1] : csrNeighbors.Length;

            for (int k = start; k < end; k++)
            {
                int j = csrNeighbors[k];
                if (nodeStates[j] == 1) // Neighbor is excited
                {
                    int edgeIdx = csrEdgeIndices[k];
                    weightedExcitation += weights[edgeIdx];
                }
            }

            // Excite if threshold exceeded (scaled by local proper time)
            float localThreshold = excitationThreshold / timeDilation[i];
            if (weightedExcitation > localThreshold)
            {
                nodeStates[i] = 1; // Become excited
            }
        }
    }
}
