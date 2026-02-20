// ============================================================
// ISimulationProfile.cs
// Core interface for simulation profile configuration
// Part of Strong Science Simulation Config architecture
// ============================================================

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Base contract for simulation profiles. Implements the Strategy pattern
/// to allow different simulation modes with different constraints.
/// 
/// <para><strong>SCIENTIFIC INTEGRITY:</strong></para>
/// <para>
/// The profile system enforces the "Clean Room" principle: Scientific simulations
/// must be physically isolated from fitted/tuned parameters that were calibrated
/// for visual appeal rather than physical accuracy.
/// </para>
/// 
/// <para><strong>USE CASES:</strong></para>
/// <list type="bullet">
///   <item><description><see cref="StrictScienceProfile"/>: Lattice QCD, HPC simulations, publishable results</description></item>
///   <item><description><see cref="VisualSandboxProfile"/>: UI demos, interactive exploration, 60 FPS visuals</description></item>
/// </list>
/// </summary>
public interface ISimulationProfile
{
    /// <summary>
    /// Profile name for logging and identification.
    /// </summary>
    string ProfileName { get; }

    /// <summary>
    /// Gets whether strict validation is enabled.
    /// <para>When TRUE:</para>
    /// <list type="bullet">
    ///   <item><description>Simulation throws on NaN/Infinity (numerical breakdown)</description></item>
    ///   <item><description>Energy conservation violations trigger exceptions</description></item>
    ///   <item><description>Gauge constraint violations are logged and flagged</description></item>
    ///   <item><description>Hamiltonian invariance is checked at each step</description></item>
    /// </list>
    /// <para>When FALSE:</para>
    /// <list type="bullet">
    ///   <item><description>NaN values are clamped or reset</description></item>
    ///   <item><description>Conservation violations are silently corrected</description></item>
    ///   <item><description>Simulation prioritizes visual stability over accuracy</description></item>
    /// </list>
    /// </summary>
    bool IsStrictValidationEnabled { get; }

    /// <summary>
    /// Provider for physical constants (Strategy pattern).
    /// <para>Returns <see cref="IPhysicalConstants"/> implementation that provides either:</para>
    /// <list type="bullet">
    ///   <item><description>Fundamental constants (Planck units, CODATA values)</description></item>
    ///   <item><description>Lattice units (dimensionless, rescaled)</description></item>
    ///   <item><description>Fitted constants (tuned for visual stability)</description></item>
    /// </list>
    /// </summary>
    IPhysicalConstants Constants { get; }

    /// <summary>
    /// Gets whether user intervention is allowed during simulation.
    /// <para>When TRUE: UI sliders can modify parameters at runtime (sandbox mode)</para>
    /// <para>When FALSE: Parameters are immutable after initialization (science mode)</para>
    /// </summary>
    bool AllowInteractiveRewiring { get; }

    /// <summary>
    /// Gets whether soft walls (Clamp, Tanh saturation) are enabled.
    /// <para>When TRUE: Edge weights are clamped to prevent extreme values</para>
    /// <para>When FALSE: Physics equations evolve freely; universe may explode</para>
    /// </summary>
    bool UseSoftWalls { get; }

    /// <summary>
    /// Gets whether artificial viscosity/damping is enabled.
    /// <para>When TRUE: GeometryInertia provides artificial damping for stability</para>
    /// <para>When FALSE: Pure Hamiltonian evolution without damping</para>
    /// </summary>
    bool UseArtificialViscosity { get; }

    /// <summary>
    /// Gets the numerical precision mode.
    /// </summary>
    NumericalPrecision Precision { get; }

    /// <summary>
    /// Generates a SHA256 hash of the profile configuration.
    /// <para>Used for experiment reproducibility: the hash is saved with simulation
    /// data to ensure exact configuration can be verified.</para>
    /// </summary>
    /// <returns>Hexadecimal SHA256 hash string</returns>
    string GetConfigurationHash();

    /// <summary>
    /// Validates that the profile is internally consistent.
    /// <para>Throws <see cref="ScientificMalpracticeException"/> if:</para>
    /// <list type="bullet">
    ///   <item><description>Strict mode but using fitted constants</description></item>
    ///   <item><description>Scientific mode but allowing interactive rewiring</description></item>
    ///   <item><description>Any other inconsistent combination</description></item>
    /// </list>
    /// </summary>
    void Validate();
}

/// <summary>
/// Numerical precision modes for simulation.
/// </summary>
public enum NumericalPrecision
{
    /// <summary>
    /// Single precision (float, 32-bit). Fast but limited accuracy.
    /// Suitable for visual demos and quick iterations.
    /// </summary>
    Single,

    /// <summary>
    /// Double precision (double, 64-bit). Standard scientific precision.
    /// Required for most physics simulations.
    /// </summary>
    Double,

    /// <summary>
    /// Quad precision (decimal or software quad). Maximum accuracy.
    /// Required for high-precision tests and unitarity verification.
    /// </summary>
    Quad
}
