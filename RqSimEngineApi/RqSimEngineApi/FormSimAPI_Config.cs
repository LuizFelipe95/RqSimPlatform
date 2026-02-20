using RQSimulation;
using RQSimulation.GPUOptimized;
using RQSimulation.EventBasedModel;
using RqSimForms.Forms.Interfaces.AutoTuning;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RqSimForms.Forms.Interfaces;

public partial class RqSimEngineApi
{
    // === Live Config (thread-safe, for runtime parameter updates) ===
    /// <summary>
    /// Thread-safe container for runtime-adjustable simulation parameters.
    /// UI thread writes, calculation thread reads.
    /// Note: double reads/writes are atomic on x64, no volatile needed.
    /// </summary>
    public class LiveConfigData
    {
        // Physics Constants (grpPhysicsConstants)
        public double GravitationalCoupling = 0.05;
        public double VacuumEnergyScale = 0.00005;
        public double AnnealingCoolingRate = 0.995;
        public double DecoherenceRate = 0.001;
        public double HotStartTemperature = 3.0;
        public double InitialEdgeProb = 0.02;
        public double AdaptiveThresholdSigma = 1.5;
        public double WarmupDuration = 200;
        public double GravityTransitionDuration = 137;

        // Simulation Parameters (grpSimParams)
        public volatile int TargetDegree = 8;
        public double InitialExcitedProb = 0.02;
        public double LambdaState = 0.5;
        public double Temperature = 10.0;
        public double EdgeTrialProb = 0.02;
        public double MeasurementThreshold = 0.3;
        public volatile int FractalLevels = 0;
        public volatile int FractalBranchFactor = 0;

        // RQ-Hypothesis Checklist Constants (Energy Quantization)
        // These control discrete correlation quanta and vacuum energy accounting
        public double EdgeWeightQuantum = PhysicsConstants.EdgeWeightQuantum;
        public double RngStepCost = PhysicsConstants.RngStepCost;
        public double EdgeCreationCost = PhysicsConstants.EdgeCreationCost;
        public double InitialVacuumEnergy = PhysicsConstants.InitialVacuumEnergy;

        // === Advanced Physics Parameters (added for Settings UI sync) ===
        public double LapseFunctionAlpha = PhysicsConstants.LapseFunctionAlpha;
        public double TimeDilationAlpha = PhysicsConstants.TimeDilationAlpha;

        public double WilsonParameter = PhysicsConstants.WilsonParameter;

        public volatile int TopologyDecoherenceInterval = PhysicsConstants.TopologyDecoherenceInterval;
        public double TopologyDecoherenceTemperature = PhysicsConstants.TopologyDecoherenceTemperature;

        public double GaugeTolerance = PhysicsConstants.GaugeTolerance;
        public double MaxRemovableFlux = PhysicsConstants.MaxRemovableFlux;

        public double GeometryInertiaMass = PhysicsConstants.GeometryInertiaMass; // HamiltonianMomentumTerm
        public double GaugeFieldDamping = PhysicsConstants.GaugeFieldDamping;

        public double PairCreationMassThreshold = PhysicsConstants.PairCreationMassThreshold;
        public double PairCreationEnergy = PhysicsConstants.PairCreationEnergy;

        public double SpectralLambdaCutoff = PhysicsConstants.SpectralActionConstants.LambdaCutoff;
        public double SpectralTargetDimension = PhysicsConstants.SpectralActionConstants.TargetSpectralDimension;
        public double SpectralDimensionPotentialStrength = PhysicsConstants.SpectralActionConstants.DimensionPotentialStrength;

        // ============================================================
        // MCMC METROPOLIS-HASTINGS PARAMETERS
        // ============================================================

        /// <summary>Inverse temperature ? = 1/kT for MCMC acceptance.</summary>
        public double McmcBeta = 1.0;

        /// <summary>Number of MCMC proposal steps per simulation step.</summary>
        public volatile int McmcStepsPerCall = 10;

        /// <summary>Weight perturbation magnitude for MCMC change moves.</summary>
        public double McmcWeightPerturbation = 0.1;

        // ============================================================
        // SINKHORN OLLIVIER-RICCI PARAMETERS
        // ============================================================

        /// <summary>Maximum Sinkhorn iterations for optimal transport.</summary>
        public volatile int SinkhornIterations = 50;

        /// <summary>Entropic regularization ? for Sinkhorn algorithm.</summary>
        public double SinkhornEpsilon = 0.01;

        /// <summary>Convergence threshold for Sinkhorn iterations.</summary>
        public double SinkhornConvergenceThreshold = 1e-6;

        /// <summary>Lazy random walk parameter ? for Ollivier-Ricci curvature.</summary>
        public double LazyWalkAlpha = 0.1;

        // ============================================================
        // AUTO-TUNING PARAMETERS (v2.0)
        // ============================================================

        /// <summary>Target spectral dimension for auto-tuning (default: 4.0 for 4D spacetime).</summary>
        public double AutoTuneTargetDimension = 4.0;

        /// <summary>Tolerance around target dimension before corrections are applied.</summary>
        public double AutoTuneDimensionTolerance = 0.5;

        /// <summary>Enable hybrid spectral dimension computation (combines multiple methods).</summary>
        public volatile bool AutoTuneUseHybridSpectral = true;

        /// <summary>Enable vacuum energy management during auto-tuning.</summary>
        public volatile bool AutoTuneManageVacuumEnergy = true;

        /// <summary>Allow emergency vacuum energy injection when critically low.</summary>
        public volatile bool AutoTuneAllowEnergyInjection = true;

        /// <summary>Energy recycling rate from weak edges (0 = disabled, 1 = aggressive).</summary>
        public double AutoTuneEnergyRecyclingRate = 0.5;

        /// <summary>Gravity adjustment rate (0 = no adjustment, 1 = instant).</summary>
        public double AutoTuneGravityAdjustmentRate = 0.8;

        /// <summary>Exploration probability when system is healthy.</summary>
        public double AutoTuneExplorationProb = 0.05;

        /// <summary>
        /// Timestamp of last update (for change detection)
        /// </summary>
        private long _lastUpdateTicks;
        public long LastUpdateTicks
        {
            get => Interlocked.Read(ref _lastUpdateTicks);
            private set => Interlocked.Exchange(ref _lastUpdateTicks, value);
        }

        /// <summary>
        /// Marks config as updated
        /// </summary>
        public void MarkUpdated() => LastUpdateTicks = DateTime.UtcNow.Ticks;

        /// <summary>
        /// Applies auto-tuning parameters to an AutoTuningConfig instance.
        /// </summary>
        public void ApplyToAutoTuningConfig(AutoTuningConfig config)
        {
            config.TargetSpectralDimension = AutoTuneTargetDimension;
            config.SpectralDimensionTolerance = AutoTuneDimensionTolerance;
            config.UseHybridSpectralComputation = AutoTuneUseHybridSpectral;
            config.EnableVacuumEnergyManagement = AutoTuneManageVacuumEnergy;
            config.AllowEmergencyEnergyInjection = AutoTuneAllowEnergyInjection;
            config.EnergyRecyclingRate = AutoTuneEnergyRecyclingRate;
            config.GravityAdjustmentRate = AutoTuneGravityAdjustmentRate;
            config.ExplorationProbability = AutoTuneExplorationProb;
        }
    }

    /// <summary>
    /// Live configuration that can be modified during simulation run.
    /// Thread-safe: UI writes, calculation thread reads.
    /// </summary>
    public LiveConfigData LiveConfig { get; } = new();

    // === Graph Health Live Config (from UI NumericUpDown controls) ===
    // These mirror PhysicsConstants but can be adjusted via UI at runtime
    // Note: double reads/writes are atomic on x64, no volatile needed
    public class GraphHealthConfig
    {
        public double GiantClusterThreshold = PhysicsConstants.GiantClusterThreshold;
        public double EmergencyGiantClusterThreshold = PhysicsConstants.EmergencyGiantClusterThreshold;
        public double GiantClusterDecoherenceRate = PhysicsConstants.GiantClusterDecoherenceRate;
        public double MaxDecoherenceEdgesFraction = PhysicsConstants.MaxDecoherenceEdgesFraction;
        public double CriticalSpectralDimension = PhysicsConstants.CriticalSpectralDimension;
        public double WarningSpectralDimension = PhysicsConstants.WarningSpectralDimension;
    }

    /// <summary>
    /// Live Graph Health configuration (updated from UI controls).
    /// Thread-safe: UI writes, calculation thread reads.
    /// </summary>
    public GraphHealthConfig GraphHealthLive { get; } = new();

    // === RQ-Hypothesis Experimental Flags (runtime-adjustable) ===
    /// <summary>
    /// Thread-safe container for RQ-Hypothesis experimental flags.
    /// These control various physics behaviors per the RQ-Hypothesis checklist.
    /// UI thread writes, calculation thread reads.
    /// </summary>
    public class RQFlagsData
    {
        /// <summary>
        /// Use Hamiltonian (symplectic) gravity with geometry momenta.
        /// When TRUE: Second-order dynamics instead of gradient descent.
        /// </summary>
        public bool UseHamiltonianGravity = PhysicsConstants.UseHamiltonianGravity;

        /// <summary>
        /// Enable vacuum energy reservoir for pair creation.
        /// When TRUE: Track vacuum energy pool for Hawking-like effects.
        /// </summary>
        public bool EnableVacuumEnergyReservoir = PhysicsConstants.EnableVacuumEnergyReservoir;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST FIX #1: Natural Dimension Emergence
        /// When TRUE: Disable DimensionPenalty - allow d_S to emerge naturally.
        /// </summary>
        public bool EnableNaturalDimensionEmergence = PhysicsConstants.EnableNaturalDimensionEmergence;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST FIX #3: Topological Parity
        /// When TRUE: Use dynamic graph 2-coloring for staggered fermion parity.
        /// </summary>
        public bool EnableTopologicalParity = PhysicsConstants.EnableTopologicalParity;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST FIX #2: Lapse-Synchronized Geometry
        /// When TRUE: Geometry evolution dt is scaled by edge lapse function.
        /// </summary>
        public bool EnableLapseSynchronizedGeometry = PhysicsConstants.EnableLapseSynchronizedGeometry;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST FIX #5: Topology Energy Compensation
        /// When TRUE: Energy stored in fields on an edge is captured when edge is removed.
        /// </summary>
        public bool EnableTopologyEnergyCompensation = PhysicsConstants.EnableTopologyEnergyCompensation;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST FIX #4: Plaquette-Based Yang-Mills
        /// When TRUE: Use plaquette (triangle Wilson loop) definition for Yang-Mills.
        /// </summary>
        public bool EnablePlaquetteYangMills = PhysicsConstants.EnablePlaquetteYangMills;

        /// <summary>
        /// Enable symplectic (second-order) gauge evolution.
        /// When TRUE: Uses wave equation (photons propagate).
        /// When FALSE: Uses diffusion equation (fields decay).
        /// </summary>
        public bool EnableSymplecticGaugeEvolution = PhysicsConstants.EnableSymplecticGaugeEvolution;

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST FIX #6: Adaptive Topology Decoherence
        /// When TRUE: Topology flip probability depends on field amplitude.
        /// High-amplitude edges (strong correlations) flip less often.
        /// </summary>
        public bool EnableAdaptiveTopologyDecoherence = PhysicsConstants.EnableAdaptiveTopologyDecoherence;

        /// <summary>
        /// Enable strict Wilson loop protection for edge removal.
        /// </summary>
        public bool EnableWilsonLoopProtection = PhysicsConstants.EnableWilsonLoopProtection;

        /// <summary>
        /// Enable strict spectral action mode.
        /// </summary>
        public bool EnableSpectralActionMode = PhysicsConstants.SpectralActionConstants.EnableSpectralActionMode;

        /// <summary>
        /// Enable strict Wheeler-DeWitt mode.
        /// </summary>
        public bool EnableWheelerDeWittStrictMode = PhysicsConstants.WheelerDeWittConstants.EnableStrictMode;

        /// <summary>
        /// Prefer Ollivier-Ricci curvature.
        /// </summary>
        public bool PreferOllivierRicciCurvature = PhysicsConstants.PreferOllivierRicciCurvature;

        /// <summary>
        /// Timestamp of last update (for change detection)
        /// </summary>
        private long _lastUpdateTicks;
        public long LastUpdateTicks
        {
            get => Interlocked.Read(ref _lastUpdateTicks);
            private set => Interlocked.Exchange(ref _lastUpdateTicks, value);
        }

        /// <summary>
        /// Marks flags as updated
        /// </summary>
        public void MarkUpdated() => LastUpdateTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// RQ-Hypothesis experimental flags that can be modified during simulation run.
    /// Thread-safe: UI writes, calculation thread reads.
    /// </summary>
    public RQFlagsData RQFlags { get; } = new();

    public RqSimEngineApi.LiveConfigData RQChecklist { get; } = new RqSimEngineApi.LiveConfigData();

    /// <summary>
    /// Initializes LiveConfig from SimulationConfig at simulation start
    /// </summary>
    public void InitializeLiveConfig(SimulationConfig config)
    {
        LiveConfig.GravitationalCoupling = config.GravitationalCoupling;
        LiveConfig.VacuumEnergyScale = config.VacuumEnergyScale;
        LiveConfig.AnnealingCoolingRate = config.AnnealingCoolingRate;
        LiveConfig.DecoherenceRate = config.DecoherenceRate;
        LiveConfig.HotStartTemperature = config.HotStartTemperature;
        LiveConfig.InitialEdgeProb = config.InitialEdgeProb;
        LiveConfig.TargetDegree = config.TargetDegree;
        LiveConfig.InitialExcitedProb = config.InitialExcitedProb;
        LiveConfig.LambdaState = config.LambdaState;
        LiveConfig.Temperature = config.Temperature;
        LiveConfig.EdgeTrialProb = config.EdgeTrialProbability;
        LiveConfig.MeasurementThreshold = config.MeasurementThreshold;
        LiveConfig.FractalLevels = config.FractalLevels;
        LiveConfig.FractalBranchFactor = config.FractalBranchFactor;
        LiveConfig.MarkUpdated();
    }
}
