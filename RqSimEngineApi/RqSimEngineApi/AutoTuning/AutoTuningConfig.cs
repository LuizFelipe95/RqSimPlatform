using System;
using RQSimulation;

namespace RqSimForms.Forms.Interfaces.AutoTuning;

/// <summary>
/// Configuration for the RQ-Hypothesis Auto-Tuning System.
/// 
/// This class encapsulates all configurable parameters for automatic
/// simulation tuning to achieve target spectral dimension (d_S ? 4).
/// 
/// RQ-HYPOTHESIS GOAL:
/// - Spectral dimension should emerge naturally as d_S ? 4 (4D spacetime)
/// - UV regime (short scales): d_S ? 2
/// - IR regime (long scales): d_S ? 4
/// - Auto-tuning guides the system toward this target
/// </summary>
public sealed class AutoTuningConfig
{
    // ============================================================
    // SPECTRAL DIMENSION TARGETS
    // ============================================================

    /// <summary>Target spectral dimension for IR regime (4D spacetime).</summary>
    public double TargetSpectralDimension { get; set; } = 4.0;

    /// <summary>Acceptable range around target [Target - Tolerance, Target + Tolerance].</summary>
    public double SpectralDimensionTolerance { get; set; } = 0.5;

    /// <summary>Critical low d_S threshold - below this, graph is fragmenting.</summary>
    public double CriticalSpectralDimension { get; set; } = PhysicsConstants.CriticalSpectralDimension;

    /// <summary>Warning low d_S threshold - approaching fragmentation.</summary>
    public double WarningSpectralDimension { get; set; } = PhysicsConstants.WarningSpectralDimension;

    /// <summary>High d_S threshold - hyperbolic/over-connected regime.</summary>
    public double HighSpectralDimension { get; set; } = 5.5;

    // ============================================================
    // SPECTRAL DIMENSION COMPUTATION
    // ============================================================

    /// <summary>Enable hybrid spectral dimension computation (multiple methods).</summary>
    public bool UseHybridSpectralComputation { get; set; } = true;

    /// <summary>Number of random walkers for spectral dimension (higher = more accurate).</summary>
    public int SpectralWalkerCount { get; set; } = 500;

    /// <summary>Maximum time steps for random walk / heat kernel.</summary>
    public int SpectralMaxSteps { get; set; } = 100;

    /// <summary>
    /// Minimum confidence threshold for spectral dimension estimate.
    /// If confidence is below this, use ensemble of methods.
    /// </summary>
    public double SpectralConfidenceThreshold { get; set; } = 0.7;

    /// <summary>EMA smoothing factor for spectral dimension (0 = no smoothing, 1 = no memory).</summary>
    public double SpectralSmoothingAlpha { get; set; } = 0.3;

    // ============================================================
    // GRAVITATIONAL COUPLING (G)
    // ============================================================

    /// <summary>Base gravitational coupling strength.</summary>
    public double BaseGravitationalCoupling { get; set; } = 0.05;

    /// <summary>Minimum allowed G (prevent complete decoupling).</summary>
    public double MinGravitationalCoupling { get; set; } = 0.0005;

    /// <summary>Maximum allowed G (prevent over-collapse).</summary>
    public double MaxGravitationalCoupling { get; set; } = 0.5;

    /// <summary>
    /// G adjustment rate per tuning interval.
    /// Values closer to 1 = slower adjustment.
    /// </summary>
    public double GravityAdjustmentRate { get; set; } = 0.8;

    /// <summary>G suppression factor when d_S is too low (fragmentation risk).</summary>
    public double GravitySuppressionFactor { get; set; } = 0.2;

    /// <summary>G boost factor when d_S is too high (hyperbolic regime).</summary>
    public double GravityBoostFactor { get; set; } = 1.5;

    // ============================================================
    // DECOHERENCE AND CLUSTER DYNAMICS
    // ============================================================

    /// <summary>Base decoherence rate.</summary>
    public double BaseDecoherenceRate { get; set; } = 0.001;

    /// <summary>Minimum decoherence rate.</summary>
    public double MinDecoherenceRate { get; set; } = 0.0001;

    /// <summary>Maximum decoherence rate.</summary>
    public double MaxDecoherenceRate { get; set; } = 0.15;

    /// <summary>Giant cluster threshold (fraction of nodes).</summary>
    public double GiantClusterThreshold { get; set; } = PhysicsConstants.GiantClusterThreshold;

    /// <summary>Emergency giant cluster threshold (requires aggressive action).</summary>
    public double EmergencyClusterThreshold { get; set; } = PhysicsConstants.EmergencyGiantClusterThreshold;

    /// <summary>Extreme cluster threshold (topology tunneling required).</summary>
    public double ExtremeClusterThreshold { get; set; } = 0.70;

    /// <summary>Decoherence boost factor for giant clusters.</summary>
    public double ClusterDecoherenceBoost { get; set; } = 3.0;

    /// <summary>Consecutive extreme cluster steps before topology tunneling.</summary>
    public int TopologyTunnelingTriggerCount { get; set; } = 3;

    // ============================================================
    // VACUUM ENERGY MANAGEMENT
    // ============================================================

    /// <summary>Enable active vacuum energy management.</summary>
    public bool EnableVacuumEnergyManagement { get; set; } = true;

    /// <summary>Critical vacuum energy fraction (below this = simulation dying).</summary>
    public double CriticalVacuumFraction { get; set; } = 0.05;

    /// <summary>Warning vacuum energy fraction.</summary>
    public double WarningVacuumFraction { get; set; } = 0.15;

    /// <summary>Target vacuum energy fraction to maintain.</summary>
    public double TargetVacuumFraction { get; set; } = 0.25;

    /// <summary>Energy recycling rate from decaying edges.</summary>
    public double EnergyRecyclingRate { get; set; } = 0.6;

    /// <summary>
    /// Enable emergency energy injection when critically low.
    /// Note: This violates strict Wheeler-DeWitt conservation.
    /// </summary>
    public bool AllowEmergencyEnergyInjection { get; set; } = true;

    /// <summary>Emergency injection amount (fraction of initial energy).</summary>
    public double EmergencyInjectionFraction { get; set; } = 0.2;

    /// <summary>
    /// Enable proactive energy injection before reaching critical levels.
    /// Injects smaller amounts earlier to prevent crisis.
    /// </summary>
    public bool EnableProactiveEnergyInjection { get; set; } = true;

    /// <summary>Proactive injection threshold (inject when below this fraction).</summary>
    public double ProactiveInjectionThreshold { get; set; } = 0.12;

    /// <summary>Proactive injection amount (fraction of initial energy).</summary>
    public double ProactiveInjectionFraction { get; set; } = 0.08;

    // ============================================================
    // EDGE DYNAMICS
    // ============================================================

    /// <summary>Base edge creation probability.</summary>
    public double BaseEdgeTrialProb { get; set; } = 0.02;

    /// <summary>Minimum edge trial probability.</summary>
    public double MinEdgeTrialProb { get; set; } = 0.001;

    /// <summary>Maximum edge trial probability.</summary>
    public double MaxEdgeTrialProb { get; set; } = 0.5;

    /// <summary>Edge probability boost factor during fragmentation.</summary>
    public double FragmentationEdgeBoost { get; set; } = 3.0;

    // ============================================================
    // TEMPERATURE AND ANNEALING
    // ============================================================

    /// <summary>Enable temperature management during auto-tuning.</summary>
    public bool EnableTemperatureManagement { get; set; } = true;

    /// <summary>Temperature boost factor during fragmentation.</summary>
    public double FragmentationTemperatureBoost { get; set; } = 2.0;

    /// <summary>Maximum hot-start temperature.</summary>
    public double MaxTemperature { get; set; } = 20.0;

    /// <summary>Minimum temperature (final cold state).</summary>
    public double MinTemperature { get; set; } = 0.01;

    // ============================================================
    // TUNING INTERVALS AND TIMING
    // ============================================================

    /// <summary>Steps between auto-tuning checks.</summary>
    public int TuningInterval { get; set; } = 100;

    /// <summary>Steps between spectral dimension computations.</summary>
    public int SpectralComputeInterval { get; set; } = 200;

    /// <summary>Steps between energy validation checks.</summary>
    public int EnergyValidationInterval { get; set; } = 50;

    /// <summary>Warmup steps before auto-tuning activates.</summary>
    public int WarmupSteps { get; set; } = 200;

    // ============================================================
    // EXPLORATION AND RANDOMIZATION
    // ============================================================

    /// <summary>Enable stochastic exploration when system is healthy.</summary>
    public bool EnableExploration { get; set; } = true;

    /// <summary>Probability of exploration step when conditions are met.</summary>
    public double ExplorationProbability { get; set; } = 0.05;

    /// <summary>Exploration perturbation range [1-range, 1+range].</summary>
    public double ExplorationRange { get; set; } = 0.05;

    // ============================================================
    // CONTROLLER ENABLE FLAGS
    // ============================================================

    /// <summary>Enable spectral dimension controller.</summary>
    public bool EnableSpectralController { get; set; } = true;

    /// <summary>Enable gravity coupling controller.</summary>
    public bool EnableGravityController { get; set; } = true;

    /// <summary>Enable cluster dynamics controller.</summary>
    public bool EnableClusterController { get; set; } = true;

    /// <summary>Enable vacuum energy manager.</summary>
    public bool EnableVacuumManager { get; set; } = true;

    // ============================================================
    // OPTIMIZATION TARGET
    // ============================================================

    /// <summary>
    /// Primary optimization target for auto-tuning.
    /// Determines which physical metric to prioritize.
    /// </summary>
    public OptimizationTarget PrimaryTarget { get; set; } = OptimizationTarget.StableSpectralDimension;

    /// <summary>
    /// Secondary optimization target (used when primary is satisfied).
    /// </summary>
    public OptimizationTarget SecondaryTarget { get; set; } = OptimizationTarget.MassGapMaximization;

    /// <summary>
    /// Target mass gap value (??) for MassGapMaximization.
    /// </summary>
    public double TargetMassGap { get; set; } = 0.01;

    /// <summary>
    /// Maximum allowed speed of light anisotropy (variance).
    /// </summary>
    public double MaxLightSpeedAnisotropy { get; set; } = 0.05;

    /// <summary>
    /// Target average Ricci curvature for flat space.
    /// </summary>
    public double TargetRicciCurvature { get; set; } = 0.0;

    /// <summary>
    /// Tolerance for Ricci curvature deviation from target.
    /// </summary>
    public double RicciCurvatureTolerance { get; set; } = 0.1;

    // ============================================================
    // FACTORY METHODS
    // ============================================================

    /// <summary>Creates default configuration optimized for 4D emergence.</summary>
    public static AutoTuningConfig CreateDefault() => new();

    /// <summary>Creates aggressive configuration for quick convergence to 4D.</summary>
    public static AutoTuningConfig CreateAggressive()
    {
        return new AutoTuningConfig
        {
            TuningInterval = 50,
            SpectralComputeInterval = 100,
            GravityAdjustmentRate = 0.5,
            ClusterDecoherenceBoost = 5.0,
            ExplorationProbability = 0.02,
            SpectralSmoothingAlpha = 0.5
        };
    }

    /// <summary>Creates conservative configuration for stable evolution.</summary>
    public static AutoTuningConfig CreateConservative()
    {
        return new AutoTuningConfig
        {
            TuningInterval = 200,
            SpectralComputeInterval = 500,
            GravityAdjustmentRate = 0.9,
            ClusterDecoherenceBoost = 2.0,
            ExplorationProbability = 0.01,
            SpectralSmoothingAlpha = 0.2,
            AllowEmergencyEnergyInjection = false
        };
    }

    /// <summary>Creates configuration for long-running simulations.</summary>
    public static AutoTuningConfig CreateLongRun()
    {
        return new AutoTuningConfig
        {
            TuningInterval = 500,
            SpectralComputeInterval = 1000,
            SpectralWalkerCount = 1000,
            SpectralMaxSteps = 200,
            GravityAdjustmentRate = 0.95,
            EnergyRecyclingRate = 0.7,
            TargetVacuumFraction = 0.25
        };
    }
}
