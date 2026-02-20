namespace RqSimGraphEngine.Experiments;

/// <summary>
/// Extension of <see cref="IExperiment"/> for experiments that require
/// multiple sequential simulation runs with different configurations.
///
/// Each run is a complete simulation with its own StartupConfig (e.g., different N).
/// The experiment collects results from each run and can export aggregate data
/// when all runs are complete.
///
/// Example use case: Vacuum Scaling Experiment — runs the same physics
/// at N = {1K, 5K, 10K, 50K, 100K} to measure ⟨ε_vac⟩ vs N.
/// </summary>
public interface IMultiRunExperiment : IExperiment
{
    /// <summary>Total number of sequential runs in this experiment.</summary>
    int RunCount { get; }

    /// <summary>
    /// Returns the startup configuration for a specific run.
    /// </summary>
    /// <param name="runIndex">Zero-based run index (0 .. RunCount−1)</param>
    /// <returns>Configuration for this run</returns>
    StartupConfig GetConfigForRun(int runIndex);

    /// <summary>
    /// Called after each run completes. Stores results for later analysis.
    /// </summary>
    /// <param name="runIndex">Zero-based run index that completed</param>
    /// <param name="results">Actual results collected from the simulation</param>
    void OnRunCompleted(int runIndex, ActualResults results);

    /// <summary>
    /// Called after all runs have completed. Exports aggregate data.
    /// </summary>
    void OnAllRunsCompleted();
}
