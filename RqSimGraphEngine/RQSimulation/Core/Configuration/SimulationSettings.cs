namespace RQSimulation.Core.Configuration;

/// <summary>
/// Configuration settings for the RQ simulation platform.
///
/// This class supports the Options pattern from Microsoft.Extensions.Options.
/// Settings can be loaded from:
/// - JSON configuration files (appsettings.json, settings.json)
/// - Environment variables
/// - Command-line arguments
/// - Code-based configuration
///
/// Usage with Options pattern:
/// <code>
/// services.Configure&lt;SimulationSettings&gt;(configuration.GetSection("Simulation"));
///
/// public class EnergyLedger
/// {
///     private readonly IOptions&lt;SimulationSettings&gt; _settings;
///
///     public EnergyLedger(IOptions&lt;SimulationSettings&gt; settings)
///     {
///         _settings = settings;
///     }
/// }
/// </code>
/// </summary>
public sealed record SimulationSettings
{
    /// <summary>
    /// Section name for configuration binding.
    /// Use this when calling configuration.GetSection().
    /// </summary>
    public const string SectionName = "Simulation";

    /// <summary>
    /// Initial vacuum energy pool available for topology changes and particle creation.
    /// Default: 1.0e5 (from PhysicsConstants.InitialVacuumEnergy)
    /// </summary>
    public double InitialVacuumEnergy { get; init; } = 1.0e5;

    /// <summary>
    /// Enable strict Wheeler-DeWitt constraint mode.
    /// When enabled, external energy injection is forbidden.
    /// Default: false
    /// </summary>
    public bool StrictConservation { get; init; } = false;

    /// <summary>
    /// Energy conservation tolerance for violation detection.
    /// Default: 1e-6
    /// </summary>
    public double EnergyTolerance { get; init; } = 1e-6;

    /// <summary>
    /// Maximum number of constraint violations to keep in history.
    /// Default: 1000
    /// </summary>
    public int MaxViolations { get; init; } = 1000;

    /// <summary>
    /// Maximum degree of parallelism for CPU module execution.
    /// Default: Number of processor cores
    /// </summary>
    public int MaxCpuParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Temperature for entropy calculations (in Kelvin).
    /// Default: 2.725 (CMB temperature)
    /// </summary>
    public double Temperature { get; init; } = 2.725;

    /// <summary>
    /// Enable constraint violation logging.
    /// Default: true
    /// </summary>
    public bool EnableViolationLogging { get; init; } = true;

    /// <summary>
    /// Topology decoherence interval (in time steps).
    /// Determines how often topology changes can occur.
    /// Default: 100
    /// </summary>
    public int TopologyDecoherenceInterval { get; init; } = 100;

    /// <summary>
    /// Enable adaptive topology decoherence.
    /// When enabled, the interval adjusts based on graph size and energy.
    /// Default: false
    /// </summary>
    public bool AdaptiveTopologyDecoherence { get; init; } = false;

    /// <summary>
    /// Base interval factor for adaptive topology decoherence.
    /// The adaptive algorithm multiplies this by size and energy factors.
    /// Default: 1.0
    /// </summary>
    public double AdaptiveDecoherenceBaseInterval { get; init; } = 1.0;

    /// <summary>
    /// Energy density scaling factor for adaptive topology decoherence.
    /// Higher values make topology more stable at high energies.
    /// Default: 1.0
    /// </summary>
    public double AdaptiveDecoherenceEnergyFactor { get; init; } = 1.0;

    /// <summary>
    /// Graph size scaling exponent for adaptive topology decoherence.
    /// Used as: (N / 1000)^exponent in the adaptive formula.
    /// Default: -0.5 (larger graphs have shorter intervals)
    /// </summary>
    public double AdaptiveDecoherenceSizeExponent { get; init; } = -0.5;

    /// <summary>
    /// Amplitude threshold for quantum coherence protection.
    /// Edges with |ψ|² above this are protected from topology flips.
    /// Default: 0.1
    /// </summary>
    public double TopologyFlipAmplitudeThreshold { get; init; } = 0.1;

    /// <summary>
    /// Temperature for adaptive topology flip probability.
    /// Lower values = stronger protection for high-amplitude edges.
    /// Default: 1.0
    /// </summary>
    public double TopologyDecoherenceTemperature { get; init; } = 1.0;

    /// <summary>
    /// Geometry inertia mass for Hamiltonian gravity evolution.
    /// Controls resistance of metric to curvature-driven changes.
    /// Default: 10.0
    /// </summary>
    public double GeometryInertiaMass { get; init; } = 10.0;

    /// <summary>
    /// Initial vacuum energy pool for topology changes and particle creation.
    /// This is the legacy InitialVacuumEnergy property (kept for backward compatibility).
    /// Default: 1000.0 (from PhysicsConstants.InitialVacuumEnergy)
    /// </summary>
    public double VacuumEnergyPool
    {
        get => InitialVacuumEnergy;
        init => InitialVacuumEnergy = value;
    }

    /// <summary>
    /// Snapshot interval for graph state capture (in steps).
    /// Default: 100
    /// </summary>
    public int SnapshotInterval { get; init; } = 100;

    /// <summary>
    /// Enable graph snapshot compression using LZ4.
    /// Default: false
    /// </summary>
    public bool CompressSnapshots { get; init; } = false;

    /// <summary>
    /// Validates the configuration settings and throws if invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">If settings are invalid</exception>
    public void Validate()
    {
        if (InitialVacuumEnergy < 0)
            throw new InvalidOperationException($"{nameof(InitialVacuumEnergy)} must be non-negative.");

        if (EnergyTolerance <= 0)
            throw new InvalidOperationException($"{nameof(EnergyTolerance)} must be positive.");

        if (MaxViolations < 1)
            throw new InvalidOperationException($"{nameof(MaxViolations)} must be at least 1.");

        if (MaxCpuParallelism < 1)
            throw new InvalidOperationException($"{nameof(MaxCpuParallelism)} must be at least 1.");

        if (Temperature < 0)
            throw new InvalidOperationException($"{nameof(Temperature)} must be non-negative.");

        if (TopologyDecoherenceInterval < 1)
            throw new InvalidOperationException($"{nameof(TopologyDecoherenceInterval)} must be at least 1.");

        if (SnapshotInterval < 1)
            throw new InvalidOperationException($"{nameof(SnapshotInterval)} must be at least 1.");

        if (AdaptiveDecoherenceBaseInterval <= 0)
            throw new InvalidOperationException($"{nameof(AdaptiveDecoherenceBaseInterval)} must be positive.");

        if (AdaptiveDecoherenceEnergyFactor <= 0)
            throw new InvalidOperationException($"{nameof(AdaptiveDecoherenceEnergyFactor)} must be positive.");

        if (TopologyFlipAmplitudeThreshold < 0)
            throw new InvalidOperationException($"{nameof(TopologyFlipAmplitudeThreshold)} must be non-negative.");

        if (TopologyDecoherenceTemperature <= 0)
            throw new InvalidOperationException($"{nameof(TopologyDecoherenceTemperature)} must be positive.");

        if (GeometryInertiaMass <= 0)
            throw new InvalidOperationException($"{nameof(GeometryInertiaMass)} must be positive.");
    }

    /// <summary>
    /// Creates default simulation settings.
    /// </summary>
    public static SimulationSettings Default => new();
}
