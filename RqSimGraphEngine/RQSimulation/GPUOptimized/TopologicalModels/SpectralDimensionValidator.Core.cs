using System;
using System.Collections.Generic;
using System.Linq;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// Spectral Dimension Validation and Spinor Compatibility Checker
    /// 
    /// CHECKLIST ITEM 5: Verify that spinor fields are compatible with actual spectral dimension
    /// 
    /// Problem: The code uses 4-component spinors (assuming 3+1 dimensions), but if the graph
    /// is fractal with d_S ? 2.5, the Dirac operator is incorrect.
    /// 
    /// Solution: Compute spectral dimension periodically. If d_S deviates from 4 by >10%,
    /// issue warning or suppress spinor evolution until geometry stabilizes.
    /// 
    /// ADDITIONAL VALIDATION (Action 3):
    /// If d_S = 1.000 exactly for many steps, this indicates walkers are trapped in small
    /// clusters (1D chain) and the calculation may be unreliable.
    /// </summary>
    public static partial class SpectralDimensionValidator
    {
        private const double TargetDimension = 4.0;
        private const double TolerancePercent = 0.10; // 10% tolerance
        private const double MinAcceptableDimension = TargetDimension * (1.0 - TolerancePercent);
        private const double MaxAcceptableDimension = TargetDimension * (1.0 + TolerancePercent);

        /// <summary>
        /// Check if spinor fields should be active given current spectral dimension
        /// </summary>
        public static bool ShouldEnableSpinorFields(double spectralDimension)
        {
            // Spinors are only valid for d_S ? 4 (within 10% tolerance)
            return spectralDimension >= MinAcceptableDimension &&
                   spectralDimension <= MaxAcceptableDimension;
        }

        /// <summary>
        /// Get recommendation based on spectral dimension
        /// </summary>
        private static string GetRecommendation(double spectralDim)
        {
            if (spectralDim < MinAcceptableDimension)
            {
                return $"Spectral dimension {spectralDim:F2} < {MinAcceptableDimension:F2}. " +
                       "Graph is too fractal for 4D spinors. Suppress spinor evolution " +
                       "until geometry crystallizes to 4D.";
            }
            else if (spectralDim > MaxAcceptableDimension)
            {
                return $"Spectral dimension {spectralDim:F2} > {MaxAcceptableDimension:F2}. " +
                       "Graph has too many dimensions. Check for over-connectivity.";
            }
            else
            {
                return $"Spectral dimension {spectralDim:F2} is compatible with 4D spinors.";
            }
        }

        /// <summary>
        /// Decide whether to suppress spinor field evolution
        /// </summary>
        public static bool ShouldSuppressSpinorEvolution(RQGraph graph)
        {
            double spectralDim = graph.ComputeSpectralDimension();
            return !ShouldEnableSpinorFields(spectralDim);
        }

        /// <summary>
        /// Monitor spectral dimension evolution and detect transitions
        /// </summary>
        public static SpectralTransitionInfo MonitorTransition(
            double previousDimension,
            double currentDimension)
        {
            var info = new SpectralTransitionInfo
            {
                PreviousDimension = previousDimension,
                CurrentDimension = currentDimension,
                DimensionChange = currentDimension - previousDimension,
                HasCrossedThreshold = false,
                TransitionType = TransitionType.None
            };

            // Detect transition from fractal (d < 3) to spacetime (d ? 4)
            if (previousDimension < 3.0 && currentDimension >= 3.8)
            {
                info.HasCrossedThreshold = true;
                info.TransitionType = TransitionType.FractalToSpacetime;
                //Console.WriteLine($"[SUCCESS] Spectral dimension crossed from {previousDimension:F2} to {currentDimension:F2}!");
                //Console.WriteLine("[SUCCESS] Graph has crystallized into 4D spacetime!");
            }
            // Detect transition from spacetime to fractal (collapse)
            else if (previousDimension >= 3.8 && currentDimension < 3.0)
            {
                info.HasCrossedThreshold = true;
                info.TransitionType = TransitionType.SpacetimeToFractal;
                //Console.WriteLine($"[WARNING] Spectral dimension collapsed from {previousDimension:F2} to {currentDimension:F2}");
                //Console.WriteLine("[WARNING] Graph has become fractal - spacetime structure lost!");
            }
            // Detect gradual increase toward 4D
            else if (currentDimension > previousDimension && currentDimension < 4.5)
            {
                info.TransitionType = TransitionType.Crystallizing;
            }
            // Detect gradual decrease (fragmentation)
            else if (currentDimension < previousDimension && currentDimension > 1.5)
            {
                info.TransitionType = TransitionType.Fragmenting;
            }

            return info;
        }
    }
}
