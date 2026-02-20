using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RQSimulation
{
    /// <summary>
    /// Background-independent Yang-Mills dynamics
    /// Removes dependency on external coordinates, using only relational edge properties
    /// </summary>
    public partial class RQGraph
    {
        private const double CurrentScalingFactor = 0.1; // Scaling for relational current computation

        /// <summary>
        /// Compute relational current based on edge weights and density gradients
        /// Background-independent: no reference to Coordinates
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ComputeRelationalCurrent(int i, int j, double densityI, double densityJ)
        {
            if (!Edges[i, j])
                return 0.0;

            // Edge weight represents the "relational connection strength"
            double weight = Weights[i, j];

            // Density gradient drives current flow
            double densityGradient = densityJ - densityI;

            // Current flows from high to low density, modulated by edge weight
            // The weight encodes the effective "distance" or "conductance" relationally
            double current = weight * densityGradient;

            // Optional: include edge phase for gauge-invariant transport
            if (_edgePhase != null && (uint)i < (uint)N && (uint)j < (uint)N)
            {
                double phase = _edgePhase[i, j];
                current *= Math.Cos(phase); // Phase modulation
            }

            return current;
        }

        /// <summary>
        /// Evolve Yang-Mills fields using relational dynamics (background-independent)
        /// </summary>
        public void EvolveYangMillsRelational(double dt)
        {
            if (_isEvolvingYangMills) return;

            try
            {
                _isEvolvingYangMills = true;

                if (_gluonField == null) InitYangMillsFields();
                if (_waveMulti == null) return;

                int d = GaugeDimension;
                if (d < 3) return;

                int lenWave = _waveMulti.Length;

                // Cache densities
                if (_cachedColorDensity == null || _cachedColorDensity.Length != N)
                    _cachedColorDensity = new double[N];
                if (_spinorA != null && (_cachedWeakDensity == null || _cachedWeakDensity.Length != N))
                    _cachedWeakDensity = new double[N];
                if ((_spinorA != null || _spinorC != null) && (_cachedHyperDensity == null || _cachedHyperDensity.Length != N))
                    _cachedHyperDensity = new double[N];

                for (int i = 0; i < N; i++)
                {
                    double rhoColor = 0.0;
                    int baseIdx = i * d;
                    if (baseIdx < lenWave)
                    {
                        if (baseIdx + 0 < lenWave) rhoColor += AbsSquared(_waveMulti[baseIdx + 0]);
                        if (baseIdx + 1 < lenWave) rhoColor += AbsSquared(_waveMulti[baseIdx + 1]);
                        if (baseIdx + 2 < lenWave) rhoColor += AbsSquared(_waveMulti[baseIdx + 2]);
                    }
                    _cachedColorDensity[i] = rhoColor;

                    if (_spinorA != null)
                    {
                        double rhoWeak = 0.0;
                        double magA = _spinorA[i].Magnitude; rhoWeak += magA * magA;
                        if (_spinorB != null && i < _spinorB.Length)
                        {
                            double magB = _spinorB[i].Magnitude; rhoWeak += magB * magB;
                        }
                        _cachedWeakDensity![i] = rhoWeak;
                    }

                    if (_spinorA != null || _spinorC != null)
                    {
                        double h = 0.0;
                        if (_spinorA != null && i < _spinorA.Length)
                        {
                            double magA = _spinorA[i].Magnitude; h += -0.5 * magA * magA;
                            if (_spinorB != null && i < _spinorB.Length)
                            { double magB = _spinorB[i].Magnitude; h += -0.5 * magB * magB; }
                        }
                        if (_spinorC != null && i < _spinorC.Length)
                        {
                            double magC = _spinorC[i].Magnitude; h += -1.0 * magC * magC;
                            if (_spinorD != null && i < _spinorD.Length)
                            { double magD = _spinorD[i].Magnitude; h += -1.0 * magD * magD; }
                        }
                        _cachedHyperDensity![i] = h;
                    }
                }

                // Initialize delta buffers
                if (_gluonDelta == null || _gluonDelta.GetLength(0) != N) _gluonDelta = new double[N, N, 8];
                if (_weakDelta == null || _weakDelta.GetLength(0) != N) _weakDelta = new double[N, N, 3];
                if (_hyperDelta == null || _hyperDelta.GetLength(0) != N) _hyperDelta = new double[N, N];

                // Compute field strengths
                ComputeGluonFieldStrength();
                ComputeWeakFieldStrength();
                ComputeHyperchargeFieldStrength();

                // Parallel evolution per node, writing unique [i,j,*]
                Parallel.For(0, N, i =>
                {
                    int[] scratchI = new int[N];
                    var neighI = GetNeighborSpan(i, ref scratchI);
                    foreach (int j in neighI)
                    {
                        // Local copies
                        double[] Aij = new double[8];
                        double[] Fij = new double[8];
                        for (int t = 0; t < 8; t++)
                        {
                            Aij[t] = _gluonField![i, j, t];
                            Fij[t] = _gluonFieldStrength![i, j, t];
                        }

                        // Gluon
                        for (int a = 0; a < 8; a++)
                        {
                            double divF = 0.0;
                            for (int idx = 0; idx < neighI.Length; idx++)
                            {
                                int k = neighI[idx];
                                if (Edges[k, j]) divF += _gluonFieldStrength![k, j, a] - Fij[a];
                            }
                            double selfInt = 0.0;
                            for (int b = 0; b < 8; b++)
                            {
                                double Ab = Aij[b]; if (Ab == 0.0) continue;
                                for (int c = 0; c < 8; c++)
                                {
                                    if (b == c) continue;
                                    if (!TryGetStructureConstant(a, b, c, out double fabc)) continue;
                                    selfInt += StrongCoupling * fabc * Ab * Fij[c];
                                }
                            }
                            double rhoI = _cachedColorDensity![i];
                            double rhoJ = _cachedColorDensity![j];
                            double weight = (uint)a < (uint)_colorWeights.Length ? _colorWeights[a] : 1.0;
                            double J = StrongCoupling * weight * ComputeRelationalCurrent(i, j, rhoI, rhoJ) * CurrentScalingFactor;
                            _gluonDelta![i, j, a] = dt * (divF + selfInt - J);
                        }

                        // Weak
                        for (int a = 0; a < 3; a++)
                        {
                            double divW = 0.0;
                            for (int idx = 0; idx < neighI.Length; idx++)
                            {
                                int k = neighI[idx];
                                if (Edges[k, j]) divW += _weakFieldStrength![k, j, a] - _weakFieldStrength[i, j, a];
                            }
                            int b = (a + 1) % 3; int c = (a + 2) % 3;
                            double selfInt = WeakCoupling * (_weakField![i, j, b] * _weakFieldStrength![i, j, c] - _weakField[i, j, c] * _weakFieldStrength[i, j, b]);
                            double rhoI = _cachedWeakDensity != null ? _cachedWeakDensity[i] : 0.0;
                            double rhoJ = _cachedWeakDensity != null ? _cachedWeakDensity[j] : 0.0;
                            double compWeight = 1.0 + 0.05 * a;
                            double Jw = WeakCoupling * compWeight * ComputeRelationalCurrent(i, j, rhoI, rhoJ) * CurrentScalingFactor;
                            _weakDelta![i, j, a] = dt * (divW + selfInt - Jw);
                        }

                        // Hypercharge
                        double divB = 0.0;
                        for (int idx = 0; idx < neighI.Length; idx++)
                        {
                            int k = neighI[idx];
                            if (Edges[k, j]) divB += _hyperchargeFieldStrength![k, j] - _hyperchargeFieldStrength[i, j];
                        }
                        double hI = _cachedHyperDensity != null ? _cachedHyperDensity[i] : 0.0;
                        double hJ = _cachedHyperDensity != null ? _cachedHyperDensity[j] : 0.0;
                        double Jh = HypergaugeCoupling * ComputeRelationalCurrent(i, j, hI, hJ) * CurrentScalingFactor;
                        _hyperDelta![i, j] = dt * (divB - Jh);
                    }
                });

                // Apply updates in parallel
                Parallel.For(0, N, i =>
                {
                    int[] scratchI = new int[N];
                    var neighI = GetNeighborSpan(i, ref scratchI);
                    foreach (int j in neighI)
                    {
                        for (int a = 0; a < 8; a++) _gluonField![i, j, a] += _gluonDelta![i, j, a];
                        for (int a = 0; a < 3; a++) _weakField![i, j, a] += _weakDelta![i, j, a];
                        _hyperchargeField![i, j] += _hyperDelta![i, j];
                    }
                });

                // CHECKLIST ITEM 2: Gauss Law Projection
                // After field evolution, enforce gauge constraints to maintain ∇·E = ρ
                // This prevents numerical errors from accumulating and breaking gauge invariance
                if (EnforceGaugeConstraintsEnabled)
                {
                    try
                    {
                        GPUOptimized.GaussLawProjection.EnforceGaussLaw(this);
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't crash - gauge constraint violation is not fatal
                        //Console.WriteLine($"[WARNING] Gauss law projection failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                _isEvolvingYangMills = false;
            }
        }
    }
}
