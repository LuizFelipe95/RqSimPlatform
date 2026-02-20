namespace RqSimForms.Events;

/// <summary>
/// Types of physics verification events for the RQ-Hypothesis simulation.
/// These events track key emergent properties that validate the simulation.
/// </summary>
public enum PhysicsEventType
{
    /// <summary>
    /// Mass gap detection/change event (Yang-Mills spectral gap).
    /// Tracks ?? - ?? eigenvalue separation.
    /// </summary>
    MassGap,

    /// <summary>
    /// Spectral dimension measurement event.
    /// Tracks d_S convergence toward 4D.
    /// </summary>
    SpectralDimension,

    /// <summary>
    /// Speed of light isotropy measurement (Lieb-Robinson bounds).
    /// Tracks signal propagation velocity consistency.
    /// </summary>
    SpeedOfLightIsotropy,

    /// <summary>
    /// Ricci curvature flatness measurement (vacuum state).
    /// Tracks average curvature approaching zero.
    /// </summary>
    RicciFlatness,

    /// <summary>
    /// Holographic entropy area law verification.
    /// Tracks S ~ Area vs S ~ Volume scaling.
    /// </summary>
    HolographicAreaLaw,

    /// <summary>
    /// Hausdorff dimension measurement.
    /// Tracks geometric dimension via ball growth.
    /// </summary>
    HausdorffDimension,

    /// <summary>
    /// Giant cluster formation/dissolution event.
    /// Critical phase transition indicator.
    /// </summary>
    ClusterTransition,

    /// <summary>
    /// Energy conservation violation detected.
    /// Constraint monitoring for Wheeler-DeWitt.
    /// </summary>
    EnergyViolation,

    /// <summary>
    /// Auto-tuning parameter adjustment event.
    /// Tracks G, decoherence, or other parameter changes.
    /// </summary>
    AutoTuningAdjustment,

    /// <summary>
    /// General simulation milestone.
    /// </summary>
    Milestone
}
