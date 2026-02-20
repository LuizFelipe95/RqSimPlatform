using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using RQSimulation;

namespace RqSimGraphEngine.Experiments.Definitions;

/// <summary>
/// Vacuum Scaling Experiment — measures average vacuum energy density
/// ⟨ε_vac⟩ as a function of graph size N to verify the RQ-hypothesis
/// prediction of vacuum energy dilution.
///
/// Physics:
///   The vacuum catastrophe arises because QFT predicts vacuum energy
///   density independent of volume. In the RQ framework, vacuum energy
///   should scale as ⟨ε_vac⟩ ~ N^α because the discrete graph structure
///   distributes zero-point fluctuations across its degrees of freedom.
///
/// Protocol:
///   1. Initialize a "pure vacuum" graph at target N (no excited nodes)
///   2. Run with standard gravity + vacuum fluctuations until thermalized
///   3. Measure ⟨ε_vac⟩, σ²(ε), d_S, ⟨κ_OR⟩ at thermalization
///   4. Repeat for each N in the scaling sequence
///   5. Export Log-Log data for linear regression → slope = α
///
/// Success criterion: α ∈ {−0.5, −1}
/// </summary>
public class VacuumScalingExperiment : IMultiRunExperiment
{
    private static readonly int[] DefaultNodeSteps = [1000, 5000, 10000, 50000, 100000];

    private readonly int[] _nodeSteps;
    private readonly List<ScalingDataPoint> _results = [];

    public VacuumScalingExperiment()
        : this(DefaultNodeSteps)
    {
    }

    public VacuumScalingExperiment(int[] nodeSteps)
    {
        ArgumentNullException.ThrowIfNull(nodeSteps);

        if (nodeSteps.Length == 0)
        {
            throw new ArgumentException("At least one node count is required.", nameof(nodeSteps));
        }

        _nodeSteps = nodeSteps;
    }

    // ============================================================
    // IExperiment (base — returns first-run config)
    // ============================================================

    public string Name => "Vacuum Energy Scaling";

    public string Description =>
        "Measures average vacuum energy density ⟨ε_vac⟩ at different graph sizes N " +
        "to verify the RQ-hypothesis prediction ⟨ε_vac⟩ ~ N^α (α ≈ −0.5 or −1). " +
        "Runs pure-vacuum simulations at N = " + string.Join(", ", _nodeSteps) + ".";

    public StartupConfig GetConfig() => BuildConfigForNodeCount(_nodeSteps[0]);

    public void ApplyPhysicsOverrides()
    {
        // No global constant overrides needed — experiment uses standard physics.
        // VacuumFluctuations are enabled per-config.
    }

    public Action<RQGraph>? CustomInitializer => graph =>
    {
        // Ensure vacuum field is initialized for measurement
        graph.InitVacuumField();

        // Mark all nodes as vacuum particles
        if (graph.PhysicsProperties != null)
        {
            for (int i = 0; i < graph.N; i++)
            {
                if (i < graph.PhysicsProperties.Length)
                {
                    graph.PhysicsProperties[i].Type = ParticleType.Vacuum;
                    graph.PhysicsProperties[i].Mass = 0;
                }
            }
        }
    };

    // ============================================================
    // IMultiRunExperiment
    // ============================================================

    public int RunCount => _nodeSteps.Length;

    public StartupConfig GetConfigForRun(int runIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(runIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(runIndex, _nodeSteps.Length);

        return BuildConfigForNodeCount(_nodeSteps[runIndex]);
    }

    public void OnRunCompleted(int runIndex, ActualResults results)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfNegative(runIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(runIndex, _nodeSteps.Length);

        // Map ActualResults into a ScalingDataPoint.
        // The caller is responsible for populating vacuum-specific fields
        // via ExperimentValidator.CollectResults or custom collection logic.
        var point = new ScalingDataPoint(
            N: _nodeSteps[runIndex],
            VacuumNodeCount: results.FinalStep > 0 ? _nodeSteps[runIndex] - results.FinalExcitedCount : 0,
            AvgVacuumEnergy: results.FinalQNorm,             // Repurposed: caller should set this
            EnergyVariance: results.SpectralDimensionVarianceLast20Pct,
            SpectralDimension: results.FinalSpectralDimension,
            AvgRicciCurvature: results.FinalCorrelation,     // Repurposed: caller should set this
            ThermalizationStep: results.FinalStep,
            WallClockSeconds: results.WallClockSeconds
        );

        _results.Add(point);
    }

    public void OnAllRunsCompleted()
    {
        // Export results to CSV + JSON in the working directory
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string csvPath = $"vacuum_scaling_{timestamp}.csv";
        string jsonPath = $"vacuum_scaling_{timestamp}.json";

        ExportCsv(csvPath);
        ExportJson(jsonPath);
    }

    // ============================================================
    // Results access
    // ============================================================

    /// <summary>Collected data points (one per completed run).</summary>
    public IReadOnlyList<ScalingDataPoint> Results => _results;

    /// <summary>
    /// Adds a fully-populated scaling data point (for direct use without ActualResults mapping).
    /// </summary>
    public void AddDataPoint(ScalingDataPoint point) => _results.Add(point);

    /// <summary>Clears all collected results.</summary>
    public void ClearResults() => _results.Clear();

    // ============================================================
    // Export
    // ============================================================

    /// <summary>Exports results to a CSV file.</summary>
    public void ExportCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("N,VacuumNodeCount,AvgVacuumEnergy,EnergyVariance,SpectralDimension,AvgRicciCurvature,ThermalizationStep,WallClockSeconds");

        foreach (var p in _results)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{p.N},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.VacuumNodeCount},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.AvgVacuumEnergy:G6},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.EnergyVariance:G6},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.SpectralDimension:F4},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.AvgRicciCurvature:G6},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.ThermalizationStep},");
            sb.AppendLine(p.WallClockSeconds.ToString("F2", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Exports results to a JSON file.</summary>
    public void ExportJson(string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(_results, options);
        File.WriteAllText(path, json);
    }

    // ============================================================
    // Private helpers
    // ============================================================

    private static StartupConfig BuildConfigForNodeCount(int nodeCount)
    {
        return new StartupConfig
        {
            // Graph size for this run
            NodeCount = nodeCount,

            // Long evolution to allow thermalization
            TotalSteps = 20_000,

            // Sparse hypercubic seed (target degree ≈ 6 for 3D simplicial)
            InitialEdgeProb = 0.006,

            // Pure vacuum — no initial excitation
            InitialExcitedProb = 0.0,

            // Standard gravity
            GravitationalCoupling = 0.25,

            // Hot start with ultra-slow cooling for crystallization
            HotStartTemperature = 20.0,
            AnnealingCoolingRate = 0.9995,

            // Lattice parameters
            TargetDegree = 6,
            Temperature = 10.0,
            LambdaState = 0.5,
            EdgeTrialProbability = 0.02,
            DecoherenceRate = 0.005,
            WarmupDuration = 300,
            GravityTransitionDuration = 200,

            // Physics modules: pure gravity + vacuum, no matter
            UseSpectralGeometry = true,
            UseNetworkGravity = true,
            UseVacuumFluctuations = true,
            UseHotStartAnnealing = true,
            UseQuantumDrivenStates = false,   // No quantum states for clean vacuum
            UseSpinorField = false,
            UseTopologicalProtection = true,

            // No fractal structure
            FractalLevels = 0,
            FractalBranchFactor = 0
        };
    }
}
