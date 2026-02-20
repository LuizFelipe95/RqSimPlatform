using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class QuantumRingExperiment : IExperiment
    {
        public string Name => "Topo: Quantum Ring (Loop)";
        public string Description => "A perfect 1D loop. Tests topological stability against thermal fluctuations.";

        public StartupConfig GetConfig()
        {
            return new StartupConfig
            {
                NodeCount = 40,
                TotalSteps = 3000,
                InitialEdgeProb = 0.0,
                TargetDegree = 2,
                GravitationalCoupling = 0.2,
                HotStartTemperature = 0.2,
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

            for (int i = 0; i < graph.N; i++)
            {
                int next = (i + 1) % graph.N;
                graph.AddEdge(i, next);
                graph.Weights[i, next] = 1.0;
                graph.Weights[next, i] = 1.0;
            }
        };
    }
}
