using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // ============================================================
        // RQ-HYPOTHESIS CHECKLIST: Stress-Energy Tensor Smoothing
        // ============================================================
        
        /// <summary>
        /// History buffer for exponential moving average of stress-energy tensor.
        /// Smoothing prevents high-frequency oscillations in gravity from
        /// destabilizing geometry evolution.
        /// </summary>
        private double[,]? _averagedStressTensor;

        /// <summary>
        /// Compute the change in local action due to modifying edge (i,j) weight.
        /// This is the core method for RQ-compliant Metropolis-Hastings.
        /// 
        /// Implements RQ-Hypothesis Checklist: Locality of Action (Step 1).
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX (Volume Stabilization):
        /// Uses QUADRATIC volume potential S_vol = ?(V - V_target)? instead of
        /// linear ??V term. This creates a restoring force toward target volume,
        /// following CDT (Causal Dynamical Triangulations) approach for stable 4D emergence.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX (Critical Fix #1 - Chirality Penalty):
        /// Instead of forbidding same-sublattice edges (which prevents triangles),
        /// we add a SOFT MASS PENALTY for edges connecting nodes in the same sublattice.
        /// This allows triangles to form (required for positive curvature/gravity)
        /// while still suppressing fermion doubling via effective mass term.
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <param name="newWeight">Proposed new weight</param>
        /// <returns>Change in action ?S (positive = action increases = unfavorable)</returns>
        public double ComputeLocalActionChange(int i, int j, double newWeight)
        {
            if (i < 0 || i >= N || j < 0 || j >= N || i == j)
                return double.MaxValue; // Invalid edge

            double oldWeight = Edges[i, j] ? Weights[i, j] : 0.0;
            double deltaW = newWeight - oldWeight;

            if (Math.Abs(deltaW) < 1e-12)
                return 0.0; // No change

            // === 1. Geometry contribution: ?S_geom = -G?? ? ?R_ij ===
            // Curvature change for all triangles containing edge (i,j)
            double deltaS_geometry = ComputeLocalRicciChange(i, j, newWeight);

            // === 2. Matter contribution: ?S_matter = T_ij ? ?w_ij ===
            // RQ-HYPOTHESIS CHECKLIST FIX: Use smoothed (EMA) stress-energy tensor
            // to prevent high-frequency oscillations from destabilizing gravity.
            // The averaged tensor provides stable coupling to geometry.
            double stressEnergy = GetStressEnergyTensorAveraged(i, j);
            double deltaS_matter = stressEnergy * deltaW * PhysicsConstants.CurvatureTermScale;

            // === 3. Volume contribution: QUADRATIC STABILIZATION (CDT approach) ===
            // RQ-HYPOTHESIS CHECKLIST FIX:
            // CHANGED FROM: deltaS_volume = ? ? ?w (linear - causes collapse/explosion)
            // TO: deltaS_volume = ?(V - V_target) ? ?w (quadratic - restoring force)
            //
            // Physics: S_vol = ?(V - V_target)? => dS/dw = 2?(V - V_target) ? dV/dw
            // For edge weight change: dV/dw = 1 (each edge contributes its weight to volume)
            // So: ?S_vol ? 2?(V_current - V_target) ? ?w
            //
            // Volume constraint fields (_volumeLambda, _targetTotalWeight, etc.) are
            // defined in RQGraph.VolumeStabilization.cs
            double deltaS_volume;
            if (_volumeConstraintInitialized && _volumeLambda > 0)
            {
                // Quadratic volume stabilization (CDT approach for stable 4D)
                // Use ComputeVolumePenaltyChange from VolumeStabilization.cs for efficient delta
                int edgeCreated = 0;
                if (!Edges[i, j] && newWeight > 0) edgeCreated = 1;  // Creating edge
                else if (Edges[i, j] && newWeight <= 0) edgeCreated = -1;  // Removing edge

                deltaS_volume = ComputeVolumePenaltyChange(edgeCreated, deltaW);

                // Also add small cosmological constant term for baseline
                deltaS_volume += PhysicsConstants.CosmologicalConstant * deltaW * 0.1;
            }
            else
            {
                // Fallback to linear cosmological constant (less stable)
                deltaS_volume = PhysicsConstants.CosmologicalConstant * deltaW;
            }

            // === 4. Field gradient contribution ===
            double deltaS_field = ComputeLocalFieldActionChange(i, j, newWeight);

            // === 5. CHIRALITY PENALTY (Critical Fix #1) ===
            // RQ-HYPOTHESIS CHECKLIST: Soft Mass Penalty for Same-Sublattice Edges
            // =====================================================================
            // Instead of FORBIDDING edges within the same sublattice (which kills
            // all triangles and makes Forman-Ricci curvature always negative),
            // we add a SOFT MASS PENALTY that disfavors such edges but doesn't
            // make them impossible.
            //
            // Physics interpretation:
            // - Staggered fermions require bipartite structure for exact chirality
            // - But triangles (non-bipartite elements) are required for positive curvature
            // - Resolution: same-sublattice edges contribute MASS TERM to fermions
            // - Heavy fermions = suppressed propagation on non-bipartite edges
            // - This is equivalent to Wilson mass term in lattice QCD
            //
            // The penalty is: ?S_chiral = ?_chiral ? ?w ? (1 if same sublattice else 0)
            double deltaS_chirality = ComputeChiralityPenalty(i, j, deltaW);

            // Total action change
            return deltaS_geometry + deltaS_matter + deltaS_volume + deltaS_field + deltaS_chirality;
        }

        /// <summary>
        /// Compute change in Forman-Ricci curvature contribution to action.
        /// Only considers triangles containing edge (i,j).
        /// 
        /// Einstein-Hilbert on graph: S_EH = ? w_ij ? R_ij
        /// Change: ?S_EH = w_new ? R_new - w_old ? R_old
        /// </summary>
        private double ComputeLocalRicciChange(int i, int j, double newWeight)
        {
            double oldWeight = Edges[i, j] ? Weights[i, j] : 0.0;

            // Compute curvature with old weight
            double R_old = ComputeFormanRicciCurvatureLocal(i, j, oldWeight);

            // Compute curvature with new weight (temporary calculation)
            double R_new = ComputeFormanRicciCurvatureLocal(i, j, newWeight);

            // Action change: ?S = -(1/G) ? ?(w ? R)
            // Negative sign because we MINIMIZE action (gravity is attractive)
            double S_old = oldWeight * R_old;
            double S_new = newWeight * R_new;

            double G_inv = 1.0 / PhysicsConstants.GravitationalCoupling;
            return -G_inv * (S_new - S_old);
        }

        /// <summary>
        /// Compute Forman-Ricci curvature for edge (i,j) with specified weight (Jost formula).
        /// Uses LOCAL computation - only considers edges incident to endpoints.
        /// 
        /// Jost formula:
        ///   Ric_F(e) = 2 - w_e Ј [ ?_{e'~i, e'?e} ?(1/(w_eЈw_{e'})) + ?_{e''~j, e''?e} ?(1/(w_eЈw_{e''})) ]
        /// </summary>
        private double ComputeFormanRicciCurvatureLocal(int i, int j, double w_e)
        {
            if (w_e <= 0)
                return 0.0;

            // Sum over edges incident to node i (excluding edge e)
            double sumI = 0.0;
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                double w_ik = Weights[i, k];
                if (w_ik > 0)
                    sumI += Math.Sqrt(1.0 / (w_e * w_ik));
            }

            // Sum over edges incident to node j (excluding edge e)
            double sumJ = 0.0;
            foreach (int k in Neighbors(j))
            {
                if (k == i) continue;
                double w_jk = Weights[j, k];
                if (w_jk > 0)
                    sumJ += Math.Sqrt(1.0 / (w_e * w_jk));
            }

            // Jost weighted Forman-Ricci curvature
            return 2.0 - w_e * (sumI + sumJ);
        }

        /// <summary>
        /// Compute change in field action from modifying edge weight.
        /// Gradient energy: E_grad = (1/2) ? w_ij |?_i - ?_j|?
        /// </summary>
        private double ComputeLocalFieldActionChange(int i, int j, double newWeight)
        {
            double oldWeight = Edges[i, j] ? Weights[i, j] : 0.0;
            double deltaW = newWeight - oldWeight;

            if (Math.Abs(deltaW) < 1e-12 || ScalarField == null)
                return 0.0;

            // Field gradient at edge (i,j)
            double phi_i = ScalarField[i];
            double phi_j = ScalarField[j];
            double gradSq = (phi_i - phi_j) * (phi_i - phi_j);

            // Gradient energy contribution: (1/2) ? ?w ? |??|?
            return 0.5 * deltaW * gradSq;
        }

        /// <summary>
        /// Get stress-energy tensor component T_ij for edge (i,j).
        /// This is the source term for gravity in Einstein equations.
        /// 
        /// T_ij = T_matter + T_field where:
        /// - T_matter = (m_i + m_j) / 2 (average mass at endpoints)
        /// - T_field = (1/2)|D_i ?||D_j ?| (field gradient product)
        /// 
        /// Implements RQ-Hypothesis Checklist: Replace _correlationMass with T_ij
        /// </summary>
        public double GetStressEnergyTensor(int i, int j)
        {
            double T_ij = 0.0;

            // === Matter contribution ===
            if (NodeMasses != null && i < NodeMasses.Length && j < NodeMasses.Length)
            {
                T_ij += 0.5 * (NodeMasses[i].TotalMass + NodeMasses[j].TotalMass);
            }
            else if (_correlationMass != null && i < _correlationMass.Length && j < _correlationMass.Length)
            {
                // Fallback to correlation mass
                T_ij += 0.5 * (_correlationMass[i] + _correlationMass[j]);
            }

            // === Scalar field contribution ===
            if (ScalarField != null && i < ScalarField.Length && j < ScalarField.Length)
            {
                double phi_i = ScalarField[i];
                double phi_j = ScalarField[j];

                // Kinetic energy density from field gradient
                // T_?? ~ (?_? ?)(?_? ?) - (1/2)g_?? L
                double gradPhiSq = (phi_i - phi_j) * (phi_i - phi_j);
                T_ij += 0.5 * PhysicsConstants.ScalarFieldEnergyWeight * gradPhiSq;

                // Potential energy contribution (if Mexican Hat)
                if (UseMexicanHatPotential)
                {
                    double V_i = -HiggsMuSquared * phi_i * phi_i + HiggsLambda * Math.Pow(phi_i, 4);
                    double V_j = -HiggsMuSquared * phi_j * phi_j + HiggsLambda * Math.Pow(phi_j, 4);
                    T_ij += 0.5 * (V_i + V_j);
                }
            }

            // === Spinor field contribution ===
            if (_spinorA != null && i < _spinorA.Length && j < _spinorA.Length)
            {
                double psi_i_sq = _spinorA[i].Magnitude * _spinorA[i].Magnitude;
                double psi_j_sq = _spinorA[j].Magnitude * _spinorA[j].Magnitude;
                T_ij += 0.5 * PhysicsConstants.FermionFieldEnergyWeight * (psi_i_sq + psi_j_sq);
            }

            // === Gauge field contribution ===
            if (_edgePhaseU1 != null)
            {
                // Electric field energy E? ~ (phase gradient)?
                double E_ij = _edgePhaseU1[i, j];
                T_ij += PhysicsConstants.GaugeFieldEnergyWeight * E_ij * E_ij;
            }

            return T_ij;
        }

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST: Smoothed stress-energy tensor using EMA.
        /// 
        /// Physics: Direct (instantaneous) coupling of T_ij to geometry causes
        /// high-frequency oscillations that destabilize gravity evolution.
        /// This is analogous to the "stiff equation" problem in numerical ODE.
        /// 
        /// Solution: Use Exponential Moving Average (EMA) to smooth T_ij:
        ///   T_avg(t) = (1 - ?) ? T_avg(t-1) + ? ? T_instant(t)
        /// 
        /// The smoothing parameter ? controls the "memory" of the tensor:
        /// - ? ~ 0.1 means averaging over ~10 timesteps
        /// - Should depend on mass ratio (heavy objects need more smoothing)
        /// 
        /// Physical interpretation: Gravity doesn't respond instantaneously
        /// to matter fluctuations - there's a "light-crossing time" delay.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <returns>Smoothed stress-energy tensor component T_ij</returns>
        public double GetStressEnergyTensorAveraged(int i, int j)
        {
            // Get instantaneous (current step) value
            double T_instant = GetStressEnergyTensor(i, j);

            // Lazy initialization of the averaging buffer
            if (_averagedStressTensor == null)
            {
                _averagedStressTensor = new double[N, N];
                // On first call, just return instantaneous value (no history yet)
                _averagedStressTensor[i, j] = T_instant;
                _averagedStressTensor[j, i] = T_instant; // Symmetry
                return T_instant;
            }

            // EMA smoothing parameter (alpha)
            // alpha ~ 0.05 означает усреднение за ~20 шагов симул€ции.
            // Ёто раздел€ет временные масштабы: быстрые пол€ (HighFreq) vs медленна€ метрика (LowFreq).
            // Could be made adaptive based on local mass ratio or curvature
            const double alpha = 0.05;

            // Exponential Moving Average: T_new = (1 - ?) ? T_old + ? ? T_instant
            double T_old = _averagedStressTensor[i, j];
            double T_new = (1.0 - alpha) * T_old + alpha * T_instant;

            // Store for next iteration (maintain symmetry)
            _averagedStressTensor[i, j] = T_new;
            _averagedStressTensor[j, i] = T_new;

            return T_new;
        }

        /// <summary>
        /// Compute soft chirality penalty for edges connecting nodes in the same sublattice.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX (Critical Fix #1):
        /// =============================================
        /// In staggered fermions, the graph should ideally be bipartite (alternating
        /// even/odd sites). However, strict bipartite enforcement prevents ALL triangles,
        /// making Forman-Ricci curvature always negative (tree-like geometry).
        /// 
        /// Instead of hard rejection, we add a soft mass penalty:
        /// - Same-sublattice edges get extra "mass" that suppresses fermion propagation
        /// - This is analogous to Wilson fermion mass term in lattice QCD
        /// - The penalty makes same-sublattice edges energetically costly but not forbidden
        /// - Triangles can still form (needed for gravity), just at higher energy cost
        /// 
        /// Physics: The penalty acts as effective fermion mass on "wrong" edges,
        /// suppressing fermion doubling without destroying curvature structure.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #3: Now uses IsSameParity (topological parity)
        /// instead of (i % 2) == (j % 2) (array index parity) for background independence.
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <param name="deltaW">Change in edge weight</param>
        /// <returns>Action penalty for same-sublattice edge (0 for bipartite edges)</returns>
        private double ComputeChiralityPenalty(int i, int j, double deltaW)
        {
            // RQ-HYPOTHESIS CHECKLIST FIX #3: Use topological parity
            // Check if nodes are in same sublattice using IsSameParity (graph 2-coloring)
            // instead of (i % 2) == (j % 2) (array index parity)
            bool sameSublattice = IsSameParity(i, j);

            if (!sameSublattice)
                return 0.0; // No penalty for bipartite edges (correct chirality)

            // Soft penalty for same-sublattice edges
            // The penalty coefficient is derived from fermion physics:
            // ?_chiral ~ (a ? m_Wilson)? where a = lattice spacing, m_Wilson = Wilson mass
            // In Planck units with a = 1, this simplifies to m? ~ ? (fine structure)
            // 
            // We use ? as the natural small coupling that disfavors non-bipartite edges
            // without making them thermodynamically impossible.
            double chiralityPenalty = PhysicsConstants.FineStructureConstant;

            // For edge creation (deltaW > 0), penalty increases action ? unfavorable
            // For edge removal (deltaW < 0), penalty decreases action ? favorable
            // This naturally favors removing non-bipartite edges over bipartite ones
            return chiralityPenalty * Math.Abs(deltaW);
        }
    }
}
