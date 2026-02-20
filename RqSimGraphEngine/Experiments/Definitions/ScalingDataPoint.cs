namespace RqSimGraphEngine.Experiments.Definitions;

/// <summary>
/// Single data point from a vacuum scaling experiment run at a specific node count N.
///
/// RQ-HYPOTHESIS:
/// We expect ⟨ε_vac(N)⟩ ~ N^α where:
///   α ≈  0   → failure (vacuum catastrophe not resolved)
///   α ≈ −0.5 → partial success (energy dilution ~ 1/√N)
///   α ≈ −1   → full success (energy dilution ~ 1/N)
/// </summary>
public readonly record struct ScalingDataPoint(
    /// <summary>Total node count for this run.</summary>
    int N,

    /// <summary>Number of vacuum (matter-free) nodes.</summary>
    int VacuumNodeCount,

    /// <summary>Average vacuum energy density ⟨ε_vac⟩.</summary>
    double AvgVacuumEnergy,

    /// <summary>Variance of per-node vacuum energy σ²(ε).</summary>
    double EnergyVariance,

    /// <summary>Spectral dimension d_S at the measurement point.</summary>
    double SpectralDimension,

    /// <summary>Average Ollivier-Ricci curvature across edges.</summary>
    double AvgRicciCurvature,

    /// <summary>Simulation step at which thermalization was reached.</summary>
    int ThermalizationStep,

    /// <summary>Wall-clock time for this run in seconds.</summary>
    double WallClockSeconds
);
