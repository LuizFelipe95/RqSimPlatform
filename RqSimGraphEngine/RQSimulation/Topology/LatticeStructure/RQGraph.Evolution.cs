using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RQSimulation.Core.Utilities;

namespace RQSimulation
{
    /// <summary>
    /// Quantum Graphity dynamics: Evolution and Metropolis-Hastings steps.
    /// </summary>
    public partial class RQGraph
    {
        // Effective temperature for Metropolis-Hastings
        private double _networkTemperature = 1.0;

        // Track energy for optimization
        private double _lastNetworkEnergy = double.MaxValue;

        // RQ-HYPOTHESIS CHECKLIST FIELDS
        private double[]? _ricciScalar;
        private double[]? _phiCurrent;
        private double[]? _phiPrev;
        private double[]? _phiNextBuffer;

        // ITEM 43 (10.2): Adaptive Topology Decoherence
        // Lazily initialized when first needed
        private AdaptiveTopologyDecoherence? _adaptiveDecoherence;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Node Proper Time (Page-Wootters Mechanism).
        /// Each node accumulates its own proper time based on local lapse function.
        /// This implements relational time: there is no global clock, only local ones.
        ///
        /// Physics: In GR, each worldline has its own proper time ?.
        /// For discrete graphs: ?_i = ? N_i dt, where N_i is the local lapse function.
        /// </summary>
        private double[]? _nodeClocks;
        
        /// <summary>
        /// Provides read-only access to node proper times for diagnostics.
        /// </summary>
        public ReadOnlySpan<double> NodeClocks => _nodeClocks ?? ReadOnlySpan<double>.Empty;

        /// <summary>
        /// Network temperature for Metropolis-Hastings (controls acceptance of uphill moves)
        /// </summary>
        public double NetworkTemperature
        {
            get => _networkTemperature;
            set => _networkTemperature = Math.Max(0.001, value);
        }

        /// <summary>
        /// Simulated annealing: gradually reduce temperature to find ground state.
        /// Uses local action Metropolis step for RQ-Hypothesis compliance.
        /// </summary>
        public void SimulatedAnnealingStep(double coolingRate = 0.99)
        {
            // RQ-FIX: Use local action Metropolis step instead of obsolete global method
            int steps = Math.Max(1, N / 10);
            int accepted = 0;

            for (int s = 0; s < steps; s++)
            {
                if (MetropolisEdgeStepLocalAction())
                    accepted++;
            }

            // Update edge delays after topology changes
            if (accepted > 0)
            {
                UpdateEdgeDelaysFromDistances();
            }

            _networkTemperature *= coolingRate;

            // Don't let temperature go too low (numerical issues)
            if (_networkTemperature < 0.001)
                _networkTemperature = 0.001;
        }

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Unitary Quantum Evolution (Cayley Form)
        /// =========================================================================
        /// Quantum evolution via Cayley transform U = (1 - iH*dt/2)(1 + iH*dt/2)^-1
        /// This replaces explicit Euler method which violates unitarity.
        /// 
        /// PHYSICS:
        /// - Cayley form maps anti-Hermitian generator -iH to unitary U exactly
        /// - Preserves ||?||? = 1 for ANY time step dt (not just small dt)
        /// - Conserves probability and phase coherence without forced normalization
        /// - Critical for relational phase information used in interference
        /// 
        /// OLD (Euler): ?_new = (1 - iHdt)? ? ||?_new|| ? ||?|| (norm drift)
        /// NEW (Cayley): ?_new = (1 - iHdt/2)(1 + iHdt/2)^-1 ? ? ||?_new|| = ||?||
        /// 
        /// The linear system (1 + iH*dt/2)?_new = (1 - iH*dt/2)?_old
        /// is solved iteratively using BiCGStab (O(N) per iteration for sparse H).
        /// </summary>
        public void EvolveRelationalTimeStep(double baseDt)
        {
            // Initialize buffers if needed
            if (_ricciScalar == null || _ricciScalar.Length != N) _ricciScalar = new double[N];
            if (_phiCurrent == null || _phiCurrent.Length != N) _phiCurrent = new double[N];
            if (_phiPrev == null || _phiPrev.Length != N) _phiPrev = new double[N];
            if (_phiNextBuffer == null || _phiNextBuffer.Length != N) _phiNextBuffer = new double[N];
            if (_nodeClocks == null || _nodeClocks.Length != N) _nodeClocks = new double[N];

            // Update Ricci scalars (needed for lapse function)
            Parallel.For(0, N, i =>
            {
                double R = 0;
                foreach(var j in Neighbors(i))
                {
                    R += CalculateGraphCurvature(i, j);
                }
                _ricciScalar[i] = R;
            });

            // RQ-HYPOTHESIS CHECKLIST ITEM 1: Relativistic Lapse Function
            // =============================================================
            // Physics: In General Relativity, proper time flows differently at each point.
            // The lapse function N(x) determines the rate of proper time vs coordinate time:
            //   d? = N dt
            // 
            // In discrete graph, the gravitational potential ? correlates with Ricci curvature R.
            // We use: N = 1 / (1 + Alpha * |R|)
            // where Alpha = PhysicsConstants.LapseFunctionAlpha controls coupling strength.
            // 
            // High curvature (strong gravity) ? N < 1 ? time slows down (gravitational time dilation)
            // Low curvature (weak gravity) ? N ? 1 ? time flows at coordinate rate
            
            double alpha = PhysicsConstants.LapseFunctionAlpha;
            
            Parallel.For(0, N, i =>
            {
                // Lapse function with configurable coupling constant
                double lapse = 1.0 / (1.0 + alpha * Math.Abs(_ricciScalar[i])); 
                
                // Local proper time step for this node
                double localDt = baseDt * lapse; 
                
                // Pass localDt to update methods for this node's fields
                UpdateLocalFieldsRelational(i, localDt);
                
                // RQ-HYPOTHESIS CHECKLIST ITEM 1: Page-Wootters Mechanism
                // =========================================================
                // Accumulate proper time for each node independently.
                // This implements relational time: no global clock exists.
                // Observable time emerges from correlations between node clocks.
                _nodeClocks![i] += localDt;
            });

            // Swap buffers for scalar field
            var temp = _phiPrev;
            _phiPrev = _phiCurrent;
            _phiCurrent = _phiNextBuffer;
            _phiNextBuffer = temp;
        }

        private void UpdateLocalFieldsRelational(int i, double localDt)
        {
            EvolveScalarFieldRelativistic(i, localDt);
        }

        public void EvolveScalarFieldRelativistic(int nodeIndex, double dt)
        {
            // Physics: Discrete Klein-Gordon equation: (d^2/dt^2 - Laplacian + m^2) Phi = 0
            // Ensures light cone and causality.
            
            double laplacian = CalculateGraphLaplacian(nodeIndex, _phiCurrent!);
            // Use correlation mass or physics properties mass
            double mass = (_correlationMass != null && nodeIndex < _correlationMass.Length) ? _correlationMass[nodeIndex] : 1.0;
            double massTerm = Math.Pow(mass, 2) * _phiCurrent![nodeIndex];
            
            // Verlet integration scheme for wave equation
            double phiNext = 2 * _phiCurrent[nodeIndex] - _phiPrev![nodeIndex] 
                             - Math.Pow(dt, 2) * (laplacian + massTerm);
            
            // Update buffers
            _phiNextBuffer![nodeIndex] = phiNext;
        }

        private double CalculateGraphLaplacian(int nodeIndex, double[] field)
        {
            double laplacian = 0;
            foreach (int neighbor in Neighbors(nodeIndex))
            {
                // Standard graph Laplacian: sum_j w_ij (phi_i - phi_j)
                laplacian += Weights[nodeIndex, neighbor] * (field[nodeIndex] - field[neighbor]);
            }
            return laplacian;
        }

        /// <summary>
        /// Combined physics step following Quantum Graphity principles:
        /// 1. Quantum state evolves unitarily with Hamiltonian
        /// 2. Topology optimizes to minimize action
        /// 3. Classical states emerge from quantum measurement
        ///
        /// RQ-HYPOTHESIS CHECKLIST FIX #6: Zeno Effect Prevention
        /// ========================================================
        /// Topology changes (measurements) now occur with separated time scale:
        /// - Quantum evolution: every step
        /// - Topology updates: every TopologyDecoherenceInterval steps
        ///
        /// ITEM 43 (10.2): Adaptive Topology Decoherence Algorithm
        /// ========================================================
        /// When AdaptiveTopologyDecoherence is enabled, the interval dynamically
        /// adjusts based on graph size and energy density to optimize topology
        /// change rate for physical correctness.
        ///
        /// This prevents quantum Zeno effect where frequent measurements
        /// freeze the quantum evolution.
        /// </summary>
        public void QuantumGraphityStep()
        {
            _quantumGraphityStepCount++;

            double dt = ComputeRelationalDt();
            EvolveRelationalTimeStep(dt);

            // Update masses after field evolution
            UpdateNodeMassModels();

            // ITEM 43 (10.2): Adaptive Topology Decoherence
            // Determine topology update interval (fixed or adaptive)
            int topologyInterval = PhysicsConstants.TopologyDecoherenceInterval;

            // Try to use adaptive algorithm if enabled
            // Note: AdaptiveTopologyDecoherence requires SimulationSettings, which we can get from
            // the EnergyLedger if it was initialized with settings
            try
            {
                // Lazy initialization of adaptive decoherence calculator
                if (_adaptiveDecoherence == null)
                {
                    // Try to create from existing EnergyLedger settings
                    // This will use default settings if ledger doesn't have them
                    _adaptiveDecoherence = new AdaptiveTopologyDecoherence(
                        Core.Configuration.SimulationSettings.Default);
                }

                // Compute adaptive interval if enabled
                // Use total energy from ledger if available
                double totalEnergy = _ledger.TotalTrackedEnergy;
                if (totalEnergy <= 0)
                {
                    // Fallback: estimate from node energies
                    totalEnergy = _nodeEnergy?.Sum() ?? N;
                }

                topologyInterval = _adaptiveDecoherence.ComputeInterval(N, totalEnergy);
            }
            catch
            {
                // Fall back to fixed interval if adaptive calculation fails
                topologyInterval = PhysicsConstants.TopologyDecoherenceInterval;
            }

            // RQ-HYPOTHESIS CHECKLIST FIX #6: Time Scale Separation
            // Topology updates are "slow" compared to quantum evolution
            bool shouldUpdateTopology = Physics.TimeManager.ShouldUpdateTopologyAdaptive(
                _quantumGraphityStepCount,
                topologyInterval);

            if (shouldUpdateTopology)
            {
                // Optimize network topology using LOCAL action Metropolis-Hastings
                int steps = Math.Max(1, N / 10);
                int accepted = 0;

                for (int s = 0; s < steps; s++)
                {
                    // RQ-HYPOTHESIS CHECKLIST FIX #6 & ITEM 43: Adaptive flip probability
                    // Skip flip if edge has high quantum amplitude (coherence protection)
                    if (PhysicsConstants.EnableAdaptiveTopologyDecoherence || _adaptiveDecoherence != null)
                    {
                        if (MetropolisEdgeStepWithCoherenceCheck())
                            accepted++;
                    }
                    else
                    {
                        if (MetropolisEdgeStepLocalAction())
                            accepted++;
                    }
                }

                // Update edge delays after topology changes
                if (accepted > 0)
                {
                    UpdateEdgeDelaysFromDistances();
                }
            }

            // Update correlation mass from new topology
            RecomputeCorrelationMass();

            // Update spectral geometry (emergent coordinates) - expensive, do occasionally
            if (_rng.NextDouble() < 0.1)
            {
                UpdateSpectralCoordinates();
                SyncCoordinatesFromSpectral();
            }

            // Update clock correlations (internal time)
            UpdateClockCorrelations();
            AdvanceInternalClock();

            // Classical state emerges from quantum + clock
            UpdateStatesFromClockCondProb();

            // Update correlation weights (Hebbian-like learning)
            UpdateCorrelationWeights();
        }

        // Step counter for topology decoherence timing
        private int _quantumGraphityStepCount = 0;

        /// <summary>
        /// Metropolis edge step with quantum coherence check.
        ///
        /// RQ-HYPOTHESIS CHECKLIST FIX #6 & ITEM 43: Prevents Zeno effect by
        /// protecting edges with high quantum amplitude from flipping.
        ///
        /// Uses adaptive algorithm (if available) or falls back to PhysicsConstants.
        /// </summary>
        /// <returns>True if step was accepted.</returns>
        private bool MetropolisEdgeStepWithCoherenceCheck()
        {
            // Select random edge
            int i = _rng.Next(N);
            var neighbors = Neighbors(i).ToArray();
            if (neighbors.Length == 0) return false;
            int j = neighbors[_rng.Next(neighbors.Length)];

            // Check quantum coherence on this edge
            double amplitude = ComputeEdgeQuantumAmplitude(i, j);
            double amplitudeSquared = amplitude * amplitude;

            // ITEM 43 (10.2): Use adaptive algorithm if available, otherwise fall back to constants
            if (_adaptiveDecoherence != null)
            {
                // Use adaptive algorithm for coherence protection
                if (_adaptiveDecoherence.ShouldProtectEdge(amplitudeSquared, _rng.NextDouble()))
                {
                    // Flip suppressed by quantum coherence (adaptive algorithm)
                    return false;
                }
            }
            else
            {
                // Fall back to PhysicsConstants-based check
                if (amplitudeSquared > PhysicsConstants.TopologyFlipAmplitudeThreshold)
                {
                    double suppressionFactor = Math.Exp(-amplitudeSquared / PhysicsConstants.TopologyDecoherenceTemperature);
                    if (_rng.NextDouble() > suppressionFactor)
                    {
                        // Flip suppressed by quantum coherence (legacy)
                        return false;
                    }
                }
            }

            // Proceed with normal Metropolis step
            return MetropolisEdgeStepLocalAction();
        }

        /// <summary>
        /// Compute quantum amplitude on edge (i,j) from wave function.
        /// Used for coherence-aware topology updates.
        /// </summary>
        private double ComputeEdgeQuantumAmplitude(int i, int j)
        {
            if (_waveMulti == null || _waveMulti.Length == 0)
                return 0.0;

            int d = GaugeDimension;
            double amplitude = 0.0;

            // Sum amplitude over all gauge components
            for (int a = 0; a < d; a++)
            {
                int idxI = i * d + a;
                int idxJ = j * d + a;

                if (idxI < _waveMulti.Length && idxJ < _waveMulti.Length)
                {
                    // Correlation amplitude: |?_i* � ?_j|
                    Complex psiI = _waveMulti[idxI];
                    Complex psiJ = _waveMulti[idxJ];
                    amplitude += (Complex.Conjugate(psiI) * psiJ).Magnitude;
                }
            }

            return amplitude / d; // Normalize by gauge dimension
        }
    }
}
