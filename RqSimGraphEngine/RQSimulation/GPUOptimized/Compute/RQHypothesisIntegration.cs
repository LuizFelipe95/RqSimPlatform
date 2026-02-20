using RQSimulation;
using RQSimulation.EventBasedModel;
using RQSimulation.GPUOptimized;
using System;
using System.Collections.Generic;

namespace RqSimGraphEngine.RQSimulation.GPUOptimized.Compute
{
    /// <summary>
    /// Integration layer for RQ-Hypothesis compliance improvements
    /// 
    /// This class provides the main entry points for using the GPU-optimized
    /// implementations with the existing RQGraph infrastructure.
    /// 
    /// Implements all 5 checklist items:
    /// 1. Event-Driven Time Architecture (DES)
    /// 2. Gauss Law Projection
    /// 3. Phase Transition with Increased Coupling and Annealing
    /// 4. Ollivier-Ricci Curvature
    /// 5. Spectral Dimension Validation
    /// </summary>
    public static class RQHypothesisIntegration
    {
        private static double _lastSpectralDimension = double.NaN;
        private static bool _useEventDrivenTime = false;
        private static bool _useImprovedGravity = true;
        private static bool _validateSpectralDimension = true;

        /// <summary>
        /// Enable or disable event-driven time architecture
        /// Default: disabled (to maintain backward compatibility)
        /// </summary>
        public static bool UseEventDrivenTime
        {
            get => _useEventDrivenTime;
            set => _useEventDrivenTime = value;
        }

        /// <summary>
        /// Enable or disable improved gravity with annealing
        /// Default: enabled
        /// </summary>
        public static bool UseImprovedGravity
        {
            get => _useImprovedGravity;
            set => _useImprovedGravity = value;
        }

        /// <summary>
        /// Enable or disable spectral dimension validation
        /// Default: enabled
        /// </summary>
        public static bool ValidateSpectralDimension
        {
            get => _validateSpectralDimension;
            set => _validateSpectralDimension = value;
        }

        /// <summary>
        /// Initialize RQ-Hypothesis compliance features
        /// Call this at the start of simulation
        /// </summary>
        public static void Initialize(RQGraph graph)
        {
            //Console.WriteLine("=== RQ-HYPOTHESIS COMPLIANCE INITIALIZATION ===");
            //Console.WriteLine();

            // Log computation mode
            ComputationHelpers.LogStatus();
            //Console.WriteLine();

            // Log enabled features
            //Console.WriteLine("Enabled Features:");
            //Console.WriteLine($"  [1] Event-Driven Time (DES): {(UseEventDrivenTime ? "ENABLED" : "disabled")}");
            //Console.WriteLine($"  [2] Gauss Law Projection: {(graph.EnforceGaugeConstraintsEnabled ? "ENABLED" : "disabled")}");
            //Console.WriteLine($"  [3] Improved Gravity + Annealing: {(UseImprovedGravity ? "ENABLED" : "disabled")}");
            //Console.WriteLine($"  [4] Ollivier-Ricci Curvature: ENABLED (replaces Forman-Ricci)");
            //Console.WriteLine($"  [5] Spectral Dimension Validation: {(ValidateSpectralDimension ? "ENABLED" : "disabled")}");
            //Console.WriteLine();

            // Initialize spectral dimension tracking
            if (ValidateSpectralDimension)
            {
                _lastSpectralDimension = graph.ComputeSpectralDimension();
                //Console.WriteLine($"Initial spectral dimension: {_lastSpectralDimension:F3}");

                var status = SpectralDimensionValidator.ValidateSpinorCompatibility(graph);
                //Console.WriteLine($"Spinor compatibility: {(status.IsCompatible ? "OK" : "INCOMPATIBLE")}");
                if (!status.IsCompatible)
                {
                    //Console.WriteLine($"  {status.Recommendation}");
                }
            }

            //Console.WriteLine();
            //Console.WriteLine("===============================================");
            //Console.WriteLine();
        }

        /// <summary>
        /// Update physics for one simulation step
        /// This is the main integration point that replaces the old for-loop
        /// </summary>
        public static void StepPhysics(
            RQGraph graph,
            int currentStep,
            double dt,
            List<string>? diagnosticsExport = null)
        {
            // CHECKLIST ITEM 3: Use improved gravity with annealing
            if (UseImprovedGravity && currentStep % 10 == 0)
            {
                ImprovedNetworkGravity.EvolveNetworkGeometryOllivier(graph, dt, currentStep);
            }

            // CHECKLIST ITEM 5: Validate spectral dimension and suppress spinors if needed
            if (ValidateSpectralDimension && currentStep % 100 == 0)
            {
                double currentDimension = graph.ComputeSpectralDimension();

                // Export diagnostics
                if (diagnosticsExport != null)
                {
                    SpectralDimensionValidator.ExportDiagnostics(graph, currentStep, diagnosticsExport);
                }

                // Check for transitions
                if (!double.IsNaN(_lastSpectralDimension))
                {
                    var transitionInfo = SpectralDimensionValidator.MonitorTransition(
                        _lastSpectralDimension,
                        currentDimension);

                    if (transitionInfo.HasCrossedThreshold)
                    {
                        LogTransition(transitionInfo, currentStep);
                    }
                }

                _lastSpectralDimension = currentDimension;

                // Suppress spinor evolution if dimension is incompatible
                if (SpectralDimensionValidator.ShouldSuppressSpinorEvolution(graph))
                {
                    //Console.WriteLine($"[WARNING] Step {currentStep}: Spinor evolution suppressed due to incompatible spectral dimension");
                }
            }

            // CHECKLIST ITEM 2: Gauss law projection is automatically called in
            // EvolveYangMillsRelational when EnforceGaugeConstraintsEnabled is true

            // Regular physics updates continue as before
            // The event-driven architecture is optional and can be enabled separately
        }

        /// <summary>
        /// Run event-driven simulation (CHECKLIST ITEM 1)
        /// This completely replaces the standard for-loop approach
        /// </summary>
        public static void RunEventDrivenSimulation(
            RQGraph graph,
            double totalTime,
            int seed = 42)
        {
            if (!UseEventDrivenTime)
            {
                throw new InvalidOperationException(
                    "Event-driven time is disabled. Set UseEventDrivenTime = true first.");
            }

            //Console.WriteLine("=== STARTING EVENT-DRIVEN SIMULATION (DES) ===");
            //Console.WriteLine($"Total simulation time: {totalTime}");
            //Console.WriteLine();

            var engine = new EventDrivenEngine(graph, totalTime, seed);
            engine.Run();

            //Console.WriteLine();
            //Console.WriteLine($"Final global clock: {engine.GlobalClock:F3}");
            //Console.WriteLine("=== EVENT-DRIVEN SIMULATION COMPLETE ===");
        }

        /// <summary>
        /// Get status report for RQ-Hypothesis compliance
        /// </summary>
        public static string GetStatusReport(RQGraph graph)
        {
            var report = new System.Text.StringBuilder();

            report.AppendLine("=== RQ-HYPOTHESIS COMPLIANCE STATUS ===");
            report.AppendLine();

            // Item 1: Event-driven time
            report.AppendLine($"[1] Event-Driven Time: {(UseEventDrivenTime ? "ACTIVE" : "INACTIVE")}");

            // Item 2: Gauge constraints
            double gaussViolation = graph.ComputeGaussLawViolation();
            string gaussStatus = gaussViolation < 1e-6 ? "OK" : "VIOLATED";
            report.AppendLine($"[2] Gauss Law: {gaussStatus} (violation: {gaussViolation:E3})");

            // Item 3: Phase transition
            bool hasHeavyClusters = ImprovedNetworkGravity.HasFormattedHeavyClusters(graph);
            report.AppendLine($"[3] Phase Transition: {(hasHeavyClusters ? "COMPLETE" : "IN PROGRESS")}");

            // Item 4: Curvature
            double avgCurvature = ImprovedNetworkGravity.ComputeAverageOllivierCurvature(graph);
            report.AppendLine($"[4] Avg Ollivier-Ricci Curvature: {avgCurvature:F6}");

            // Item 5: Spectral dimension
            if (ValidateSpectralDimension && !double.IsNaN(_lastSpectralDimension))
            {
                var status = SpectralDimensionValidator.ValidateSpinorCompatibility(graph);
                report.AppendLine($"[5] Spectral Dimension: {status.SpectralDimension:F3} ({(status.IsCompatible ? "OK" : "INCOMPATIBLE")})");
            }
            else
            {
                report.AppendLine("[5] Spectral Dimension: NOT MONITORED");
            }

            report.AppendLine();
            report.AppendLine("========================================");

            return report.ToString();
        }

        /// <summary>
        /// Log spectral dimension transition
        /// </summary>
        private static void LogTransition(SpectralTransitionInfo info, int step)
        {
            //Console.WriteLine();
            //Console.WriteLine($"=== SPECTRAL TRANSITION DETECTED at step {step} ===");
            //Console.WriteLine($"Previous dimension: {info.PreviousDimension:F3}");
            //Console.WriteLine($"Current dimension: {info.CurrentDimension:F3}");
            //Console.WriteLine($"Change: {info.DimensionChange:+F3}");
            //Console.WriteLine($"Type: {info.TransitionType}");

            switch (info.TransitionType)
            {
                case TransitionType.FractalToSpacetime:
                    //Console.WriteLine("*** SUCCESS: Graph has crystallized into 4D spacetime! ***");
                    break;
                case TransitionType.SpacetimeToFractal:
                    //Console.WriteLine("*** WARNING: Spacetime structure has collapsed! ***");
                    break;
                case TransitionType.Crystallizing:
                    //Console.WriteLine("Progress: Dimension increasing toward 4D");
                    break;
                case TransitionType.Fragmenting:
                    //Console.WriteLine("Regression: Dimension decreasing");
                    break;
            }

            //Console.WriteLine("================================================");
            //Console.WriteLine();
        }
    }
}
