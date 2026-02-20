using System;
using Microsoft.Extensions.Options;
using RQSimulation.Core.Configuration;

namespace RQSimulation.Core.Utilities
{
    /// <summary>
    /// Adaptive topology decoherence algorithm implementation.
    /// Implements Item 43 (10.2): Adaptive Topology Decoherence Algorithm
    ///
    /// PHYSICS RATIONALE:
    /// ==================
    /// Topology changes (edge flips) represent quantum measurements that cause
    /// decoherence. The rate of topology change should adapt to:
    ///
    /// 1. Graph Size (N): Larger graphs have more degrees of freedom
    ///    - Scaling: interval ? N^(-0.5) (larger graphs need shorter intervals)
    ///
    /// 2. Energy Density (E/N): Higher energy increases quantum fluctuations
    ///    - Scaling: interval ? (1 + k * E/N) (higher energy = longer intervals = more stable)
    ///
    /// 3. Field Amplitude (|ψ|²): High-amplitude edges should be protected
    ///    - P_flip ? exp(-|ψ|² / kT) (quantum coherence protection)
    ///
    /// The adaptive formula:
    ///   interval = base * (N / N_ref)^α * (1 + β * E/N)
    ///
    /// where:
    ///   - base: Base interval from configuration
    ///   - N: Current node count
    ///   - N_ref: Reference graph size (1000 nodes)
    ///   - α: Size exponent (default: -0.5)
    ///   - β: Energy density factor (default: 1.0)
    ///   - E: Total energy
    /// </summary>
    public class AdaptiveTopologyDecoherence
    {
        private readonly SimulationSettings _settings;
        private const double ReferenceGraphSize = 1000.0;

        /// <summary>
        /// Creates a new adaptive topology decoherence calculator.
        /// </summary>
        /// <param name="settings">Simulation settings</param>
        public AdaptiveTopologyDecoherence(SimulationSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Creates a new adaptive topology decoherence calculator with Options pattern support.
        /// </summary>
        /// <param name="settings">Simulation settings options</param>
        public AdaptiveTopologyDecoherence(IOptions<SimulationSettings> settings)
            : this(settings.Value)
        {
        }

        /// <summary>
        /// Compute the adaptive topology decoherence interval.
        ///
        /// Formula:
        ///   interval = base * (N / 1000)^α * (1 + β * energyDensity)
        ///
        /// where:
        ///   - base = TopologyDecoherenceInterval * AdaptiveDecoherenceBaseInterval
        ///   - α = AdaptiveDecoherenceSizeExponent
        ///   - β = AdaptiveDecoherenceEnergyFactor
        ///   - energyDensity = totalEnergy / nodeCount
        /// </summary>
        /// <param name="nodeCount">Current number of nodes in the graph</param>
        /// <param name="totalEnergy">Total energy in the system</param>
        /// <returns>Adaptive decoherence interval (in simulation steps)</returns>
        public int ComputeInterval(int nodeCount, double totalEnergy)
        {
            if (!_settings.AdaptiveTopologyDecoherence)
            {
                // Adaptive mode disabled, return fixed interval
                return _settings.TopologyDecoherenceInterval;
            }

            if (nodeCount <= 0)
            {
                throw new ArgumentException("Node count must be positive", nameof(nodeCount));
            }

            // Base interval from configuration
            double baseInterval = _settings.TopologyDecoherenceInterval *
                                  _settings.AdaptiveDecoherenceBaseInterval;

            // Size factor: (N / N_ref)^α
            // For α = -0.5: larger graphs have shorter intervals (more frequent updates)
            double sizeFactor = Math.Pow(nodeCount / ReferenceGraphSize,
                                        _settings.AdaptiveDecoherenceSizeExponent);

            // Energy density: E / N
            double energyDensity = totalEnergy / nodeCount;

            // Energy factor: (1 + β * E/N)
            // Higher energy density = longer intervals (more stable topology)
            double energyFactor = 1.0 + _settings.AdaptiveDecoherenceEnergyFactor * energyDensity;

            // Combined formula
            double adaptiveInterval = baseInterval * sizeFactor * energyFactor;

            // Clamp to reasonable bounds: [1, 10000]
            int interval = (int)Math.Round(adaptiveInterval);
            interval = Math.Max(1, Math.Min(10000, interval));

            return interval;
        }

        /// <summary>
        /// Compute flip suppression factor for high-amplitude edges.
        ///
        /// Formula:
        ///   suppressionFactor = exp(-|ψ|² / kT)
        ///
        /// where:
        ///   - |ψ|² = field amplitude squared
        ///   - kT = TopologyDecoherenceTemperature
        ///
        /// Returns a value in [0, 1]:
        ///   - 0: Complete suppression (high amplitude, locked by quantum coherence)
        ///   - 1: No suppression (low amplitude, free to flip)
        /// </summary>
        /// <param name="amplitudeSquared">Field amplitude squared |ψ|²</param>
        /// <returns>Flip probability suppression factor in [0, 1]</returns>
        public double ComputeFlipSuppressionFactor(double amplitudeSquared)
        {
            if (amplitudeSquared < _settings.TopologyFlipAmplitudeThreshold)
            {
                // Below threshold: no suppression
                return 1.0;
            }

            // Boltzmann suppression: exp(-|ψ|² / kT)
            double exponent = -amplitudeSquared / _settings.TopologyDecoherenceTemperature;
            double suppressionFactor = Math.Exp(exponent);

            return suppressionFactor;
        }

        /// <summary>
        /// Check if an edge should be protected from topology flips based on its amplitude.
        /// </summary>
        /// <param name="amplitudeSquared">Field amplitude squared |ψ|²</param>
        /// <param name="randomValue">Random value in [0, 1] for probabilistic check</param>
        /// <returns>True if edge should be protected, false if flip is allowed</returns>
        public bool ShouldProtectEdge(double amplitudeSquared, double randomValue)
        {
            if (amplitudeSquared < _settings.TopologyFlipAmplitudeThreshold)
            {
                // Below threshold: no protection
                return false;
            }

            double suppressionFactor = ComputeFlipSuppressionFactor(amplitudeSquared);

            // Edge is protected if random value exceeds suppression factor
            // High suppression (low factor) = high protection probability
            return randomValue > suppressionFactor;
        }

        /// <summary>
        /// Get diagnostic information about the adaptive algorithm parameters.
        /// </summary>
        /// <returns>Diagnostic string with current settings</returns>
        public string GetDiagnostics()
        {
            return $"AdaptiveTopologyDecoherence: " +
                   $"Enabled={_settings.AdaptiveTopologyDecoherence}, " +
                   $"BaseInterval={_settings.TopologyDecoherenceInterval}, " +
                   $"BaseFactor={_settings.AdaptiveDecoherenceBaseInterval}, " +
                   $"SizeExponent={_settings.AdaptiveDecoherenceSizeExponent}, " +
                   $"EnergyFactor={_settings.AdaptiveDecoherenceEnergyFactor}, " +
                   $"AmplitudeThreshold={_settings.TopologyFlipAmplitudeThreshold}, " +
                   $"Temperature={_settings.TopologyDecoherenceTemperature}";
        }
    }
}
