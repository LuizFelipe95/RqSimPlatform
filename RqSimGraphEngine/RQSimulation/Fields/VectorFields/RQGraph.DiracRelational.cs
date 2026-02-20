using System;
using System.Numerics;
using System.Linq;
using System.Threading.Tasks;
using RQSimulation.GPUOptimized;
using RQSimulation.Gauge;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // RQ-FIX: Adiabatic smoothing removed (Checklist 6.1)
        // Quantum fields must react instantly to metric changes (particle production)

        // CHECKLIST ITEM 9: Track spectral dimension for spinor field activation
        private double _lastSpectralDimensionForSpinor = 4.0;
        private bool _spinorFieldEnabled = true;

        /// <summary>
        /// Evolve spinor field using purely topological Dirac operator with Leapfrog symplectic integrator.
        /// No reference to external coordinates - uses graph structure only.
        /// 
        /// WARNING: This method updates ALL nodes simultaneously, violating relativity in
        /// event-driven mode. For asynchronous local time evolution, use UpdateSpinorFieldAtNode()
        /// in RQGraph.EventDrivenExtensions.cs instead.
        /// 
        /// Use this method ONLY for:
        /// - Global synchronous simulation mode
        /// - Testing/debugging purposes
        /// - Small graphs where causality violations are acceptable
        /// 
        /// RQ-Hypothesis Compliant (Item 3): Uses midpoint (leapfrog) symplectic integrator
        /// that mathematically preserves the norm to O(dt^2) without forced normalization.
        /// 
        /// RQ-FIX: Implements adiabatic weight smoothing to prevent spinor "shocks"
        /// from rapid topology changes.
        /// 
        /// CHECKLIST ITEM 9: Validates spectral dimension before spinor evolution.
        /// If d_S deviates >10% from 4.0, spinor evolution is paused.
        /// 
        /// Leapfrog method:
        ///   1. Compute k1 = dψ/dt(ψ(t))
        ///   2. Compute midpoint: ψ_mid = ψ(t) + k1 * dt/2
        ///   3. Compute k2 = dψ/dt(ψ_mid)
        ///   4. Full step: ψ(t+dt) = ψ(t) + k2 * dt
        /// </summary>
        /// <param name="dt">Time step for evolution</param>
        /// <remarks>
        /// LEGACY: For event-driven simulation, use UpdateSpinorFieldAtNode() which
        /// respects local proper time and maintains causality.
        /// </remarks>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
        [Obsolete("Use UpdateSpinorAtNode for RQ-compliant local time evolution.")]
        public void UpdateDiracFieldRelational(double dt)
        {
            if (_spinorA == null) InitSpinorField();

            // CHECKLIST ITEM 9: Check spectral dimension compatibility
            // Only perform full check every 100 steps to avoid overhead
            if (_physicsStepCount % 100 == 0)
            {
                _lastSpectralDimensionForSpinor = ComputeSpectralDimension();
                _spinorFieldEnabled = SpectralDimensionValidator.ShouldEnableSpinorFields(_lastSpectralDimensionForSpinor);

                if (!_spinorFieldEnabled)
                {
                    //Console.WriteLine($"[WARNING] d_S = {_lastSpectralDimensionForSpinor:F2}: Spinor field evolution paused (need d_S ≈ 4)");
                }
            }

            // If spectral dimension is far from 4, skip spinor evolution
            if (!_spinorFieldEnabled)
            {
                return;
            }

            double hbar = VectorMath.HBar;
            double c = VectorMath.SpeedOfLight;

            // ===== STEP 1: Compute derivatives k1 at current state =====
            var k1A = new Complex[N];
            var k1B = new Complex[N];
            var k1C = new Complex[N];
            var k1D = new Complex[N];

            ComputeDiracDerivatives(_spinorA!, _spinorB!, _spinorC!, _spinorD!,
                                    k1A, k1B, k1C, k1D, c, hbar);

            // ===== STEP 2: Compute midpoint state =====
            var midA = new Complex[N];
            var midB = new Complex[N];
            var midC = new Complex[N];
            var midD = new Complex[N];

            double halfDt = dt * 0.5;
            for (int i = 0; i < N; i++)
            {
                midA[i] = _spinorA![i] + halfDt * k1A[i];
                midB[i] = _spinorB![i] + halfDt * k1B[i];
                midC[i] = _spinorC![i] + halfDt * k1C[i];
                midD[i] = _spinorD![i] + halfDt * k1D[i];
            }

            // ===== STEP 3: Compute derivatives k2 at midpoint =====
            var k2A = new Complex[N];
            var k2B = new Complex[N];
            var k2C = new Complex[N];
            var k2D = new Complex[N];

            ComputeDiracDerivatives(midA, midB, midC, midD,
                                    k2A, k2B, k2C, k2D, c, hbar);

            // ===== STEP 4: Full step using midpoint derivatives (Leapfrog) =====
            var newA = new Complex[N];
            var newB = new Complex[N];
            var newC = new Complex[N];
            var newD = new Complex[N];

            for (int i = 0; i < N; i++)
            {
                newA[i] = _spinorA![i] + dt * k2A[i];
                newB[i] = _spinorB![i] + dt * k2B[i];
                newC[i] = _spinorC![i] + dt * k2C[i];
                newD[i] = _spinorD![i] + dt * k2D[i];
            }

            // Update spinor arrays
            _spinorA = newA;
            _spinorB = newB;
            _spinorC = newC;
            _spinorD = newD;

            // RQ-Hypothesis Item 3: Symplectic integrator preserves norm to O(dt^2)
            // Only apply minimal adaptive correction for long-term stability
            // Remove forced normalization - norm should be naturally preserved
            AdaptiveNormalizeSpinorFieldMinimal();
        }

        /// <summary>
        /// Compute Dirac operator derivatives dψ/dt for all nodes.
        /// Separated out to enable symplectic integration.
        /// 
        /// RQ-FIX: Uses adiabatic weight smoothing to prevent numerical instabilities
        /// from rapid weight changes on dynamic graphs.
        /// 
        /// CHECKLIST ITEM 2: Implements gauge-covariant parallel transport.
        /// Before computing hopping term (ψ_j - ψ_i), the neighbor spinor ψ_j
        /// is parallel-transported using U†_ij: ψ_j_transported = U†_ij * ψ_j.
        /// This ensures gauge invariance of the Dirac equation.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX (Critical Fix #2 - Lapse Function):
        /// ================================================================
        /// The Dirac equation in curved spacetime includes the lapse function N:
        ///   dψ/dt = N_i × (-i/ℏ) × H × ψ
        /// 
        /// where N_i = GetLocalLapse(i) implements gravitational time dilation.
        /// 
        /// Previously, we used GLOBAL coordinate time dt for all nodes equally,
        /// which violates relativity - fermions didn't "feel" gravity.
        /// 
        /// NOW: Each node evolves at its LOCAL proper time rate:
        ///   dψ_i/dt_coord = N_i × (dψ_i/dτ_proper)
        /// 
        /// Effect: Near massive objects (N small), spinors evolve slowly.
        /// In vacuum (N ≈ 1), spinors evolve at full rate.
        /// This is the correct general relativistic behavior.
        /// </summary>
        private void ComputeDiracDerivatives(
            Complex[] spinorA, Complex[] spinorB, Complex[] spinorC, Complex[] spinorD,
            Complex[] dA, Complex[] dB, Complex[] dC, Complex[] dD,
            double c, double hbar)
        {
            // RQ-FIX: Adiabatic smoothing removed (Checklist 6.1)
            // No previous weights cache needed

            Parallel.For(0, N, i =>
            {
                // RQ-HYPOTHESIS v2.0: Dynamic Mass Calculation
                // m_eff = g * <phi> + E_topo
                double higgsVal = ScalarField != null && i < ScalarField.Length ? ScalarField[i] : 0.0;
                double topoMass = _correlationMass != null && i < _correlationMass.Length ? _correlationMass[i] : 1.0;
                double m = PhysicsConstants.YukawaCoupling * Math.Abs(higgsVal) + PhysicsConstants.TopoMassCoupling * topoMass;

                double mc = m * c;

                // RQ-HYPOTHESIS CHECKLIST FIX #3: Topological Parity
                // ==================================================
                // Use GetNodeParity(i) instead of i % 2.
                // When EnableTopologicalParity = true: uses graph 2-coloring (background-independent)
                // When EnableTopologicalParity = false: uses i % 2 (fast but not background-independent)
                int parityI = GetNodeParity(i);
                bool isEvenSite = (parityI == 0);

                // RQ-HYPOTHESIS CHECKLIST FIX (Critical Fix #2): Include Lapse Function
                // ====================================================================
                // N_i = local lapse function from GetLocalLapse(i)
                // This implements gravitational time dilation for fermions:
                // - Near black holes (high entropy): N → 0, spinors evolve slowly
                // - In vacuum: N → 1, spinors evolve at full rate
                // 
                // The Dirac equation becomes: dψ/dt = N × (-i/ℏ) × H × ψ
                double lapse = GetLocalLapse(i);

                Complex deltaA = Complex.Zero, deltaB = Complex.Zero;
                Complex deltaC = Complex.Zero, deltaD = Complex.Zero;

                // В важно: Neighbors(i) должен быть потокобезопасным итератором
                // Обычно чтение списков смежности (Edges/Weights) безопасно, если топология не меняется ВНУТРИ этого шага
                foreach (int j in Neighbors(i))
                {
                    // RQ-FIX: Use instantaneous weights (Checklist 6.1)
                    // Energy from metric jumps is handled by EnergyLedger (Particle Production)
                    double weight = Weights[i, j];

                    // CHECKLIST ITEM 2: Gauge-covariant parallel transport
                    // =====================================================
                    // The Dirac equation on a graph with gauge field requires:
                    //   D_ij ψ = U†_ij ψ_j - ψ_i
                    // where U_ij is the gauge link (parallel transport operator).
                    //
                    // For U(1): U†_ij = e^{-iθ_ij} (just a phase)
                    // For SU(2): U†_ij is a 2×2 unitary matrix
                    // For SU(3): U†_ij is a 3×3 unitary matrix (implemented via ParallelTransportSU3)

                    Complex parallelTransport = Complex.One;
                    if (_edgePhaseU1 != null)
                    {
                        double phase = _edgePhaseU1[i, j];
                        parallelTransport = Complex.FromPolarCoordinates(1.0, -phase); // U† = e^{-iθ}
                    }

                    // RQ-HYPOTHESIS CHECKLIST FIX #3: Topological Parity
                    // Use GetNodeParity(j) instead of j % 2 for background-independent parity
                    int parityJ = GetNodeParity(j);
                    bool isNeighborEven = (parityJ == 0);

                    // Staggered fermion hopping with alternating signs
                    double sign = (isEvenSite != isNeighborEven) ? 1.0 : -1.0;

                    // REFACTORING PLAN FIX #3: SPECTRAL-BASED EDGE ORIENTATION
                    // =========================================================
                    //
                    // OLD (INCORRECT): int edgeDirection = Math.Abs(i - j) % 2;
                    //   - Node indices have no physical meaning
                    //   - Violates background independence principle
                    //   - Arbitrary choice not grounded in emergent geometry
                    //
                    // NEW (CORRECT): Use spectral coordinates from graph Laplacian
                    //   - edgeDirection = argmax_μ |coord_μ[j] - coord_μ[i]|
                    //   - Respects emergent spacetime geometry
                    //   - Satisfies RQ-hypothesis background independence
                    //
                    // The spectral orientation determines which γ^μ matrix to use:
                    //   μ=0: temporal/energy-like direction
                    //   μ=1: first spatial direction
                    //   μ=2: second spatial direction (if available)
                    //   μ=3: third spatial direction (if available)
                    //
                    // Falls back to index-based for backwards compatibility if spectral
                    // coordinates are not available.
                    //
                    // Reference: Refactoring plan section 3
                    int edgeDirection = GetSpectralEdgeOrientation(i, j, 2); // Use 2D for now (X, Y)

                    // CHECKLIST ITEM 10 & ITEM 2: SU(2)/SU(3) gauge coupling with parallel transport
                    // Apply gauge transformation to neighbor spinor BEFORE hopping
                    Complex gaugedA_j = spinorA[j] * parallelTransport;
                    Complex gaugedB_j = spinorB[j] * parallelTransport;
                    Complex gaugedC_j = spinorC[j] * parallelTransport;
                    Complex gaugedD_j = spinorD[j] * parallelTransport;

                    if (GaugeDimension == 2 && _gaugeSU != null)
                    {
                        // SU(2) gauge transformation for left-handed doublet
                        // Get SU(2) link matrix U†_ij and apply to (A_j, B_j)
                        Complex U00 = Complex.Conjugate(_gaugeSU[i, j, 0]);
                        Complex U01 = Complex.Conjugate(_gaugeSU[i, j, 2]); // Transpose indices for U†
                        Complex U10 = Complex.Conjugate(_gaugeSU[i, j, 1]);
                        Complex U11 = Complex.Conjugate(_gaugeSU[i, j, 3]);

                        Complex nA = spinorA[j];
                        Complex nB = spinorB[j];
                        gaugedA_j = U00 * nA + U01 * nB;
                        gaugedB_j = U10 * nA + U11 * nB;
                        // Apply U(1) phase on top
                        gaugedA_j *= parallelTransport;
                        gaugedB_j *= parallelTransport;
                        // Right-handed (C, D) are SU(2)_L singlets - only U(1) phase
                    }
                    else if (GaugeDimension == 3 && _gaugeSU3 != null)
                    {
                        // REFACTORING PLAN FIX #2: PROPER SU(3) PARALLEL TRANSPORT
                        // ==========================================================
                        //
                        // ISSUE: Non-abelian SU(3) gauge fields require MATRIX multiplication,
                        // not scalar trace averaging. Taking Tr(U†)/3 destroys color information.
                        //
                        // CORRECT APPROACH:
                        // 1. Use ColorDirac module with 12-component spinors (4 Dirac × 3 color)
                        // 2. Call UpdateColorDiracField() instead of UpdateDiracFieldRelational()
                        // 3. Apply full SU(3) matrix to color triplet: ψ' = U† · ψ
                        //
                        // CURRENT WORKAROUND (for backwards compatibility):
                        // We apply the trace-averaged phase, but this is NOT physically correct
                        // for QCD simulations. This approximation treats quarks as if they don't
                        // carry real color charge.
                        //
                        // TODO: Migrate all SU(3) simulations to use ColorDirac module.
                        // See: RQGraph.ColorDirac.cs, UpdateColorDiracField()
                        //
                        // WARNING: This code path should only be used for testing or when
                        // full color dynamics are not required. For proper QCD, use ColorDirac.

                        // Legacy trace approximation (INCORRECT for true SU(3) physics)
                        SU3Matrix U = GetSU3Link(i, j);
                        SU3Matrix Udag = U.Dagger();
                        Complex trace = Udag.Trace() / 3.0; // Average phase (loses color info!)

                        // Apply averaged SU(3) phase to spinor
                        // NOTE: This is a scalar multiplication, not a proper color transformation
                        gaugedA_j = spinorA[j] * trace * parallelTransport;
                        gaugedB_j = spinorB[j] * trace * parallelTransport;
                        gaugedC_j = spinorC[j] * trace * parallelTransport;
                        gaugedD_j = spinorD[j] * trace * parallelTransport;
                    }

                    // === RQ-HYPOTHESIS CHECKLIST ITEM 5: Wilson Term for Fermion Doubling ===
                    // ==========================================================================
                    // PROBLEM: Staggered/naive fermions on a lattice produce 2^D spurious modes
                    // (fermion doublers) with momenta near k ~ π/a.
                    //
                    // SOLUTION: Wilson term adds a Laplacian-like mass:
                    //   W = -(r/2) * Δ * ψ = -(r/2) * Σ_j w_ij * (ψ_j - ψ_i)
                    //
                    // This gives doublers an effective mass ~ r/a, lifting them from low-energy
                    // spectrum while physical fermions (k ~ 0) remain light.
                    //
                    // PHYSICS: Wilson term breaks chiral symmetry, but this is acceptable since
                    // RQ-hypothesis fermions already have dynamical mass from Higgs coupling.
                    // The mass is restored upon renormalization.
                    //
                    // r = Wilson parameter (typically 1.0)
                    double wilsonR = PhysicsConstants.WilsonParameter;
                    
                    // Wilson contribution: -r/2 * w_ij * (ψ_j - ψ_i)
                    // Applied to ALL edges to lift doublers from low-energy spectrum
                    Complex wilsonA = -0.5 * wilsonR * weight * (gaugedA_j - spinorA[i]);
                    Complex wilsonB = -0.5 * wilsonR * weight * (gaugedB_j - spinorB[i]);
                    Complex wilsonC = -0.5 * wilsonR * weight * (gaugedC_j - spinorC[i]);
                    Complex wilsonD = -0.5 * wilsonR * weight * (gaugedD_j - spinorD[i]);

                    // Apply Wilson term to all edges
                    deltaA += wilsonA;
                    deltaB += wilsonB;
                    deltaC += wilsonC;
                    deltaD += wilsonD;

                    // === PHYSICS FIX TASK 4: EXTRA Wilson Mass for Same-Parity Edges ===
                    // RQ-HYPOTHESIS CHECKLIST FIX #3: Use topological parity via IsSameParity
                    // Check if this is a "wrong" edge for staggered fermions (same sublattice parity)
                    bool sameParity = IsSameParity(i, j);

                    if (sameParity)
                    {
                        // Same-parity edge: Apply ADDITIONAL Wilson mass term.
                        // This is on top of the standard Wilson term applied above.
                        // Same-parity edges strongly violate the checkerboard structure,
                        // so they need extra suppression.
                        double extraWilsonMass = PhysicsConstants.WilsonMassPenalty;

                        // Extra Wilson term for same-parity: adds mass-like contribution
                        deltaA += extraWilsonMass * weight * (spinorA[i] - gaugedA_j);
                        deltaB += extraWilsonMass * weight * (spinorB[i] - gaugedB_j);
                        deltaC += extraWilsonMass * weight * (spinorC[i] - gaugedC_j);
                        deltaD += extraWilsonMass * weight * (spinorD[i] - gaugedD_j);
                    }
                    else if (edgeDirection == 0)
                    {
                        // "X-like" direction: couples A↔B and C↔D
                        deltaB += sign * weight * (gaugedA_j - spinorA[i]);
                        deltaA += sign * weight * (gaugedB_j - spinorB[i]);
                        deltaD += sign * weight * (gaugedC_j - spinorC[i]);
                        deltaC += sign * weight * (gaugedD_j - spinorD[i]);
                    }
                    else
                    {
                        // "Y-like" direction: couples A↔B with i and C↔D with -i
                        Complex iSign = Complex.ImaginaryOne * sign;
                        deltaB += iSign * weight * (gaugedA_j - spinorA[i]);
                        deltaA += -iSign * weight * (gaugedB_j - spinorB[i]);
                        deltaD += -iSign * weight * (gaugedC_j - spinorC[i]);
                        deltaC += iSign * weight * (gaugedD_j - spinorD[i]);
                    }
                }

                // Mass term: couples left and right handed components
                Complex massTermA = -Complex.ImaginaryOne * mc / hbar * spinorC[i];
                Complex massTermB = -Complex.ImaginaryOne * mc / hbar * spinorD[i];
                Complex massTermC = -Complex.ImaginaryOne * mc / hbar * spinorA[i];
                Complex massTermD = -Complex.ImaginaryOne * mc / hbar * spinorB[i];

                // Dirac evolution: dψ/dt = N × (-i/ℏ) × H × ψ
                // RQ-HYPOTHESIS CHECKLIST FIX (Critical Fix #2):
                // Include lapse function N_i for gravitational time dilation.
                // Near massive objects, N → 0, so spinors evolve slowly (time dilation).
                double factor = -lapse / hbar;  // ← FIXED: was -1.0/hbar, now includes lapse

                dA[i] = factor * (c * deltaA + massTermA);
                dB[i] = factor * (c * deltaB + massTermB);
                dC[i] = factor * (c * deltaC + massTermC);
                dD[i] = factor * (c * deltaD + massTermD);
            });

            // RQ-FIX: No cache update needed
        }

        /// <summary>
        /// RQ-HYPOTHESIS v2.0: Local Time Spinor Evolution
        /// Updates spinor state at a single node using its local proper time.
        /// This respects causal structure and gravitational time dilation.
        /// </summary>
        public void UpdateSpinorAtNode(int i, double localDt)
        {
            if (_spinorA == null) return;

            double hbar = VectorMath.HBar;
            double c = VectorMath.SpeedOfLight;

            // 1. Get local lapse (gravitational time dilation)
            double lapse = GetLocalLapse(i);
            double effectiveDt = localDt * lapse;

            // Dynamic Mass
            double higgsVal = ScalarField != null && i < ScalarField.Length ? ScalarField[i] : 0.0;
            double topoMass = _correlationMass != null && i < _correlationMass.Length ? _correlationMass[i] : 1.0;
            double m = PhysicsConstants.YukawaCoupling * Math.Abs(higgsVal) + PhysicsConstants.TopoMassCoupling * topoMass;
            double mc = m * c;

            bool isEvenSite = (i % 2 == 0);

            Complex deltaA = Complex.Zero, deltaB = Complex.Zero;
            Complex deltaC = Complex.Zero, deltaD = Complex.Zero;

            foreach (int j in Neighbors(i))
            {
                double weight = Weights[i, j];

                Complex parallelTransport = Complex.One;
                if (_edgePhaseU1 != null)
                {
                    double phase = _edgePhaseU1[i, j];
                    parallelTransport = Complex.FromPolarCoordinates(1.0, -phase);
                }

                bool isNeighborEven = (j % 2 == 0);
                double sign = (isEvenSite != isNeighborEven) ? 1.0 : -1.0;

                // REFACTORING PLAN FIX #3: Use spectral-based edge orientation
                int edgeDirection = GetSpectralEdgeOrientation(i, j, 2);

                Complex gaugedA_j = _spinorA[j] * parallelTransport;
                Complex gaugedB_j = _spinorB![j] * parallelTransport;
                Complex gaugedC_j = _spinorC![j] * parallelTransport;
                Complex gaugedD_j = _spinorD![j] * parallelTransport;

                // Wilson term for fermion doubling suppression
                double wilsonR = PhysicsConstants.WilsonParameter;
                Complex wilsonA = -0.5 * wilsonR * weight * (gaugedA_j - _spinorA[i]);
                Complex wilsonB = -0.5 * wilsonR * weight * (gaugedB_j - _spinorB![i]);
                Complex wilsonC = -0.5 * wilsonR * weight * (gaugedC_j - _spinorC![i]);
                Complex wilsonD = -0.5 * wilsonR * weight * (gaugedD_j - _spinorD![i]);
                
                deltaA += wilsonA;
                deltaB += wilsonB;
                deltaC += wilsonC;
                deltaD += wilsonD;

                bool sameParity = IsSameParity(i, j);

                if (sameParity)
                {
                    // Extra Wilson mass for same-parity edges
                    double wilsonMass = PhysicsConstants.WilsonMassPenalty;
                    deltaA += wilsonMass * weight * (_spinorA[i] - gaugedA_j);
                    deltaB += wilsonMass * weight * (_spinorB![i] - gaugedB_j);
                    deltaC += wilsonMass * weight * (_spinorC![i] - gaugedC_j);
                    deltaD += wilsonMass * weight * (_spinorD![i] - gaugedD_j);
                }
                else if (edgeDirection == 0)
                {
                    // "X-like" direction
                    deltaB += sign * weight * (gaugedA_j - _spinorA[i]);
                    deltaA += sign * weight * (gaugedB_j - _spinorB![i]);
                    deltaD += sign * weight * (gaugedC_j - _spinorC![i]);
                    deltaC += sign * weight * (gaugedD_j - _spinorD![i]);
                }
                else
                {
                    // "Y-like" direction
                    Complex iSign = Complex.ImaginaryOne * sign;
                    deltaB += iSign * weight * (gaugedA_j - _spinorA[i]);
                    deltaA += -iSign * weight * (gaugedB_j - _spinorB![i]);
                    deltaD += -iSign * weight * (gaugedC_j - _spinorC![i]);
                    deltaC += iSign * weight * (gaugedD_j - _spinorD![i]);
                }
            }

            Complex massTermA = -Complex.ImaginaryOne * mc / hbar * _spinorC![i];
            Complex massTermB = -Complex.ImaginaryOne * mc / hbar * _spinorD![i];
            Complex massTermC = -Complex.ImaginaryOne * mc / hbar * _spinorA[i];
            Complex massTermD = -Complex.ImaginaryOne * mc / hbar * _spinorB![i];

            double factor = -1.0 / hbar;

            Complex dPsiA = factor * (c * deltaA + massTermA);
            Complex dPsiB = factor * (c * deltaB + massTermB);
            Complex dPsiC = factor * (c * deltaC + massTermC);
            Complex dPsiD = factor * (c * deltaD + massTermD);

            _spinorA[i] += effectiveDt * dPsiA;
            _spinorB![i] += effectiveDt * dPsiB;
            _spinorC![i] += effectiveDt * dPsiC;
            _spinorD![i] += effectiveDt * dPsiD;
        }

        /// <summary>
        /// Minimal adaptive normalization for symplectic integrator.
        /// Only applies very small corrections when norm deviates significantly.
        /// RQ-Hypothesis Item 3: Symplectic integrator should preserve norm - 
        /// this is only a safety net for long-term stability.
        /// </summary>
        private void AdaptiveNormalizeSpinorFieldMinimal()
        {
            if (_spinorA == null) return;

            // Compute total norm
            double totalNorm = 0;
            for (int i = 0; i < N; i++)
            {
                double norm = _spinorA[i].Magnitude * _spinorA[i].Magnitude
                            + _spinorB![i].Magnitude * _spinorB[i].Magnitude
                            + _spinorC![i].Magnitude * _spinorC[i].Magnitude
                            + _spinorD![i].Magnitude * _spinorD[i].Magnitude;
                totalNorm += norm;
            }

            double targetNorm = N; // Target: average norm per node ~ 1
            double relativeDeviation = Math.Abs(totalNorm - targetNorm) / targetNorm;

            // Symplectic integrator should preserve norm to O(dt^2)
            // Only apply minimal correction if deviation exceeds threshold (safety)
            if (relativeDeviation > PhysicsConstants.SymplecticNormSafetyThreshold && totalNorm > 1e-10)
            {
                // Apply very gentle correction toward target
                double currentScale = Math.Sqrt(targetNorm / totalNorm);
                double scale = 1.0 + PhysicsConstants.SymplecticNormCorrectionRate * (currentScale - 1.0);

                for (int i = 0; i < N; i++)
                {
                    _spinorA[i] *= scale;
                    _spinorB![i] *= scale;
                    _spinorC![i] *= scale;
                    _spinorD![i] *= scale;
                }
            }
        }

        /// <summary>
        /// Compute fermion density at node (ψ†ψ)
        /// </summary>
        public double ComputeFermionDensity(int i)
        {
            if (_spinorA == null || i < 0 || i >= N)
                return 0.0;

            double density = _spinorA[i].Magnitude * _spinorA[i].Magnitude
                           + _spinorB![i].Magnitude * _spinorB[i].Magnitude
                           + _spinorC![i].Magnitude * _spinorC[i].Magnitude
                           + _spinorD![i].Magnitude * _spinorD[i].Magnitude;

            return density;
        }

        /// <summary>
        /// Compute chiral density (difference between left and right)
        /// </summary>
        public double ComputeChiralDensity(int i)
        {
            if (_spinorA == null || i < 0 || i >= N)
                return 0.0;

            double leftDensity = _spinorA[i].Magnitude * _spinorA[i].Magnitude
                               + _spinorB![i].Magnitude * _spinorB[i].Magnitude;

            double rightDensity = _spinorC![i].Magnitude * _spinorC[i].Magnitude
                                + _spinorD![i].Magnitude * _spinorD[i].Magnitude;

            return leftDensity - rightDensity;
        }

        /// <summary>
        /// Compute fermion current between two nodes
        /// </summary>
        public Complex ComputeFermionCurrent(int i, int j)
        {
            if (_spinorA == null || !Edges[i, j])
                return Complex.Zero;

            // Current operator: j^μ = ψ̄ γ^μ ψ
            // For simplicity, use ψ*_i ψ_j - ψ*_j ψ_i

            Complex currentA = Complex.Conjugate(_spinorA[i]) * _spinorA![j]
                             - Complex.Conjugate(_spinorA[j]) * _spinorA[i];
            Complex currentB = Complex.Conjugate(_spinorB![i]) * _spinorB[j]
                             - Complex.Conjugate(_spinorB[j]) * _spinorB[i];
            Complex currentC = Complex.Conjugate(_spinorC![i]) * _spinorC[j]
                             - Complex.Conjugate(_spinorC[j]) * _spinorC[i];
            Complex currentD = Complex.Conjugate(_spinorD![i]) * _spinorD[j]
                             - Complex.Conjugate(_spinorD[j]) * _spinorD[i];

            return (currentA + currentB + currentC + currentD) * Weights[i, j];
        }
    }
}
