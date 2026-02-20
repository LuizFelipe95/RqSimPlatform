using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class WormholeExperiment : IExperiment
    {
        public string Name => "Wormhole Stability";
        public string Description => "Two separate universes (clusters) connected by a single 'Einstein-Rosen bridge'. Tests if the bridge collapses or stabilizes.";

        public StartupConfig GetConfig()
        {
            // Physical intent: keep two universes loosely gravitating while protecting the bridge from tearing.
            return new StartupConfig
            {
                NodeCount = 200,
                TotalSteps = 2000,
                InitialEdgeProb = 0.0,
                GravitationalCoupling = 0.15,
                HotStartTemperature = 0.5,
                UseNetworkGravity = true,
                UseTopologicalProtection = true
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => InitializeWormhole;

        private static void InitializeWormhole(RQGraph graph)
        {
            if (graph == null) return;
            int n = graph.N;
            int universeSize = Math.Min(100, n/2);
            var rng = new Random(123);

            // Clear edges
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (graph.Edges != null && graph.Edges[i, j])
                        graph.RemoveEdge(i, j);

            // Universe A
            for (int i = 0; i < universeSize; i++)
            {
                for (int j = i + 1; j < universeSize; j++)
                {
                    if (rng.NextDouble() < 0.1)
                    {
                        graph.AddEdge(i, j);
                        double w = 0.2 + rng.NextDouble() * 0.8;
                        graph.Weights[i, j] = w;
                        graph.Weights[j, i] = w;
                    }
                }
            }

            // Universe B
            for (int i = universeSize; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (rng.NextDouble() < 0.1)
                    {
                        graph.AddEdge(i, j);
                        double w = 0.2 + rng.NextDouble() * 0.8;
                        graph.Weights[i, j] = w;
                        graph.Weights[j, i] = w;
                    }
                }
            }

            // Bridge between node 0 and universeSize
            if (n > universeSize)
            {
                graph.AddEdge(0, universeSize);
                graph.Weights[0, universeSize] = 1.0;
                graph.Weights[universeSize, 0] = 1.0;
            }

#pragma warning disable CS0618
            if (graph.Coordinates != null)
            {
                for (int i = 0; i < universeSize; i++) graph.Coordinates[i] = (-0.5, 0.0);
                for (int i = universeSize; i < n; i++) graph.Coordinates[i] = (0.5, 0.0);
            }
#pragma warning restore CS0618
        }
    }
}
