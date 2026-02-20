using RqSimEngineApi.Contracts;

namespace RqSimForms.Forms.Interfaces;

/// <summary>
/// Complete physics settings configuration for serialization.
/// Contains all runtime-adjustable physics parameters, RQ-Hypothesis flags,
/// and simulation settings.
/// </summary>
public sealed class PhysicsSettingsConfig
{
    /// <summary>
    /// Configuration version for migration support.
    /// </summary>
    public string Version { get; set; } = "2.0";

    /// <summary>
    /// When this configuration was last modified.
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional preset name for this configuration.
    /// </summary>
    public string? PresetName { get; set; }

    // ============================================================
    // SIMULATION PARAMETERS
    // ============================================================

    /// <summary>Number of nodes in the graph (not hot-swappable).</summary>
    public int NodeCount { get; set; } = 256;

    /// <summary>Target average degree per node (not hot-swappable).</summary>
    public int TargetDegree { get; set; } = 8;

    /// <summary>Initial edge probability (not hot-swappable).</summary>
    public double InitialEdgeProb { get; set; } = 0.035;

    /// <summary>Initial excited node probability (not hot-swappable).</summary>
    public double InitialExcitedProb { get; set; } = 0.02;

    /// <summary>Total simulation steps.</summary>
    public int TotalSteps { get; set; } = 10000;

    /// <summary>Fractal levels for graph generation (not hot-swappable).</summary>
    public int FractalLevels { get; set; } = 0;

    /// <summary>Fractal branch factor (not hot-swappable).</summary>
    public int FractalBranchFactor { get; set; } = 0;

    // ============================================================
    // PHYSICS CONSTANTS (HOT-SWAPPABLE)
    // ============================================================

    public double GravitationalCoupling { get; set; } = 0.05;
    public double VacuumEnergyScale { get; set; } = 0.00005;
    public double AnnealingCoolingRate { get; set; } = 0.995;
    public double DecoherenceRate { get; set; } = 0.001;
    public double HotStartTemperature { get; set; } = 3.0;
    public double AdaptiveThresholdSigma { get; set; } = 1.5;
    public int WarmupDuration { get; set; } = 200;
    public int GravityTransitionDuration { get; set; } = 137;
    public double LambdaState { get; set; } = 0.5;
    public double Temperature { get; set; } = 10.0;
    public double EdgeTrialProb { get; set; } = 0.02;
    public double MeasurementThreshold { get; set; } = 0.3;

    // ============================================================
    // RQ-HYPOTHESIS CHECKLIST PARAMETERS
    // ============================================================

    public double EdgeWeightQuantum { get; set; } = 0.01;
    public double RngStepCost { get; set; } = 0.001;
    public double EdgeCreationCost { get; set; } = 0.1;
    public double InitialVacuumEnergy { get; set; } = 1000.0;

    // ============================================================
    // ADVANCED PHYSICS PARAMETERS
    // ============================================================

    public double LapseFunctionAlpha { get; set; } = 1.0;
    public double TimeDilationAlpha { get; set; } = 0.5;
    public double WilsonParameter { get; set; } = 1.0;
    public int TopologyDecoherenceInterval { get; set; } = 10;
    public double TopologyDecoherenceTemperature { get; set; } = 1.0;
    public double GaugeTolerance { get; set; } = 0.1;
    public double MaxRemovableFlux { get; set; } = Math.PI / 4.0;
    public double GeometryInertiaMass { get; set; } = 10.0;
    public double GaugeFieldDamping { get; set; } = 0.001;
    public double PairCreationMassThreshold { get; set; } = 0.1;
    public double PairCreationEnergy { get; set; } = 0.01;

    // ============================================================
    // SPECTRAL ACTION PARAMETERS
    // ============================================================

    public double SpectralLambdaCutoff { get; set; } = 1.0;
    public double SpectralTargetDimension { get; set; } = 4.0;
    public double SpectralDimensionPotentialStrength { get; set; } = 0.1;

    // ============================================================
    // MCMC METROPOLIS-HASTINGS PARAMETERS
    // ============================================================

    /// <summary>Inverse temperature ? = 1/kT for MCMC acceptance.</summary>
    public double McmcBeta { get; set; } = 1.0;

    /// <summary>Number of MCMC proposal steps per simulation step.</summary>
    public int McmcStepsPerCall { get; set; } = 10;

    /// <summary>Weight perturbation magnitude for MCMC change moves.</summary>
    public double McmcWeightPerturbation { get; set; } = 0.1;

    /// <summary>Minimum weight threshold below which edge is removed.</summary>
    public double McmcMinWeight { get; set; } = 0.01;

    // ============================================================
    // SINKHORN OLLIVIER-RICCI PARAMETERS
    // ============================================================

    /// <summary>Maximum Sinkhorn iterations for optimal transport.</summary>
    public int SinkhornIterations { get; set; } = 50;

    /// <summary>Entropic regularization ? for Sinkhorn algorithm.</summary>
    public double SinkhornEpsilon { get; set; } = 0.01;

    /// <summary>Convergence threshold for Sinkhorn iterations.</summary>
    public double SinkhornConvergenceThreshold { get; set; } = 1e-6;

    /// <summary>Lazy random walk parameter ? for Ollivier-Ricci curvature.</summary>
    public double LazyWalkAlpha { get; set; } = 0.1;

    // ============================================================
    // GRAPH HEALTH PARAMETERS
    // ============================================================

    public double GiantClusterThreshold { get; set; } = 0.3;
    public double EmergencyGiantClusterThreshold { get; set; } = 0.5;
    public double GiantClusterDecoherenceRate { get; set; } = 0.1;
    public double MaxDecoherenceEdgesFraction { get; set; } = 0.1;
    public double CriticalSpectralDimension { get; set; } = 1.5;
    public double WarningSpectralDimension { get; set; } = 2.5;

    // ============================================================
    // AUTO-TUNING PARAMETERS
    // ============================================================

    public double AutoTuneTargetDimension { get; set; } = 4.0;
    public double AutoTuneDimensionTolerance { get; set; } = 0.5;
    public bool AutoTuneUseHybridSpectral { get; set; } = true;
    public bool AutoTuneManageVacuumEnergy { get; set; } = true;
    public bool AutoTuneAllowEnergyInjection { get; set; } = true;
    public double AutoTuneEnergyRecyclingRate { get; set; } = 0.5;
    public double AutoTuneGravityAdjustmentRate { get; set; } = 0.8;
    public double AutoTuneExplorationProb { get; set; } = 0.05;

    // ============================================================
    // MODE FLAGS (PERSISTED)
    // ============================================================

    /// <summary>
    /// Whether Science Mode is enabled (strict physical validation, Planck-scale constants).
    /// </summary>
    public bool ScienceModeEnabled { get; set; } = false;

    /// <summary>
    /// Whether Auto-Tuning is enabled.
    /// </summary>
    public bool AutoTuningEnabled { get; set; } = false;

    /// <summary>
    /// Whether to use Ollivier-Ricci curvature (true) or Forman-Ricci (false).
    /// </summary>
    public bool UseOllivierRicciCurvature { get; set; } = true;

    /// <summary>
    /// Whether conservation validation is enabled.
    /// </summary>
    public bool EnableConservationValidation { get; set; } = false;

    /// <summary>
    /// Whether GPU anisotropy computation is enabled.
    /// </summary>
    public bool UseGpuAnisotropy { get; set; } = true;

    // ============================================================
    // RQ-HYPOTHESIS EXPERIMENTAL FLAGS
    // ============================================================

    public bool UseHamiltonianGravity { get; set; } = true;
    public bool EnableVacuumEnergyReservoir { get; set; } = true;
    public bool EnableNaturalDimensionEmergence { get; set; } = true;
    public bool EnableTopologicalParity { get; set; } = false;
    public bool EnableLapseSynchronizedGeometry { get; set; } = true;
    public bool EnableTopologyEnergyCompensation { get; set; } = true;
    public bool EnablePlaquetteYangMills { get; set; } = false;
    public bool EnableSymplecticGaugeEvolution { get; set; } = true;
    public bool EnableAdaptiveTopologyDecoherence { get; set; } = false;
    public bool EnableWilsonLoopProtection { get; set; } = true;
    public bool EnableSpectralActionMode { get; set; } = true;
    public bool EnableWheelerDeWittStrictMode { get; set; } = false;
    public bool PreferOllivierRicciCurvature { get; set; } = true;

    // ============================================================
    // PHYSICS MODULE ENABLED FLAGS
    // ============================================================

    public bool EnableSpacetimePhysics { get; set; } = true;
    public bool EnableSpinorField { get; set; } = true;
    public bool EnableVacuumFluctuations { get; set; } = true;
    public bool EnableBlackHolePhysics { get; set; } = true;
    public bool EnableYangMillsGauge { get; set; } = true;
    public bool EnableKleinGordon { get; set; } = true;
    public bool EnableInternalTime { get; set; } = true;
    public bool EnableSpectralGeometry { get; set; } = true;
    public bool EnableQuantumGraphity { get; set; } = true;
    public bool EnableRelationalTime { get; set; } = true;
    public bool EnableNetworkGravity { get; set; } = true;
    public bool EnableUnifiedPhysicsStep { get; set; } = true;
    public bool EnableGaugeConstraints { get; set; } = true;
    public bool EnableCausalRewiring { get; set; } = true;
    public bool EnableTopologicalProtection { get; set; } = true;
    public bool EnableEnergyValidation { get; set; } = true;
    public bool EnableMexicanHatPotential { get; set; } = true;
    public bool EnableGeometryMomenta { get; set; } = true;
    public bool EnableTopologicalCensorship { get; set; } = true;

    /// <summary>
    /// Creates a default configuration with PhysicsConstants values.
    /// </summary>
    public static PhysicsSettingsConfig CreateDefault()
    {
        return new PhysicsSettingsConfig();
    }

    /// <summary>
    /// Gets a list of parameter names that cannot be changed at runtime.
    /// </summary>
    public static IReadOnlyList<string> NonHotSwappableParameters { get; } =
    [
        nameof(NodeCount),
        nameof(TargetDegree),
        nameof(InitialEdgeProb),
        nameof(InitialExcitedProb),
        nameof(FractalLevels),
        nameof(FractalBranchFactor)
    ];

    /// <summary>
    /// Checks if a parameter can be changed at runtime.
    /// </summary>
    public static bool IsHotSwappable(string parameterName)
    {
        return !NonHotSwappableParameters.Contains(parameterName);
    }

    // ============================================================
    // GPU PARAMETER CONVERSION
    // ============================================================

    /// <summary>
    /// Converts UI configuration to GPU-compatible SimulationParameters struct.
    /// Call this before each simulation frame to get current UI values.
    /// 
    /// USAGE:
    /// var gpuParams = config.ToGpuParameters();
    /// context.Params = gpuParams;
    /// </summary>
    public SimulationParameters ToGpuParameters()
    {
        return new SimulationParameters
        {
            // Time (set by simulation loop, not config)
            DeltaTime = 0.01,  // Will be overwritten by simulation
            CurrentTime = 0.0,
            TickId = 0,
            
            // Gravity & Geometry
            GravitationalCoupling = GravitationalCoupling,
            RicciFlowAlpha = LapseFunctionAlpha,  // UI uses LapseFunctionAlpha
            LapseFunctionAlpha = LapseFunctionAlpha,
            CosmologicalConstant = 0.0,  // Not exposed in current UI
            VacuumEnergyScale = VacuumEnergyScale,
            LazyWalkAlpha = LazyWalkAlpha,
            
            // Thermodynamics
            Temperature = Temperature,
            InverseBeta = Temperature > 0 ? 1.0 / Temperature : 0.1,
            AnnealingRate = AnnealingCoolingRate,
            DecoherenceRate = DecoherenceRate,
            
            // Gauge
            GaugeCoupling = WilsonParameter,  // Use Wilson as gauge coupling
            WilsonParameter = WilsonParameter,
            GaugeFieldDamping = GaugeFieldDamping,
            
            // Topology
            EdgeCreationProbability = EdgeTrialProb,
            EdgeDeletionProbability = EdgeTrialProb * 0.2,  // Deletion is rarer
            TopologyBreakThreshold = 0.001,
            EdgeTrialProbability = EdgeTrialProb,
            
            // Quantum
            MeasurementThreshold = MeasurementThreshold,
            ScalarFieldMassSquared = PairCreationMassThreshold * PairCreationMassThreshold,
            FermionMass = PairCreationMassThreshold,
            PairCreationEnergy = PairCreationEnergy,
            
            // Spectral
            SpectralCutoff = SpectralLambdaCutoff,
            TargetSpectralDimension = SpectralTargetDimension,
            SpectralDimensionStrength = SpectralDimensionPotentialStrength,
            
            // Numerical (Sinkhorn)
            SinkhornIterations = SinkhornIterations,
            SinkhornEpsilon = SinkhornEpsilon,
            ConvergenceThreshold = SinkhornConvergenceThreshold,
            
            // Flags
            Flags = BuildFlags()
        };
    }

    /// <summary>
    /// Builds packed flags integer from boolean settings.
    /// </summary>
    private int BuildFlags()
    {
        int flags = 0;
        
        // Bit 0: UseDoublePrecision (always true for scientific work)
        flags |= (1 << 0);
        
        // Bit 1: ScientificMode
        // (Not directly exposed, but could use EnableWheelerDeWittStrictMode)
        if (EnableWheelerDeWittStrictMode) flags |= (1 << 1);
        
        // Bit 2: EnableVacuumReservoir
        if (EnableVacuumEnergyReservoir) flags |= (1 << 2);
        
        // Bit 3: EnableOllivierRicci
        if (PreferOllivierRicciCurvature) flags |= (1 << 3);
        
        // Bit 4: EnableSpectralAction
        if (EnableSpectralActionMode) flags |= (1 << 4);
        
        // Bit 5: EnableHamiltonianGravity
        if (UseHamiltonianGravity) flags |= (1 << 5);
        
        // Bit 6: EnableTopologyCompensation
        if (EnableTopologyEnergyCompensation) flags |= (1 << 6);
        
        // Bit 7: EnableWilsonProtection
        if (EnableWilsonLoopProtection) flags |= (1 << 7);
        
        return flags;
    }

    /// <summary>
    /// Updates this config from GPU parameters (reverse conversion).
    /// Useful for synchronizing after shader modifications or presets.
    /// </summary>
    public void FromGpuParameters(SimulationParameters p)
    {
        GravitationalCoupling = p.GravitationalCoupling;
        LapseFunctionAlpha = p.LapseFunctionAlpha;
        VacuumEnergyScale = p.VacuumEnergyScale;
        
        Temperature = p.Temperature;
        AnnealingCoolingRate = p.AnnealingRate;
        DecoherenceRate = p.DecoherenceRate;
        
        WilsonParameter = p.WilsonParameter;
        GaugeFieldDamping = p.GaugeFieldDamping;
        
        EdgeTrialProb = p.EdgeTrialProbability;
        MeasurementThreshold = p.MeasurementThreshold;
        PairCreationMassThreshold = p.FermionMass;
        PairCreationEnergy = p.PairCreationEnergy;
        
        SpectralLambdaCutoff = p.SpectralCutoff;
        SpectralTargetDimension = p.TargetSpectralDimension;
        SpectralDimensionPotentialStrength = p.SpectralDimensionStrength;

        // Sinkhorn / Numerical
        SinkhornIterations = p.SinkhornIterations;
        SinkhornEpsilon = p.SinkhornEpsilon;
        SinkhornConvergenceThreshold = p.ConvergenceThreshold;
        LazyWalkAlpha = p.LazyWalkAlpha;

        // Flags
        EnableVacuumEnergyReservoir = p.EnableVacuumReservoir;
        PreferOllivierRicciCurvature = p.EnableOllivierRicci;
        EnableSpectralActionMode = p.EnableSpectralAction;
        UseHamiltonianGravity = p.EnableHamiltonianGravity;
        EnableTopologyEnergyCompensation = p.EnableTopologyCompensation;
        EnableWilsonLoopProtection = p.EnableWilsonProtection;
        
        LastModified = DateTime.UtcNow;
    }
}
