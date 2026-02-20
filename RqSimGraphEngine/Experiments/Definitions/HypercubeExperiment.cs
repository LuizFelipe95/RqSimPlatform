using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class HypercubeExperiment : IExperiment
    {
        public string Name => "Dim: Hypercube Decay (6D)";
        public string Description => "Starts with a perfect 6-cube (64 nodes, degree 6). Tests dimensional reduction under gravity constraints.";

        public StartupConfig GetConfig()
        {
            return new StartupConfig
            {
                NodeCount = 64,
                TotalSteps = 5000,
                InitialEdgeProb = 0.0,
                TargetDegree = 4,
                GravitationalCoupling = 0.2,
                HotStartTemperature = 1.0,
                AnnealingCoolingRate = 0.998,
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

            int n = Math.Min(graph.N, 64);
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    int xor = i ^ j;
                    if (xor != 0 && (xor & (xor - 1)) == 0)
                    {
                        graph.AddEdge(i, j);
                        graph.Weights[i, j] = 1.0;
                        graph.Weights[j, i] = 1.0;
                    }
                }
            }
        };
    }
}
