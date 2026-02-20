using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class NanoWireExperiment : IExperiment
    {
        public string Name => "MatSci: Nano-Wire Stability";
        public string Description => "A flexible chain of 50 nodes. Tests if 1D topology survives thermal noise.";

        public StartupConfig GetConfig()
        {
            // Physical intent: near-weightless tension preserves a 1D wire while mild heat lets phonons smooth it.
            return new StartupConfig
            {
                NodeCount = 50,
                TotalSteps = 5000,
                InitialEdgeProb = 0.0,
                TargetDegree = 2,
                GravitationalCoupling = 0.1,
                HotStartTemperature = 0.5,
                AnnealingCoolingRate = 0.998,
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

            for (int i = 0; i < graph.N - 1; i++)
            {
                graph.AddEdge(i, i + 1);
                graph.Weights[i, i + 1] = 1.0;
                graph.Weights[i + 1, i] = 1.0;
            }
        };
    }
}
