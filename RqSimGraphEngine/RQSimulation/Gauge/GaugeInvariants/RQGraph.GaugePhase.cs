using System;
using System.Numerics;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Edge gauge phases for U(1) gauge field
        private double[,]? _edgePhaseU1;
        
        // RQ-HYPOTHESIS CHECKLIST FIX #1: Canonical momenta for U(1) gauge field
        // These are the "electric field" E_ij = ∂L/∂(∂φ/∂t) = dφ/dt
        // With these, we have wave equation: d²φ/dt² = F (instead of diffusion dφ/dt = F)
        private double[,]? _edgeMomentumU1;
        
        // Configuration constants
        private const double GaugeCouplingConstant = 0.1;
        private const double PlaquetteWeight = 0.05;
        
        /// <summary>
        /// Public access to U(1) gauge phases for GPU sync.
        /// </summary>
        public double[,]? EdgePhaseU1 => _edgePhaseU1;
        
        /// <summary>
        /// Public access to U(1) gauge momenta (electric field) for diagnostics.
        /// </summary>
        public double[,]? EdgeMomentumU1 => _edgeMomentumU1;
        
        /// <summary>
        /// Initialize gauge phases and momenta on edges
        /// </summary>
        public void InitEdgeGaugePhases()
        {
            _edgePhaseU1 = new double[N, N];
            _edgeMomentumU1 = new double[N, N];
            
            // Initialize with small random phases, zero momenta (ground state)
            var rng = new Random();
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (Edges[i, j])
                    {
                        double phase = (rng.NextDouble() - 0.5) * 0.1; // Small random phase
                        _edgePhaseU1[i, j] = phase;
                        _edgePhaseU1[j, i] = -phase; // Antisymmetric
                        
                        // Zero initial momentum (vacuum state - no photons)
                        _edgeMomentumU1[i, j] = 0.0;
                        _edgeMomentumU1[j, i] = 0.0; // Symmetric (E-field magnitude)
                    }
                }
            }
        }
        
        /// <summary>
        /// Get edge gauge data for edge (i,j)
        /// </summary>
        public EdgeGaugeData GetEdgeGaugeData(int i, int j)
        {
            if (_edgePhaseU1 == null)
                InitEdgeGaugePhases();
            
            double weight = Edges[i, j] ? Weights[i, j] : 0.0;
            double phase = _edgePhaseU1?[i, j] ?? 0.0;
            
            return new EdgeGaugeData(weight, phase);
        }
        
        /// <summary>
        /// Update gauge phases based on currents and field strength.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #1: SYMPLECTIC YANG-MILLS DYNAMICS
        /// ================================================================
        /// CRITICAL FIX: Replaced first-order diffusion equation with second-order wave equation.
        /// 
        /// OLD (WRONG - diffusion, fields decay):
        ///   dφ/dt = -g*(J + curl)  →  φ(t) → 0 exponentially
        /// 
        /// NEW (CORRECT - wave equation, photons propagate):
        ///   d²φ/dt² = -g*(J + curl)  →  φ(t) = oscillating waves
        /// 
        /// Implementation uses Velocity Verlet (symplectic leapfrog):
        ///   Step 1: π(t+½dt) = π(t) + ½dt * F(φ(t))
        ///   Step 2: φ(t+dt) = φ(t) + dt * π(t+½dt) / m
        ///   Step 3: π(t+dt) = π(t+½dt) + ½dt * F(φ(t+dt))
        /// 
        /// where π is the canonical momentum (electric field E) and F is the force
        /// (derivative of Yang-Mills action with respect to phase).
        /// </summary>
        public void UpdateGaugePhases(double dt)
        {
            if (_edgePhaseU1 == null)
                InitEdgeGaugePhases();
            
            if (_edgeMomentumU1 == null)
                _edgeMomentumU1 = new double[N, N];
            
            // Choose between symplectic (wave) and diffusive (legacy) evolution
            if (PhysicsConstants.EnableSymplecticGaugeEvolution)
            {
                UpdateGaugePhasesSymplectic(dt);
            }
            else
            {
                UpdateGaugePhasesDiffusive(dt);
            }
        }
        
        /// <summary>
        /// Symplectic (Velocity Verlet) gauge phase evolution - CORRECT wave dynamics.
        /// Implements second-order Yang-Mills equations: d²A/dt² = F.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #6: Lapse Function Integration
        /// ============================================================
        /// Each edge uses local proper time: dt_edge = dt * √(N_i * N_j)
        /// where N_i is the lapse function at node i. This ensures that
        /// gauge fields near "black holes" (high-curvature regions) evolve
        /// slower, respecting gravitational time dilation.
        /// </summary>
        private void UpdateGaugePhasesSymplectic(double dt)
        {
            double mass = PhysicsConstants.GaugeMomentumMassU1;
            double damping = PhysicsConstants.GaugeFieldDamping;
            
            // ===== STEP 1: Half-step momentum update =====
            // π(t+½dt) = π(t) + ½dt * F(φ(t))
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i >= j) continue; // Only process each edge once
                    
                    // RQ-HYPOTHESIS CHECKLIST FIX #6: Local proper time on edge
                    // dt_edge = dt * sqrt(N_i * N_j) implements gravitational time dilation
                    double localDt = dt;
                    if (PhysicsConstants.EnableLapseSynchronizedGeometry)
                    {
                        double N_i = GetLocalLapse(i);
                        double N_j = GetLocalLapse(j);
                        localDt = dt * Math.Sqrt(N_i * N_j);
                    }
                    
                    double halfDt = localDt * 0.5;
                    double force = ComputeGaugeForce(i, j);
                    
                    // Include damping: π' = π*(1-γ) + F*dt
                    _edgeMomentumU1![i, j] = _edgeMomentumU1[i, j] * (1.0 - damping * halfDt) + force * halfDt;
                    _edgeMomentumU1[j, i] = -_edgeMomentumU1[i, j]; // Antisymmetric momentum
                }
            }
            
            // ===== STEP 2: Full-step position (phase) update =====
            // φ(t+dt) = φ(t) + dt * π(t+½dt) / m
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i >= j) continue;
                    
                    // Local proper time for this edge
                    double localDt = dt;
                    if (PhysicsConstants.EnableLapseSynchronizedGeometry)
                    {
                        double N_i = GetLocalLapse(i);
                        double N_j = GetLocalLapse(j);
                        localDt = dt * Math.Sqrt(N_i * N_j);
                    }
                    
                    _edgePhaseU1![i, j] += localDt * _edgeMomentumU1![i, j] / mass;
                    _edgePhaseU1[j, i] = -_edgePhaseU1[i, j]; // Antisymmetric phase
                    
                    // Wrap to [-π, π] for compactness
                    WrapPhase(ref _edgePhaseU1[i, j]);
                    _edgePhaseU1[j, i] = -_edgePhaseU1[i, j];
                }
            }
            
            // ===== STEP 3: Half-step momentum update with new positions =====
            // π(t+dt) = π(t+½dt) + ½dt * F(φ(t+dt))
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i >= j) continue;
                    
                    // Local proper time for this edge
                    double localDt = dt;
                    if (PhysicsConstants.EnableLapseSynchronizedGeometry)
                    {
                        double N_i = GetLocalLapse(i);
                        double N_j = GetLocalLapse(j);
                        localDt = dt * Math.Sqrt(N_i * N_j);
                    }
                    
                    double halfDt = localDt * 0.5;
                    double force = ComputeGaugeForce(i, j);
                    
                    _edgeMomentumU1![i, j] = _edgeMomentumU1[i, j] * (1.0 - damping * halfDt) + force * halfDt;
                    _edgeMomentumU1[j, i] = -_edgeMomentumU1[i, j];
                }
            }
        }
        
        /// <summary>
        /// Compute gauge force F = -∂S/∂φ for edge (i,j).
        /// Force = -g*(current + curl_term) where:
        /// - current comes from matter (fermions + scalar field)
        /// - curl_term comes from plaquette field strength
        /// </summary>
        private double ComputeGaugeForce(int i, int j)
        {
            // Find minimal cycles (plaquettes) through this edge
            double curvatureSum = 0;
            int plaquetteCount = 0;
            
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                if (Edges[j, k]) // Triangle i-j-k
                {
                    // Sum phases around plaquette
                    double phaseSum = _edgePhaseU1![i, j] 
                                    + _edgePhaseU1[j, k] 
                                    + _edgePhaseU1[k, i];
                    
                    // Field strength (curvature) proportional to sin(phase sum)
                    // Force is derivative: d(1-cos(φ))/dφ = sin(φ)
                    curvatureSum += Math.Sin(phaseSum);
                    plaquetteCount++;
                }
            }
            
            // Average curvature contribution
            double avgCurvature = plaquetteCount > 0 ? curvatureSum / plaquetteCount : 0;
            
            // Compute current through edge (from fermion density if available)
            double current = ComputeEdgeCurrent(i, j);
            
            // Add scalar field back-reaction current (Higgs mechanism)
            double scalarCurrent = ComputeScalarFieldCurrent(i, j);
            current += scalarCurrent;
            
            // Force = -∂S/∂φ = -g*(J + β*curl)
            return -GaugeCouplingConstant * (current + PlaquetteWeight * avgCurvature);
        }
        
        /// <summary>
        /// Wrap phase to [-π, π] interval (compactification of U(1)).
        /// </summary>
        private static void WrapPhase(ref double phase)
        {
            while (phase > Math.PI) phase -= 2.0 * Math.PI;
            while (phase < -Math.PI) phase += 2.0 * Math.PI;
        }
        
        /// <summary>
        /// Legacy diffusive gauge phase evolution (first-order, fields decay).
        /// Kept for backward compatibility and testing.
        /// </summary>
        private void UpdateGaugePhasesDiffusive(double dt)
        {
            double[,] phaseUpdates = new double[N, N];
            
            // Compute plaquette contributions (field curvature)
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i >= j) continue; // Only process each edge once
                    
                    double force = ComputeGaugeForce(i, j);
                    double dPhase = force * dt;
                    
                    phaseUpdates[i, j] = dPhase;
                    phaseUpdates[j, i] = -dPhase; // Antisymmetric
                }
            }
            
            // Apply updates and wrap to [-π, π]
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    if (Edges[i, j])
                    {
                        _edgePhaseU1![i, j] += phaseUpdates[i, j];
                        WrapPhase(ref _edgePhaseU1[i, j]);
                    }
                }
            }
        }
        
        /// <summary>
        /// PHYSICS FIX TASK 3: Calculate gauge phase for a new edge that minimizes flux frustration.
        /// 
        /// Problem: When a new edge (i,j) is created, assigning a random phase can create
        /// non-physical magnetic flux in newly formed triangles (plaquettes).
        /// 
        /// Solution: Initialize the phase such that Wilson loops around triangles containing
        /// the new edge have minimal total flux: Phase(i,j) ≈ -(Phase(j,k) + Phase(k,i))
        /// for each common neighbor k.
        /// 
        /// This ensures gauge field consistency when topology changes.
        /// </summary>
        /// <param name="i">First node of new edge</param>
        /// <param name="j">Second node of new edge</param>
        /// <returns>Optimal phase for the new edge that minimizes flux in surrounding plaquettes</returns>
        public double CalculateMinimalFluxPhase(int i, int j)
        {
            if (_edgePhaseU1 == null)
                InitEdgeGaugePhases();

            // Find common neighbors that would form triangles with the new edge
            Complex sumPhasor = Complex.Zero;
            int triangleCount = 0;

            foreach (int k in Neighbors(i))
            {
                // Check if k is also a neighbor of j (forms triangle i-j-k)
                if (Edges[j, k])
                {
                    // For triangle i->j->k->i, the Wilson loop phase is:
                    //   Φ_loop = θ_ij + θ_jk + θ_ki
                    // We want Φ_loop ≈ 0 (minimal flux)
                    // So θ_ij = -(θ_jk + θ_ki)

                    double phi_jk = _edgePhaseU1![j, k];
                    double phi_ki = _edgePhaseU1[k, i];
                    double targetPhase = -(phi_jk + phi_ki);

                    // Accumulate as complex phasor for proper averaging of angles
                    sumPhasor += Complex.FromPolarCoordinates(1.0, targetPhase);
                    triangleCount++;
                }
            }

            // If no triangles formed, phase is arbitrary (use zero for stability)
            if (triangleCount == 0)
                return 0.0;

            // Return the average target phase (argument of the phasor sum)
            return sumPhasor.Phase;
        }

        /// <summary>
        /// Initialize gauge phase for a newly created edge using minimal flux principle.
        /// Call this when creating edges via topology changes.
        /// </summary>
        /// <param name="i">First node of new edge</param>
        /// <param name="j">Second node of new edge</param>
        public void InitializeEdgePhaseMinimalFlux(int i, int j)
        {
            if (_edgePhaseU1 == null)
                InitEdgeGaugePhases();

            double phase = CalculateMinimalFluxPhase(i, j);
            _edgePhaseU1![i, j] = phase;
            _edgePhaseU1[j, i] = -phase; // Antisymmetric for U(1)
        }

        // NOTE: ComputeScalarFieldCurrent is implemented in RQGraph.FieldTheory.cs (line 229)
        // It computes: J_ij = g * φ_i * φ_j * sin(θ_ij)
        // Do not duplicate here - use the method from FieldTheory which is gauge-covariant
        
        /// <summary>
        /// Compute current through edge based on fermion/excitation flow.
        /// Used by gauge phase evolution to determine force from matter coupling.
        /// </summary>
        private double ComputeEdgeCurrent(int i, int j)
        {
            // Current based on density difference
            double densityI = State[i] == NodeState.Excited ? 1.0 : 0.0;
            double densityJ = State[j] == NodeState.Excited ? 1.0 : 0.0;
            
            // Add contribution from local potential if available
            if (LocalPotential != null && i < LocalPotential.Length && j < LocalPotential.Length)
            {
                densityI += LocalPotential[i] * 0.1;
                densityJ += LocalPotential[j] * 0.1;
            }
            
            // Current flows from high to low density
            return (densityI - densityJ) * Weights[i, j];
        }
        
        /// <summary>
        /// Compute gauge field energy (sum over plaquettes).
        /// Used for energy tracking in unified physics step.
        /// </summary>
        public double ComputeGaugeFieldEnergy()
        {
            if (_edgePhaseU1 == null)
                return 0.0;
            
            double energy = 0;
            int plaquetteCount = 0;
            
            // Sum over all triangular plaquettes
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;
                    
                    foreach (int k in Neighbors(i))
                    {
                        if (k <= j || !Edges[j, k]) continue;
                        
                        // Triangle i-j-k forms a plaquette
                        double phaseSum = _edgePhaseU1[i, j] 
                                        + _edgePhaseU1[j, k] 
                                        + _edgePhaseU1[k, i];
                        
                        // Energy: E_gauge = sum(1 - cos(phase around plaquette))
                        energy += 1.0 - Math.Cos(phaseSum);
                        plaquetteCount++;
                    }
                }
            }
            
            // Add kinetic energy from gauge momenta (RQ-HYPOTHESIS FIX #1)
            if (_edgeMomentumU1 != null)
            {
                double mass = PhysicsConstants.GaugeMomentumMassU1;
                for (int i = 0; i < N; i++)
                {
                    foreach (int j in Neighbors(i))
                    {
                        if (j <= i) continue;
                        double p = _edgeMomentumU1[i, j];
                        energy += 0.5 * p * p / mass; // Kinetic energy = p²/2m
                    }
                }
            }
            
            return energy;
        }

        // ================================================================
        // GAUGE-AWARE TOPOLOGY HELPERS (RQ-HYPOTHESIS CHECKLIST ITEM 2)
        // ================================================================

        /// <summary>
        /// Get the U(1) gauge phase for edge (i,j).
        /// Returns 0 if gauge phases not initialized or edge doesn't exist.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Gauge Invariance
        /// =================================================
        /// This provides read access to edge phases for checking
        /// flux before topology changes.
        /// </summary>
        /// <param name="i">First endpoint.</param>
        /// <param name="j">Second endpoint.</param>
        /// <returns>Phase in radians.</returns>
        public double GetEdgePhase(int i, int j)
        {
            if (_edgePhaseU1 == null)
                return 0.0;

            if (i < 0 || i >= N || j < 0 || j >= N)
                return 0.0;

            if (!Edges[i, j])
                return 0.0;

            return _edgePhaseU1[i, j];
        }

        /// <summary>
        /// Add a phase increment to edge (i,j) for flux redistribution.
        /// Maintains antisymmetry: φ_ji = -φ_ij.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Flux Redistribution
        /// ====================================================
        /// When an edge with non-trivial flux is removed, its phase
        /// must be redistributed to an alternate path. This method
        /// adds the redistributed phase to each edge in the path.
        /// </summary>
        /// <param name="i">First endpoint.</param>
        /// <param name="j">Second endpoint.</param>
        /// <param name="phaseIncrement">Phase to add (radians).</param>
        public void AddEdgePhase(int i, int j, double phaseIncrement)
        {
            if (_edgePhaseU1 == null)
                InitEdgeGaugePhases();

            if (i < 0 || i >= N || j < 0 || j >= N)
                return;

            if (!Edges[i, j])
                return;

            _edgePhaseU1![i, j] += phaseIncrement;
            _edgePhaseU1[j, i] = -_edgePhaseU1[i, j]; // Maintain antisymmetry

            // Wrap to [-π, π]
            WrapPhase(ref _edgePhaseU1[i, j]);
            _edgePhaseU1[j, i] = -_edgePhaseU1[i, j];
        }

        /// <summary>
        /// Set the U(1) gauge phase for edge (i,j) directly.
        /// Maintains antisymmetry: φ_ji = -φ_ij.
        /// </summary>
        /// <param name="i">First endpoint.</param>
        /// <param name="j">Second endpoint.</param>
        /// <param name="phase">New phase value (radians).</param>
        public void SetEdgePhase(int i, int j, double phase)
        {
            if (_edgePhaseU1 == null)
                InitEdgeGaugePhases();

            if (i < 0 || i >= N || j < 0 || j >= N)
                return;

            if (!Edges[i, j])
                return;

            _edgePhaseU1![i, j] = phase;
            _edgePhaseU1[j, i] = -phase; // Antisymmetric for U(1)

            // Wrap to [-π, π]
            WrapPhase(ref _edgePhaseU1[i, j]);
            _edgePhaseU1[j, i] = -_edgePhaseU1[i, j];
        }

        /// <summary>
        /// Check if an edge carries non-trivial gauge flux.
        /// Returns true if the phase magnitude exceeds the trivial threshold.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 2: Gauge Protection
        /// ==================================================
        /// Edges with non-trivial flux require special handling during
        /// topology changes to preserve gauge invariance (Gauss law).
        /// </summary>
        /// <param name="i">First endpoint.</param>
        /// <param name="j">Second endpoint.</param>
        /// <param name="threshold">Phase threshold for considering trivial (default: 0.1 rad).</param>
        /// <returns>True if edge carries non-trivial flux.</returns>
        public bool HasNonTrivialFlux(int i, int j, double threshold = 0.1)
        {
            double phase = GetEdgePhase(i, j);

            // Normalize to [0, 2π)
            phase = phase % (2 * Math.PI);
            if (phase < 0) phase += 2 * Math.PI;

            // Check if far from 0 or 2π (trivial values)
            return phase > threshold && phase < (2 * Math.PI - threshold);
        }
    }
}
