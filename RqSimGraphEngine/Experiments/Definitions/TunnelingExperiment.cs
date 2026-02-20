using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class TunnelingExperiment : IExperiment
    {
        public string Name => "Quantum: Tunneling Fusion";
        public string Description => "Two separate clusters (20 nodes each). Will vacuum fluctuations create a bridge?";

        public StartupConfig GetConfig()
        {
            return new StartupConfig
            {
                NodeCount = 40,
                TotalSteps = 5000,
                InitialEdgeProb = 0.0,
                GravitationalCoupling = 0.2,
                VacuumEnergyScale = 0.005,
                UseVacuumFluctuations = true,
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

            int half = graph.N / 2;
            var rng = new Random();

            for (int i = 0; i < half; i++)
            {
                for (int j = i + 1; j < half; j++)
                {
                    if (rng.NextDouble() < 0.3)
                    {
                        graph.AddEdge(i, j);
                    }
                }
            }

            for (int i = half; i < graph.N; i++)
            {
                for (int j = i + 1; j < graph.N; j++)
                {
                    if (rng.NextDouble() < 0.3)
                    {
                        graph.AddEdge(i, j);
                    }
                }
            }
        };
    }
}
