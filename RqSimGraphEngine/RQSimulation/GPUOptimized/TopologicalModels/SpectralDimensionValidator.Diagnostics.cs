using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation.GPUOptimized
{
    public static partial class SpectralDimensionValidator
    {
        // Suspicious dimension detection (Action 3)
        private const double SuspiciousExactDimension = 1.0;
        private const double SuspiciousDimensionTolerance = 0.001; // If d_S = 1.000 ± 0.001
        private const int SuspiciousStabilityWindow = 100; // Number of steps to track
        private const int SuspiciousStabilityThreshold = 80; // If 80% of recent values are exactly 1.0

        // History for detecting suspiciously stable d_S
        private static readonly Queue<double> _recentDimensions = new();
        private static int _consecutiveExactOne = 0;

        /// <summary>
        /// Check if spectral dimension is suspiciously stable at exactly 1.0
        /// This indicates walkers are trapped in small clusters (1D chain structure)
        /// </summary>
        public static SuspiciousDimensionStatus CheckForSuspiciousStability(double spectralDimension)
        {
            var status = new SuspiciousDimensionStatus
            {
                CurrentDimension = spectralDimension,
                IsSuspicious = false,
                ConsecutiveExactOneCount = 0,
                RecentExactOnePercent = 0.0,
                Diagnosis = string.Empty
            };

            // Check if current dimension is exactly 1.0 (within tolerance)
            bool isExactlyOne = Math.Abs(spectralDimension - SuspiciousExactDimension) < SuspiciousDimensionTolerance;

            // Track consecutive exact-1.0 values
            if (isExactlyOne)
            {
                _consecutiveExactOne++;
            }
            else
            {
                _consecutiveExactOne = 0;
            }

            // Add to history
            _recentDimensions.Enqueue(spectralDimension);
            while (_recentDimensions.Count > SuspiciousStabilityWindow)
            {
                _recentDimensions.Dequeue();
            }

            // Calculate percentage of recent values that are exactly 1.0
            int exactOneCount = _recentDimensions.Count(d => Math.Abs(d - SuspiciousExactDimension) < SuspiciousDimensionTolerance);
            double exactOnePercent = _recentDimensions.Count > 0
                ? (double)exactOneCount / _recentDimensions.Count
                : 0.0;

            status.ConsecutiveExactOneCount = _consecutiveExactOne;
            status.RecentExactOnePercent = exactOnePercent;

            // Determine if suspicious
            if (_consecutiveExactOne >= 10 || exactOnePercent >= 0.8)
            {
                status.IsSuspicious = true;
                status.Diagnosis = DiagnoseSuspiciousDimension();
            }

            return status;
        }

        /// <summary>
        /// Diagnose why spectral dimension might be stuck at 1.0
        /// </summary>
        private static string DiagnoseSuspiciousDimension()
        {
            return "[SUSPICIOUS] d_S = 1.000 is too stable - possible causes:\n" +
                   "  1. Random walkers are trapped in isolated 1D chains\n" +
                   "  2. Graph has fragmented into disconnected linear components\n" +
                   "  3. EdgeCreationBarrier may still be too high for long-range links\n" +
                   "  4. GravitationalCoupling may be cementing 1D structure\n" +
                   "RECOMMENDED ACTIONS:\n" +
                   "  - Check graph connectivity (number of connected components)\n" +
                   "  - Verify LargestCluster size is reasonable\n" +
                   "  - Consider increasing TopologyTunnelingRate further\n" +
                   "  - Ensure initial graph is not too sparse (initialEdgeProb)";
        }

        /// <summary>
        /// Reset the history tracking (call when starting new simulation)
        /// </summary>
        public static void ResetHistory()
        {
            _recentDimensions.Clear();
            _consecutiveExactOne = 0;
        }

        /// <summary>
        /// Export diagnostics with spectral dimension and spinor status
        /// Now includes suspicious stability detection
        /// </summary>
        public static void ExportDiagnostics(
            RQGraph graph,
            int step,
            List<string> diagnosticsExport)
        {
            var status = ValidateSpinorCompatibility(graph);

            string statusFlag = status.IsCompatible ? "OK" : "INCOMPATIBLE";
            if (status.IsSuspiciouslyStable)
            {
                statusFlag = "SUSPICIOUS";
            }

            string line = $"{step},{status.SpectralDimension:F4},{statusFlag}," +
                         $"{status.DeviationPercent:F4},{status.Recommendation}";

            diagnosticsExport.Add(line);

            // Log warning if incompatible (to standard console to avoid UI dependency)
            if (!status.IsCompatible)
            {
                //Console.WriteLine($"[WARNING] Step {step}: {status.Recommendation}");
            }

            // Log suspicious stability warning
            if (status.IsSuspiciouslyStable)
            {
                //Console.WriteLine($"[SUSPICIOUS] Step {step}: d_S = {status.SpectralDimension:F3} is suspiciously stable");
                //Console.WriteLine(status.SuspiciousDiagnosis);
            }
        }
    }
}
