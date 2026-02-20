using System;

namespace RQSimulation
{
    /// <summary>
    /// Status struct for graph health monitoring.
    /// </summary>
    public struct GraphHealthStatus
    {
        public double SpectralDimension { get; set; }
        public double LargestClusterFraction { get; set; }
        public double AverageDegree { get; set; }

        /// <summary>
        /// Graph is fragmented if spectral dimension is too low.
        /// </summary>
        public bool IsFragmented => SpectralDimension < 1.5;

        /// <summary>
        /// Giant cluster detected if largest cluster is too big.
        /// </summary>
        public bool HasGiantCluster => LargestClusterFraction > 0.3;

        /// <summary>
        /// Emergency giant cluster detected (very large).
        /// </summary>
        public bool HasEmergencyGiantCluster => LargestClusterFraction > PhysicsConstants.EmergencyGiantClusterThreshold;

        /// <summary>
        /// Graph is healthy if not fragmented and no giant cluster.
        /// </summary>
        public bool IsHealthy => !IsFragmented && !HasGiantCluster;

        /// <summary>
        /// Status description for logging.
        /// </summary>
        public string StatusDescription
        {
            get
            {
                if (IsHealthy)
                    return $"Healthy: d_S={SpectralDimension:F2}, cluster={LargestClusterFraction:P0}";
                if (IsFragmented)
                    return $"FRAGMENTED: d_S={SpectralDimension:F2} (too low)";
                if (HasGiantCluster)
                    return $"GIANT CLUSTER: {LargestClusterFraction:P0} of graph";
                return $"Unknown: d_S={SpectralDimension:F2}, cluster={LargestClusterFraction:P0}";
            }
        }
    }

    /// <summary>
    /// Exception thrown when graph fragmentation is detected.
    /// </summary>
    public class GraphFragmentationException : Exception
    {
        public double SpectralDimension { get; }
        public int Step { get; }
        public int ConsecutiveChecks { get; }

        public GraphFragmentationException(double spectralDimension, int step, int consecutiveChecks)
            : base($"Graph fragmentation detected: d_S={spectralDimension:F2} at step {step} ({consecutiveChecks} consecutive checks)")
        {
            SpectralDimension = spectralDimension;
            Step = step;
            ConsecutiveChecks = consecutiveChecks;
        }

        public GraphFragmentationException(string message, double spectralDimension, int consecutiveChecks)
            : base(message)
        {
            SpectralDimension = spectralDimension;
            ConsecutiveChecks = consecutiveChecks;
        }
    }
}
