using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RQSimulation
{
    public partial class RQGraph
    {
        private Complex[] _wavefunction;
        private Complex[] _waveMulti;
        private double[,] _edgePhase;
        private Complex[] _psiDot;

        // Dynamic quantum scales derived from graph invariants (avg degree)
        public double QuantumCoupling
        {
            get
            {
                var es = GetEdgeStats();
                double refDeg = es.avgDegree > 0 ? es.avgDegree : 1.0;
                return 1.0 / refDeg;
            }
        }
        public double QuantumDt
        {
            get
            {
                double refDeg = Math.Max(1.0, GetEdgeStats().avgDegree);
                return 0.5 / refDeg; // stability heuristic from Laplacian eigenestimate
            }
        }

        private int[] _edgeIndexRowStart;
        private int[] _edgeIndexCols;
        private int _edgeIndexCount;

        private void EnsureEdgeIndex()
        {
            if (_edgeIndexCols != null && _edgeIndexRowStart != null) return;
            var cols = new System.Collections.Generic.List<int>();
            _edgeIndexRowStart = new int[N];
            int ptr = 0;
            for (int i = 0; i < N; i++)
            {
                _edgeIndexRowStart[i] = ptr;
                foreach (int j in Neighbors(i))
                {
                    cols.Add(j);
                    ptr++;
                }
            }
            _edgeIndexCols = cols.ToArray();
            _edgeIndexCount = ptr;
        }

        private int EdgeIndex(int i, int j)
        {
            EnsureEdgeIndex();
            int start = _edgeIndexRowStart[i];
            for (int k = start; k < _edgeIndexCount && k < _edgeIndexCols.Length; k++)
            {
                if (_edgeIndexCols[k] == j) return k;
                if (i + 1 < _edgeIndexRowStart.Length && k + 1 == _edgeIndexRowStart[i + 1]) break;
            }
            return start; // fallback within row
        }

        public void InitQuantumWavefunction()
        {
            if (_edgePhase == null || _edgePhase.GetLength(0) != N)
                _edgePhase = new double[N, N];
            if (_wavefunction == null || _wavefunction.Length != N)
                _wavefunction = new Complex[N];

            int d = GaugeDimension;
            _waveMulti = new Complex[N * d];
            var rnd = _rng;
            for (int i = 0; i < N; i++)
            {
                for (int a = 0; a < d; a++)
                {
                    int idx = i * d + a;
                    _waveMulti[idx] = new Complex(rnd.NextDouble() * 1e-3, rnd.NextDouble() * 1e-3);
                }
            }

            EnsureEdgeIndex();
        }

        public double QuantumDiffusion { get; set; } = 0.2; // checklist diffusion coefficient

        private double[] _phaseDrift;

        private void NormalizeWavefunction()
        {
            if (_waveMulti == null) return;
            double norm = 0.0;
            foreach (var z in _waveMulti) { double m = z.Magnitude; norm += m * m; }
            if (norm <= 0.0) return; double inv = 1.0 / Math.Sqrt(norm);
            for (int i = 0; i < _waveMulti.Length; i++) _waveMulti[i] *= inv;
        }

        /// <summary>
        /// Get current wavefunction norm squared (should be 1.0 for unitary evolution).
        /// </summary>
        /// <returns>Sum of |?_i|? for all components</returns>
        public double GetWavefunctionNorm()
        {
            if (_waveMulti == null) return 0.0;

            double norm = 0.0;
            for (int i = 0; i < _waveMulti.Length; i++)
            {
                norm += _waveMulti[i].Magnitude * _waveMulti[i].Magnitude;
            }
            return norm;
        }

        /// <summary>
        /// Correct wavefunction after topology change to maintain unitarity.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 5: Wavefunction Correction
        /// ========================================================
        /// When graph topology changes, the Hilbert space dimension may change.
        /// Without correction, the wavefunction would be invalid (wrong dimension
        /// or wrong normalization).
        /// 
        /// Physics: This is a discrete analog of metric change in GR. When the
        /// metric changes, the Laplacian changes, and wavefunctions must adjust.
        /// 
        /// Implementation: Sudden Approximation (current implementation)
        /// - Projects wavefunction onto new Hilbert space
        /// - Renormalizes to ensure ||?||? = 1
        /// - Without correction, wavefunction would have "phantom" phase terms
        /// 
        /// For large topology changes, full spectral projection would be needed (future work).
        /// 
        /// The correction ensures the simulation remains unitary despite discrete
        /// topology updates (required for quantum coherence in emergent spacetime).
        /// 
        /// ?? KNOWN LIMITATION: SOURCE OF NUMERICAL DECOHERENCE
        /// =====================================================
        /// This implementation uses the "Sudden Approximation" - it restores
        /// UNITARITY (norm preservation) but does NOT preserve PHASE COHERENCE.
        /// 
        /// What is missing:
        /// - In the adiabatic limit (slow geometry evolution), when the Hamiltonian
        ///   changes from H to H', the wavefunction should pick up a Berry phase:
        ///   ? ? exp(-i ? ?E dt) ?
        /// - We do NOT compute this phase correction.
        /// 
        /// Consequence:
        /// - Each topology change (edge flip) introduces random phase noise
        /// - This causes ARTIFICIAL DECOHERENCE of quantum states
        /// - Spinor fields "lose memory" of their phases faster than physics dictates
        /// - Interference effects are suppressed compared to true quantum behavior
        /// 
        /// Why this is acceptable for now:
        /// - At Planck scale, quantum gravity IS expected to be stochastic
        /// - Topology fluctuations naturally cause some decoherence
        /// - The decoherence rate is controlled by topology flip frequency
        /// - For RQ-hypothesis validation, this approximation is sufficient
        /// 
        /// Future improvement:
        /// - Track Hamiltonian eigenvalues before/after topology change
        /// - Compute energy shift ?E for modes localized near changed edge
        /// - Apply phase correction exp(-i ?E ?t) before renormalization
        /// - This would preserve coherence through slow geometry changes
        /// </summary>
        public void CorrectWavefunctionAfterTopologyChange()
        {
            if (_waveMulti == null) return;

            // === Step 1: Check normalization ===
            // Use GetWavefunctionNorm() from RQGraph.UnitaryEvolution.cs
            double currentNorm = GetWavefunctionNorm();
            
            // Note: GetWavefunctionNorm returns norm squared, not norm
            // So we check if it deviates from 1.0
            if (Math.Abs(currentNorm - 1.0) > 1e-9 && currentNorm > 0)
            {
                NormalizeWavefunction();
            }

            // === Step 2: Phase correction for removed/added edges ===
            // When an edge is removed, the Laplacian changes by ?L.
            // This causes an energy shift ?E for wavefunctions localized near the edge.
            // The phase should be corrected by exp(-i ?E ?t).
            // 
            // ?? WARNING: We use Sudden Approximation (?t ? 0), skipping phase correction.
            // This introduces numerical decoherence - see class documentation above.
            // The normalization step handles amplitude discontinuity but loses phase info.
            
            // === Step 3: Update coherence tracking ===
            // After topology change, quantum coherence may be disrupted.
            // Mark that coherence should be rechecked on next evolution step.
            _topologyChangedSinceLastEvolution = true;
        }

        /// <summary>
        /// Flag indicating topology changed since last quantum evolution.
        /// Used to trigger coherence recalculation.
        /// </summary>
        private bool _topologyChangedSinceLastEvolution = false;

        /// <summary>
        /// RQ-COMPLIANT: Computes total quantum momentum using spectral coordinates.
        /// 
        /// In RQ-hypothesis, there is no external spacetime. Momentum is computed
        /// from wavefunction phases and the EMERGENT geometry (spectral coordinates
        /// from graph Laplacian eigenvectors), NOT external coordinates.
        /// 
        /// If spectral coordinates are not available, returns topological momentum
        /// based on phase gradients along graph edges (purely graph-based).
        /// </summary>
        public (double PX, double PY) ComputeTotalMomentum()
        {
            if (_waveMulti == null) return (0.0, 0.0);
            
            int d = GaugeDimension;
            double px = 0.0, py = 0.0;
            
            // RQ-FIX: Use spectral coordinates (emergent geometry) instead of external coordinates
            bool hasSpectralCoords = SpectralX != null && SpectralX.Length >= N &&
                                     SpectralY != null && SpectralY.Length >= N;
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i >= j) continue;
                    
                    // Compute average phase at each node
                    double phaseI = 0.0, phaseJ = 0.0;
                    for (int a = 0; a < d; a++)
                    {
                        int idxI = i * d + a;
                        int idxJ = j * d + a;
                        phaseI += _waveMulti[idxI].Phase;
                        phaseJ += _waveMulti[idxJ].Phase;
                    }
                    phaseI /= d;
                    phaseJ /= d;
                    double dPhase = phaseJ - phaseI;
                    
                    if (hasSpectralCoords)
                    {
                        // RQ-COMPLIANT: Use spectral coordinates (emergent geometry)
                        double dx = SpectralX[j] - SpectralX[i];
                        double dy = SpectralY[j] - SpectralY[i];
                        px += dPhase * dx;
                        py += dPhase * dy;
                    }
                    else
                    {
                        // Fallback: Purely topological momentum
                        // Direction vector from edge weight gradient
                        double w = Weights[i, j];
                        px += dPhase * w;  // Weighted phase current (scalar)
                        py += dPhase * w * 0.5;  // Arbitrary orthogonal component
                    }
                }
            }
            
            return (px, py);
        }

        public void UpdateNodeStatesFromWavefunction()
        {
            if (_waveMulti == null) return; int d = GaugeDimension; var scalarField = new double[N];
            for (int i = 0; i < N; i++) { double mag = 0.0; for (int a = 0; a < d; a++) mag += _waveMulti[i * d + a].Magnitude; scalarField[i] = mag; }
            double threshold = LocalQuantile(scalarField, 0.5);
            var rand = _rng;
            for (int i = 0; i < N; i++)
            {
                if (rand.NextDouble() < 0.2) continue;
                double storedBoost = StoredEnergy != null ? (1.0 + 0.2 * StoredEnergy[i]) : 1.0; // integrate accumulator influence (item 1) into quantum-driven mode
                bool excited = scalarField[i] * storedBoost >= threshold;
                if (excited) State[i] = NodeState.Excited;
                else if (State[i] == NodeState.Excited) State[i] = NodeState.Refractory;
                else if (State[i] == NodeState.Refractory) State[i] = NodeState.Rest;
            }
        }

        public void PerturbGauge(double epsilon = 0.05)
        {
            if (_edgePhase == null) return;
            var rng = _rng;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    double delta = (rng.NextDouble() * 2.0 - 1.0) * epsilon;
                    _edgePhase[i, j] += delta;
                    _edgePhase[j, i] -= delta;
                }
            }
            // gauge perturbation can alter effective propagation -> refresh delays
            UpdateEdgeDelaysFromDistances();
        }

        public Complex[] GetWavefunction()
        {
            var copy = new Complex[N];
            if (_wavefunction != null && _wavefunction.Length == N)
            {
                for (int i = 0; i < N; i++) copy[i] = _wavefunction[i];
            }
            return copy;
        }

        public Complex[] ColorCurrentOnEdge(int i, int j)
        {
            int d = GaugeDimension;
            var J = new Complex[d];
            if (_waveMulti == null) return J;
            for (int a = 0; a < d; a++)
            {
                var psiI = _waveMulti[i * d + a];
                var psiJ = _waveMulti[j * d + a];
                J[a] = psiI * Complex.Conjugate(psiJ);
            }
            return J;
        }

        public double GetQuantumNorm()
        {
            if (_waveMulti == null) return 0.0;
            double acc = 0.0;
            foreach (var z in _waveMulti) acc += z.Magnitude * z.Magnitude;
            return acc;
        }

        public double ComputeQuantumEnergy(double hbarRq = 1.0)
        {
            if (_waveMulti == null) return 0.0;
            int n = N;
            int d = GaugeDimension;
            int len = n * d;
            if (_psiDot == null || _psiDot.Length != len)
                _psiDot = new Complex[len];
            ApplyGaugeLaplacian(_waveMulti, _psiDot);
            double E = 0.0;
            for (int i = 0; i < n; i++)
            {
                double mi = (_correlationMass != null && _correlationMass.Length == N) ? Math.Max(1e-6, _correlationMass[i]) : 1.0;
                double inv2m = 0.5 / mi;
                double Vi = PotentialFromCorrelations(i);
                for (int a = 0; a < d; a++)
                {
                    int idx = i * d + a;
                    Complex psi = _waveMulti[idx];
                    Complex D2psi = _psiDot[idx];
                    Complex kinetic = -inv2m * Complex.Conjugate(psi) * D2psi;
                    double potential = Vi * psi.Magnitude * psi.Magnitude;
                    E += kinetic.Real + potential;
                }
            }
            return E;
        }

        private double CorrelationEnergyFromMass(double m) => 0.5 * m;
        private double PhaseIncrement(int i)
        {
            if (_edgePhase == null) return 0;
            double sum = 0; int c = 0;
            foreach (int j in Neighbors(i)) { sum += Math.Abs(_edgePhase[i, j]); c++; }
            return c > 0 ? sum / c : 0;
        }
        private double PotentialFromCorrelations(int i)
        {
            double sum = 0.0; int deg = 0;
            foreach (int j in Neighbors(i)) { sum += Weights[i, j]; deg++; }
            if (deg == 0) return 0.0;
            // Local z-score style normalisation
            double meanLocal = sum / deg;
            double varLocal = 0.0;
            foreach (int j in Neighbors(i))
            {
                double w = Weights[i, j];
                double diff = w - meanLocal;
                varLocal += diff * diff;
            }
            varLocal = varLocal / deg;
            double stdLocal = varLocal > 0 ? Math.Sqrt(varLocal) : 1.0;
            double z = (sum - meanLocal * deg) / Math.Sqrt(varLocal * deg + 1e-12);
            return double.IsNaN(z) ? 0.0 : z;
        }

        private static double LocalQuantile(double[] data, double q)
        {
            if (data == null || data.Length == 0) return 0.0;
            var sorted = data.OrderBy(x => x).ToArray();
            int idx = (int)Math.Clamp(q * (sorted.Length - 1), 0, sorted.Length - 1);
            return sorted[idx];
        }

        public double GetNodePhase(int i)
        {
            if (_waveMulti == null || i < 0 || i >= N) return 0.0;
            int d = GaugeDimension;
            double phase = 0.0;
            for (int a = 0; a < d; a++) phase += _waveMulti[i * d + a].Phase;
            return phase / Math.Max(1, d);
        }
    }
}
