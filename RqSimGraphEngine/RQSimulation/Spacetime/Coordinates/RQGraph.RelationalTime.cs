using System;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Relational Time (Page-Wootters Mechanism) - Extended Implementation
    /// Implements fully relational dynamics without external time parameter dt
    /// 
    /// RQ-HYPOTHESIS PHYSICS:
    /// =======================
    /// The "lapse function" N(x) in ADM formalism controls how fast proper time
    /// flows at each point relative to coordinate time:
    ///   dτ = N(x) × dt
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Pure Relational Time
    /// =====================================================
    /// Time is the rate of quantum state change, not a function of entropy.
    /// N = 1 / (1 + stateVelocity)
    /// </summary>
    public partial class RQGraph
    {
        private const int DefaultClockSizeDivisor = 20;
        private const double MinRelationalDt = 0.001;
        private const double MaxRelationalDt = 0.1;

        private int[] _clockNodesArray = Array.Empty<int>();
        private Complex[] _lastClockStateVector = Array.Empty<Complex>();

        private double _currentRelationalVelocity = 0.01;
        private double[]? _lapseFunction;
        private double _avgCurvature = 0.1;
        
        /// <summary>
        /// Previous wavefunction for state velocity calculation.
        /// </summary>
        private Complex[]? _previousWaveMulti;

        /// <summary>
        /// Initialize internal clock subsystem based on connectivity
        /// </summary>
        public void InitInternalClock(int clockSize)
        {
            if (clockSize <= 0 || clockSize > N)
                clockSize = Math.Max(2, N / DefaultClockSizeDivisor);

            var nodesByDegree = Enumerable.Range(0, N)
                .OrderByDescending(i => Neighbors(i).Count())
                .Take(clockSize)
                .ToArray();

            _clockNodesArray = nodesByDegree;

            foreach (int idx in _clockNodesArray)
            {
                if (PhysicsProperties != null && PhysicsProperties.Length == N)
                    PhysicsProperties[idx].IsClock = true;
            }

            _lastClockStateVector = new Complex[clockSize];
            SaveClockState();

            _lapseFunction = new double[N];
            UpdateLapseFunctions();
        }

        /// <summary>
        /// Save current quantum state of clock nodes
        /// </summary>
        public void SaveClockState()
        {
            if (_clockNodesArray.Length == 0 || _waveMulti == null)
                return;

            int d = GaugeDimension;
            for (int k = 0; k < _clockNodesArray.Length; k++)
            {
                int i = _clockNodesArray[k];
                if (i * d < _waveMulti.Length)
                {
                    _lastClockStateVector[k] = _waveMulti[i * d];
                }
            }
        }

        /// <summary>
        /// Compute relational time increment based on clock state change
        /// </summary>
        public double ComputeRelationalDtExtended()
        {
            if (_clockNodesArray.Length == 0 || _waveMulti == null)
                return 0.01;

            int d = GaugeDimension;
            double distanceSquared = 0.0;
            double normalization = 0.0;

            for (int k = 0; k < _clockNodesArray.Length; k++)
            {
                int i = _clockNodesArray[k];
                if (i * d >= _waveMulti.Length)
                    continue;

                Complex current = _waveMulti[i * d];
                Complex last = _lastClockStateVector[k];

                Complex diff = current - last;
                double diffMagnitudeSquared = diff.Real * diff.Real + diff.Imaginary * diff.Imaginary;

                distanceSquared += diffMagnitudeSquared;
                normalization += current.Magnitude + last.Magnitude;
            }

            if (normalization < 1e-10)
                return 0.01;

            double rawDt = Math.Sqrt(distanceSquared) / (normalization + 1e-10);

            double clockInertia = 0.95;
            _currentRelationalVelocity = _currentRelationalVelocity * clockInertia + rawDt * (1.0 - clockInertia);

            return Math.Clamp(_currentRelationalVelocity, MinRelationalDt, MaxRelationalDt);
        }

        /// <summary>
        /// Advance internal clock by updating saved state
        /// </summary>
        public void AdvanceInternalClockState()
        {
            SaveClockState();
        }

        /// <summary>
        /// Get the local lapse function N_i for node i.
        /// </summary>
        public double GetLocalLapse(int node)
        {
            if (node < 0 || node >= N)
                return 1.0;

            if (_lapseFunction != null && node < _lapseFunction.Length)
            {
                return _lapseFunction[node];
            }

            return ComputeLocalLapseUncached(node);
        }

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Pure Relational Time (State Velocity)
        /// N = 1 / (1 + stateVelocity) - time as rate of quantum state change
        /// </summary>
        private double ComputeLocalLapseUncached(int node)
        {
            double stateVelocity = GetQuantumStateVelocity(node);
            
            if (stateVelocity >= 0)
            {
                double lapse = 1.0 / (1.0 + stateVelocity);
                return Math.Clamp(lapse, PhysicsConstants.MinTimeDilation, PhysicsConstants.MaxTimeDilation);
            }
            
            // Fallback: Entropy-based lapse
            double entropy = ComputeNodeEntanglementEntropy(node);
            double lapseFallback = Math.Exp(-PhysicsConstants.TimeDilationAlpha * entropy);

            return Math.Clamp(lapseFallback, PhysicsConstants.MinTimeDilation, PhysicsConstants.MaxTimeDilation);
        }
        
        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Quantum State Velocity
        /// Computes Fubini-Study velocity of quantum state at a node.
        /// </summary>
        private double GetQuantumStateVelocity(int node)
        {
            if (node < 0 || node >= N || _waveMulti == null)
                return -1.0;
                
            int d = GaugeDimension;
            int baseIdx = node * d;
            
            if (_previousWaveMulti == null || _previousWaveMulti.Length != _waveMulti.Length)
                return -1.0;
                
            double distanceSquared = 0.0;
            double normCurrent = 0.0;
            double normPrevious = 0.0;
            
            for (int a = 0; a < d; a++)
            {
                int idx = baseIdx + a;
                if (idx >= _waveMulti.Length) continue;
                
                Complex current = _waveMulti[idx];
                Complex previous = _previousWaveMulti[idx];
                
                Complex diff = current - previous;
                distanceSquared += diff.Real * diff.Real + diff.Imaginary * diff.Imaginary;
                
                normCurrent += current.Magnitude * current.Magnitude;
                normPrevious += previous.Magnitude * previous.Magnitude;
            }
            
            double normalization = Math.Sqrt(normCurrent) + Math.Sqrt(normPrevious);
            if (normalization < 1e-12)
                return 0.0;
                
            return Math.Sqrt(distanceSquared) / (normalization * 0.5);
        }
        
        /// <summary>
        /// Store current wavefunction for next step's velocity calculation.
        /// </summary>
        public void SavePreviousQuantumState()
        {
            if (_waveMulti == null) return;
            
            if (_previousWaveMulti == null || _previousWaveMulti.Length != _waveMulti.Length)
                _previousWaveMulti = new Complex[_waveMulti.Length];
                
            Array.Copy(_waveMulti, _previousWaveMulti, _waveMulti.Length);
        }

        /// <summary>
        /// Update all lapse function values.
        /// Should be called after topology or mass changes.
        /// </summary>
        public void UpdateLapseFunctions()
        {
            if (_lapseFunction == null || _lapseFunction.Length != N)
                _lapseFunction = new double[N];

            for (int i = 0; i < N; i++)
            {
                _lapseFunction[i] = ComputeLocalLapseUncached(i);
            }
        }

        /// <summary>
        /// Compute entanglement entropy for a node from its correlations.
        /// S_i = -Σ_j (w_ij/W_i) × log(w_ij/W_i)
        /// </summary>
        public double ComputeNodeEntanglementEntropy(int node)
        {
            if (node < 0 || node >= N)
                return 0.0;

            double totalWeight = 0.0;
            int neighborCount = 0;

            foreach (int j in Neighbors(node))
            {
                totalWeight += Weights[node, j];
                neighborCount++;
            }

            if (neighborCount == 0 || totalWeight < 1e-12)
                return 0.0;

            double entropy = 0.0;
            foreach (int j in Neighbors(node))
            {
                double w = Weights[node, j];
                if (w < 1e-12) continue;

                double p = w / totalWeight;
                entropy -= p * Math.Log(p);
            }

            if (_correlationMass != null && node < _correlationMass.Length)
            {
                double mass = _correlationMass[node];
                double avgMass = _avgCorrelationMass > 0 ? _avgCorrelationMass : 1.0;

                if (mass > 0)
                {
                    entropy += Math.Log(1.0 + mass / avgMass);
                }
            }

            return Math.Max(0.0, entropy);
        }
    }
}
