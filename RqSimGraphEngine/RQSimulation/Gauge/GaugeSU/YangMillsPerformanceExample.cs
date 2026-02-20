using System;
using System.Diagnostics;

namespace RQSimulation
{
    /// <summary>
    /// Example of performance testing for optimized Yang-Mills implementation
    /// with performance monitoring.
    /// </summary>
    public static class YangMillsPerformanceExample
    {
        private static int s_stepCounter = 0;

        /// <summary>
        /// Interactive performance testing of optimized methods with time output.
        /// </summary>
        public static void RunPerformanceTest(RQGraph graph, int iterations = 100)
        {
            Debug.WriteLine("=== Yang-Mills Performance Test ===");
            Debug.WriteLine($"Graph size: N={graph.N}");
            Debug.WriteLine($"Iterations: {iterations}");
            Debug.WriteLine("");

            var swGluon = new Stopwatch();
            var swWeak = new Stopwatch();
            var swHyper = new Stopwatch();
            var swTotal = new Stopwatch();

            // Warm-up (for JIT compilation)
            graph.ComputeGluonFieldStrength();
            graph.ComputeWeakFieldStrength();
            graph.ComputeHyperchargeFieldStrength();

            // Main loop
            swTotal.Start();

            for (int i = 0; i < iterations; i++)
            {
                swGluon.Start();
                graph.ComputeGluonFieldStrength();
                swGluon.Stop();

                swWeak.Start();
                graph.ComputeWeakFieldStrength();
                swWeak.Stop();

                swHyper.Start();
                graph.ComputeHyperchargeFieldStrength();
                swHyper.Stop();
            }

            swTotal.Stop();

            // Results
            Debug.WriteLine("Results:");
            Debug.WriteLine($"  Gluon Field Strength:      {swGluon.ElapsedMilliseconds:N0} ms ({swGluon.ElapsedMilliseconds / (double)iterations:F2} ms/iter)");
            Debug.WriteLine($"  Weak Field Strength:       {swWeak.ElapsedMilliseconds:N0} ms ({swWeak.ElapsedMilliseconds / (double)iterations:F2} ms/iter)");
            Debug.WriteLine($"  Hypercharge Field Strength: {swHyper.ElapsedMilliseconds:N0} ms ({swHyper.ElapsedMilliseconds / (double)iterations:F2} ms/iter)");
            Debug.WriteLine($"  Total:                     {swTotal.ElapsedMilliseconds:N0} ms ({swTotal.ElapsedMilliseconds / (double)iterations:F2} ms/iter)");
            Debug.WriteLine("");

            // CPU throughput (approximate)
            double totalSeconds = swTotal.Elapsed.TotalSeconds;
            double iterPerSecond = iterations / totalSeconds;
            Debug.WriteLine($"Throughput: {iterPerSecond:F2} iterations/second");
            Debug.WriteLine("");
        }

        /// <summary>
        /// Performance monitoring during simulation step.
        /// </summary>
        public static void MonitorSimulationStep(RQGraph graph, double dt)
        {
            var sw = Stopwatch.StartNew();

            // Main operation (includes field strength computation)
            graph.EvolveYangMillsFields(dt);

            sw.Stop();

            // Log every N steps
            s_stepCounter++;

            if (s_stepCounter % 100 == 0)
            {
                Debug.WriteLine($"[Step {s_stepCounter}] YangMills evolution: {sw.ElapsedMilliseconds} ms");
            }
        }

        /// <summary>
        /// Performance test for different graph sizes.
        /// Note: Creates multiple RQGraph instances with different node counts.
        /// </summary>
        public static void CompareGraphSizes(params RQGraph[] graphs)
        {
            Debug.WriteLine("=== Graph Size Comparison ===");
            Debug.WriteLine("");

            foreach (var graph in graphs)
            {
                int N = graph.N;

                var sw = Stopwatch.StartNew();

                // Test: 10 iterations
                for (int i = 0; i < 10; i++)
                {
                    graph.ComputeGluonFieldStrength();
                    graph.ComputeWeakFieldStrength();
                }

                sw.Stop();

                double avgTime = sw.ElapsedMilliseconds / 10.0;
                Debug.WriteLine($"N={N,3}: {avgTime:F2} ms/iteration");
            }

            Debug.WriteLine("");
        }

        /// <summary>
        /// Test for StackOverflow safety.
        /// </summary>
        public static void TestStackSafety(RQGraph graph, int stressIterations = 1000)
        {
            Debug.WriteLine("=== Stack Safety Test ===");
            Debug.WriteLine($"Running {stressIterations} iterations to test stack stability...");
            Debug.WriteLine("");

            try
            {
                for (int i = 0; i < stressIterations; i++)
                {
                    graph.EvolveYangMillsFields(0.01);

                    if (i % 100 == 0)
                    {
                        //Console.Write($"\rProgress: {i}/{stressIterations}");
                    }
                }

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine("SUCCESS: No StackOverflowException detected!");
                Debug.WriteLine($"  Completed {stressIterations} iterations safely.");
            }
            catch (StackOverflowException)
            {
                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine("FAILURE: StackOverflowException occurred!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            }

            Debug.WriteLine("");
        }

        /// <summary>
        /// Detailed performance profiling.
        /// </summary>
        public static void ProfileBottlenecks(RQGraph graph)
        {
            Debug.WriteLine("=== Performance Profiling ===");
            Debug.WriteLine("");

            // Warm-up
            graph.InitYangMillsFields();

            // Test individual components
            var swInit = Stopwatch.StartNew();
            graph.InitYangMillsFields();
            swInit.Stop();

            var swGluon = Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
                graph.ComputeGluonFieldStrength();
            swGluon.Stop();

            var swWeak = Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
                graph.ComputeWeakFieldStrength();
            swWeak.Stop();

            var swAction = Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
                graph.ComputeYangMillsAction();
            swAction.Stop();

            var swEvolve = Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
                graph.EvolveYangMillsFields(0.01);
            swEvolve.Stop();

            // Report
            Debug.WriteLine("Component breakdown (50 iterations each):");
            Debug.WriteLine($"  Init Fields:        {swInit.ElapsedMilliseconds} ms");
            Debug.WriteLine($"  Gluon Strength:     {swGluon.ElapsedMilliseconds} ms ({swGluon.ElapsedMilliseconds / 50.0:F2} ms/iter)");
            Debug.WriteLine($"  Weak Strength:      {swWeak.ElapsedMilliseconds} ms ({swWeak.ElapsedMilliseconds / 50.0:F2} ms/iter)");
            Debug.WriteLine($"  Action Computation: {swAction.ElapsedMilliseconds} ms ({swAction.ElapsedMilliseconds / 50.0:F2} ms/iter)");
            Debug.WriteLine($"  Full Evolution:     {swEvolve.ElapsedMilliseconds} ms ({swEvolve.ElapsedMilliseconds / 50.0:F2} ms/iter)");
            Debug.WriteLine("");

            // Relative distribution
            double total = swGluon.ElapsedMilliseconds + swWeak.ElapsedMilliseconds + swAction.ElapsedMilliseconds;
            Debug.WriteLine("Relative time distribution:");
            Debug.WriteLine($"  Gluon:  {100.0 * swGluon.ElapsedMilliseconds / total:F1}%");
            Debug.WriteLine($"  Weak:   {100.0 * swWeak.ElapsedMilliseconds / total:F1}%");
            Debug.WriteLine($"  Action: {100.0 * swAction.ElapsedMilliseconds / total:F1}%");
            Debug.WriteLine("");
        }
    }
}
