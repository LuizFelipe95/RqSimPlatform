using System;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Quantum edge with superposition amplitude.
    /// 
    /// RQ-HYPOTHESIS: Quantum Graphity
    /// ================================
    /// In quantum gravity, the geometry itself is in superposition.
    /// An edge can exist and not-exist simultaneously.
    /// 
    /// |edge? = ?|exists? + ?|not-exists?
    /// 
    /// The amplitude ? encodes:
    /// - Magnitude: sqrt of existence probability (|?|? = P_exist)
    /// - Phase: gauge/geometric information
    /// 
    /// The classical "weight" is stored separately as a property that applies
    /// when the edge exists.
    /// </summary>
    public readonly struct ComplexEdge
    {
        /// <summary>
        /// Classical weight of the edge (when it exists).
        /// This is separate from existence probability.
        /// </summary>
        private readonly double _classicalWeight;
        
        /// <summary>
        /// Classical phase (for compatibility with existing gauge code).
        /// </summary>
        public readonly double Phase;
        
        /// <summary>
        /// Quantum amplitude for edge existence.
        /// |Amplitude|? = probability of edge existing upon measurement.
        /// For definite classical edge, |Amplitude| = 1.0.
        /// </summary>
        public readonly Complex Amplitude;

        /// <summary>
        /// Create edge with classical weight and definite existence.
        /// Amplitude magnitude is 1.0 (100% existence probability).
        /// </summary>
        public ComplexEdge(double classicalWeight, double phase)
        {
            _classicalWeight = classicalWeight;
            Phase = phase;
            // For a definite "exists" state, amplitude magnitude = 1.0
            Amplitude = Complex.FromPolarCoordinates(1.0, phase);
        }
        
        /// <summary>
        /// Create edge from quantum amplitude (for superposition states).
        /// </summary>
        public ComplexEdge(Complex amplitude)
        {
            Amplitude = amplitude;
            _classicalWeight = amplitude.Magnitude; // Best guess for classical weight
            Phase = amplitude.Phase;
        }
        
        /// <summary>
        /// Create edge with explicit weight and amplitude (full control).
        /// </summary>
        public ComplexEdge(double classicalWeight, Complex amplitude)
        {
            _classicalWeight = classicalWeight;
            Amplitude = amplitude;
            Phase = amplitude.Phase;
        }

        /// <summary>
        /// Probability of finding edge upon measurement.
        /// This is |Amplitude|?, NOT the classical weight.
        /// </summary>
        public double ExistenceProbability => Amplitude.Magnitude * Amplitude.Magnitude;

        /// <summary>
        /// Classical weight (for backward compatibility).
        /// Returns the weight of the edge when it exists.
        /// </summary>
        public double GetMagnitude() => _classicalWeight;
        
        /// <summary>
        /// Convert to pure Complex type (returns amplitude).
        /// </summary>
        public Complex ToComplex() => Amplitude;
        
        /// <summary>
        /// Create new edge with different classical weight.
        /// Preserves phase and existence probability.
        /// </summary>
        public ComplexEdge WithMagnitude(double classicalWeight) 
            => new ComplexEdge(classicalWeight, Amplitude);
        
        /// <summary>
        /// Create new edge with different phase.
        /// Preserves classical weight and amplitude magnitude.
        /// </summary>
        public ComplexEdge WithPhase(double phase) 
            => new ComplexEdge(_classicalWeight, Complex.FromPolarCoordinates(Amplitude.Magnitude, phase));
        
        /// <summary>
        /// Create edge in superposition: ?|exists? + ?|not-exists?
        /// The returned edge represents the normalized "exists" amplitude.
        /// Classical weight is taken from alpha's magnitude.
        /// </summary>
        /// <param name="alphaExists">Amplitude for edge existing</param>
        /// <param name="betaNotExists">Amplitude for edge not existing</param>
        /// <returns>ComplexEdge with normalized existence amplitude</returns>
        public static ComplexEdge Superposition(Complex alphaExists, Complex betaNotExists)
        {
            // Normalize: |?|? + |?|? = 1
            double normSquared = 
                alphaExists.Magnitude * alphaExists.Magnitude + 
                betaNotExists.Magnitude * betaNotExists.Magnitude;
            
            if (normSquared < 1e-20)
            {
                return new ComplexEdge(0.0, Complex.Zero);
            }
            
            double norm = Math.Sqrt(normSquared);
            
            // The normalized "existence" amplitude
            Complex normalizedAlpha = alphaExists / norm;
            
            // Classical weight defaults to 0.5 for superposition
            return new ComplexEdge(0.5, normalizedAlpha);
        }
        
        /// <summary>
        /// Collapse superposition to classical state.
        /// Returns true if edge "exists" after measurement.
        /// </summary>
        /// <param name="rng">Random number generator for quantum measurement</param>
        /// <returns>True if edge exists after measurement, false otherwise</returns>
        public bool Measure(Random rng)
        {
            ArgumentNullException.ThrowIfNull(rng);
            
            double p = ExistenceProbability;
            return rng.NextDouble() < p;
        }
        
        /// <summary>
        /// Create a definite "exists" state with given weight.
        /// Existence probability is 1.0.
        /// </summary>
        public static ComplexEdge Exists(double weight, double phase = 0.0)
            => new ComplexEdge(weight, phase);
        
        /// <summary>
        /// Create a definite "not exists" state (zero amplitude).
        /// </summary>
        public static ComplexEdge NotExists()
            => new ComplexEdge(0.0, Complex.Zero);
        
        /// <summary>
        /// Check if edge is in effectively classical state (high probability either way).
        /// </summary>
        /// <param name="threshold">Probability threshold for considering state classical (default 0.99)</param>
        public bool IsClassical(double threshold = 0.99)
        {
            double p = ExistenceProbability;
            return p > threshold || p < (1.0 - threshold);
        }
    }
}
