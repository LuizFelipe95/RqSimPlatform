using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using RQSimulation.Gauge;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Enforce gauge constraints after Yang-Mills evolution.
        /// 
        /// CHECKLIST ITEM 7: Extended Gauss law enforcement to non-Abelian fields.
        /// 
        /// For U(1): ?·E = ? (implemented in EnforceGaussLaw)
        /// For SU(2)/SU(3): ?·E^a + g f^{abc} A^b E^c = ?^a (covariant divergence)
        /// 
        /// This ensures gauge invariance is maintained after numerical evolution.
        /// </summary>
        private void EnforceGaugeConstraintsInternal()
        {
            // Enforce U(1) Gauss law if gauge phases are initialized
            if (_edgePhaseU1 != null)
            {
                EnforceGaussLaw();
            }
            
            // CHECKLIST ITEM 7: Enforce SU(2) Gauss law for weak field
            if (_weakField != null && _weakFieldStrength != null)
            {
                EnforceWeakGaussLaw();
            }
            
            // CHECKLIST ITEM 7: Enforce SU(3) Gauss law for gluon field
            if (_gluonField != null && _gluonFieldStrength != null)
            {
                EnforceStrongGaussLaw();
            }
        }
        
        /// <summary>
        /// Enforce Gauss law constraint for SU(2) weak field.
        /// Iteratively corrects field to satisfy ?·E^a = ?^a.
        /// </summary>
        private void EnforceWeakGaussLaw()
        {
            if (_weakField == null || _weakFieldStrength == null || _cachedWeakDensity == null)
                return;
                
            const int maxIterations = 5;
            const double relaxation = 0.1;
            
            for (int iter = 0; iter < maxIterations; iter++)
            {
                double maxViolation = 0.0;
                
                for (int i = 0; i < N; i++)
                {
                    // Compute covariant divergence for each SU(2) component
                    for (int a = 0; a < 3; a++)
                    {
                        double divE = 0.0;
                        double totalDegree = 0.0;
                        
                        foreach (int j in Neighbors(i))
                        {
                            divE += _weakFieldStrength[i, j, a];
                            totalDegree += 1.0;
                        }
                        
                        // Charge density (weak isospin)
                        double rho_a = _cachedWeakDensity[i] * (a == 0 ? 1.0 : 0.0); // Only T^3 component carries charge
                        
                        // Gauss law violation: ?·E^a - ?^a
                        double violation = divE - rho_a;
                        maxViolation = Math.Max(maxViolation, Math.Abs(violation));
                        
                        // Correction: subtract gradient of a gauge function
                        if (totalDegree > 0 && Math.Abs(violation) > 1e-10)
                        {
                            double correction = relaxation * violation / totalDegree;
                            foreach (int j in Neighbors(i))
                            {
                                _weakField![i, j, a] -= correction;
                            }
                        }
                    }
                }
                
                // Early exit if converged
                if (maxViolation < 1e-6)
                    break;
            }
        }
        
        /// <summary>
        /// Enforce Gauss law constraint for SU(3) gluon field.
        /// Iteratively corrects field to satisfy ?·E^a + g f^{abc} A^b E^c = ?^a.
        /// </summary>
        private void EnforceStrongGaussLaw()
        {
            if (_gluonField == null || _gluonFieldStrength == null || _cachedColorDensity == null)
                return;
                
            const int maxIterations = 5;
            const double relaxation = 0.1;
            
            for (int iter = 0; iter < maxIterations; iter++)
            {
                double maxViolation = 0.0;
                
                for (int i = 0; i < N; i++)
                {
                    // Compute covariant divergence for each SU(3) component
                    for (int a = 0; a < 8; a++)
                    {
                        double divE = 0.0;
                        double totalDegree = 0.0;
                        
                        foreach (int j in Neighbors(i))
                        {
                            double E_ija = _gluonFieldStrength[i, j, a];
                            divE += E_ija;
                            
                            // Non-Abelian correction: f^{abc} A^b E^c
                            for (int b = 0; b < 8; b++)
                            {
                                for (int c = 0; c < 8; c++)
                                {
                                    if (TryGetStructureConstant(a, b, c, out double fabc))
                                    {
                                        divE += StrongCoupling * fabc * _gluonField[i, j, b] * _gluonFieldStrength[i, j, c];
                                    }
                                }
                            }
                            
                            totalDegree += 1.0;
                        }
                        
                        // Color charge density (from wavefunction)
                        double rho_a = _cachedColorDensity[i] * (a < 3 ? 1.0 : 0.0); // First 3 generators carry primary charge
                        
                        // Gauss law violation
                        double violation = divE - rho_a;
                        maxViolation = Math.Max(maxViolation, Math.Abs(violation));
                        
                        // Correction
                        if (totalDegree > 0 && Math.Abs(violation) > 1e-10)
                        {
                            double correction = relaxation * violation / totalDegree;
                            foreach (int j in Neighbors(i))
                            {
                                _gluonField![i, j, a] -= correction;
                            }
                        }
                    }
                }
                
                // Early exit if converged
                if (maxViolation < 1e-6)
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ComputeColorCurrentCached(int i, int j, int a)
        {
            if (_cachedColorDensity == null) return 0.0;
            if ((uint)i >= (uint)_cachedColorDensity.Length ||
                (uint)j >= (uint)_cachedColorDensity.Length) return 0.0;

            double rhoI = _cachedColorDensity[i];
            double rhoJ = _cachedColorDensity[j];

            // Checklist G.1: Background independence - use graph topology, not Coordinates
            // Current flows from high to low density along edge (gradient)
            double densityGradient = rhoJ - rhoI;  // Direction: i -> j
            double edgeWeight = Weights[i, j];

            double colorWeight = (uint)a < (uint)_colorWeights.Length ? _colorWeights[a] : 1.0;
            return StrongCoupling * colorWeight * densityGradient * edgeWeight * PhysicsConstants.GaugeCurrentScaleFactor;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private double ComputeWeakCurrent(int i, int j, int a)
        {
            if (_cachedWeakDensity == null) return 0.0;
            if ((uint)i >= (uint)_cachedWeakDensity.Length ||
                (uint)j >= (uint)_cachedWeakDensity.Length) return 0.0;

            double rhoI = _cachedWeakDensity[i];
            double rhoJ = _cachedWeakDensity[j];

            // Checklist G.1: Background independence - use graph topology, not Coordinates
            // Current flows from high to low density along edge
            double densityGradient = rhoJ - rhoI;  // Direction: i -> j
            double edgeWeight = Weights[i, j];

            double compWeight = 1.0 + 0.05 * a;
            return WeakCoupling * compWeight * densityGradient * edgeWeight * PhysicsConstants.GaugeCurrentScaleFactor;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private double ComputeHyperchargeCurrent(int i, int j)
        {
            double current = 0.0;
            
            // Spinor/fermion contribution
            // Checklist G.1: Use graph topology, NOT external Coordinates for physics
            // Background-independent current: J ? ?_gradient * weight
            if (_cachedHyperDensity != null &&
                (uint)i < (uint)_cachedHyperDensity.Length &&
                (uint)j < (uint)_cachedHyperDensity.Length)
            {
                double hI = _cachedHyperDensity[i];
                double hJ = _cachedHyperDensity[j];

                // Current flows from high to low density along edge
                // Using edge weight as "conductance" - purely relational, no coordinates
                double edgeWeight = Weights[i, j];
                double densityGradient = hJ - hI;  // Direction: i -> j

                current += HypergaugeCoupling * densityGradient * edgeWeight * PhysicsConstants.GaugeCurrentScaleFactor;
            }
            
            // Scalar field back-reaction contribution (Checklist E.2)
            // The scalar field current J_? = Im[?* D_? ?] contributes to Maxwell equations
            // For real scalar: J_ij = g * ?_i * ?_j * sin(?_ij)
            double scalarCurrent = ComputeScalarFieldCurrent(i, j);
            current += scalarCurrent;
            
            return current;
        }

        public double ElectromagneticField(int i, int j)
        {
            if (_weakField == null || _hyperchargeField == null) return 0;

            double sinThW = Math.Sqrt(0.23);
            double cosThW = Math.Sqrt(1 - 0.23);
            return _weakField[i, j, 2] * sinThW + _hyperchargeField[i, j] * cosThW;
        }

        public double ZBosonField(int i, int j)
        {
            if (_weakField == null || _hyperchargeField == null) return 0;

            double sinThW = Math.Sqrt(0.23);
            double cosThW = Math.Sqrt(1 - 0.23);
            return _weakField[i, j, 2] * cosThW - _hyperchargeField[i, j] * sinThW;
        }

        public (Complex WPlus, Complex WMinus) WBosonFields(int i, int j)
        {
            if (_weakField == null) return (Complex.Zero, Complex.Zero);

            double inv = 1.0 / Math.Sqrt(2);
            Complex wPlus = inv * new Complex(_weakField[i, j, 0], -_weakField[i, j, 1]);
            Complex wMinus = inv * new Complex(_weakField[i, j, 0], _weakField[i, j, 1]);
            return (wPlus, wMinus);
        }
    }
}
