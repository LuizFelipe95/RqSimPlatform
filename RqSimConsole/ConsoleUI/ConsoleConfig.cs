using System.Text.Json.Serialization;

namespace RqSimConsole.ConsoleUI;

/// <summary>
/// Complete configuration for console simulation, mirroring all WinForms UI parameters.
/// Loaded from JSON file specified by -loadparam command line argument.
/// </summary>
public sealed class ConsoleConfig
{
    // === Physics Modules (checkboxes in grpPhysicsModules) ===
    public PhysicsModulesConfig PhysicsModules { get; set; } = new();

    // === RQ Experimental Flags (checkboxes in grpRQFlags) ===
    public RQExperimentalFlagsConfig RQFlags { get; set; } = new();

    // === Simulation Parameters (grpSimParams) ===
    public SimulationParametersConfig SimulationParameters { get; set; } = new();

    // === Physics Constants (grpPhysicsConstants) ===
    public PhysicsConstantsConfig PhysicsConstants { get; set; } = new();

    // === Run Settings ===
    public RunSettingsConfig RunSettings { get; set; } = new();
}

/// <summary>
/// Physics modules configuration (checkboxes on Settings tab left panel)
/// </summary>
public sealed class PhysicsModulesConfig
{
    // Row 1
    public bool QuantumDrivenStates { get; set; } = true;
    public bool RelationalTime { get; set; } = true;
    public bool TopologicalCensorship { get; set; } = true;

    // Row 2
    public bool SpacetimePhysics { get; set; } = true;
    public bool RelationalYangMills { get; set; } = true;

    // Row 3
    public bool SpinorField { get; set; } = true;
    public bool NetworkGravity { get; set; }

    // Row 4
    public bool VacuumFluctuations { get; set; } = true;
    public bool UnifiedPhysicsStep { get; set; } = true;

    // Row 5
    public bool BlackHolePhysics { get; set; } = true;
    public bool EnforceGaugeConstraints { get; set; } = true;

    // Row 6
    public bool YangMillsGauge { get; set; } = true;
    public bool CausalRewiring { get; set; } = true;

    // Row 7
    public bool EnhancedKleinGordon { get; set; } = true;
    public bool TopologicalProtection { get; set; } = true;

    // Row 8
    public bool InternalTime { get; set; } = true;
    public bool ValidateEnergyConservation { get; set; } = true;

    // Row 9
    public bool SpectralGeometry { get; set; } = true;
    public bool MexicanHatPotential { get; set; } = true;

    // Row 10
    public bool QuantumGraphity { get; set; } = true;
    public bool GeometryMomenta { get; set; } = true;
}

/// <summary>
/// RQ-Hypothesis experimental flags (purple checkboxes on Settings tab)
/// </summary>
public sealed class RQExperimentalFlagsConfig
{
    public bool NaturalDimensionEmergence { get; set; } = true;
    public bool TopologicalParity { get; set; }
    public bool LapseSynchronizedGeometry { get; set; } = true;
    public bool TopologyEnergyCompensation { get; set; } = true;
    public bool PlaquetteYangMills { get; set; }
}

/// <summary>
/// Simulation parameters (grpSimParams on Settings tab)
/// </summary>
public sealed class SimulationParametersConfig
{
    public int NodeCount { get; set; } = 250;
    public int TargetDegree { get; set; } = 8;
    public double InitialExcitedProb { get; set; } = 0.10;
    public double LambdaState { get; set; } = 0.50;
    public double Temperature { get; set; } = 10.00;
    public double EdgeTrialProb { get; set; } = 0.020;
    public double MeasurementThreshold { get; set; } = 0.300;
    public int TotalSteps { get; set; } = 500000;
    public int FractalLevels { get; set; }
    public int FractalBranchFactor { get; set; }
    // tau_anneal is computed
}

/// <summary>
/// Physics constants (grpPhysicsConstants on Settings tab)
/// </summary>
public sealed class PhysicsConstantsConfig
{
    public double InitialEdgeProb { get; set; } = 0.0350;
    public double GravitationalCoupling { get; set; } = 0.0100;
    public double VacuumEnergyScale { get; set; } = 0.0001;
    public double GravityTransition { get; set; } = 137.0;
    public double DecoherenceRate { get; set; } = 0.0050;
    public double HotStartTemperature { get; set; } = 6.0;
    public double AdaptiveThresholdAlpha { get; set; } = 1.50;
    public double WarmupDuration { get; set; } = 200;
}

/// <summary>
/// Run settings (right panel on Settings tab)
/// </summary>
public sealed class RunSettingsConfig
{
    public bool AutoTuningParams { get; set; }
    public bool AutoScrollConsole { get; set; } = true;
    public bool EnableGPU { get; set; } = true;
    public int GpuDeviceIndex { get; set; }
    public int CpuThreads { get; set; } = 8;
    public int MaxFPS { get; set; } = 10;
    public bool HeavyClustersOnly { get; set; }

    // Multi-GPU cluster settings
    public MultiGpuSettings MultiGpu { get; set; } = new();
}

/// <summary>
/// Multi-GPU cluster configuration.
/// </summary>
public sealed class MultiGpuSettings
{
    /// <summary>Enable Multi-GPU cluster mode.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Number of GPUs for spectral dimension (0 = auto-distribute).</summary>
    public int SpectralWorkerCount { get; set; } = 0;

    /// <summary>Number of GPUs for MCMC sampling (0 = auto-distribute).</summary>
    public int McmcWorkerCount { get; set; } = 0;

    /// <summary>Maximum graph size for buffer pre-allocation.</summary>
    public int MaxGraphSize { get; set; } = 100_000;

    /// <summary>Snapshot dispatch interval (simulation steps).</summary>
    public int SnapshotInterval { get; set; } = 100;

    /// <summary>Number of random walk steps for spectral dimension.</summary>
    public int SpectralSteps { get; set; } = 100;

    /// <summary>Number of walkers for spectral dimension computation.</summary>
    public int SpectralWalkers { get; set; } = 10_000;

    /// <summary>Number of MCMC energy samples to collect.</summary>
    public int McmcSamples { get; set; } = 100;

    /// <summary>MCMC thinning interval (steps between samples).</summary>
    public int McmcThinning { get; set; } = 10;
}

/// <summary>
/// Helper to convert ConsoleConfig to SimulationConfig for engine initialization.
/// </summary>
public static class ConsoleConfigExtensions
{
    public static RQSimulation.SimulationConfig ToSimulationConfig(this ConsoleConfig config)
    {
        return new RQSimulation.SimulationConfig
        {
            // Basic parameters
            NodeCount = config.SimulationParameters.NodeCount,
            TargetDegree = config.SimulationParameters.TargetDegree,
            InitialExcitedProb = config.SimulationParameters.InitialExcitedProb,
            LambdaState = config.SimulationParameters.LambdaState,
            Temperature = config.SimulationParameters.Temperature,
            EdgeTrialProbability = config.SimulationParameters.EdgeTrialProb,
            MeasurementThreshold = config.SimulationParameters.MeasurementThreshold,
            TotalSteps = config.SimulationParameters.TotalSteps,
            FractalLevels = config.SimulationParameters.FractalLevels,
            FractalBranchFactor = config.SimulationParameters.FractalBranchFactor,

            // Physics constants
            InitialEdgeProb = config.PhysicsConstants.InitialEdgeProb,
            GravitationalCoupling = config.PhysicsConstants.GravitationalCoupling,
            VacuumEnergyScale = config.PhysicsConstants.VacuumEnergyScale,
            DecoherenceRate = config.PhysicsConstants.DecoherenceRate,
            HotStartTemperature = config.PhysicsConstants.HotStartTemperature,

            // Physics modules
            UseQuantumDrivenStates = config.PhysicsModules.QuantumDrivenStates,
            UseRelationalTime = config.PhysicsModules.RelationalTime,
            UseTopologicalCensorship = config.PhysicsModules.TopologicalCensorship,
            UseSpacetimePhysics = config.PhysicsModules.SpacetimePhysics,
            UseRelationalYangMills = config.PhysicsModules.RelationalYangMills,
            UseSpinorField = config.PhysicsModules.SpinorField,
            UseNetworkGravity = config.PhysicsModules.NetworkGravity,
            UseVacuumFluctuations = config.PhysicsModules.VacuumFluctuations,
            UseUnifiedPhysicsStep = config.PhysicsModules.UnifiedPhysicsStep,
            UseBlackHolePhysics = config.PhysicsModules.BlackHolePhysics,
            EnforceGaugeConstraints = config.PhysicsModules.EnforceGaugeConstraints,
            UseYangMillsGauge = config.PhysicsModules.YangMillsGauge,
            UseCausalRewiring = config.PhysicsModules.CausalRewiring,
            UseEnhancedKleinGordon = config.PhysicsModules.EnhancedKleinGordon,
            UseTopologicalProtection = config.PhysicsModules.TopologicalProtection,
            UseInternalTime = config.PhysicsModules.InternalTime,
            ValidateEnergyConservation = config.PhysicsModules.ValidateEnergyConservation,
            UseSpectralGeometry = config.PhysicsModules.SpectralGeometry,
            UseMexicanHatPotential = config.PhysicsModules.MexicanHatPotential,
            UseQuantumGraphity = config.PhysicsModules.QuantumGraphity,
            UseGeometryMomenta = config.PhysicsModules.GeometryMomenta,
        };
    }
}
