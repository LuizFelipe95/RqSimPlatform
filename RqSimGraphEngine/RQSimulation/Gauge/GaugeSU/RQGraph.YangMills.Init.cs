using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using RQSimulation.Gauge;

namespace RQSimulation
{
    /// <summary>
    /// Implements Yang-Mills gauge field dynamics for electroweak (SU(2)?U(1))
    /// and strong (SU(3)) interactions in the RQ framework.
    /// Gauge fields live on edges and couple to matter fields on nodes.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST FIX #1: SYMPLECTIC YANG-MILLS DYNAMICS
    /// ===============================================================
    /// All gauge fields now have canonical momenta (conjugate "electric" fields).
    /// Evolution uses symplectic (Velocity Verlet) integration for wave equation:
    ///   d?A/dt? = F  (correct, photons/gluons propagate)
    /// Instead of diffusion:
    ///   dA/dt = F    (wrong, fields decay exponentially)
    /// </summary>
    public partial class RQGraph
    {
        private double[,,]? _gluonField;      // [i,j,8] - SU(3) gauge potential A^a
        private double[,,]? _weakField;       // [i,j,3] - SU(2) gauge potential W^a
        private double[,]? _hyperchargeField; // [i,j]   - U(1) gauge potential B
        private double[,,]? _gluonFieldStrength;
        private double[,,]? _weakFieldStrength;
        private double[,]? _hyperchargeFieldStrength;

        // RQ-HYPOTHESIS CHECKLIST FIX #1: Canonical momenta for Yang-Mills fields
        // These are the "chromoelectric/weak electric" fields E^a_ij = ?L/?(?A^a/?t)
        // With these, we have wave equations instead of diffusion equations
        private double[,,]? _gluonMomentum;      // [i,j,8] - SU(3) chromoelectric field
        private double[,,]? _weakMomentum;       // [i,j,3] - SU(2) weak electric field  
        private double[,]? _hyperchargeMomentum; // [i,j]   - U(1) hypercharge electric field

        // Reusable delta buffers to avoid per-step large allocations (freeze mitigation)
        private double[,,]? _gluonDelta;
        private double[,,]? _weakDelta;
        private double[,]? _hyperDelta;

        public double StrongCoupling { get; set; } = 1.0;
        public double WeakCoupling { get; set; } = 0.65;
        public double HypergaugeCoupling { get; set; } = 0.35;
        private readonly double[] _colorWeights = { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };

        [ThreadStatic] private static bool _isEvolvingYangMills;

        // Precompute node color densities (first 3 color components) reused inside evolution.
        private double[]? _cachedColorDensity;
        private double[]? _cachedWeakDensity;
        private double[]? _cachedHyperDensity;

        // SU(3) structure constants - ONLY access via GetFabcFull() to ensure initialization
        private static readonly Lazy<double[,,]> s_fabcFullCache = new Lazy<double[,,]>(() =>
        {
            var f = new double[8, 8, 8];

            // (0,1,2) family
            f[0, 1, 2] = 1.0; f[1, 2, 0] = 1.0; f[2, 0, 1] = 1.0;
            f[0, 2, 1] = -1.0; f[2, 1, 0] = -1.0; f[1, 0, 2] = -1.0;

            // (0,3,6) family
            f[0, 3, 6] = 0.5; f[3, 6, 0] = 0.5; f[6, 0, 3] = 0.5;
            f[0, 6, 3] = -0.5; f[6, 3, 0] = -0.5; f[3, 0, 6] = -0.5;

            // (0,4,5) family
            f[0, 4, 5] = -0.5; f[4, 5, 0] = -0.5; f[5, 0, 4] = -0.5;
            f[0, 5, 4] = 0.5; f[5, 4, 0] = 0.5; f[4, 0, 5] = 0.5;

            // (1,3,5) family
            f[1, 3, 5] = 0.5; f[3, 5, 1] = 0.5; f[5, 1, 3] = 0.5;
            f[1, 5, 3] = -0.5; f[5, 3, 1] = -0.5; f[3, 1, 5] = -0.5;

            // (1,4,6) family
            f[1, 4, 6] = 0.5; f[4, 6, 1] = 0.5; f[6, 1, 4] = 0.5;
            f[1, 6, 4] = -0.5; f[6, 4, 1] = -0.5; f[4, 1, 6] = -0.5;

            // (2,3,4) family
            f[2, 3, 4] = 0.5; f[3, 4, 2] = 0.5; f[4, 2, 3] = 0.5;
            f[2, 4, 3] = -0.5; f[4, 3, 2] = -0.5; f[3, 2, 4] = -0.5;

            // (2,5,6) family
            f[2, 5, 6] = -0.5; f[5, 6, 2] = -0.5; f[6, 2, 5] = -0.5;
            f[2, 6, 5] = 0.5; f[6, 5, 2] = 0.5; f[5, 2, 6] = 0.5;

            // (3,4,7) family - sqrt(3)/2
            double sqrt3_2 = 0.8660254037844386;
            f[3, 4, 7] = sqrt3_2; f[4, 7, 3] = sqrt3_2; f[7, 3, 4] = sqrt3_2;
            f[3, 7, 4] = -sqrt3_2; f[7, 4, 3] = -sqrt3_2; f[4, 3, 7] = -sqrt3_2;

            // (5,6,7) family - sqrt(3)/2
            f[5, 6, 7] = sqrt3_2; f[6, 7, 5] = sqrt3_2; f[7, 5, 6] = sqrt3_2;
            f[5, 7, 6] = -sqrt3_2; f[7, 6, 5] = -sqrt3_2; f[6, 5, 7] = -sqrt3_2;

            return f;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        [MethodImpl(MethodImplOptions.NoInlining)] // Changed from AggressiveInlining to NoInlining
        private static double[,,] GetFabcFull()
        {
            return s_fabcFullCache.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double AbsSquared(Complex z)
        {
            double re = z.Real;
            double im = z.Imaginary;
            return re * re + im * im;
        }

        public void InitYangMillsFields()
        {
            // RQ-FIX: Strictly enforce single representation for SU(3)
            // Use matrix representation (_gaugeSU3) for evolution if available
            // Component fields (_gluonField) are kept for visualization/compatibility

            // RQ-FIX: Initialize SU(3) matrices for proper lattice gauge theory
            if (GaugeDimension == 3)
            {
                ConfigureGaugeDimension(3);
            }

            _gluonField = new double[N, N, 8];
            _weakField = new double[N, N, 3];
            _hyperchargeField = new double[N, N];
            _gluonFieldStrength = new double[N, N, 8];
            _weakFieldStrength = new double[N, N, 3];
            _hyperchargeFieldStrength = new double[N, N];

            // RQ-HYPOTHESIS CHECKLIST FIX #1: Initialize canonical momenta (electric fields)
            // Zero initial momenta = vacuum ground state (no gluons/weak bosons present)
            _gluonMomentum = new double[N, N, 8];
            _weakMomentum = new double[N, N, 3];
            _hyperchargeMomentum = new double[N, N];

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    for (int a = 0; a < 8; a++)
                    {
                        _gluonField[i, j, a] = (_rng.NextDouble() - 0.5) * 0.01;
                        _gluonMomentum[i, j, a] = 0.0; // Vacuum state
                    }
                    for (int a = 0; a < 3; a++)
                    {
                        _weakField[i, j, a] = (_rng.NextDouble() - 0.5) * 0.01;
                        _weakMomentum[i, j, a] = 0.0; // Vacuum state
                    }
                    _hyperchargeField[i, j] = (_rng.NextDouble() - 0.5) * 0.01;
                    _hyperchargeMomentum[i, j] = 0.0; // Vacuum state
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // Changed from AggressiveInlining to NoInlining
        private static bool TryGetStructureConstant(int a, int b, int c, out double fabc)
        {
            // Bounds check
            if ((uint)a >= 8u || (uint)b >= 8u || (uint)c >= 8u)
            {
                fabc = 0.0;
                return false;
            }

            // Diagonal check
            if (a == b || b == c || a == c)
            {
                fabc = 0.0;
                return false;
            }

            // Get cached structure constants
            var fabcFull = GetFabcFull();
            fabc = fabcFull[a, b, c];
            return fabc != 0.0;
        }

        public double ComputeYangMillsAction()
        {
            ComputeGluonFieldStrength();
            ComputeWeakFieldStrength();
            ComputeHyperchargeFieldStrength();

            double S = 0;

            if (_gluonFieldStrength != null)
            {
                for (int i = 0; i < N; i++)
                {
                    foreach (int j in Neighbors(i))
                    {
                        if (j <= i) continue;

                        for (int a = 0; a < 8; a++)
                        {
                            double F = _gluonFieldStrength[i, j, a];
                            S += 0.25 * F * F / (StrongCoupling * StrongCoupling);
                        }
                    }
                }
            }

            if (_weakFieldStrength != null)
            {
                for (int i = 0; i < N; i++)
                {
                    foreach (int j in Neighbors(i))
                    {
                        if (j <= i) continue;

                        for (int a = 0; a < 3; a++)
                        {
                            double W = _weakFieldStrength[i, j, a];
                            S += 0.25 * W * W / (WeakCoupling * WeakCoupling);
                        }
                    }
                }
            }

            if (_hyperchargeFieldStrength != null)
            {
                for (int i = 0; i < N; i++)
                {
                    foreach (int j in Neighbors(i))
                    {
                        if (j <= i) continue;

                        double B = _hyperchargeFieldStrength[i, j];
                        S += 0.25 * B * B / (HypergaugeCoupling * HypergaugeCoupling);
                    }
                }
            }

            return S;
        }

        /// <summary>
        /// RQ-HYPOTHESIS COMPLIANT: Confinement potential using graph-based distance.
        /// 
        /// PHYSICS FIX: Uses -log(weight) as distance metric instead of GetPhysicalDistance.
        /// In RQ-Hypothesis, the metric emerges from correlation strength:
        ///   d(i,j) = -ln(w_ij)
        /// Strong correlations (w ? 1) give small distance, weak correlations give large distance.
        /// 
        /// Linear confinement: V(r) = ?·r + const (QCD string tension)
        /// where r is now the graph-derived distance, not external Euclidean distance.
        /// </summary>
        public double ConfinementPotential(int i, int j)
        {
            if (_gluonFieldStrength == null || !Edges[i, j]) return 0;

            double E2 = 0;
            for (int a = 0; a < 8; a++)
                E2 += _gluonFieldStrength[i, j, a] * _gluonFieldStrength[i, j, a];

            double stringTension = 0.2;
            
            // RQ-FIX: Use graph-based distance instead of external coordinates
            // Distance = -ln(weight), where stronger correlation = shorter distance
            // This is the emergent metric from correlation structure
            double w = Weights[i, j];
            double r = w > 1e-10 ? -Math.Log(w + 1e-10) : 10.0; // Clamp for numerical stability
            
            return stringTension * r + E2 * 0.01;
        }

        public double RunningStrongCoupling(double Q)
        {
            int Nf = 6;
            double Lambda = 0.2;

            if (Q <= Lambda) return 1.0;

            double b0 = (33 - 2 * Nf) / (12.0 * Math.PI);
            return 1.0 / (b0 * Math.Log(Q * Q / (Lambda * Lambda)));
        }
    }
}
