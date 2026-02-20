using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Quantum Graphity dynamics: Hamiltonian and energy calculations.
    /// </summary>
    public partial class RQGraph
    {
        // Network Hamiltonian parameters
        private double _linkCostCoeff = 0.1;    // Penalty for having links
        private double _lengthCostCoeff = 0.05; // Penalty for long links
        private double _matterCouplingCoeff = 0.2; // Matter-geometry coupling

        /// <summary>
        /// Cosmological constant (Lambda) - prevents graph collapse/explosion.
        /// Positive Lambda favors expansion (de Sitter), negative favors contraction.
        /// The term Lambda * V is added to the Hamiltonian where V is effective volume.
        /// REDUCED from 0.01 to 0.001 to prevent excessive edge deletion.
        /// </summary>
        private double _cosmologicalConstant = 0.001;

        /// <summary>
        /// Gets or sets the cosmological constant (Lambda).
        /// Positive values resist collapse, negative values resist expansion.
        /// </summary>
        public double CosmologicalConstant
        {
            get => _cosmologicalConstant;
            set => _cosmologicalConstant = value;
        }

        // ============================================================
        // RQ-HYPOTHESIS STAGE 2: WHEELER-DEWITT CONSTRAINT
        // ============================================================
        
        /// <summary>
        /// RQ-HYPOTHESIS: Wheeler-DeWitt Constraint
        /// H_local = H_gravity + ? * H_matter ? 0
        /// 
        /// PHYSICS:
        /// - In quantum gravity, the Hamiltonian generates diffeomorphisms
        /// - Physical states satisfy H|?? = 0 (frozen time)
        /// - We compute the local constraint violation at each node
        /// 
        /// Returns the squared violation (constraint? ? 0).
        /// </summary>
        /// <param name="nodeId">Node index to compute constraint for</param>
        /// <returns>Squared constraint violation at this node</returns>
        public double CalculateWheelerDeWittConstraint(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                throw new ArgumentOutOfRangeException(nameof(nodeId));
            
            // H_geometry from local Ricci scalar curvature
            double H_geom = GetLocalCurvature(nodeId);
            
            // H_matter from stress-energy tensor T_00 (correlation mass)
            double H_matter = 0.0;
            if (_correlationMass != null && nodeId < _correlationMass.Length)
            {
                H_matter = _correlationMass[nodeId];
            }
            
            // Wheeler-DeWitt constraint: (H_G - ? * H_M) should be zero
            // Sign convention: geometry = matter (Einstein equation)
            double kappa = PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling;
            double constraint = H_geom - kappa * H_matter;
            
            // Return squared violation for use in energy functional
            return constraint * constraint;
        }
        
        /// <summary>
        /// Total Wheeler-DeWitt constraint violation for the entire graph.
        /// Used in MCMC sampling - prefer configurations with lower violation.
        /// 
        /// Returns normalized violation (per-node average).
        /// </summary>
        public double CalculateTotalConstraintViolation()
        {
            if (N == 0) return 0.0;
            
            double total = 0.0;
            for (int i = 0; i < N; i++)
            {
                total += CalculateWheelerDeWittConstraint(i);
            }
            return total / N; // Normalized by node count
        }
        
        /// <summary>
        /// Check if the graph configuration satisfies Wheeler-DeWitt constraint.
        /// Returns true if the total constraint violation is below tolerance.
        /// </summary>
        public bool SatisfiesWheelerDeWittConstraint()
        {
            double violation = CalculateTotalConstraintViolation();
            return violation < PhysicsConstants.WheelerDeWittConstants.ConstraintTolerance;
        }
        
        /// <summary>
        /// Constraint-weighted Hamiltonian for Metropolis acceptance.
        /// Includes penalty for violating Wheeler-DeWitt.
        /// 
        /// H_effective = H_standard + ? * (constraint violation)
        /// 
        /// This ensures that MCMC sampling prefers configurations
        /// that satisfy the Wheeler-DeWitt equation.
        /// </summary>
        public double ComputeConstraintWeightedHamiltonian()
        {
            double H_standard = ComputeNetworkHamiltonian();
            double constraint = CalculateTotalConstraintViolation();
            
            // Lagrange multiplier for constraint enforcement
            double lambda = PhysicsConstants.WheelerDeWittConstants.ConstraintLagrangeMultiplier;
            
            return H_standard + lambda * constraint;
        }
        
        /// <summary>
        /// Compute local constraint contribution for use in local Metropolis moves.
        /// More efficient than recalculating total constraint for each move.
        /// </summary>
        /// <param name="nodeId">Node index to compute constraint for</param>
        /// <returns>Local constraint contribution weighted by Lagrange multiplier</returns>
        public double ComputeLocalConstraintContribution(int nodeId)
        {
            double localViolation = CalculateWheelerDeWittConstraint(nodeId);
            double lambda = PhysicsConstants.WheelerDeWittConstants.ConstraintLagrangeMultiplier;
            return lambda * localViolation / N; // Normalized contribution
        }
        
        /// <summary>
        /// Get individual components of the Wheeler-DeWitt constraint at a node.
        /// Useful for diagnostics and debugging.
        /// </summary>
        /// <param name="nodeId">Node index</param>
        /// <returns>Tuple of (H_geometry, H_matter, total_constraint)</returns>
        public (double HGeometry, double HMatter, double Constraint) GetConstraintComponents(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                throw new ArgumentOutOfRangeException(nameof(nodeId));
            
            double H_geom = GetLocalCurvature(nodeId);
            
            double H_matter = 0.0;
            if (_correlationMass != null && nodeId < _correlationMass.Length)
            {
                H_matter = _correlationMass[nodeId];
            }
            
            double kappa = PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling;
            double constraint = H_geom - kappa * H_matter;
            
            return (H_geom, H_matter, constraint);
        }
        
        /// <summary>
        /// Check if a proposed topology change would violate the Wheeler-DeWitt constraint.
        /// Used to reject moves in strict mode.
        /// </summary>
        /// <param name="nodeA">First affected node</param>
        /// <param name="nodeB">Second affected node</param>
        /// <returns>True if the move is allowed under constraint checking</returns>
        public bool IsTopologyMoveConstraintAllowed(int nodeA, int nodeB)
        {
            if (!PhysicsConstants.WheelerDeWittConstants.EnableStrictMode)
                return true; // Non-strict mode allows all moves
            
            // Check current violation at affected nodes
            double violationA = CalculateWheelerDeWittConstraint(nodeA);
            double violationB = CalculateWheelerDeWittConstraint(nodeB);
            double maxViolation = Math.Max(violationA, violationB);
            
            // Block if violation exceeds threshold
            return maxViolation <= PhysicsConstants.WheelerDeWittConstants.MaxAllowedViolation;
        }

        /// <summary>
        /// Compute the network Hamiltonian H = H_links + H_nodes
        /// H_links: Cost of having edges (prefers sparse graphs)
        /// H_nodes: Matter contribution (correlation mass)
        /// </summary>
        public double ComputeNetworkHamiltonian()
        {
            double H_links = 0.0;
            double H_nodes = 0.0;

            // H_links: Sum over all edges of (1 - w_ij) weighted by link cost
            // This penalizes weak links more than strong ones
            int edgeCount = 0;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    edgeCount++;

                    double w = Weights[i, j];

                    // Link existence cost (sparse graph preference)
                    H_links += _linkCostCoeff * (1.0 - w * w);

                    // Length cost (strong links = short effective distance)
                    double effectiveLength = w > 1e-10 ? 1.0 / w : 10.0;
                    H_links += _lengthCostCoeff * effectiveLength;
                }
            }

            // H_nodes: Matter contribution
            if (_correlationMass != null && _correlationMass.Length == N)
            {
                for (int i = 0; i < N; i++)
                {
                    // Mass-curvature coupling (Einstein-like)
                    double mass = _correlationMass[i];
                    double curvature = GetLocalCurvature(i);

                    // Matter wants to curve space (positive contribution when uncurved)
                    H_nodes += _matterCouplingCoeff * mass * (1.0 - Math.Abs(curvature));
                }
            }

            // Add string energy contribution if present
            if (_stringEnergy != null && _stringEnergy.GetLength(0) == N)
            {
                for (int i = 0; i < N; i++)
                {
                    for (int j = i + 1; j < N; j++)
                    {
                        H_links += _stringEnergy[i, j];
                    }
                }
            }

            return H_links + H_nodes;
        }

        /// <summary>
        /// Compute local energy contribution for a single edge.
        /// Used for efficient delta-energy calculation in Metropolis step.
        /// </summary>
        private double ComputeEdgeEnergy(int i, int j)
        {
            if (!Edges[i, j]) return 0.0;

            double w = Weights[i, j];

            double energy = _linkCostCoeff * (1.0 - w * w);
            double effectiveLength = w > 1e-10 ? 1.0 / w : 10.0;
            energy += _lengthCostCoeff * effectiveLength;

            if (_stringEnergy != null && i < N && j < N)
                energy += _stringEnergy[i, j];

            return energy;
        }

        /// <summary>
        /// Compute local Hamiltonian contribution for a single edge (i,j) and its neighbors.
        /// This includes: gravity (Ricci curvature), matter coupling, and cosmological constant.
        /// Used for efficient O(degree) delta-energy calculation instead of O(N^2) full recalculation.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 4: 4D Dimension Driver
        /// ===================================================
        /// Adds penalty term when local volume growth deviates from r^4 scaling.
        /// Without this, simulation produces fractal "dust" or collapses rather than
        /// emergent 4D spacetime geometry.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <returns>Local energy contribution from edge (i,j) and affected neighbors</returns>
        public double ComputeLocalHamiltonian(int i, int j)
        {
            double H_local = 0.0;

            // 1. Edge energy contribution (link cost, length cost)
            if (Edges[i, j])
            {
                double w = Weights[i, j];

                // Link existence cost
                H_local += _linkCostCoeff * (1.0 - w * w);

                // Length cost (strong links = short effective distance)
                double effectiveLength = w > 1e-10 ? 1.0 / w : 10.0;
                H_local += _lengthCostCoeff * effectiveLength;

                // String energy contribution
                if (_stringEnergy != null && i < N && j < N)
                    H_local += _stringEnergy[i, j];

                // 2. Gravity contribution: Ricci curvature for this edge
                double ricci = CalculateApproximateRicci(i, j);
                double G_inv = 1.0 / 0.1;
                H_local -= G_inv * ricci;
            }

            // 3. Matter coupling for affected nodes (i and j)
            if (_correlationMass != null && _correlationMass.Length == N)
            {
                // Node i contribution
                double mass_i = _correlationMass[i];
                double curvature_i = GetLocalCurvature(i);
                H_local += _matterCouplingCoeff * mass_i * (1.0 - Math.Abs(curvature_i));

                // Node j contribution
                double mass_j = _correlationMass[j];
                double curvature_j = GetLocalCurvature(j);
                H_local += _matterCouplingCoeff * mass_j * (1.0 - Math.Abs(curvature_j));
            }

            // 4. Cosmological constant term: Lambda * local_volume
            // Local volume is approximated as sum of weights around nodes i and j
            double localVolume = 0.0;
            foreach (int k in Neighbors(i))
                localVolume += Weights[i, k];
            foreach (int k in Neighbors(j))
                localVolume += Weights[j, k];
            // Avoid double counting edge (i,j)
            if (Edges[i, j])
                localVolume -= Weights[i, j];

            H_local += _cosmologicalConstant * localVolume;

            // 5. RQ-HYPOTHESIS CHECKLIST ITEM 4: 4D Dimension Driver (Volume Growth Constraint)
            // ================================================================================
            // Penalize graph configurations where local volume growth V(r) deviates from
            // r^4 scaling expected for 4D spacetime (Hausdorff dimension check).
            // 
            // For d-dimensional lattice: V(r) ~ r^d
            // At r=1: V1 = degree (number of neighbors)
            // At r=2: V2 = second neighbors
            // Ratio: V2/V1 ? 2^(d-1) for regular lattice
            // For 4D: target ratio = 2^3 = 8
            H_local += ComputeDimensionalityPenalty(i, j);

            return H_local;
        }

        public double CalculateApproximateRicci(int i, int j)
        {
            // Упрощенная Форман-Риччи кривизна для взвешенных графов
            // Ric(e) ~ 4 - deg(i) - deg(j) + 3 * (triangles containing e)
            // Для взвешенных графов используем сумму весов вместо степени.

            if (!Edges[i, j]) return 0.0;

            double w_e = Weights[i, j];
            double w_i = 0.0; // Сумма весов соседей i
            double w_j = 0.0; // Сумма весов соседей j

            // Считаем "взвешенные степени", исключая само ребро (i,j)
            foreach (var n in Neighbors(i)) w_i += Weights[i, n];
            foreach (var n in Neighbors(j)) w_j += Weights[j, n];

            w_i -= w_e;
            w_j -= w_e;

            // Считаем треугольники (корреляции соседей)
            double triangles = 0.0;
            // Находим общих соседей
            foreach (var n_i in Neighbors(i))
            {
                if (n_i == j) continue;
                if (Edges[j, n_i]) // Треугольник i-j-n_i
                {
                    // Вклад треугольника зависит от силы связей
                    double w_in = Weights[i, n_i];
                    double w_jn = Weights[j, n_i];
                    triangles += Math.Sqrt(w_in * w_jn); // Геометрическое среднее
                }
            }

            // Эвристическая формула кривизны (базируется на Forman's Ricci curvature)
            // Положительная кривизна = много треугольников (кластер).
            // Отрицательная = древовидная структура.
            return w_e * (triangles - (w_i + w_j) * 0.1);
        }

        /// <summary>
        /// Compute penalty for deviation from 4D spacetime topology.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 4: 4D Dimension Driver
        /// ===================================================
        /// Uses local Hausdorff dimension estimate from volume growth ratio.
        /// 
        /// Physics: In a d-dimensional space, the number of nodes within
        /// graph distance r scales as V(r) ~ r^d (up to boundary effects).
        /// 
        /// We estimate local dimension from:
        ///   V(1) = degree of node
        ///   V(2) = second neighbors (nodes at distance 2)
        ///   d_local ? 1 + log2(V(2)/V(1))
        /// 
        /// For 4D: V(2)/V(1) ? 8, giving d_local ? 4
        /// 
        /// Penalty = ? ? (V2/V1 - 8)? drives graph toward 4D configuration.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #1 (Critical):
        /// ===========================================
        /// WARNING: This is a TELEOLOGICAL TRAP - we are forcing the answer!
        /// If EnableNaturalDimensionEmergence is true, this penalty is DISABLED
        /// to allow the dimension to emerge purely from Ricci flow + matter coupling.
        /// 
        /// Use EnableNaturalDimensionEmergence = true to TEST whether 4D emerges
        /// without artificial forcing. If graph collapses or expands without bound,
        /// the physics constants need rebalancing, not artificial dimension penalty.
        /// </summary>
        private double ComputeDimensionalityPenalty(int i, int j)
        {
            // RQ-HYPOTHESIS FIX #1: Allow natural emergence mode
            if (PhysicsConstants.EnableNaturalDimensionEmergence)
            {
                return 0.0; // No artificial 4D forcing - let dimension emerge naturally
            }

            // Only apply penalty for nodes with sufficient degree
            int v1_i = 0;
            foreach (var _ in Neighbors(i)) v1_i++;

            int v1_j = 0;
            foreach (var _ in Neighbors(j)) v1_j++;

            if (v1_i < 2 || v1_j < 2)
                return 0.0; // Not enough neighbors for meaningful dimension estimate

            // Count second neighbors for node i
            int v2_i = CountSecondNeighbors(i);

            // Compute growth ratio
            double growthRatio_i = (double)v2_i / v1_i;

            // Target ratio for 4D: 2^(4-1) = 8
            double deviation = growthRatio_i - PhysicsConstants.TargetGrowthRatio4D;

            // Quadratic penalty for deviation from 4D topology
            return PhysicsConstants.DimensionPenalty * deviation * deviation;
        }

        /// <summary>
        /// Count nodes at graph distance 2 from given node (second neighbors).
        /// Excludes the node itself and its direct neighbors (distance 1).
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 4: Helper for dimension estimation.
        /// </summary>
        /// <param name="node">Center node</param>
        /// <returns>Number of second neighbors</returns>
        private int CountSecondNeighbors(int node)
        {
            // Use HashSet for O(1) lookups
            var visited = new System.Collections.Generic.HashSet<int> { node };
            var firstNeighbors = new System.Collections.Generic.List<int>();

            // Mark first neighbors
            foreach (int n1 in Neighbors(node))
            {
                visited.Add(n1);
                firstNeighbors.Add(n1);
            }

            // Count second neighbors (neighbors of first neighbors, excluding visited)
            int secondNeighborCount = 0;
            foreach (int n1 in firstNeighbors)
            {
                foreach (int n2 in Neighbors(n1))
                {
                    if (!visited.Contains(n2))
                    {
                        visited.Add(n2);
                        secondNeighborCount++;
                    }
                }
            }

            return secondNeighborCount;
        }
    }
}
