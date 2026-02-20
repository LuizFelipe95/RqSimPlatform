using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class BinaryMergerExperiment : IExperiment
    {
        public string Name => "Astro: Binary Cluster Merger";
        public string Description => "Two dense clusters (30 nodes each). Will they merge under gravity?";

        public StartupConfig GetConfig()
        {
            return new StartupConfig
            {
                NodeCount = 60,
                TotalSteps = 4000,
                InitialEdgeProb = 0.0,
                GravitationalCoupling = 0.35,
                HotStartTemperature = 0.5,
                UseNetworkGravity = true,
                EdgeTrialProbability = 0.2
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => graph =>
        {
            if (graph == null)
            {
                return;
            }

            var rng = new Random();

            // Cluster A (0-29)
            for (int i = 0; i < 30; i++)
            {
                for (int j = i + 1; j < 30; j++)
                {
                    if (rng.NextDouble() < 0.5)
                    {
                        graph.AddEdge(i, j);
                    }
                }
            }

            // Cluster B (30-59)
            for (int i = 30; i < 60; i++)
            {
                for (int j = i + 1; j < 60; j++)
                {
                    if (rng.NextDouble() < 0.5)
                    {
                        graph.AddEdge(i, j);
                    }
                }
            }

            // Weak bridge between clusters
            graph.AddEdge(29, 30);
            graph.Weights[29, 30] = 0.01;
            graph.Weights[30, 29] = 0.01;
        };
    }
}
