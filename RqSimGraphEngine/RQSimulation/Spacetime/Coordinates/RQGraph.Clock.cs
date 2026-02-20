using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Page-Wootters internal time mechanism.
    /// Replaces external iteration-based time with relational time from clock subsystem correlations.
    /// </summary>
    public partial class RQGraph
    {
        // Clock subsystem nodes
        private HashSet<int> _clockSubsystem = new();
        
        // Clock state tracking for Page-Wootters mechanism
        private int _clockTick;
        private double[] _clockCorrelation;
        private double[] _systemClockCondProb;
        
        /// <summary>
        /// Clock tick counter derived from clock subsystem state changes
        /// </summary>
        public int ClockTick => _clockTick;
        
        /// <summary>
        /// Correlation between system nodes and clock subsystem
        /// </summary>
        public double[] ClockCorrelation => _clockCorrelation ?? Array.Empty<double>();
        
        /// <summary>
        /// Conditional probabilities P(State_S | State_C = T) for visualization
        /// </summary>
        public double[] SystemClockCondProb => _systemClockCondProb ?? Array.Empty<double>();

        /// <summary>
        /// Initialize the clock subsystem from heavy clusters or random selection.
        /// The clock is a small subsystem that provides relational time reference.
        /// </summary>
        public void InitClockSubsystem(double fraction = 0.05)
        {
            _clockSubsystem.Clear();
            _clockTick = 0;
            
            // Try to use existing heavy clusters as clock (more stable)
            var heavy = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            var clockCandidate = heavy.OrderByDescending(c => c.Count).FirstOrDefault();
            
            if (clockCandidate != null && clockCandidate.Count >= 2)
            {
                // Use a subset of the largest heavy cluster as clock
                int clockSize = Math.Min(clockCandidate.Count, Math.Max(2, (int)(N * fraction)));
                foreach (int idx in clockCandidate.Take(clockSize))
                {
                    _clockSubsystem.Add(idx);
                    if (PhysicsProperties != null && PhysicsProperties.Length == N)
                        PhysicsProperties[idx].IsClock = true;
                }
            }
            else
            {
                // Fallback: select random nodes as clock subsystem
                int count = Math.Max(2, (int)(N * fraction));
                var candidates = Enumerable.Range(0, N).OrderBy(_ => _rng.Next()).Take(count);
                foreach (int idx in candidates)
                {
                    _clockSubsystem.Add(idx);
                    if (PhysicsProperties != null && PhysicsProperties.Length == N)
                        PhysicsProperties[idx].IsClock = true;
                }
            }
            
            // Initialize correlation arrays
            _clockCorrelation = new double[N];
            _systemClockCondProb = new double[N];
            
            // Sync with legacy _clockNodes list for backward compatibility with existing code.
            // _clockSubsystem is the authoritative source; _clockNodes is kept in sync for
            // code that references the older InitClocks() API.
            _clockNodes.Clear();
            _clockNodes.AddRange(_clockSubsystem);
        }

        /// <summary>
        /// Compute the clock state as a scalar (average excitation fraction in clock subsystem).
        /// This represents the "time reading" T in Page-Wootters mechanism.
        /// </summary>
        public double GetClockState()
        {
            if (_clockSubsystem.Count == 0) return 0.0;
            
            int excited = 0;
            foreach (int c in _clockSubsystem)
            {
                if (State[c] == NodeState.Excited) excited++;
            }
            return (double)excited / _clockSubsystem.Count;
        }

        /// <summary>
        /// Get clock phase from quantum wavefunction (averaged phase in clock subsystem)
        /// </summary>
        public double GetClockQuantumPhase()
        {
            if (_waveMulti == null || _clockSubsystem.Count == 0) return 0.0;
            
            int d = GaugeDimension;
            double totalPhase = 0.0;
            foreach (int c in _clockSubsystem)
            {
                for (int a = 0; a < d; a++)
                {
                    int idx = c * d + a;
                    totalPhase += _waveMulti[idx].Phase;
                }
            }
            return totalPhase / (_clockSubsystem.Count * d);
        }

        /// <summary>
        /// Advance the internal clock based on clock subsystem state change.
        /// Returns true if clock ticked (state transition detected).
        /// </summary>
        public bool AdvanceInternalClock()
        {
            if (_clockSubsystem.Count == 0) return false;
            
            double prevClockState = _lastClockState;
            double currentClockState = GetClockState();
            
            // Detect state transition in clock subsystem (tick event)
            bool ticked = Math.Abs(currentClockState - prevClockState) > 0.1;
            
            if (ticked)
            {
                _clockTick++;
            }
            
            _lastClockState = currentClockState;
            return ticked;
        }
        
        private double _lastClockState = 0.0;

        /// <summary>
        /// Compute correlation between each system node and the clock subsystem.
        /// This implements the Page-Wootters conditional probability concept.
        /// </summary>
        public void UpdateClockCorrelations()
        {
            if (_clockCorrelation == null || _clockCorrelation.Length != N)
                _clockCorrelation = new double[N];
            if (_systemClockCondProb == null || _systemClockCondProb.Length != N)
                _systemClockCondProb = new double[N];
            
            if (_clockSubsystem.Count == 0) return;
            
            double clockState = GetClockState();
            double clockPhase = GetClockQuantumPhase();
            
            for (int i = 0; i < N; i++)
            {
                if (_clockSubsystem.Contains(i))
                {
                    // Clock nodes have perfect self-correlation
                    _clockCorrelation[i] = 1.0;
                    _systemClockCondProb[i] = 1.0;
                    continue;
                }
                
                // Compute correlation as product of:
                // 1. Edge connectivity to clock
                // 2. State correlation (both excited or both not)
                // 3. Quantum phase coherence
                
                double edgeCorr = 0.0;
                double stateCorr = 0.0;
                double phaseCorr = 0.0;
                int clockConnections = 0;
                
                foreach (int c in _clockSubsystem)
                {
                    if (Edges[i, c])
                    {
                        edgeCorr += Weights[i, c];
                        clockConnections++;
                        
                        // State correlation
                        bool sameState = (State[i] == NodeState.Excited) == (State[c] == NodeState.Excited);
                        stateCorr += sameState ? 1.0 : 0.0;
                        
                        // Phase correlation from wavefunction
                        if (_waveMulti != null)
                        {
                            double phaseI = GetNodePhase(i);
                            double phaseC = GetNodePhase(c);
                            double phaseDiff = Math.Abs(phaseI - phaseC);
                            phaseCorr += Math.Cos(phaseDiff);
                        }
                    }
                }
                
                if (clockConnections > 0)
                {
                    edgeCorr /= clockConnections;
                    stateCorr /= clockConnections;
                    phaseCorr /= clockConnections;
                }
                
                // Combined correlation measure
                _clockCorrelation[i] = 0.4 * edgeCorr + 0.3 * stateCorr + 0.3 * Math.Max(0, phaseCorr);
                
                // Conditional probability P(State_i | Clock = T)
                // Based on correlation and current clock reading
                double condProb = _clockCorrelation[i] * (1.0 + clockState) / 2.0;
                _systemClockCondProb[i] = Math.Clamp(condProb, 0.0, 1.0);
            }
        }

        /// <summary>
        /// Compute internal time step for a node based on clock correlation.
        /// Replaces external dt with relational time derived from clock subsystem.
        /// </summary>
        public double ComputeInternalTimeStep(int node)
        {
            if (_clockCorrelation == null || node < 0 || node >= N)
                return ComputeRelationalDt(); // Fallback
            
            double baseDt = ComputeRelationalDt();
            double clockCorr = _clockCorrelation[node];
            
            // Time flows faster for nodes highly correlated with clock
            // This implements the relational aspect of Page-Wootters
            double factor = 1.0 + 0.5 * clockCorr;
            
            // Mass-dependent time dilation (GR-like effect)
            double mass = _correlationMass != null && _correlationMass.Length == N ? _correlationMass[node] : 0.0;
            double avgMass = _avgCorrelationMass > 0 ? _avgCorrelationMass : 1.0;
            double massDilation = 1.0 / (1.0 + mass / avgMass);
            
            return baseDt * factor * massDilation;
        }

        /// <summary>
        /// Step the simulation using internal clock-based time.
        /// Global wavefunction evolves to minimize Wheeler-DeWitt Hamiltonian constraint.
        /// </summary>
        public void StepWithInternalTime()
        {
            // Update clock correlations
            UpdateClockCorrelations();
            
            // Advance internal clock (check for tick)
            bool ticked = AdvanceInternalClock();
            
            // Update quantum state (wavefunction evolves to constraint-satisfying state)
            UpdateQuantumState();
            
            // Update proper time using internal time steps
            if (ProperTime != null)
            {
                for (int i = 0; i < N; i++)
                {
                    double dt = ComputeInternalTimeStep(i);
                    ProperTime[i] += dt;
                }
            }
            
            // Update classical states based on conditional probabilities
            UpdateStatesFromClockCondProb();
            
            // Update correlation weights (Hebbian learning)
            UpdateCorrelationWeights();
        }

        /// <summary>
        /// Update node states based on conditional probability given clock state.
        /// Implements P(State_S | State_C = T) for state assignment.
        /// </summary>
        private void UpdateStatesFromClockCondProb()
        {
            if (_systemClockCondProb == null || _systemClockCondProb.Length != N) return;
            if (_nextState == null || _nextState.Length != N) _nextState = new NodeState[N];
            
            for (int i = 0; i < N; i++)
            {
                if (_clockSubsystem.Contains(i))
                {
                    // Clock nodes follow their own dynamics (driven by quantum state)
                    _nextState[i] = ComputeNextState(i, State[i]);
                    continue;
                }
                
                // System nodes: state depends on conditional probability
                double condProb = _systemClockCondProb[i];
                
                // Add influence from local neighborhood
                double localInfluence = GetLocalExcitedDensity(i);
                double totalProb = 0.5 * condProb + 0.5 * localInfluence;
                
                // Quantum amplitude boost
                if (_waveMulti != null)
                {
                    int d = GaugeDimension;
                    double amp = 0.0;
                    for (int a = 0; a < d; a++)
                        amp += _waveMulti[i * d + a].Magnitude;
                    totalProb *= (1.0 + 0.2 * amp);
                }
                
                totalProb = Math.Clamp(totalProb, 0.0, 1.0);
                
                switch (State[i])
                {
                    case NodeState.Rest:
                        _nextState[i] = _rng.NextDouble() < totalProb ? NodeState.Excited : NodeState.Rest;
                        break;
                    case NodeState.Excited:
                        _refractoryCounter[i] = DynamicBaseRefractorySteps;
                        _nextState[i] = NodeState.Refractory;
                        break;
                    case NodeState.Refractory:
                        if (_refractoryCounter[i] <= 0)
                            _nextState[i] = NodeState.Rest;
                        else
                        {
                            _refractoryCounter[i]--;
                            _nextState[i] = NodeState.Refractory;
                        }
                        break;
                    default:
                        _nextState[i] = State[i];
                        break;
                }
            }
            
            // Apply state updates
            for (int i = 0; i < N; i++)
                State[i] = _nextState[i];
        }

        /// <summary>
        /// Get the set of clock node indices for visualization
        /// </summary>
        public IReadOnlyCollection<int> GetClockNodes() => _clockSubsystem;
        
        /// <summary>
        /// Check if a node is part of the clock subsystem
        /// </summary>
        public bool IsClockNode(int node) => _clockSubsystem.Contains(node);
    }
}
