using System;
using System.Collections.Generic;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// RQGraph partial class for Internal Observer functionality.
    /// 
    /// RQ-HYPOTHESIS: Relational Measurement
    /// =====================================
    /// Replaces external "God's eye view" measurements with
    /// internal observer subsystems that become entangled
    /// with the measured system.
    /// </summary>
    public partial class RQGraph
    {
        private InternalObserver? _internalObserver;

        /// <summary>
        /// Flag indicating whether internal observer mode is active.
        /// </summary>
        public bool UseInternalObserver => _internalObserver != null;

        /// <summary>
        /// Current internal observer instance.
        /// </summary>
        public InternalObserver? InternalObserver => _internalObserver;

        /// <summary>
        /// Configure internal observer subsystem.
        /// Once configured, measurements should use internal observation.
        /// </summary>
        /// <param name="observerNodes">Nodes that form the observer subsystem</param>
        /// <param name="seed">Random seed for stochastic outcomes</param>
        public void ConfigureInternalObserver(IEnumerable<int> observerNodes, int? seed = null)
        {
            ArgumentNullException.ThrowIfNull(observerNodes);
            _internalObserver = new InternalObserver(this, observerNodes, seed);
        }

        /// <summary>
        /// Configure internal observer using automatic selection.
        /// Selects a small region of low-degree nodes as observer.
        /// </summary>
        /// <param name="observerSize">Number of nodes in observer subsystem</param>
        /// <param name="seed">Random seed for selection and outcomes</param>
        public void ConfigureInternalObserverAuto(int observerSize = 5, int? seed = null)
        {
            if (observerSize <= 0 || observerSize >= N)
                throw new ArgumentOutOfRangeException(nameof(observerSize), 
                    $"Observer size must be in (0, {N})");

            // Select nodes with lowest degrees (less connected = more "isolated" observer)
            var nodesByDegree = new List<(int node, int degree)>();
            for (int i = 0; i < N; i++)
            {
                nodesByDegree.Add((i, _degree[i]));
            }
            nodesByDegree.Sort((a, b) => a.degree.CompareTo(b.degree));

            var observerNodes = new List<int>();
            for (int i = 0; i < Math.Min(observerSize, nodesByDegree.Count); i++)
            {
                observerNodes.Add(nodesByDegree[i].node);
            }

            _internalObserver = new InternalObserver(this, observerNodes, seed);
        }

        /// <summary>
        /// Disable internal observer and return to legacy mode.
        /// </summary>
        public void DisableInternalObserver()
        {
            _internalObserver = null;
        }

        /// <summary>
        /// RQ-COMPLIANT: Get energy statistics via internal observer.
        /// Returns data that has been "measured" by the observer subsystem.
        /// </summary>
        public double GetInternallyObservedEnergy()
        {
            if (_internalObserver == null)
            {
                // Fallback to direct read (legacy mode)
                return ComputeNetworkHamiltonian();
            }

            // Trigger internal measurement sweep
            _internalObserver.MeasureSweep();

            // Return observer's expectation value (correlated with actual energy)
            return _internalObserver.GetObserverExpectationValue();
        }

        /// <summary>
        /// RQ-COMPLIANT: Get energy with correlation to specific region.
        /// </summary>
        /// <param name="targetNodes">Region to measure</param>
        public double GetInternallyObservedEnergyOfRegion(IEnumerable<int> targetNodes)
        {
            if (_internalObserver == null)
            {
                // Legacy: compute energy directly
                double energy = 0.0;
                foreach (int node in targetNodes)
                {
                    if (node >= 0 && node < N)
                        energy += GetNodeMass(node);
                }
                return energy;
            }

            // Measure specific targets
            _internalObserver.MeasureSweep(targetNodes);

            // Return correlation as proxy for measured energy
            return _internalObserver.GetCorrelationWithRegion(targetNodes);
        }

        /// <summary>
        /// Shift phase of wavefunction at a specific node.
        /// Used by internal observer for creating entanglement.
        /// </summary>
        /// <param name="nodeId">Node index</param>
        /// <param name="deltaPhase">Phase shift in radians</param>
        public void ShiftNodePhase(int nodeId, double deltaPhase)
        {
            if (nodeId < 0 || nodeId >= N)
                return;

            // Phase rotation: ? ? ? * exp(i * ??)
            Complex rotation = Complex.FromPolarCoordinates(1.0, deltaPhase);

            // Apply to multi-component wavefunction
            if (_waveMulti != null)
            {
                int d = GaugeDimension;
                int startIdx = nodeId * d;
                for (int a = 0; a < d && startIdx + a < _waveMulti.Length; a++)
                {
                    _waveMulti[startIdx + a] *= rotation;
                }
            }

            // Apply to single-component wavefunction if exists
            if (_wavefunction != null && nodeId < _wavefunction.Length)
            {
                _wavefunction[nodeId] *= rotation;
            }
        }

        /// <summary>
        /// Get wavefunction value at a specific node.
        /// </summary>
        /// <param name="nodeId">Node index</param>
        /// <returns>Complex wavefunction amplitude</returns>
        public Complex GetNodeWavefunction(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                return Complex.Zero;

            // Try multi-component wavefunction first
            if (_waveMulti != null)
            {
                int d = GaugeDimension;
                if (nodeId * d < _waveMulti.Length)
                {
                    return _waveMulti[nodeId * d]; // First component
                }
            }

            // Fallback to single-component wavefunction
            if (_wavefunction != null && nodeId < _wavefunction.Length)
            {
                return _wavefunction[nodeId];
            }

            return Complex.Zero;
        }

        /// <summary>
        /// Set wavefunction value at a specific node.
        /// </summary>
        /// <param name="nodeId">Node index</param>
        /// <param name="value">New wavefunction value</param>
        public void SetNodeWavefunction(int nodeId, Complex value)
        {
            if (nodeId < 0 || nodeId >= N)
                return;

            // Set in multi-component wavefunction
            if (_waveMulti != null)
            {
                int d = GaugeDimension;
                if (nodeId * d < _waveMulti.Length)
                {
                    _waveMulti[nodeId * d] = value;
                }
            }

            // Set in single-component wavefunction if exists
            if (_wavefunction != null && nodeId < _wavefunction.Length)
            {
                _wavefunction[nodeId] = value;
            }
        }

        /// <summary>
        /// Get mutual information between internal observer and a target region.
        /// Returns 0 if no internal observer configured.
        /// </summary>
        public double GetObserverMutualInformation(IEnumerable<int> targetNodes)
        {
            if (_internalObserver == null)
                return 0.0;

            return _internalObserver.GetMutualInformation(targetNodes);
        }

        /// <summary>
        /// Get observation statistics from internal observer.
        /// </summary>
        public ObservationStatistics GetObservationStatistics()
        {
            if (_internalObserver == null)
            {
                return new ObservationStatistics
                {
                    TotalObservations = 0,
                    UniqueTargetsObserved = 0,
                    AveragePhaseShift = 0.0,
                    MaxPhaseShift = 0.0,
                    AverageConnectionWeight = 0.0
                };
            }

            return _internalObserver.GetStatistics();
        }
    }
}
