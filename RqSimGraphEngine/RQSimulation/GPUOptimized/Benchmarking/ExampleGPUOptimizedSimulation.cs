using System;
using System.Collections.Generic;
using RqSimGraphEngine.RQSimulation.GPUOptimized.Compute;
using RQSimulation.GPUOptimized;

namespace RQSimulation
{
    /// <summary>
    /// Example demonstrating the GPU-optimized RQ-Hypothesis compliance features
    /// 
    /// This shows how to integrate all 5 checklist items:
    /// 1. Event-Driven Time
    /// 2. Gauss Law Projection
    /// 3. Improved Gravity with Annealing
    /// 4. Ollivier-Ricci Curvature
    /// 5. Spectral Dimension Validation
    /// </summary>
    public class ExampleGPUOptimizedSimulation
    {
        public static void RunStandardSimulation()
        {
            //Console.WriteLine("=== RQ-Hypothesis Compliant Simulation ===");

            // Create simulation configuration
            var config = new SimulationConfig
            {
                NodeCount = 200,
                InitialEdgeProb = 0.1,
                InitialExcitedProb = 0.3,
                TargetDegree = 6,
                LambdaState = 1.0,
                Temperature = 0.1,
                EdgeTrialProbability = 0.05,
                MeasurementThreshold = 0.5,
                Seed = 42,
                TotalSteps = 2000,
                LogEvery = 100,

                // Enable RQ-compliant features
                UseSpacetimePhysics = true,
                UseSpinorField = true,
                UseYangMillsGauge = true,
                UseNetworkGravity = true,
                UseRelationalTime = true,
                UseRelationalYangMills = true,

                // Enable gauge constraint enforcement (CHECKLIST ITEM 2)
                EnforceGaugeConstraints = true,

                // Use geometry momenta for proper gravitational dynamics
                UseGeometryMomenta = true,
            };

            // Create simulation engine
            var engine = new SimulationEngine(config);
            var graph = engine.Graph;

            // Configure GPU-optimized features
            RQHypothesisIntegration.UseEventDrivenTime = false; // Keep standard for now
            RQHypothesisIntegration.UseImprovedGravity = true;  // CHECKLIST ITEM 3
            RQHypothesisIntegration.ValidateSpectralDimension = true; // CHECKLIST ITEM 5

            // Initialize RQ features
            RQHypothesisIntegration.Initialize(graph);

            // Run simulation
            //Console.WriteLine("Starting simulation...");

            var diagnosticsExport = new List<string>();
            diagnosticsExport.Add("step,spectralDim,status,deviationPercent,recommendation");

            for (int step = 0; step < config.TotalSteps; step++)
            {
                // Compute relational time step
                double dt = graph.ComputeRelationalDtExtended();
                if (dt <= 0) dt = 0.01;

                // CHECKLIST ITEMS 3, 4, 5: Integrated physics step
                RQHypothesisIntegration.StepPhysics(graph, step, dt, diagnosticsExport);

                // Standard RQGraph physics updates
                if (config.UseSpinorField)
                {
                    // Only update spinors if spectral dimension is compatible
                    if (!SpectralDimensionValidator.ShouldSuppressSpinorEvolution(graph))
                    {
                        graph.UpdateDiracFieldRelational(dt);
                    }
                }

                if (config.UseYangMillsGauge)
                {
                    // CHECKLIST ITEM 2: Gauss law is enforced inside this call
                    graph.EvolveYangMillsRelational(dt);
                }

                // Periodic logging
                if (step % config.LogEvery == 0)
                {
                    LogProgress(graph, step, config.TotalSteps);
                }
            }

            // Final status report
            //Console.WriteLine();
            //Console.WriteLine(RQHypothesisIntegration.GetStatusReport(graph));

            // Export diagnostics
            System.IO.File.WriteAllLines("spectral_diagnostics.csv", diagnosticsExport);
            //Console.WriteLine();
            //Console.WriteLine("Diagnostics exported to: spectral_diagnostics.csv");
        }

        public static void RunEventDrivenSimulation()
        {
            //Console.WriteLine("=== Event-Driven Simulation (CHECKLIST ITEM 1) ===");
            //Console.WriteLine();

            // Create graph
            var config = new SimulationConfig
            {
                NodeCount = 100,
                InitialEdgeProb = 0.1,
                TargetDegree = 6,
                Seed = 42
            };

            var engine = new SimulationEngine(config);
            var graph = engine.Graph;

            // Enable event-driven time
            RQHypothesisIntegration.UseEventDrivenTime = true;
            RQHypothesisIntegration.UseImprovedGravity = true;
            RQHypothesisIntegration.ValidateSpectralDimension = true;

            // Initialize
            RQHypothesisIntegration.Initialize(graph);

            // Run event-driven simulation
            // This completely replaces the standard for-loop
            RQHypothesisIntegration.RunEventDrivenSimulation(
                graph,
                totalTime: 10.0,
                seed: 42);

            //Console.WriteLine();
            //Console.WriteLine(RQHypothesisIntegration.GetStatusReport(graph));
        }

        public static void DemonstrateOllivierRicciCurvature()
        {
            //Console.WriteLine("=== Ollivier-Ricci Curvature Demo (CHECKLIST ITEM 4) ===");
            //Console.WriteLine();

            // Create small graph
            var config = new SimulationConfig
            {
                NodeCount = 50,
                InitialEdgeProb = 0.15,
                TargetDegree = 5,
                Seed = 42
            };

            var engine = new SimulationEngine(config);
            var graph = engine.Graph;

            //Console.WriteLine("Comparing Forman-Ricci vs Ollivier-Ricci curvature:");
            //Console.WriteLine();

            // Sample a few edges
            int samplesShown = 0;
            for (int i = 0; i < graph.N && samplesShown < 10; i++)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;
                    if (samplesShown >= 10) break;

                    // Old method: Forman-Ricci
                    double formanRicci = graph.CalculateGraphCurvature(i, j);

                    // New method: Ollivier-Ricci
                    double ollivierRicci = graph.CalculateOllivierRicciCurvature(i, j);

                    //Console.WriteLine($"Edge ({i},{j}): Forman={formanRicci:F4}, Ollivier={ollivierRicci:F4}");
                    samplesShown++;
                }
            }

            //Console.WriteLine();
            //Console.WriteLine("Key differences:");
            //Console.WriteLine("- Forman-Ricci: Based on triangle count (topology)");
            //Console.WriteLine("- Ollivier-Ricci: Based on mass transport (geometry)");
            //Console.WriteLine("- Ollivier-Ricci is more sensitive to geometric properties");
        }

        public static void DemonstrateGaussLawProjection()
        {
            //Console.WriteLine("=== Gauss Law Projection Demo (CHECKLIST ITEM 2) ===");
            //Console.WriteLine();

            // Create graph with Yang-Mills fields
            var config = new SimulationConfig
            {
                NodeCount = 100,
                UseYangMillsGauge = true,
                UseSpinorField = true,
                Seed = 42
            };

            var engine = new SimulationEngine(config);
            var graph = engine.Graph;

            // Evolve without projection
            //Console.WriteLine("Evolving Yang-Mills fields without Gauss law projection...");
            for (int i = 0; i < 100; i++)
            {
                graph.EvolveYangMillsRelational(0.01);
            }
            double violationBefore = graph.ComputeGaussLawViolation();
            //Console.WriteLine($"Gauss law violation: {violationBefore:E3}");

            // Apply projection
            //Console.WriteLine();
            //Console.WriteLine("Applying Gauss law projection...");
            GaussLawProjection.EnforceGaussLaw(graph);

            double violationAfter = graph.ComputeGaussLawViolation();
            //Console.WriteLine($"Gauss law violation after projection: {violationAfter:E3}");
            //Console.WriteLine($"Improvement: {violationBefore / Math.Max(violationAfter, 1e-15):F2}x");
        }

        public static void DemonstrateSpectralDimensionValidation()
        {
            //Console.WriteLine("=== Spectral Dimension Validation (CHECKLIST ITEM 5) ===");
            //Console.WriteLine();

            var config = new SimulationConfig
            {
                NodeCount = 200,
                InitialEdgeProb = 0.05, // Start sparse (fractal)
                Seed = 42,
                UseQuantumGraphity = true,
                UseSpectralGeometry = true
            };

            var engine = new SimulationEngine(config);
            var graph = engine.Graph;

            //Console.WriteLine("Monitoring spectral dimension evolution...");
            //Console.WriteLine();

            double lastDimension = double.NaN;

            for (int step = 0; step < 500; step += 50)
            {
                // Evolve network (crystallization process)
                for (int i = 0; i < 50; i++)
                {
                    graph.QuantumGraphityStep();
                }

                // Check dimension
                double currentDimension = graph.ComputeSpectralDimension();
                var status = SpectralDimensionValidator.ValidateSpinorCompatibility(graph);

                //Console.WriteLine($"Step {step}: d_S = {currentDimension:F3} ({(status.IsCompatible ? "OK" : "INCOMPATIBLE")})");

                // Check for transition
                if (!double.IsNaN(lastDimension))
                {
                    var transition = SpectralDimensionValidator.MonitorTransition(lastDimension, currentDimension);
                    if (transition.HasCrossedThreshold)
                    {
                        //Console.WriteLine($"  *** TRANSITION: {transition.TransitionType} ***");
                    }
                }

                lastDimension = currentDimension;
            }
        }

        private static void LogProgress(RQGraph graph, int step, int totalSteps)
        {
            int excitedCount = 0;
            for (int i = 0; i < graph.N; i++)
            {
                if (graph.State[i] == NodeState.Excited)
                    excitedCount++;
            }

            double totalEnergy = graph.ComputeTotalEnergy();
            double spectralDim = graph.ComputeSpectralDimension();
            double gaussViolation = graph.ComputeGaussLawViolation();
            double avgCurvature = ImprovedNetworkGravity.ComputeAverageOllivierCurvature(graph);

            //Console.WriteLine($"[{step}/{totalSteps}] Excited: {excitedCount}, " +
            //                   $"E: {totalEnergy:F3}, d_S: {spectralDim:F3}, " +
            //                   $"Gauss: {gaussViolation:E2}, κ_avg: {avgCurvature:F4}");
        }

        /// <summary>
        /// Run all demonstrations
        /// </summary>
        public static void RunAllDemos()
        {
            //Console.WriteLine("RQ-Hypothesis GPU-Optimized Implementation Demos");
            //Console.WriteLine("=================================================");
            //Console.WriteLine();

            // Demo 1: Standard simulation with all features
            RunStandardSimulation();
            //Console.WriteLine();
            //Console.WriteLine("Press Enter to continue to next demo...");
            Console.ReadLine();

            // Demo 2: Ollivier-Ricci curvature
            DemonstrateOllivierRicciCurvature();
            //Console.WriteLine();
            //Console.WriteLine("Press Enter to continue to next demo...");
            Console.ReadLine();

            // Demo 3: Gauss law projection
            DemonstrateGaussLawProjection();
            //Console.WriteLine();
            //Console.WriteLine("Press Enter to continue to next demo...");
            Console.ReadLine();

            // Demo 4: Spectral dimension validation
            DemonstrateSpectralDimensionValidation();
            //Console.WriteLine();
            //Console.WriteLine("Press Enter to continue to next demo...");
            Console.ReadLine();

            // Demo 5: Event-driven simulation
            RunEventDrivenSimulation();

            //Console.WriteLine();
            //Console.WriteLine("All demos complete!");
        }
    }
}
