using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Optimized Metropolis step using local action calculation.
        /// Replaces O(N?) global Hamiltonian with O(degree?) local action.
        /// 
        /// Implements RQ-Hypothesis Checklist: Locality of Action (Step 1).
        /// Uses quadratic volume stabilization for stable 4D emergence.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIXES:
        /// - RNG cost: Each random sampling deducts energy from vacuum reservoir
        /// - Weight quantization: All weight changes rounded to EdgeWeightQuantum
        /// - Edge creation/removal: Energy cost/refund from vacuum pool
        /// - [FIX] Soft bipartite penalty: Same-sublattice edges are penalized via mass term,
        ///   not forbidden. This allows triangles to form (required for curvature).
        /// </summary>
        /// <returns>True if change was accepted</returns>
        public bool MetropolisEdgeStepLocalAction()
        {
            // === RQ-HYPOTHESIS CHECKLIST: RNG COST ===
            // Random number generation is not free - it requires energy from vacuum.
            // This prevents unlimited "thermal noise" and enforces finite entropy budget.
            var ledger = GetEnergyLedger();
            if (!ledger.CanAfford(PhysicsConstants.RngStepCost))
                return false; // Not enough vacuum energy for even one random step
            ledger.TrySpendVacuumEnergy(PhysicsConstants.RngStepCost);

            // Select random edge
            int i = _rng.Next(N);
            int j = _rng.Next(N);
            if (i == j) return false;

            // Store old state
            double w_old = Weights[i, j];
            bool edge_existed = Edges[i, j];

            // Proposal requires another random number - pay the cost
            if (!ledger.TrySpendVacuumEnergy(PhysicsConstants.RngStepCost))
                return false;
            double proposal = _rng.NextDouble();
            double w_new;
            bool create_edge = false;
            bool remove_edge = false;

            if (proposal < 0.2 && !edge_existed)
            {
                // === PHYSICS FIX TASK 2: Causal Locality Check ===
                // Before creating a new edge, verify it doesn't violate causality.
                // Only allow edge creation between nodes that are already "nearby"
                // in the existing graph topology (within CausalMaxHops hops).
                // This prevents "wormhole" creation that would allow FTL communication.
                if (!IsCausallyAllowed(i, j))
                    return false;

                // RQ-FIX: REMOVED hard bipartite check (Checklist Critical Fix #1)
                // ==============================================================
                // The previous check: if ((i % 2) == (j % 2)) return false;
                // This enforced strict bipartite structure, preventing ALL triangles.
                // 
                // PROBLEM: Without triangles, Forman-Ricci curvature is always
                // extremely negative (tree-like), preventing 4D spacetime emergence.
                // 
                // SOLUTION: Instead of forbidding same-sublattice edges, we add a
                // soft MASS PENALTY to the Hamiltonian via ComputeSameSublatticeChiralityPenalty().
                // This allows triangles (needed for positive curvature/gravity) while
                // still suppressing fermion doubling via effective mass term.
                // 
                // See ComputeLocalActionChange() where the penalty is now applied.

                // === RQ-HYPOTHESIS CHECKLIST: EDGE CREATION COST ===
                // Creating new correlation requires vacuum energy (like particle creation)
                if (!ledger.CanAfford(PhysicsConstants.EdgeCreationCost))
                    return false; // Not enough vacuum energy - cannot create edge

                create_edge = true;
                // Pay another RNG cost for initial weight sampling
                if (!ledger.TrySpendVacuumEnergy(PhysicsConstants.RngStepCost))
                    return false;
                w_new = 0.1 + 0.3 * _rng.NextDouble();
            }
            else if (proposal < 0.4 && edge_existed && w_old < 0.15)
            {
                // RQ-HYPOTHESIS CHECKLIST ITEM 2: Gauge Invariance Check (Gauss's Law)
                // Cannot remove edge if significant gauge flux flows through it.
                // Removing such edge would violate charge conservation (create monopole).
                if (!CanRemoveEdgeGaugeInvariant(i, j))
                    return false; // Reject: edge carries significant gauge flux

                remove_edge = true;
                w_new = 0.0;
            }
            else if (edge_existed)
            {
                // Pay RNG cost for weight perturbation
                if (!ledger.TrySpendVacuumEnergy(PhysicsConstants.RngStepCost))
                    return false;
                double delta = (_rng.NextDouble() * 2.0 - 1.0) * 0.1;
                w_new = Math.Clamp(w_old + delta, 0.05, 1.0);
            }
            else
            {
                return false;
            }

            // === RQ-HYPOTHESIS CHECKLIST: WEIGHT QUANTIZATION ===
            // Correlations come in discrete quanta, not continuous values.
            // Round to nearest quantum to enforce discrete correlation steps.
            if (w_new > 0)
            {
                w_new = Math.Round(w_new / PhysicsConstants.EdgeWeightQuantum)
                        * PhysicsConstants.EdgeWeightQuantum;
                // Ensure minimum positive weight after quantization
                if (w_new < PhysicsConstants.EdgeWeightQuantum && !remove_edge)
                    w_new = PhysicsConstants.EdgeWeightQuantum;
            }

            // === KEY OPTIMIZATION: Compute LOCAL action change ===
            // NOTE: ComputeLocalActionChange now includes soft chirality penalty
            // for same-sublattice edges instead of hard rejection above.
            double deltaS = ComputeLocalActionChange(i, j, w_new);

            // Pay RNG cost for Metropolis acceptance test
            if (deltaS > 0 && !ledger.TrySpendVacuumEnergy(PhysicsConstants.RngStepCost))
                return false;

            // Metropolis acceptance criterion
            bool accept;
            if (deltaS <= 0)
            {
                accept = true;
            }
            else
            {
                double prob = Math.Exp(-deltaS / _networkTemperature);
                accept = _rng.NextDouble() < prob;
            }

            if (accept)
            {
                // Apply topology change
                if (create_edge)
                {
                    // === RQ-HYPOTHESIS CHECKLIST: SPEND EDGE CREATION COST ===
                    if (!ledger.TrySpendVacuumEnergy(PhysicsConstants.EdgeCreationCost))
                    {
                        // Should not happen (we checked earlier), but safety first
                        return false;
                    }

                    Edges[i, j] = true;
                    Edges[j, i] = true;
                    _degree[i]++;
                    _degree[j]++;
                    InvalidateTopologyCache();

                    // RQ-HYPOTHESIS CHECKLIST FIX #3: Invalidate topological parity
                    InvalidateParity();

                    // === PHYSICS FIX TASK 3: Initialize gauge phase with minimal flux ===
                    // When creating a new edge, set its gauge phase to minimize Wilson loop
                    // flux in surrounding triangles. This preserves gauge field consistency.
                    InitializeEdgePhaseMinimalFlux(i, j);
                }
                else if (remove_edge)
                {
                    // === RQ-HYPOTHESIS CHECKLIST FIX #5: Topology Energy Compensation ===
                    // When removing an edge, capture all field energy stored on it and
                    // transfer to vacuum/radiation pool. This ensures strict energy conservation
                    // during topology changes (prevents energy "disappearing" into thin air).
                    if (PhysicsConstants.EnableTopologyEnergyCompensation)
                    {
                        double capturedEnergy = CaptureEdgeFieldEnergy(i, j);
                        if (capturedEnergy > 0)
                        {
                            ledger.RegisterRadiation(capturedEnergy);
                        }
                    }

                    // === RQ-HYPOTHESIS CHECKLIST: REFUND EDGE ENERGY ===
                    // Energy returns to vacuum when correlation is destroyed
                    ledger.RegisterRadiation(PhysicsConstants.EdgeCreationCost);

                    Edges[i, j] = false;
                    Edges[j, i] = false;
                    _degree[i]--;
                    _degree[j]--;
                    InvalidateTopologyCache();

                    // RQ-HYPOTHESIS CHECKLIST FIX #3: Invalidate topological parity
                    InvalidateParity();
                }

                Weights[i, j] = w_new;
                Weights[j, i] = w_new;

                // RQ-HYPOTHESIS CHECKLIST ITEM 5: Correct wavefunction after topology change
                if (create_edge || remove_edge)
                {
                    CorrectWavefunctionAfterTopologyChange();
                }
            }

            return accept;
        }

        /// <summary>
        /// PHYSICS FIX TASK 2: Check if creating an edge between nodes i and j
        /// respects causal locality (light cone constraint).
        /// 
        /// In a relational model where c=1, information can only travel one hop per time step.
        /// Creating a direct edge between distant nodes would create "wormholes" that
        /// violate special relativity by allowing faster-than-light communication.
        /// 
        /// Resolution: Only allow edge creation between nodes that are already "nearby"
        /// in the existing graph topology (within CausalMaxHops hops).
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <returns>True if edge creation is causally allowed, false otherwise</returns>
        public bool IsCausallyAllowed(int i, int j)
        {
            // Self-loops are never allowed
            if (i == j) return false;

            // If edge already exists, modification is always allowed
            if (Edges[i, j]) return true;

            // Maximum hop distance for causally safe edge creation
            int maxHops = PhysicsConstants.CausalMaxHops;

            // Find shortest path hop count between i and j via existing edges
            int currentDist = GetShortestPathHopCount(i, j, maxHops + 1);

            // If nodes are too far apart in the existing graph, creating a direct
            // edge would violate locality (instantaneous connection across spacetime)
            return currentDist <= maxHops;
        }

        /// <summary>
        /// PHYSICS FIX TASK 2: Compute shortest path hop count between two nodes.
        /// Uses BFS with early termination once maxLimit is exceeded.
        /// 
        /// Returns the minimum number of edges that must be traversed to go from
        /// node 'from' to node 'to' using existing graph edges.
        /// </summary>
        /// <param name="from">Starting node</param>
        /// <param name="to">Target node</param>
        /// <param name="maxLimit">Maximum distance to search (returns maxLimit+1 if exceeded)</param>
        /// <returns>Shortest hop count, or maxLimit+1 if path is longer/nonexistent</returns>
        public int GetShortestPathHopCount(int from, int to, int maxLimit)
        {
            if (from == to) return 0;
            if (Edges[from, to]) return 1; // Direct edge exists

            // BFS for shortest path
            var visited = new bool[N];
            var queue = new Queue<(int node, int dist)>();

            visited[from] = true;
            queue.Enqueue((from, 0));

            while (queue.Count > 0)
            {
                var (current, dist) = queue.Dequeue();

                // Early termination if we've exceeded max limit
                if (dist >= maxLimit)
                    return maxLimit + 1;

                foreach (int neighbor in Neighbors(current))
                {
                    if (neighbor == to)
                        return dist + 1; // Found target

                    if (!visited[neighbor])
                    {
                        visited[neighbor] = true;
                        queue.Enqueue((neighbor, dist + 1));
                    }
                }
            }

            // No path found within limit (disconnected graph regions)
            return maxLimit + 1;
        }

        /// <summary>
        /// Check if an edge can be safely removed without violating gauge invariance.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Gauss's Law Conservation
        /// =========================================================
        /// Gauge invariance is fundamental to the Standard Model:
        /// - U(1) charge conservation (electrodynamics)
        /// - SU(3) color confinement (QCD)
        /// 
        /// Physics: Removing an edge with non-zero gauge flux creates a "monopole"
        /// which violates Gauss's law: div(E) = ? (charge must be conserved).
        /// In QCD, it would break the color flux tube, creating free quarks.
        /// 
        /// STAGE 3 ENHANCEMENT: Wilson Loop Protection
        /// ============================================
        /// Uses Wilson loops (triangles) to detect physical gauge flux.
        /// If edge is part of a triangle with non-trivial Wilson loop phase,
        /// the edge carries physical flux and cannot be removed.
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <returns>True if edge can be safely removed without violating gauge invariance</returns>
        private bool CanRemoveEdgeGaugeInvariant(int i, int j)
        {
            // === STAGE 3: Wilson Loop Protection (enhanced check) ===
            // Check all triangles containing this edge for non-trivial flux
            if (PhysicsConstants.EnableWilsonLoopProtection)
            {
                if (!Physics.GaugeAwareTopology.IsTopologicalMoveGaugeInvariant(
                    this, i, j, Physics.TopologyMove.RemoveEdge))
                {
                    // Wilson loop check failed - edge carries significant flux
                    return false;
                }
            }
            
            // === Legacy checks (still useful for non-triangle edges) ===
            
            // Check U(1) gauge flux (electrodynamics)
            if (_edgePhaseU1 != null)
            {
                double fluxU1 = Math.Abs(_edgePhaseU1[i, j]);
                if (fluxU1 > PhysicsConstants.GaugeFluxTolerance)
                {
                    // Significant electric flux through edge - removal would violate Gauss's law
                    return false;
                }
            }

            // Check SU(3) gauge flux (QCD color field)
            if (GaugeDimension == 3 && _gaugeSU3 != null)
            {
                // Measure "distance to identity" of the SU(3) link variable
                // Identity matrix means no color flux; deviation indicates flux tube
                double colorFlux = ComputeSU3FluxMagnitude(i, j);
                if (colorFlux > PhysicsConstants.ColorFluxTolerance)
                {
                    // Significant color flux - removal would break confinement
                    return false;
                }
            }

            // Check SU(2) gauge flux if applicable
            if (GaugeDimension == 2 && _gaugeSU != null)
            {
                double su2Flux = ComputeSU2FluxMagnitude(i, j);
                if (su2Flux > PhysicsConstants.GaugeFluxTolerance)
                {
                    return false;
                }
            }

            return true; // Edge can be safely removed
        }

        /// <summary>
        /// Compute SU(3) flux magnitude as trace distance from identity.
        /// |flux| = ||U - I||_F / sqrt(6) where ||.||_F is Frobenius norm.
        /// Returns 0 for identity (no flux), ~1 for maximally non-trivial link.
        /// </summary>
        private double ComputeSU3FluxMagnitude(int i, int j)
        {
            if (_gaugeSU3 == null || i >= N || j >= N)
                return 0.0;

            // Get the SU(3) link variable
            var U = _gaugeSU3[i, j];

            // Compute trace distance to identity: Re(Tr(U)) = 3 for identity
            // |flux| = (3 - Re(Tr(U))) / 6, normalized to [0, 1]
            double traceReal = U.TraceReal();
            double fluxMagnitude = (3.0 - traceReal) / 6.0;

            return Math.Max(0.0, Math.Min(1.0, fluxMagnitude));
        }

        /// <summary>
        /// Compute SU(2) flux magnitude from the gauge link matrix.
        /// </summary>
        private double ComputeSU2FluxMagnitude(int i, int j)
        {
            if (_gaugeSU == null || i >= N || j >= N)
                return 0.0;

            // For SU(2), compute trace distance to identity
            // Identity has trace = 2, maximum deviation has trace = -2
            double traceReal = 0.0;
            int d = 2; // SU(2) dimension
            for (int r = 0; r < d; r++)
            {
                traceReal += _gaugeSU[i, j, r * d + r].Real;
            }

            // Normalize: (2 - trace) / 4, gives [0, 1]
            double fluxMagnitude = (2.0 - traceReal) / 4.0;
            return Math.Max(0.0, Math.Min(1.0, fluxMagnitude));
        }

        /// <summary>
        /// Capture all field energy stored on an edge before removal.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #5: Topology Energy Compensation
        /// =============================================================
        /// When removing an edge (i,j), energy stored in fields on that edge
        /// must be accounted for to maintain energy conservation:
        /// 
        /// 1. Gauge field energy: E_gauge = (1/2) ?? (U(1)) + (1/4) Tr(F?) (SU(N))
        /// 2. Spinor current energy: from fermion flow through the edge
        /// 3. Scalar gradient energy: E_scalar = (1/2) w (??)?
        /// 
        /// This energy is "radiated away" - transferred to the vacuum pool
        /// where it can be reused for other processes.
        /// 
        /// Physics: This is analogous to Hawking radiation from black holes,
        /// or particle production during topology changes in string theory.
        /// Energy cannot simply vanish - it must be accounted for.
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <returns>Total field energy captured from the edge</returns>
        private double CaptureEdgeFieldEnergy(int i, int j)
        {
            double totalEnergy = 0.0;
            double weight = Weights[i, j];

            // === 1. U(1) Gauge Field Energy ===
            // E_U1 = (1/2) ??_ij (electric field stored in phase gradient)
            if (_edgePhaseU1 != null)
            {
                double phase = _edgePhaseU1[i, j];
                totalEnergy += 0.5 * phase * phase * PhysicsConstants.GaugeFieldEnergyWeight;
            }

            // === 2. SU(3) Gluon Field Energy ===
            // E_SU3 = (1/2) ?_a A?_a (color field energy)
            if (_gluonField != null)
            {
                for (int a = 0; a < 8; a++)
                {
                    double A_a = _gluonField[i, j, a];
                    totalEnergy += 0.5 * A_a * A_a * PhysicsConstants.GaugeFieldEnergyWeight;
                }
            }

            // === 3. SU(2) Weak Field Energy ===
            if (_weakField != null)
            {
                for (int a = 0; a < 3; a++)
                {
                    double W_a = _weakField[i, j, a];
                    totalEnergy += 0.5 * W_a * W_a * PhysicsConstants.GaugeFieldEnergyWeight;
                }
            }

            // === 4. Scalar Field Gradient Energy ===
            // E_scalar = (1/2) w_ij (?_i - ?_j)?
            if (ScalarField != null && i < ScalarField.Length && j < ScalarField.Length)
            {
                double phi_i = ScalarField[i];
                double phi_j = ScalarField[j];
                double gradSq = (phi_i - phi_j) * (phi_i - phi_j);
                totalEnergy += 0.5 * weight * gradSq * PhysicsConstants.ScalarFieldEnergyWeight;
            }

            // === 5. Spinor Current Energy ===
            // Approximate energy from fermion density at endpoints
            if (_spinorA != null && i < _spinorA.Length && j < _spinorA.Length)
            {
                double densityI = _spinorA[i].Magnitude;
                double densityJ = _spinorA[j].Magnitude;
                // Current ~ (density_i + density_j) * hopping ~ w * (?_i + ?_j)
                double currentEnergy = 0.25 * weight * (densityI + densityJ) * PhysicsConstants.FermionFieldEnergyWeight;
                totalEnergy += currentEnergy;
            }

            return totalEnergy;
        }

        /// <summary>
        /// РQ-ГИПОТЕЗА: Принудительное удаление под-Planckian рёбер
        /// 
        /// Планковский обрезатель (Vacuum Cutoff / Planck Cutoff).
        /// Принудительно удаляет ребра, вес которых застрял на нижнем пределе SoftWall,
        /// игнорируя сопротивление гравитационного потенциала. Это необходимо для
        /// предотвращения образования "волосяного кома" (hairball problem).
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: Теперь использует удаление с учетом калибровки,
        /// чтобы сохранить закон Гаусса.
        /// 
        /// Должен вызываться в конце каждого цикла обновления топологии (после Метрополя).
        /// Или периодически в основном физическом цикле (например, каждые 10 шагов).
        /// </summary>
        public void EnforcePlanckCutoff()
        {
            // Порог отсечения: чуть ниже мягкой стены, чтобы удалять только "мертвые" связи
            double planckThreshold = PhysicsConstants.WeightLowerSoftWall * 1.1;

            // Используем потокобезопасную коллекцию для параллельного сканирования
            var edgesToRemove = new System.Collections.Concurrent.ConcurrentBag<(int u, int v)>();

            // Параллельное сканирование для производительности
            // (чтение Weights безопасно, запись в ConcurrentBag тоже)
            System.Threading.Tasks.Parallel.For(0, N, i =>
            {
                foreach (int j in Neighbors(i))
                {
                    // Only process each edge once (i < j)
                    if (i >= j) continue;

                    // Если вес лежит на дне (или ниже)
                    if (Weights[i, j] <= planckThreshold)
                    {
                        // Дополнительная проверка на калибровочный поток (чтобы не нарушить Gauss Law)
                        // Если поток мал, помечаем на удаление
                        if (CanRemoveEdgeGaugeInvariant(i, j))
                        {
                            edgesToRemove.Add((i, j));
                        }
                    }
                }
            });

            // Apply the "Planck razor" - remove sub-Planckian edges
            foreach (var (u, v) in edgesToRemove)
            {
                // Capture field energy before removal (RQ-HYPOTHESIS CHECKLIST FIX #5)
                if (PhysicsConstants.EnableTopologyEnergyCompensation)
                {
                    double capturedEnergy = CaptureEdgeFieldEnergy(u, v);
                    if (capturedEnergy > 0)
                    {
                        Ledger.RegisterRadiation(capturedEnergy);
                    }
                }

                // Return edge creation cost to vacuum (energy recycling)
                Ledger.RegisterRadiation(PhysicsConstants.EdgeCreationCost * Weights[u, v]);

                // Remove edge from topology
                Edges[u, v] = false;
                Edges[v, u] = false;
                Weights[u, v] = 0.0;
                Weights[v, u] = 0.0;
                _degree[u]--;
                _degree[v]--;
            }

            // Invalidate caches if any edges were removed
            if (!edgesToRemove.IsEmpty)
            {
                InvalidateTopologyCache();
                InvalidateParity(); // Critical for staggered fermion parity
            }
        }
    }
}
