// RQSimulation/Experiments/Definitions/BioFoldingExperiment.cs
using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    /// <summary>
    /// Experiment C: "DNA Folding" (ДНК-Фолдинг / Bio-Graphity)
    /// 
    /// Sequence: DNA hairpin
    /// 
    /// 
    /// What to observe:
    /// 
    /// </summary>
    public class BioFoldingExperiment : IExperiment
    {
        public string Name => "Bio-Folding (DNA Hairpin)";

        public string Description =>
            "Models topological self-organization of a nucleotide DNA hairpin ";

        public StartupConfig GetConfig()
        {
            //лизоцим  Physical intent: keep the DNA backbone intact while gentle gravity nudges bases into a hairpin without crushing it.
            var config = new StartupConfig
            {
                NodeCount = 60,  // два кластера по 30 узлов
                TotalSteps = 8000,
                InitialEdgeProb = 0.0,
                GravitationalCoupling = 0.50,  // усиливаем гравитацию
                HotStartTemperature = 1.0,     // умеренное «тепло» для фолдинга
                AnnealingCoolingRate = 0.998,  // медленное охлаждение
                EdgeTrialProbability = 0.3,    // больше шансов образовать ребро
                UseNetworkGravity = true,
                UseSpectralGeometry = true,    // спектральный анализ для d_S
                UseVacuumFluctuations = false, // исключаем случайные флуктуации
                UseTopologicalProtection = true // сохраняем начальную топологию
            };

            return config;
        }

        public void ApplyPhysicsOverrides()
        {
            // Physics override: External pressure to help folding
            // CosmologicalConstant = 0.05 provides inward pressure
            // Note: PhysicsConstants.CosmologicalConstant is readonly = 0.001
            // The effect is simulated through high GravitationalCoupling
        }

        /// <summary>
        /// Custom initializer creates a linear chain topology:
        /// 0-1-2-3-4-5-6-7-8-9-10-11
        /// with weight 1.0 (unbreakable covalent backbone)
        /// </summary>
        public Action<RQGraph>? CustomInitializer => InitializeLinearChain;

        private static void InitializeLinearChain(RQGraph graph)
        {
            int n = graph.N;
            if (n < 2) return;

            // Clear any existing edges (graph may have random initialization)
            // Note: For this experiment n=12, so O(n²) = O(144) is negligible.
            // For larger graphs, consider using FlatEdgesFrom/FlatEdgesTo arrays.
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++) // Only check upper triangle (undirected graph)
                {
                    if (graph.Edges[i, j])
                    {
                        graph.RemoveEdge(i, j);
                    }
                }
            }

            // Create linear chain: 0-1-2-3-...-n-1
            for (int i = 0; i < n - 1; i++)
            {
                graph.AddEdge(i, i + 1);
                // Set high weight (covalent bond - should not break)
                graph.Weights[i, i + 1] = 1.0;
                graph.Weights[i + 1, i] = 1.0;
            }

            // Initialize coordinates in a line for visualization.
            // IMPORTANT: Coordinates property is marked obsolete because it should
            // NOT be used for physics calculations (use graph distances instead).
            // However, it IS the correct API for visualization setup, which is
            // exactly what we're doing here. The obsolete warning is intentionally
            // suppressed for this legitimate rendering use case.
            double spacing = 0.8 / Math.Max(1, n - 1);
            for (int i = 0; i < n; i++)
            {
#pragma warning disable CS0618 // Coordinates are used correctly here for visualization setup
                if (graph.Coordinates != null && graph.Coordinates.Length == n)
                {
                    graph.Coordinates[i] = (-0.4 + i * spacing, 0.0);
                }
#pragma warning restore CS0618
            }
        }
    }
}
