using System;
using RqSimGraphEngine.Experiments;
using RQSimulation;

namespace RqSimGraphEngine.Experiments.Definitions
{
    /// <summary>
    /// Experiment: Black Hole Evaporation
    /// Creates a dense clique (micro black hole) and sparse vacuum around it.
    /// </summary>
    public class BlackHoleEvaporationExperiment : IExperiment
    {
        public string Name => "Black Hole Evaporation";

        public string Description => "Simulates a pre-formed micro black hole (dense clique) interacting with vacuum. " +
                                     "Observes mass loss via Hawking-like radiation (simulated by decoherence/fluctuations).";

        public StartupConfig GetConfig()
        {
            // Physical intent: keep the horizon compact yet allow Hawking-like leakage via strong vacuum noise.
            return new StartupConfig
            {
                NodeCount = 300,
                TotalSteps = 5000,
                InitialEdgeProb = 0.0, // manual initializer will set topology
                GravitationalCoupling = 0.4,
                VacuumEnergyScale = 0.005,
                DecoherenceRate = 0.02,
                HotStartTemperature = 0.1,
                UseQuantumDrivenStates = true,
                UseVacuumFluctuations = true
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => InitializeBlackHole;

        private static void InitializeBlackHole(RQGraph graph)
        {
            if (graph == null) return;

            int n = graph.N;
            int bhSize = Math.Min(50, Math.Max(1, n / 10));

            // Remove existing edges (upper triangle)
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (graph.Edges != null && graph.Edges[i, j])
                        graph.RemoveEdge(i, j);
                }
            }

            // Create dense clique (nodes 0..bhSize-1)
            for (int i = 0; i < bhSize; i++)
            {
                for (int j = i + 1; j < bhSize; j++)
                {
                    graph.AddEdge(i, j);
                    graph.Weights[i, j] = 1.0;
                    graph.Weights[j, i] = 1.0;
                }

                // Mark as excited (hot core) if property exists
                try { graph.State[i] = NodeState.Excited; } catch { }
            }

            // Sparse vacuum background (nodes bhSize..n-1)
            var rng = new Random(42);
            for (int i = bhSize; i < n; i++)
            {
                // connect occasionally to other vacuum nodes
                if (rng.NextDouble() < 0.02)
                {
                    int target = rng.Next(bhSize, n);
                    if (target != i)
                    {
                        graph.AddEdge(i, target);
                        double w = 0.1 + rng.NextDouble() * 0.2;
                        graph.Weights[i, target] = w;
                        graph.Weights[target, i] = w;
                    }
                }

                // small chance to connect to black hole periphery
                if (rng.NextDouble() < 0.01)
                {
                    int target = rng.Next(0, bhSize);
                    graph.AddEdge(i, target);
                    double w = 0.05 + rng.NextDouble() * 0.1;
                    graph.Weights[i, target] = w;
                    graph.Weights[target, i] = w;
                }
            }
        }
    }
}
