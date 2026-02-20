namespace RQSimulation.Analysis.VacuumEnergy;

/// <summary>
/// Statistics for vacuum energy across vacuum nodes in the graph.
///
/// RQ-HYPOTHESIS CONTEXT:
/// Measures the average spectral energy density of vacuum nodes:
///   ⟨ε_vac⟩ = (1 / N_vac) × Σ_{i ∈ vacuum} E_node(i)
///
/// Expected scaling: ⟨ε_vac(N)⟩ ~ N^α where α ∈ {−0.5, −1}
/// confirms the RQ mechanism for vacuum energy compensation.
/// </summary>
public readonly record struct VacuumStats(
    double TotalVacuumEnergy,
    int VacuumNodeCount,
    double EnergyVariance,
    double MinNodeEnergy,
    double MaxNodeEnergy)
{
    /// <summary>Average vacuum energy density per vacuum node.</summary>
    public double AverageVacuumDensity => VacuumNodeCount > 0
        ? TotalVacuumEnergy / VacuumNodeCount
        : 0;
}
