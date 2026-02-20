using System;
using System.Collections.Generic;
using RQSimulation.GPUOptimized;

namespace RQSimulation.Analysis.VacuumEnergy;

/// <summary>
/// Core analysis engine for vacuum energy statistics and curvature computations.
///
/// This class lives in the core engine project so that Experiments and GPU modules
/// can access it without depending on the UI/AutoTuning layer.
///
/// RQ-HYPOTHESIS CONTEXT:
/// Provides measurement tools for the Vacuum Scaling Experiment:
/// 1. Per-node vacuum energy statistics (mean, variance, min, max)
/// 2. Average Ollivier-Ricci curvature (emergent geometry probe)
/// 3. Curvature distribution statistics (mean, variance, min, max)
/// 4. Ricci-flat test (is the emergent geometry Minkowski-like?)
/// </summary>
public static class VacuumEnergyAnalyzer
{
    /// <summary>
    /// Default mass threshold below which a node is classified as vacuum.
    /// </summary>
    public const double DefaultMassThreshold = 1e-6;

    /// <summary>
    /// Computes vacuum energy statistics over all vacuum nodes in the graph.
    /// </summary>
    /// <param name="graph">The RQ graph to analyze</param>
    /// <param name="massThreshold">Nodes with mass below this are vacuum</param>
    /// <returns>Aggregated vacuum energy statistics</returns>
    public static VacuumStats ComputeVacuumNodeStatistics(RQGraph graph, double massThreshold = DefaultMassThreshold)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.N < 1)
        {
            return default;
        }

        var vacuumEnergy = graph.VacuumEnergyField;

        double sum = 0;
        double sumSq = 0;
        int count = 0;
        double min = double.MaxValue;
        double max = double.MinValue;

        for (int i = 0; i < graph.N; i++)
        {
            if (!graph.IsVacuumNode(i, massThreshold))
            {
                continue;
            }

            double energy = vacuumEnergy.Length > i ? vacuumEnergy[i] : 0;
            sum += energy;
            sumSq += energy * energy;
            count++;

            if (energy < min) min = energy;
            if (energy > max) max = energy;
        }

        if (count == 0)
        {
            return default;
        }

        double mean = sum / count;
        double variance = (sumSq / count) - (mean * mean);

        return new VacuumStats(
            TotalVacuumEnergy: sum,
            VacuumNodeCount: count,
            EnergyVariance: Math.Max(0, variance),
            MinNodeEnergy: min,
            MaxNodeEnergy: max
        );
    }

    /// <summary>
    /// Computes vacuum energy statistics using per-node spectral action contributions.
    ///
    /// This is the primary metric for the Vacuum Scaling Experiment:
    ///   ⟨ε_vac⟩ = (1 / N_vac) × Σ_{i ∈ vacuum} S_node(i)
    ///
    /// Uses <see cref="SpectralAction.ComputeNodeSpectralContribution"/> for each
    /// vacuum node to compute the spectral action density.
    /// </summary>
    /// <param name="graph">The RQ graph to analyze</param>
    /// <param name="massThreshold">Nodes with mass below this are vacuum</param>
    /// <returns>Vacuum stats where energy = spectral action contribution</returns>
    public static VacuumStats ComputeSpectralVacuumDensity(RQGraph graph, double massThreshold = DefaultMassThreshold)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.N < 1)
        {
            return default;
        }

        // Ensure vacuum field is available for ComputeNodeSpectralContribution
        graph.EnsureVacuumFieldInitialized();

        double sum = 0;
        double sumSq = 0;
        int count = 0;
        double min = double.MaxValue;
        double max = double.MinValue;

        for (int i = 0; i < graph.N; i++)
        {
            if (!graph.IsVacuumNode(i, massThreshold))
            {
                continue;
            }

            double contribution = SpectralAction.ComputeNodeSpectralContribution(graph, i);
            sum += contribution;
            sumSq += contribution * contribution;
            count++;

            if (contribution < min) min = contribution;
            if (contribution > max) max = contribution;
        }

        if (count == 0)
        {
            return default;
        }

        double mean = sum / count;
        double variance = Math.Max(0, (sumSq / count) - (mean * mean));

        return new VacuumStats(
            TotalVacuumEnergy: sum,
            VacuumNodeCount: count,
            EnergyVariance: variance,
            MinNodeEnergy: min,
            MaxNodeEnergy: max
        );
    }

    // ============================================================
    // RICCI CURVATURE ANALYSIS (moved from VacuumEnergyManager)
    // ============================================================

    /// <summary>
    /// Calculates average Ollivier-Ricci curvature across all edges.
    ///
    /// RQ-HYPOTHESIS CONTEXT:
    /// For emergent flat spacetime, average curvature should approach zero.
    /// Positive bias → closed universe (like sphere)
    /// Negative bias → open universe (like hyperbolic space)
    /// Zero → flat Minkowski spacetime (desired)
    /// </summary>
    /// <param name="graph">The RQ graph to analyze</param>
    /// <param name="sampleFraction">Fraction of edges to sample (1.0 = all edges)</param>
    /// <returns>Average Ricci curvature value</returns>
    public static double CalculateAverageRicciCurvature(RQGraph graph, double sampleFraction = 1.0)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.N < 2)
        {
            return 0.0;
        }

        graph.BuildSoAViews();

        double totalCurvature = 0.0;
        int edgeCount = 0;
        Random? rng = sampleFraction < 1.0 ? new Random() : null;

        for (int i = 0; i < graph.N; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (j <= i)
                {
                    continue;
                }

                if (rng != null && rng.NextDouble() > sampleFraction)
                {
                    continue;
                }

                double curvature = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                totalCurvature += curvature;
                edgeCount++;
            }
        }

        return edgeCount > 0 ? totalCurvature / edgeCount : 0.0;
    }

    /// <summary>
    /// Calculates Ricci curvature statistics across all edges.
    /// </summary>
    /// <param name="graph">The RQ graph to analyze</param>
    /// <returns>Tuple of (mean, variance, min, max) curvature</returns>
    public static (double Mean, double Variance, double Min, double Max) CalculateRicciCurvatureStats(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.N < 2)
        {
            return (0, 0, 0, 0);
        }

        graph.BuildSoAViews();

        List<double> curvatures = [];

        for (int i = 0; i < graph.N; i++)
        {
            foreach (int j in graph.Neighbors(i))
            {
                if (j <= i)
                {
                    continue;
                }

                double curvature = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                curvatures.Add(curvature);
            }
        }

        if (curvatures.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        double mean = curvatures.Average();
        double variance = curvatures.Sum(c => (c - mean) * (c - mean)) / curvatures.Count;
        double min = curvatures.Min();
        double max = curvatures.Max();

        return (mean, variance, min, max);
    }

    /// <summary>
    /// Checks if the graph curvature is within tolerance of flat space.
    /// </summary>
    /// <param name="graph">The RQ graph to analyze</param>
    /// <param name="targetCurvature">Target curvature (default 0 for flat)</param>
    /// <param name="tolerance">Acceptable deviation from target</param>
    /// <returns>True if curvature is within tolerance</returns>
    public static bool IsRicciFlat(RQGraph graph, double targetCurvature = 0.0, double tolerance = 0.1)
    {
        double avgCurvature = CalculateAverageRicciCurvature(graph, sampleFraction: 0.5);
        return Math.Abs(avgCurvature - targetCurvature) <= tolerance;
    }
}
