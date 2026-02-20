namespace RqSimForms.Forms.Interfaces.AutoTuning;

/// <summary>
/// Optimization targets for the RQ-Hypothesis Auto-Tuning System.
/// These targets guide parameter adjustment toward specific physical goals.
/// </summary>
public enum OptimizationTarget
{
    /// <summary>
    /// Default: Balance all metrics toward stable 4D spacetime.
    /// </summary>
    Balanced,

    /// <summary>
    /// Prioritize stable spectral dimension d_S ? 4.
    /// </summary>
    StableSpectralDimension,

    /// <summary>
    /// Maximize the spectral gap ?? - ?? (Yang-Mills mass gap).
    /// Key for demonstrating mass gap existence.
    /// </summary>
    MassGapMaximization,

    /// <summary>
    /// Minimize speed of light anisotropy (Lieb-Robinson bounds).
    /// Ensures signal propagation is direction-independent.
    /// </summary>
    SpeedOfLightIsotropy,

    /// <summary>
    /// Minimize average Ricci curvature toward flat vacuum.
    /// Target: <R> ? 0 (or cosmological constant value).
    /// </summary>
    RicciFlatness,

    /// <summary>
    /// Verify holographic area law: S ~ A (not S ~ V).
    /// Tests for proper boundary-bulk correspondence.
    /// </summary>
    HolographicAreaLaw,

    /// <summary>
    /// Maximize cluster count while minimizing largest cluster.
    /// Prevents giant cluster formation (thermal death).
    /// </summary>
    ClusterDiversification,

    /// <summary>
    /// Minimize energy fluctuations for Wheeler-DeWitt compliance.
    /// Target: |H|??| < ?.
    /// </summary>
    EnergyConstraint,

    /// <summary>
    /// Maximize simulation lifetime while maintaining physics.
    /// Balances vacuum energy consumption with structure formation.
    /// </summary>
    SimulationLongevity
}
