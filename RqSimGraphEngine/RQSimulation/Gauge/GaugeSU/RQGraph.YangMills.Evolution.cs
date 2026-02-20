using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using RQSimulation.Gauge;

namespace RQSimulation
{
    public partial class RQGraph
    {
        public void EvolveYangMillsFields(double dt)
        {
            if (_isEvolvingYangMills) return;

            try
            {
                _isEvolvingYangMills = true;

                // RQ-FIX: Use proper SU(3) lattice evolution if available
                // This ensures unitarity via exponential map updates
                if (GaugeDimension == 3 && _gaugeSU3 != null)
                {
                    // 1. Pure gauge evolution (Wilson action)
                    UpdateNonAbelianGauge(dt);

                    // 2. Matter coupling (currents)
                    UpdateGaugeFromMatter(dt);

                    // 3. Enforce constraints
                    EnforceGaugeConstraintsInternal();

                    // Sync back to component fields for visualization/compatibility
                    // (Optional, but good for consistency)
                    // SyncSU3ToComponents(); 

                    // Continue to evolve Weak and Hypercharge fields below...
                }

                if (_gluonField == null) InitYangMillsFields();
                if (_waveMulti == null) return;

                int d = GaugeDimension;
                if (d < 3) return;

                int lenWave = _waveMulti.Length;

                // Cache color densities
                if (_cachedColorDensity == null || _cachedColorDensity.Length != N)
                    _cachedColorDensity = new double[N];

                for (int i = 0; i < N; i++)
                {
                    double rho = 0.0;
                    int baseIdx = i * d;

                    if (baseIdx < lenWave)
                    {
                        if (baseIdx + 0 < lenWave) rho += AbsSquared(_waveMulti[baseIdx + 0]);
                        if (baseIdx + 1 < lenWave) rho += AbsSquared(_waveMulti[baseIdx + 1]);
                        if (baseIdx + 2 < lenWave) rho += AbsSquared(_waveMulti[baseIdx + 2]);
                    }

                    _cachedColorDensity[i] = rho;
                }

                // Cache weak densities
                if (_spinorA != null)
                {
                    if (_cachedWeakDensity == null || _cachedWeakDensity.Length != N)
                        _cachedWeakDensity = new double[N];

                    for (int i = 0; i < N; i++)
                    {
                        double rho = 0.0;
                        double magA = _spinorA[i].Magnitude;
                        rho += magA * magA;

                        if (_spinorB != null && i < _spinorB.Length)
                        {
                            double magB = _spinorB[i].Magnitude;
                            rho += magB * magB;
                        }

                        _cachedWeakDensity[i] = rho;
                    }
                }

                // Cache hypercharge densities
                if (_spinorA != null || _spinorC != null)
                {
                    if (_cachedHyperDensity == null || _cachedHyperDensity.Length != N)
                        _cachedHyperDensity = new double[N];

                    for (int i = 0; i < N; i++)
                    {
                        double h = 0.0;

                        if (_spinorA != null && i < _spinorA.Length)
                        {
                            double magA = _spinorA[i].Magnitude;
                            h += -0.5 * magA * magA;

                            if (_spinorB != null && i < _spinorB.Length)
                            {
                                double magB = _spinorB[i].Magnitude;
                                h += -0.5 * magB * magB;
                            }
                        }

                        if (_spinorC != null && i < _spinorC.Length)
                        {
                            double magC = _spinorC[i].Magnitude;
                            h += -1.0 * magC * magC;

                            if (_spinorD != null && i < _spinorD.Length)
                            {
                                double magD = _spinorD[i].Magnitude;
                                h += -1.0 * magD * magD;
                            }
                        }

                        _cachedHyperDensity[i] = h;
                    }
                }

                // Initialize delta buffers
                if (_gluonDelta == null || _gluonDelta.GetLength(0) != N)
                    _gluonDelta = new double[N, N, 8];
                if (_weakDelta == null || _weakDelta.GetLength(0) != N)
                    _weakDelta = new double[N, N, 3];
                if (_hyperDelta == null || _hyperDelta.GetLength(0) != N)
                    _hyperDelta = new double[N, N];

                // Ensure spinor fields exist
                bool needsSpinors = (_spinorA == null || _spinorA.Length != N) ||
                                   (_spinorC == null || _spinorC.Length != N);
                if (needsSpinors)
                {
                    if (_spinorA == null || _spinorA.Length != N)
                        _spinorA = new Complex[N];
                    if (_spinorC == null || _spinorC.Length != N)
                        _spinorC = new Complex[N];
                }

                // Compute field strengths
                ComputeGluonFieldStrength();
                ComputeWeakFieldStrength();
                ComputeHyperchargeFieldStrength();

                int[] scratchI = new int[N];

                // Compute field evolution
                for (int i = 0; i < N; i++)
                {
                    var neighI = GetNeighborSpan(i, ref scratchI);

                    foreach (int j in neighI)
                    {
                        // [RQ-PHYSICS] Effective time step for the edge considering gravitational time dilation
                        // Photon delay (Shapiro effect): dt' = dt * sqrt(N_i * N_j)
                        double lapseI = GetLocalLapse(i);
                        double lapseJ = GetLocalLapse(j);
                        // Use average of lapse functions for edge
                        double localDt = dt * 0.5 * (lapseI + lapseJ);

                        double[] Aij = new double[8]; // Changed from stackalloc to heap allocation
                        double[] Fij = new double[8]; // Changed from stackalloc to heap allocation

                        for (int t = 0; t < 8; t++)
                        {
                            Aij[t] = _gluonField![i, j, t];
                            Fij[t] = _gluonFieldStrength![i, j, t];
                        }

                        // Gluon field evolution (RQ-FIX: Multiplicative update per Checklist Item 3)
                        // Replaces additive update with unitary SU(3) matrix exponential
                        // CHECKLIST ITEM 2: Disable matrix diffusion to enforce symplectic wave propagation
                        if (GaugeDimension == 3)
                        {
                            // 1. Get current link matrix U_ij
                            var U_old = GetSU3Link(i, j);

                            // 2. Compute Force F (gradient of Action)
                            // F^a = div E^a + g f^{abc} A^b E^c - J^a
                            double[] force = new double[8];

                            for (int a = 0; a < 8; a++)
                            {
                                double divF = 0.0;
                                for (int idx = 0; idx < neighI.Length; idx++)
                                {
                                    int k = neighI[idx];
                                    if (Edges[k, j])
                                        divF += _gluonFieldStrength![k, j, a] - Fij[a];
                                }

                                double selfInt = 0.0;
                                for (int b = 0; b < 8; b++)
                                {
                                    double Ab = Aij[b];
                                    if (Ab == 0.0) continue;

                                    for (int c = 0; c < 8; c++)
                                    {
                                        if (b == c) continue;
                                        if (!TryGetStructureConstant(a, b, c, out double fabc))
                                            continue;

                                        selfInt += StrongCoupling * fabc * Ab * Fij[c];
                                    }
                                }

                                double J = ComputeColorCurrentCached(i, j, a);

                                // Force component
                                force[a] = divF + selfInt - J;
                            }

                            // [RQ-PHYSICS] localDt already calculated above
                            for (int a = 0; a < 8; a++) force[a] *= -localDt;

                            var Update = Gauge.SU3Matrix.Exp(force);
                            var U_new = Update.Multiply(U_old);

                            // Re-unitarize periodically to prevent drift
                            if (_quantumGraphityStepCount % 100 == 0)
                            {
                                U_new = U_new.ProjectToSU3();
                            }

                            // 4. Update link
                            SetSU3Link(i, j, U_new);

                            // Note: We do NOT update _gluonDelta here as we applied the update directly
                        }
                        else
                        {
                            // Fallback for non-SU(3) or legacy mode
                            // RQ-HYPOTHESIS CHECKLIST FIX #4: SYMPLECTIC GLUON EVOLUTION
                            // Use momentum-based Velocity Verlet instead of diffusion
                            if (PhysicsConstants.EnableSymplecticGaugeEvolution && _gluonMomentum != null)
                            {
                                // Symplectic integration for SU(3) gluon field
                                for (int a = 0; a < 8; a++)
                                {
                                    // Compute force: F^a = ?·E^a + g*f^{abc}*A^b*E^c - J^a
                                    double divF = 0.0;
                                    for (int idx = 0; idx < neighI.Length; idx++)
                                    {
                                        int k = neighI[idx];
                                        if (Edges[k, j])
                                            divF += _gluonFieldStrength![k, j, a] - Fij[a];
                                    }

                                    double selfInt = 0.0;
                                    for (int b = 0; b < 8; b++)
                                    {
                                        double Ab = Aij[b];
                                        if (Ab == 0.0) continue;

                                        for (int c = 0; c < 8; c++)
                                        {
                                            if (b == c) continue;
                                            if (!TryGetStructureConstant(a, b, c, out double fabc))
                                                continue;
                                            selfInt += StrongCoupling * fabc * Ab * Fij[c];
                                        }
                                    }

                                    double J = ComputeColorCurrentCached(i, j, a);
                                    double force = divF + selfInt - J;

                                    // Velocity Verlet: half-step momentum update
                                    // [RQ-PHYSICS] Use localDt for gravitational time dilation
                                    double damping = PhysicsConstants.GaugeFieldDamping;
                                    double mass = PhysicsConstants.GaugeMomentumMassSU3;
                                    _gluonMomentum[i, j, a] = _gluonMomentum[i, j, a] * (1.0 - damping * localDt * 0.5) + force * localDt * 0.5;

                                    // Full-step field update: dA/dt = p/m
                                    _gluonDelta![i, j, a] = localDt * _gluonMomentum[i, j, a] / mass;
                                }
                            }
                            else
                            {
                                // Legacy diffusive fallback (not recommended)
                                for (int a = 0; a < 8; a++)
                                {
                                    double divF = 0.0;
                                    for (int idx = 0; idx < neighI.Length; idx++)
                                    {
                                        int k = neighI[idx];
                                        if (Edges[k, j])
                                            divF += _gluonFieldStrength![k, j, a] - Fij[a];
                                    }

                                    double selfInt = 0.0;
                                    for (int b = 0; b < 8; b++)
                                    {
                                        double Ab = Aij[b];
                                        if (Ab == 0.0) continue;

                                        for (int c = 0; c < 8; c++)
                                        {
                                            if (b == c) continue;
                                            if (!TryGetStructureConstant(a, b, c, out double fabc))
                                                continue;
                                            selfInt += StrongCoupling * fabc * Ab * Fij[c];
                                        }
                                    }

                                    double J = ComputeColorCurrentCached(i, j, a);
                                    // [RQ-PHYSICS] Use localDt for gravitational time dilation
                                    _gluonDelta![i, j, a] = localDt * (divF + selfInt - J);
                                }
                            }
                        }

                        // Weak field evolution
                        // RQ-HYPOTHESIS CHECKLIST ITEM 3: Exponential Map for SU(2)
                        // =========================================================
                        // Replace linear update W_new = W + ?W with multiplicative:
                        //   U_new = exp(-i E dt) * U_old
                        // This preserves gauge invariance and det(U) = 1.
                        if (_gaugeSU2 != null && _gaugeSU2.GetLength(0) == N)
                        {
                            // Compute force for each su(2) generator
                            double[] force = new double[3];
                            for (int a = 0; a < 3; a++)
                            {
                                force[a] = ComputeWeakForce(i, j, a, neighI);
                                force[a] *= -localDt; // Negative for gradient descent
                            }
                            
                            // Exponential map: exp(-iEdt) in su(2) ? SU(2)
                            var update = Gauge.SU2Matrix.Exponential(force);
                            var currentLink = _gaugeSU2[i, j];
                            _gaugeSU2[i, j] = update.Multiply(currentLink);
                            
                            // Also update component fields for compatibility
                            for (int a = 0; a < 3; a++)
                                _weakDelta![i, j, a] = 0.0; // Handled by multiplicative update
                        }
                        else if (PhysicsConstants.EnableSymplecticGaugeEvolution && _weakMomentum != null)
                        {
                            // RQ-HYPOTHESIS CHECKLIST FIX #1: Symplectic integration for SU(2)
                            // Use Velocity Verlet: momentum update then field update
                            // CHECKLIST ITEM 2: Use explicit SU(2) structure constants
                            for (int a = 0; a < 3; a++)
                            {
                                double force = ComputeWeakForce(i, j, a, neighI);

                                // Half-step momentum update (first half of Verlet)
                                // [RQ-PHYSICS] Use localDt for gravitational time dilation
                                double damping = PhysicsConstants.GaugeFieldDamping;
                                double mass = PhysicsConstants.GaugeMomentumMassSU2;
                                _weakMomentum[i, j, a] = _weakMomentum[i, j, a] * (1.0 - damping * localDt * 0.5) + force * localDt * 0.5;

                                // Full-step field update
                                _weakDelta![i, j, a] = localDt * _weakMomentum[i, j, a] / mass;
                            }
                        }
                        else
                        {
                            // Fallback: diffusive evolution (legacy)
                            for (int a = 0; a < 3; a++)
                            {
                                double divW = 0.0;
                                for (int idx = 0; idx < neighI.Length; idx++)
                                {
                                    int k = neighI[idx];
                                    if (Edges[k, j])
                                        divW += _weakFieldStrength![k, j, a] - _weakFieldStrength[i, j, a];
                                }

                                // Use explicit Levi-Civita structure constants for SU(2)
                                double selfInt = 0.0;
                                for (int b = 0; b < 3; b++)
                                {
                                    for (int c = 0; c < 3; c++)
                                    {
                                        double fabc = Gauge.PauliMatrices.GetStructureConstant(a, b, c);
                                        if (fabc != 0.0)
                                        {
                                            selfInt += WeakCoupling * fabc * _weakField![i, j, b] * _weakFieldStrength![i, j, c];
                                        }
                                    }
                                }

                                double Jw = ComputeWeakCurrent(i, j, a);
                                // [RQ-PHYSICS] Use localDt for gravitational time dilation
                                _weakDelta![i, j, a] = localDt * (divW + selfInt - Jw);
                            }
                        }

                        // Hypercharge field evolution
                        // RQ-HYPOTHESIS CHECKLIST FIX #1: Symplectic integration for U(1) hypercharge
                        if (PhysicsConstants.EnableSymplecticGaugeEvolution && _hyperchargeMomentum != null)
                        {
                            double force = ComputeHyperchargeForce(i, j, neighI);

                            // [RQ-PHYSICS] Use localDt for gravitational time dilation
                            double damping = PhysicsConstants.GaugeFieldDamping;
                            double mass = PhysicsConstants.GaugeMomentumMassU1;
                            _hyperchargeMomentum[i, j] = _hyperchargeMomentum[i, j] * (1.0 - damping * localDt * 0.5) + force * localDt * 0.5;

                            _hyperDelta![i, j] = localDt * _hyperchargeMomentum[i, j] / mass;
                        }
                        else
                        {
                            // Fallback: diffusive evolution (legacy)
                            double divB = 0.0;
                            for (int idx = 0; idx < neighI.Length; idx++)
                            {
                                int k = neighI[idx];
                                if (Edges[k, j])
                                    divB += _hyperchargeFieldStrength![k, j] - _hyperchargeFieldStrength[i, j];
                            }

                            double Jh = ComputeHyperchargeCurrent(i, j);
                            // [RQ-PHYSICS] Use localDt for gravitational time dilation
                            _hyperDelta![i, j] = localDt * (divB - Jh);
                        }
                    }
                }

                // Apply field updates
                for (int i = 0; i < N; i++)
                {
                    var neighI = GetNeighborSpan(i, ref scratchI);

                    foreach (int j in neighI)
                    {
                        // Skip gluon update if handled by SU(3) lattice engine (multiplicative update)
                        if (GaugeDimension != 3)
                        {
                            for (int a = 0; a < 8; a++)
                                _gluonField![i, j, a] += _gluonDelta![i, j, a];
                        }

                        for (int a = 0; a < 3; a++)
                            _weakField![i, j, a] += _weakDelta![i, j, a];

                        _hyperchargeField![i, j] += _hyperDelta![i, j];
                    }
                }
                
                // Complete second half of Velocity Verlet momentum update after field update
                if (PhysicsConstants.EnableSymplecticGaugeEvolution)
                {
                    CompleteSymplecticMomentumUpdate(dt);
                }

                // Checklist item 3: Enforce gauge constraints after evolution step
                // This projects phases onto the constraint surface (Gauss law)
                EnforceGaugeConstraintsInternal();
            }
            finally
            {
                _isEvolvingYangMills = false;
            }
        }
        
        /// <summary>
        /// Compute force for SU(2) weak field: F^a = ?·E^a + g*f^{abc}*W^b*E^c - J^a
        /// </summary>
        private double ComputeWeakForce(int i, int j, int a, ReadOnlySpan<int> neighI)
        {
            double divW = 0.0;
            for (int idx = 0; idx < neighI.Length; idx++)
            {
                int k = neighI[idx];
                if (Edges[k, j])
                    divW += _weakFieldStrength![k, j, a] - _weakFieldStrength[i, j, a];
            }

            // Use explicit Levi-Civita structure constants for SU(2)
            double selfInt = 0.0;
            for (int b = 0; b < 3; b++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double fabc = Gauge.PauliMatrices.GetStructureConstant(a, b, c);
                    if (fabc != 0.0)
                    {
                        selfInt += WeakCoupling * fabc * _weakField![i, j, b] * _weakFieldStrength![i, j, c];
                    }
                }
            }

            double Jw = ComputeWeakCurrent(i, j, a);
            return divW + selfInt - Jw;
        }
        
        /// <summary>
        /// Compute force for U(1) hypercharge field: F = ?·E - J
        /// </summary>
        private double ComputeHyperchargeForce(int i, int j, ReadOnlySpan<int> neighI)
        {
            double divB = 0.0;
            for (int idx = 0; idx < neighI.Length; idx++)
            {
                int k = neighI[idx];
                if (Edges[k, j])
                    divB += _hyperchargeFieldStrength![k, j] - _hyperchargeFieldStrength[i, j];
            }

            double Jh = ComputeHyperchargeCurrent(i, j);
            return divB - Jh;
        }

        /// <summary>
        /// Complete second half of Velocity Verlet momentum update after field positions are updated.
        /// Includes SU(3) gluon, SU(2) weak, and U(1) hypercharge momenta.
        /// [RQ-PHYSICS] Now uses localDt with gravitational time dilation per edge.
        /// </summary>
        private void CompleteSymplecticMomentumUpdate(double dt)
        {
            if (_weakMomentum == null || _hyperchargeMomentum == null) return;

            // Recompute field strengths with new field values
            ComputeGluonFieldStrength();
            ComputeWeakFieldStrength();
            ComputeHyperchargeFieldStrength();

            double damping = PhysicsConstants.GaugeFieldDamping;
            int[] scratchI = new int[N];

            for (int i = 0; i < N; i++)
            {
                var neighI = GetNeighborSpan(i, ref scratchI);

                foreach (int j in neighI)
                {
                    // [RQ-PHYSICS] Effective time step for the edge considering gravitational time dilation
                    // Photon delay (Shapiro effect): dt' = dt * sqrt(N_i * N_j)
                    double lapseI = GetLocalLapse(i);
                    double lapseJ = GetLocalLapse(j);
                    double localDt = dt * 0.5 * (lapseI + lapseJ);

                    // RQ-HYPOTHESIS CHECKLIST FIX #4: Gluon momentum - second half-step
                    // Complete Velocity Verlet for SU(3) gluon field
                    // FIX: Skip gluon momentum update if SU(3) matrix engine is active,
                    // as it uses its own unitary dynamics and doesn't use _gluonMomentum
                    if (_gluonMomentum != null && _gluonField != null && _gluonFieldStrength != null)
                    {
                        if (GaugeDimension != 3)
                        {
                            for (int a = 0; a < 8; a++)
                            {
                                double force = ComputeGluonForce(i, j, a, neighI);
                                // [RQ-PHYSICS] Use localDt for gravitational time dilation
                                _gluonMomentum[i, j, a] = _gluonMomentum[i, j, a] * (1.0 - damping * localDt * 0.5) + force * localDt * 0.5;
                            }
                        }
                    }

                    // Weak field momentum - second half-step
                    for (int a = 0; a < 3; a++)
                    {
                        double force = ComputeWeakForce(i, j, a, neighI);
                        // [RQ-PHYSICS] Use localDt for gravitational time dilation
                        _weakMomentum[i, j, a] = _weakMomentum[i, j, a] * (1.0 - damping * localDt * 0.5) + force * localDt * 0.5;
                    }

                    // Hypercharge momentum - second half-step
                    double hyperForce = ComputeHyperchargeForce(i, j, neighI);
                    // [RQ-PHYSICS] Use localDt for gravitational time dilation
                    _hyperchargeMomentum[i, j] = _hyperchargeMomentum[i, j] * (1.0 - damping * localDt * 0.5) + hyperForce * localDt * 0.5;
                }
            }
        }
        
        /// <summary>
        /// Compute force for SU(3) gluon field: F^a = ?·E^a + g*f^{abc}*A^b*E^c - J^a
        /// </summary>
        private double ComputeGluonForce(int i, int j, int a, ReadOnlySpan<int> neighI)
        {
            double divF = 0.0;
            for (int idx = 0; idx < neighI.Length; idx++)
            {
                int k = neighI[idx];
                if (Edges[k, j])
                    divF += _gluonFieldStrength![k, j, a] - _gluonFieldStrength[i, j, a];
            }

            // Non-Abelian self-interaction term: g*f^{abc}*A^b*F^c
            double selfInt = 0.0;
            for (int b = 0; b < 8; b++)
            {
                double Ab = _gluonField![i, j, b];
                if (Ab == 0.0) continue;

                for (int c = 0; c < 8; c++)
                {
                    if (b == c) continue;
                    if (!TryGetStructureConstant(a, b, c, out double fabc))
                        continue;
                    selfInt += StrongCoupling * fabc * Ab * _gluonFieldStrength![i, j, c];
                }
            }

            double J = ComputeColorCurrentCached(i, j, a);
            return divF + selfInt - J;
        }
    }
}
