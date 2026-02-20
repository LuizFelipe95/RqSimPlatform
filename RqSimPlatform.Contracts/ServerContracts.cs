namespace RqSimPlatform.Contracts;

/// <summary>
/// Command types for IPC communication between UI clients and the simulation console.
/// </summary>
public enum SimCommandType
{
    Handshake = 0,
    Start = 1,
    Pause = 2,
    Step = 3,
    UpdateSettings = 4,

    /// <summary>
    /// Resume/Attach command — reconnect to running simulation without restart.
    /// Unlike Start, this command does NOT reinitialize the graph.
    /// </summary>
    Resume = 5,

    GetMultiGpuStatus = 10,
    Shutdown = 99,
    Stop = 100
}

/// <summary>
/// A single IPC command with an optional JSON payload.
/// </summary>
public sealed class SimCommand
{
    public SimCommandType Type { get; init; }

    /// <summary>
    /// Optional JSON payload with command parameters/settings.
    /// </summary>
    public string? PayloadJson { get; init; }
}

/// <summary>
/// Unified settings DTO transferred between UI clients and the simulation console.
/// This is the single source of truth — all projects reference this definition.
/// </summary>
public sealed record ServerModeSettingsDto
{
    // ============================================================
    // CORE SIMULATION PARAMETERS
    // ============================================================
    public int NodeCount { get; init; }
    public int TargetDegree { get; init; }
    public int Seed { get; init; }
    public double Temperature { get; init; }
    public int TotalSteps { get; init; }

    // ============================================================
    // EXTENDED PHYSICS PARAMETERS
    // ============================================================
    public double InitialExcitedProb { get; init; }
    public double LambdaState { get; init; }
    public double EdgeTrialProb { get; init; }
    public double InitialEdgeProb { get; init; }
    public double GravitationalCoupling { get; init; }
    public double VacuumEnergyScale { get; init; }
    public double DecoherenceRate { get; init; }
    public double HotStartTemperature { get; init; }
    public int WarmupDuration { get; init; }
    public double GravityTransitionDuration { get; init; }

    // ============================================================
    // LAPSE & WILSON
    // ============================================================
    public double LapseFunctionAlpha { get; init; }
    public double WilsonParameter { get; init; }

    // ============================================================
    // GEOMETRY INERTIA
    // ============================================================
    public double GeometryInertiaMass { get; init; }
    public double GaugeFieldDamping { get; init; }

    // ============================================================
    // TOPOLOGY DECOHERENCE
    // ============================================================
    public int TopologyDecoherenceInterval { get; init; }
    public double TopologyDecoherenceTemperature { get; init; }

    // ============================================================
    // GAUGE PROTECTION
    // ============================================================
    public double GaugeTolerance { get; init; }
    public double MaxRemovableFlux { get; init; }

    // ============================================================
    // HAWKING RADIATION
    // ============================================================
    public double PairCreationMassThreshold { get; init; }
    public double PairCreationEnergy { get; init; }

    // ============================================================
    // RQ-HYPOTHESIS CHECKLIST
    // ============================================================
    public double EdgeWeightQuantum { get; init; }
    public double RngStepCost { get; init; }
    public double EdgeCreationCost { get; init; }
    public double InitialVacuumEnergy { get; init; }

    // ============================================================
    // SPECTRAL ACTION
    // ============================================================
    public double SpectralLambdaCutoff { get; init; }
    public double SpectralTargetDimension { get; init; }
    public double SpectralDimensionPotentialStrength { get; init; }

    // ============================================================
    // GRAPH HEALTH
    // ============================================================
    public double GiantClusterThreshold { get; init; }
    public double EmergencyGiantClusterThreshold { get; init; }
    public double GiantClusterDecoherenceRate { get; init; }
    public double MaxDecoherenceEdgesFraction { get; init; }
    public double CriticalSpectralDimension { get; init; }
    public double WarningSpectralDimension { get; init; }

    // ============================================================
    // RQ EXPERIMENTAL FLAGS
    // ============================================================
    public bool EnableNaturalDimensionEmergence { get; init; }
    public bool EnableTopologicalParity { get; init; }
    public bool EnableLapseSynchronizedGeometry { get; init; }
    public bool EnableTopologyEnergyCompensation { get; init; }
    public bool EnablePlaquetteYangMills { get; init; }
    public bool EnableSymplecticGaugeEvolution { get; init; }
    public bool EnableAdaptiveTopologyDecoherence { get; init; }
    public bool PreferOllivierRicciCurvature { get; init; }

    // ============================================================
    // PIPELINE MODULE ENABLE/DISABLE FLAGS
    // ============================================================
    public bool UseSpacetimePhysics { get; init; }
    public bool UseSpinorField { get; init; }
    public bool UseVacuumFluctuations { get; init; }
    public bool UseBlackHolePhysics { get; init; }
    public bool UseYangMillsGauge { get; init; }
    public bool UseEnhancedKleinGordon { get; init; }
    public bool UseInternalTime { get; init; }
    public bool UseRelationalTime { get; init; }
    public bool UseSpectralGeometry { get; init; }
    public bool UseQuantumGraphity { get; init; }
    public bool UseMexicanHatPotential { get; init; }
    public bool UseGeometryMomenta { get; init; }
    public bool UseUnifiedPhysicsStep { get; init; }
    public bool EnforceGaugeConstraints { get; init; }
    public bool ValidateEnergyConservation { get; init; }

    /// <summary>
    /// Enable MCMC Metropolis-Hastings sampler module in pipeline.
    /// </summary>
    public bool UseMcmc { get; init; }

    // ============================================================
    // MCMC METROPOLIS-HASTINGS PARAMETERS
    // ============================================================

    /// <summary>Inverse temperature ? = 1/kT for MCMC acceptance.</summary>
    public double McmcBeta { get; init; }

    /// <summary>Number of MCMC proposal steps per simulation step.</summary>
    public int McmcStepsPerCall { get; init; }

    /// <summary>Weight perturbation magnitude for MCMC change moves.</summary>
    public double McmcWeightPerturbation { get; init; }

    /// <summary>Minimum weight threshold below which edge is removed.</summary>
    public double McmcMinWeight { get; init; }

    // ============================================================
    // SINKHORN OLLIVIER-RICCI PARAMETERS
    // ============================================================

    /// <summary>Maximum Sinkhorn iterations for optimal transport.</summary>
    public int SinkhornIterations { get; init; }

    /// <summary>Entropic regularization ? for Sinkhorn algorithm.</summary>
    public double SinkhornEpsilon { get; init; }

    /// <summary>Convergence threshold for Sinkhorn iterations.</summary>
    public double SinkhornConvergenceThreshold { get; init; }

    /// <summary>Lazy random walk parameter ? for Ollivier-Ricci curvature.</summary>
    public double LazyWalkAlpha { get; init; }

    /// <summary>
    /// Default settings matching GUI defaults (FIX 27).
    /// Console-specific overrides should be applied on top of this baseline.
    /// </summary>
    public static ServerModeSettingsDto Default { get; } = new()
    {
        NodeCount = 250,
        TargetDegree = 8,
        Seed = 42,
        Temperature = 10.0,
        TotalSteps = 500000,
        InitialExcitedProb = 0.1,
        LambdaState = 0.5,
        EdgeTrialProb = 0.02,
        InitialEdgeProb = 0.035,
        GravitationalCoupling = 0.010,
        VacuumEnergyScale = 0.00005,
        DecoherenceRate = 0.005,
        HotStartTemperature = 6.0,
        WarmupDuration = 200,
        GravityTransitionDuration = 137.0,

        // Lapse & Wilson — Planck unit defaults
        LapseFunctionAlpha = 1.0,
        WilsonParameter = 1.0,

        // Geometry Inertia
        GeometryInertiaMass = 10.0,
        GaugeFieldDamping = 0.001,

        // Topology Decoherence
        TopologyDecoherenceInterval = 10,
        TopologyDecoherenceTemperature = 1.0,

        // Gauge Protection
        GaugeTolerance = 0.1,
        MaxRemovableFlux = Math.PI / 4.0,

        // Hawking Radiation
        PairCreationMassThreshold = 0.1,
        PairCreationEnergy = 0.01,

        // RQ-Hypothesis Checklist
        EdgeWeightQuantum = 0.01,
        RngStepCost = 0.001,
        EdgeCreationCost = 0.1,
        InitialVacuumEnergy = 1000.0,

        // Spectral Action
        SpectralLambdaCutoff = 1.0,
        SpectralTargetDimension = 4.0,
        SpectralDimensionPotentialStrength = 0.1,

        // Graph Health
        GiantClusterThreshold = 0.3,
        EmergencyGiantClusterThreshold = 0.5,
        GiantClusterDecoherenceRate = 0.1,
        MaxDecoherenceEdgesFraction = 0.1,
        CriticalSpectralDimension = 1.5,
        WarningSpectralDimension = 2.5,

        // RQ Experimental Flags
        EnableNaturalDimensionEmergence = true,
        EnableTopologicalParity = false,
        EnableLapseSynchronizedGeometry = true,
        EnableTopologyEnergyCompensation = true,
        EnablePlaquetteYangMills = false,
        EnableSymplecticGaugeEvolution = true,
        EnableAdaptiveTopologyDecoherence = false,
        PreferOllivierRicciCurvature = false,

        // Pipeline modules: all enabled by default
        UseSpacetimePhysics = true,
        UseSpinorField = true,
        UseVacuumFluctuations = true,
        UseBlackHolePhysics = true,
        UseYangMillsGauge = true,
        UseEnhancedKleinGordon = true,
        UseInternalTime = true,
        UseRelationalTime = true,
        UseSpectralGeometry = true,
        UseQuantumGraphity = true,
        UseMexicanHatPotential = true,
        UseGeometryMomenta = true,
        UseUnifiedPhysicsStep = true,
        EnforceGaugeConstraints = true,
        ValidateEnergyConservation = true,
        UseMcmc = true,

        // MCMC Metropolis-Hastings
        McmcBeta = 1.0,
        McmcStepsPerCall = 10,
        McmcWeightPerturbation = 0.1,
        McmcMinWeight = 0.01,

        // Sinkhorn Ollivier-Ricci
        SinkhornIterations = 50,
        SinkhornEpsilon = 0.01,
        SinkhornConvergenceThreshold = 1e-6,
        LazyWalkAlpha = 0.1
    };
}
