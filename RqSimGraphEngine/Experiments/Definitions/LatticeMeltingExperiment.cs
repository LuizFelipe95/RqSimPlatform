using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class LatticeMeltingExperiment : IExperiment
    {
        public string Name => "Thermo: Lattice Melting";
        public string Description => "Starts with a 2D Grid (10x10). Heats up rapidly. Observe loss of order.";

        public StartupConfig GetConfig()
        {
            return new StartupConfig
            {
                NodeCount = 100,
                TotalSteps = 5000,
                InitialEdgeProb = 0.0,
                TargetDegree = 4,
                GravitationalCoupling = 0.1,
                HotStartTemperature = 0.1,
                Temperature = 20.0,
                AnnealingCoolingRate = 1.0,
                UseSpectralGeometry = true,
                UseNetworkGravity = true
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => graph =>
        {
            if (graph == null)
            {
                return;
            }

            int w = 10;
            int n = Math.Min(graph.N, 100);

            for (int i = 0; i < n; i++)
            {
                int x = i % w;
                int y = i / w;

                if (x < w - 1 && i + 1 < n)
                {
                    graph.AddEdge(i, i + 1);
                    graph.Weights[i, i + 1] = 1.0;
                    graph.Weights[i + 1, i] = 1.0;
                }

                if (y < w - 1 && i + w < n)
                {
                    graph.AddEdge(i, i + w);
                    graph.Weights[i, i + w] = 1.0;
                    graph.Weights[i + w, i] = 1.0;
                }
            }
        };
    }
}
