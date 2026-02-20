using System;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// Status of spinor field compatibility with spectral dimension
    /// </summary>
    public class SpinorCompatibilityStatus
    {
        public double SpectralDimension { get; set; }
        public bool IsCompatible { get; set; }
        public double DeviationPercent { get; set; }
        public string Recommendation { get; set; } = string.Empty;

        /// <summary>
        /// True if d_S has been suspiciously stable at exactly 1.0
        /// </summary>
        public bool IsSuspiciouslyStable { get; set; }

        /// <summary>
        /// Diagnosis message if suspicious stability detected
        /// </summary>
        public string SuspiciousDiagnosis { get; set; } = string.Empty;
    }

    /// <summary>
    /// Status of suspicious dimension detection
    /// </summary>
    public class SuspiciousDimensionStatus
    {
        public double CurrentDimension { get; set; }
        public bool IsSuspicious { get; set; }
        public int ConsecutiveExactOneCount { get; set; }
        public double RecentExactOnePercent { get; set; }
        public string Diagnosis { get; set; } = string.Empty;
    }

    /// <summary>
    /// Extended validation result with walker spread analysis
    /// </summary>
    public class ExtendedValidationResult
    {
        public double SpectralDimension { get; set; }
        public int UniqueNodesVisited { get; set; }
        public double UniqueNodesFraction { get; set; }
        public double AverageWalkerDistance { get; set; }
        public double MaxWalkerDistance { get; set; }
        public double TrappedWalkerFraction { get; set; }
        public bool WalkersAreLocked { get; set; }
        public string Diagnosis { get; set; } = string.Empty;
    }

    /// <summary>
    /// Information about spectral dimension transitions
    /// </summary>
    public class SpectralTransitionInfo
    {
        public double PreviousDimension { get; set; }
        public double CurrentDimension { get; set; }
        public double DimensionChange { get; set; }
        public bool HasCrossedThreshold { get; set; }
        public TransitionType TransitionType { get; set; }
    }

    /// <summary>
    /// Types of spectral dimension transitions
    /// </summary>
    public enum TransitionType
    {
        None,
        FractalToSpacetime,    // d: 2 ? 4 (SUCCESS: crystallization)
        SpacetimeToFractal,    // d: 4 ? 2 (FAILURE: collapse)
        Crystallizing,         // d increasing toward 4
        Fragmenting           // d decreasing
    }
}
