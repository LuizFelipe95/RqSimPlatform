using RqSimForms.Forms.Interfaces;
using RQSimulation;
using RQSimulation.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RqSimUI.Forms.PartialForms.SettingsConfig.Interfaces;

/// <summary>
/// Manages physics settings synchronization between UI, configuration, and running simulation.
/// Handles save/load on startup/exit and runtime parameter application.
/// </summary>
public class PhysicsSettingsManager
{

    private RqSimForms.Forms.Interfaces.RqSimEngineApi _simApi = new();
    private PhysicsSettingsConfig _currentConfig;
    private bool _isDirty;

    /// <summary>
    /// Event raised when settings are applied to the simulation.
    /// </summary>
    public event EventHandler<SettingsAppliedEventArgs>? SettingsApplied;

    /// <summary>
    /// Event raised when a parameter cannot be changed at runtime.
    /// </summary>
    public event EventHandler<NonHotSwappableParameterEventArgs>? NonHotSwappableParameterChanged;

    /// <summary>
    /// Gets whether there are unsaved changes.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    public PhysicsSettingsConfig CurrentConfig => _currentConfig;

    public PhysicsSettingsManager(RqSimForms.Forms.Interfaces.RqSimEngineApi simApi)
    {
        _simApi = simApi ?? throw new ArgumentNullException(nameof(simApi));
        _currentConfig = PhysicsSettingsConfig.CreateDefault();
    }

    /// <summary>
    /// Loads settings from disk or creates defaults.
    /// Call this on application startup.
    /// </summary>
    public void LoadSettings()
    {
        _currentConfig = PhysicsSettingsSerializer.LoadOrCreateDefault();
        ApplyToLiveConfig(_currentConfig);
        ApplyToRQFlags(_currentConfig);
        _isDirty = false;
    }

    /// <summary>
    /// Saves current settings to disk.
    /// Call this on application exit.
    /// </summary>
    public void SaveSettings()
    {
        CaptureFromLiveConfig(_currentConfig);
        CaptureFromRQFlags(_currentConfig);
        PhysicsSettingsSerializer.Save(_currentConfig);
        _isDirty = false;
    }

    /// <summary>
    /// Applies settings to the running simulation.
    /// Returns list of parameters that couldn't be changed at runtime.
    /// </summary>
    public IReadOnlyList<string> ApplyToSimulation(bool isSimulationRunning)
    {
        var nonHotSwappable = new List<string>();

        // Apply hot-swappable parameters
        ApplyToLiveConfig(_currentConfig);
        ApplyToRQFlags(_currentConfig);

        // Check for non-hot-swappable changes during runtime
        if (isSimulationRunning)
        {
            foreach (var param in PhysicsSettingsConfig.NonHotSwappableParameters)
            {
                nonHotSwappable.Add(param);
            }
        }

        // Notify listeners
        SettingsApplied?.Invoke(this, new SettingsAppliedEventArgs(
            hotSwappableCount: GetHotSwappableParameterCount(),
            nonHotSwappableCount: nonHotSwappable.Count));

        if (nonHotSwappable.Count > 0)
        {
            NonHotSwappableParameterChanged?.Invoke(this,
                new NonHotSwappableParameterEventArgs(nonHotSwappable));
        }

        return nonHotSwappable;
    }

    /// <summary>
    /// Captures current UI/LiveConfig state into the configuration.
    /// </summary>
    public void CaptureCurrentState()
    {
        CaptureFromLiveConfig(_currentConfig);
        CaptureFromRQFlags(_currentConfig);
        _isDirty = true;
    }

    /// <summary>
    /// Applies configuration to RqSimEngineApi.LiveConfig.
    /// </summary>
    private void ApplyToLiveConfig(PhysicsSettingsConfig config)
    {
        var liveConfig = _simApi.LiveConfig;

        // Simulation parameters
        liveConfig.TargetDegree = config.TargetDegree;
        liveConfig.InitialEdgeProb = config.InitialEdgeProb;
        liveConfig.InitialExcitedProb = config.InitialExcitedProb;
        liveConfig.FractalLevels = config.FractalLevels;
        liveConfig.FractalBranchFactor = config.FractalBranchFactor;

        // Physics constants
        liveConfig.GravitationalCoupling = config.GravitationalCoupling;
        liveConfig.VacuumEnergyScale = config.VacuumEnergyScale;
        liveConfig.AnnealingCoolingRate = config.AnnealingCoolingRate;
        liveConfig.DecoherenceRate = config.DecoherenceRate;
        liveConfig.HotStartTemperature = config.HotStartTemperature;
        liveConfig.AdaptiveThresholdSigma = config.AdaptiveThresholdSigma;
        liveConfig.WarmupDuration = config.WarmupDuration;
        liveConfig.GravityTransitionDuration = config.GravityTransitionDuration;
        liveConfig.LambdaState = config.LambdaState;
        liveConfig.Temperature = config.Temperature;
        liveConfig.EdgeTrialProb = config.EdgeTrialProb;
        liveConfig.MeasurementThreshold = config.MeasurementThreshold;

        // RQ Checklist
        liveConfig.EdgeWeightQuantum = config.EdgeWeightQuantum;
        liveConfig.RngStepCost = config.RngStepCost;
        liveConfig.EdgeCreationCost = config.EdgeCreationCost;
        liveConfig.InitialVacuumEnergy = config.InitialVacuumEnergy;

        // Advanced physics
        liveConfig.LapseFunctionAlpha = config.LapseFunctionAlpha;
        liveConfig.TimeDilationAlpha = config.TimeDilationAlpha;
        liveConfig.WilsonParameter = config.WilsonParameter;
        liveConfig.TopologyDecoherenceInterval = config.TopologyDecoherenceInterval;
        liveConfig.TopologyDecoherenceTemperature = config.TopologyDecoherenceTemperature;
        liveConfig.GaugeTolerance = config.GaugeTolerance;
        liveConfig.MaxRemovableFlux = config.MaxRemovableFlux;
        liveConfig.GeometryInertiaMass = config.GeometryInertiaMass;
        liveConfig.GaugeFieldDamping = config.GaugeFieldDamping;
        liveConfig.PairCreationMassThreshold = config.PairCreationMassThreshold;
        liveConfig.PairCreationEnergy = config.PairCreationEnergy;

        // Spectral action
        liveConfig.SpectralLambdaCutoff = config.SpectralLambdaCutoff;
        liveConfig.SpectralTargetDimension = config.SpectralTargetDimension;
        liveConfig.SpectralDimensionPotentialStrength = config.SpectralDimensionPotentialStrength;

        // MCMC Metropolis-Hastings
        liveConfig.McmcBeta = config.McmcBeta;
        liveConfig.McmcStepsPerCall = config.McmcStepsPerCall;
        liveConfig.McmcWeightPerturbation = config.McmcWeightPerturbation;

        // Sinkhorn Ollivier-Ricci
        liveConfig.SinkhornIterations = config.SinkhornIterations;
        liveConfig.SinkhornEpsilon = config.SinkhornEpsilon;
        liveConfig.SinkhornConvergenceThreshold = config.SinkhornConvergenceThreshold;
        liveConfig.LazyWalkAlpha = config.LazyWalkAlpha;

        // Auto-tuning
        liveConfig.AutoTuneTargetDimension = config.AutoTuneTargetDimension;
        liveConfig.AutoTuneDimensionTolerance = config.AutoTuneDimensionTolerance;
        liveConfig.AutoTuneUseHybridSpectral = config.AutoTuneUseHybridSpectral;
        liveConfig.AutoTuneManageVacuumEnergy = config.AutoTuneManageVacuumEnergy;
        liveConfig.AutoTuneAllowEnergyInjection = config.AutoTuneAllowEnergyInjection;
        liveConfig.AutoTuneEnergyRecyclingRate = config.AutoTuneEnergyRecyclingRate;
        liveConfig.AutoTuneGravityAdjustmentRate = config.AutoTuneGravityAdjustmentRate;
        liveConfig.AutoTuneExplorationProb = config.AutoTuneExplorationProb;

        liveConfig.MarkUpdated();

        // Graph health
        var graphHealth = _simApi.GraphHealthLive;
        graphHealth.GiantClusterThreshold = config.GiantClusterThreshold;
        graphHealth.EmergencyGiantClusterThreshold = config.EmergencyGiantClusterThreshold;
        graphHealth.GiantClusterDecoherenceRate = config.GiantClusterDecoherenceRate;
        graphHealth.MaxDecoherenceEdgesFraction = config.MaxDecoherenceEdgesFraction;
        graphHealth.CriticalSpectralDimension = config.CriticalSpectralDimension;
        graphHealth.WarningSpectralDimension = config.WarningSpectralDimension;
    }

    /// <summary>
    /// Applies configuration to RqSimEngineApi.RQFlags.
    /// </summary>
    private void ApplyToRQFlags(PhysicsSettingsConfig config)
    {
        var flags = _simApi.RQFlags;

        flags.UseHamiltonianGravity = config.UseHamiltonianGravity;
        flags.EnableVacuumEnergyReservoir = config.EnableVacuumEnergyReservoir;
        flags.EnableNaturalDimensionEmergence = config.EnableNaturalDimensionEmergence;
        flags.EnableTopologicalParity = config.EnableTopologicalParity;
        flags.EnableLapseSynchronizedGeometry = config.EnableLapseSynchronizedGeometry;
        flags.EnableTopologyEnergyCompensation = config.EnableTopologyEnergyCompensation;
        flags.EnablePlaquetteYangMills = config.EnablePlaquetteYangMills;
        flags.EnableSymplecticGaugeEvolution = config.EnableSymplecticGaugeEvolution;
        flags.EnableAdaptiveTopologyDecoherence = config.EnableAdaptiveTopologyDecoherence;
        flags.EnableWilsonLoopProtection = config.EnableWilsonLoopProtection;
        flags.EnableSpectralActionMode = config.EnableSpectralActionMode;
        flags.EnableWheelerDeWittStrictMode = config.EnableWheelerDeWittStrictMode;
        flags.PreferOllivierRicciCurvature = config.PreferOllivierRicciCurvature;

        flags.MarkUpdated();
    }

    /// <summary>
    /// Captures LiveConfig values into configuration.
    /// </summary>
    private void CaptureFromLiveConfig(PhysicsSettingsConfig config)
    {
        var liveConfig = _simApi.LiveConfig;

        config.TargetDegree = liveConfig.TargetDegree;
        config.InitialEdgeProb = liveConfig.InitialEdgeProb;
        config.InitialExcitedProb = liveConfig.InitialExcitedProb;
        config.FractalLevels = liveConfig.FractalLevels;
        config.FractalBranchFactor = liveConfig.FractalBranchFactor;

        config.GravitationalCoupling = liveConfig.GravitationalCoupling;
        config.VacuumEnergyScale = liveConfig.VacuumEnergyScale;
        config.AnnealingCoolingRate = liveConfig.AnnealingCoolingRate;
        config.DecoherenceRate = liveConfig.DecoherenceRate;
        config.HotStartTemperature = liveConfig.HotStartTemperature;
        config.AdaptiveThresholdSigma = liveConfig.AdaptiveThresholdSigma;
        config.WarmupDuration = (int)liveConfig.WarmupDuration;
        config.GravityTransitionDuration = (int)liveConfig.GravityTransitionDuration;
        config.LambdaState = liveConfig.LambdaState;
        config.Temperature = liveConfig.Temperature;
        config.EdgeTrialProb = liveConfig.EdgeTrialProb;
        config.MeasurementThreshold = liveConfig.MeasurementThreshold;

        config.EdgeWeightQuantum = liveConfig.EdgeWeightQuantum;
        config.RngStepCost = liveConfig.RngStepCost;
        config.EdgeCreationCost = liveConfig.EdgeCreationCost;
        config.InitialVacuumEnergy = liveConfig.InitialVacuumEnergy;

        config.LapseFunctionAlpha = liveConfig.LapseFunctionAlpha;
        config.TimeDilationAlpha = liveConfig.TimeDilationAlpha;
        config.WilsonParameter = liveConfig.WilsonParameter;
        config.TopologyDecoherenceInterval = liveConfig.TopologyDecoherenceInterval;
        config.TopologyDecoherenceTemperature = liveConfig.TopologyDecoherenceTemperature;
        config.GaugeTolerance = liveConfig.GaugeTolerance;
        config.MaxRemovableFlux = liveConfig.MaxRemovableFlux;
        config.GeometryInertiaMass = liveConfig.GeometryInertiaMass;
        config.GaugeFieldDamping = liveConfig.GaugeFieldDamping;
        config.PairCreationMassThreshold = liveConfig.PairCreationMassThreshold;
        config.PairCreationEnergy = liveConfig.PairCreationEnergy;

        config.SpectralLambdaCutoff = liveConfig.SpectralLambdaCutoff;
        config.SpectralTargetDimension = liveConfig.SpectralTargetDimension;
        config.SpectralDimensionPotentialStrength = liveConfig.SpectralDimensionPotentialStrength;

        // MCMC Metropolis-Hastings
        config.McmcBeta = liveConfig.McmcBeta;
        config.McmcStepsPerCall = liveConfig.McmcStepsPerCall;
        config.McmcWeightPerturbation = liveConfig.McmcWeightPerturbation;

        // Sinkhorn Ollivier-Ricci
        config.SinkhornIterations = liveConfig.SinkhornIterations;
        config.SinkhornEpsilon = liveConfig.SinkhornEpsilon;
        config.SinkhornConvergenceThreshold = liveConfig.SinkhornConvergenceThreshold;
        config.LazyWalkAlpha = liveConfig.LazyWalkAlpha;

        config.AutoTuneTargetDimension = liveConfig.AutoTuneTargetDimension;
        config.AutoTuneDimensionTolerance = liveConfig.AutoTuneDimensionTolerance;
        config.AutoTuneUseHybridSpectral = liveConfig.AutoTuneUseHybridSpectral;
        config.AutoTuneManageVacuumEnergy = liveConfig.AutoTuneManageVacuumEnergy;
        config.AutoTuneAllowEnergyInjection = liveConfig.AutoTuneAllowEnergyInjection;
        config.AutoTuneEnergyRecyclingRate = liveConfig.AutoTuneEnergyRecyclingRate;
        config.AutoTuneGravityAdjustmentRate = liveConfig.AutoTuneGravityAdjustmentRate;
        config.AutoTuneExplorationProb = liveConfig.AutoTuneExplorationProb;

        // Mode flags - captured from SimApi directly
        config.AutoTuningEnabled = _simApi.AutoTuningEnabled;

        var graphHealth = _simApi.GraphHealthLive;
        config.GiantClusterThreshold = graphHealth.GiantClusterThreshold;
        config.EmergencyGiantClusterThreshold = graphHealth.EmergencyGiantClusterThreshold;
        config.GiantClusterDecoherenceRate = graphHealth.GiantClusterDecoherenceRate;
        config.MaxDecoherenceEdgesFraction = graphHealth.MaxDecoherenceEdgesFraction;
        config.CriticalSpectralDimension = graphHealth.CriticalSpectralDimension;
        config.WarningSpectralDimension = graphHealth.WarningSpectralDimension;
    }

    /// <summary>
    /// Captures RQFlags values into configuration.
    /// </summary>
    private void CaptureFromRQFlags(PhysicsSettingsConfig config)
    {
        var flags = _simApi.RQFlags;

        config.UseHamiltonianGravity = flags.UseHamiltonianGravity;
        config.EnableVacuumEnergyReservoir = flags.EnableVacuumEnergyReservoir;
        config.EnableNaturalDimensionEmergence = flags.EnableNaturalDimensionEmergence;
        config.EnableTopologicalParity = flags.EnableTopologicalParity;
        config.EnableLapseSynchronizedGeometry = flags.EnableLapseSynchronizedGeometry;
        config.EnableTopologyEnergyCompensation = flags.EnableTopologyEnergyCompensation;
        config.EnablePlaquetteYangMills = flags.EnablePlaquetteYangMills;
        config.EnableSymplecticGaugeEvolution = flags.EnableSymplecticGaugeEvolution;
        config.EnableAdaptiveTopologyDecoherence = flags.EnableAdaptiveTopologyDecoherence;
        config.EnableWilsonLoopProtection = flags.EnableWilsonLoopProtection;
        config.EnableSpectralActionMode = flags.EnableSpectralActionMode;
        config.EnableWheelerDeWittStrictMode = flags.EnableWheelerDeWittStrictMode;
        config.PreferOllivierRicciCurvature = flags.PreferOllivierRicciCurvature;
    }

    /// <summary>
    /// Checks if a specific flag is used by any enabled pipeline module.
    /// </summary>
    public bool IsFlagUsedInPipeline(string flagName, PhysicsPipeline? pipeline)
    {
        if (pipeline is null) return false;

        // Map flag names to module categories/names
        var flagToModuleMap = new Dictionary<string, string[]>
        {
            ["UseHamiltonianGravity"] = ["Geometry Momenta", "Network Gravity"],
            ["EnableVacuumEnergyReservoir"] = ["Vacuum Fluctuations"],
            ["EnableNaturalDimensionEmergence"] = ["Spectral Geometry", "Unified Physics Step"],
            ["EnableTopologicalParity"] = ["Spinor Field"],
            ["EnableLapseSynchronizedGeometry"] = ["Relational Time", "Network Gravity"],
            ["EnableTopologyEnergyCompensation"] = ["Unified Physics Step"],
            ["EnablePlaquetteYangMills"] = ["Yang-Mills Gauge"],
            ["EnableSymplecticGaugeEvolution"] = ["Yang-Mills Gauge"],
            ["EnableAdaptiveTopologyDecoherence"] = ["Quantum Graphity"],
            ["EnableWilsonLoopProtection"] = ["Yang-Mills Gauge", "Unified Physics Step"],
            ["EnableSpectralActionMode"] = ["Spectral Geometry"],
            ["EnableWheelerDeWittStrictMode"] = ["Unified Physics Step"],
            ["PreferOllivierRicciCurvature"] = ["Network Gravity"]
        };

        if (!flagToModuleMap.TryGetValue(flagName, out var moduleNames))
            return false;

        return pipeline.Modules.Any(m =>
            m.IsEnabled && moduleNames.Contains(m.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets list of flags that are enabled but have no corresponding pipeline module.
    /// </summary>
    public IReadOnlyList<string> GetUnusedFlags(PhysicsPipeline? pipeline)
    {
        var unusedFlags = new List<string>();
        var flags = _simApi.RQFlags;

        void CheckFlag(string name, bool value)
        {
            if (value && !IsFlagUsedInPipeline(name, pipeline))
                unusedFlags.Add(name);
        }

        CheckFlag(nameof(flags.UseHamiltonianGravity), flags.UseHamiltonianGravity);
        CheckFlag(nameof(flags.EnableVacuumEnergyReservoir), flags.EnableVacuumEnergyReservoir);
        CheckFlag(nameof(flags.EnableNaturalDimensionEmergence), flags.EnableNaturalDimensionEmergence);
        CheckFlag(nameof(flags.EnableTopologicalParity), flags.EnableTopologicalParity);
        CheckFlag(nameof(flags.EnableLapseSynchronizedGeometry), flags.EnableLapseSynchronizedGeometry);
        CheckFlag(nameof(flags.EnableTopologyEnergyCompensation), flags.EnableTopologyEnergyCompensation);
        CheckFlag(nameof(flags.EnablePlaquetteYangMills), flags.EnablePlaquetteYangMills);
        CheckFlag(nameof(flags.EnableSymplecticGaugeEvolution), flags.EnableSymplecticGaugeEvolution);
        CheckFlag(nameof(flags.EnableAdaptiveTopologyDecoherence), flags.EnableAdaptiveTopologyDecoherence);
        CheckFlag(nameof(flags.EnableWilsonLoopProtection), flags.EnableWilsonLoopProtection);
        CheckFlag(nameof(flags.EnableSpectralActionMode), flags.EnableSpectralActionMode);
        CheckFlag(nameof(flags.EnableWheelerDeWittStrictMode), flags.EnableWheelerDeWittStrictMode);
        CheckFlag(nameof(flags.PreferOllivierRicciCurvature), flags.PreferOllivierRicciCurvature);

        return unusedFlags;
    }

    private static int GetHotSwappableParameterCount()
    {
        // Total parameters minus non-hot-swappable
        return 80 - PhysicsSettingsConfig.NonHotSwappableParameters.Count;
    }
}

/// <summary>
/// Event args for settings applied event.
/// </summary>
public class SettingsAppliedEventArgs : EventArgs
{
    public int HotSwappableCount { get; }
    public int NonHotSwappableCount { get; }

    public SettingsAppliedEventArgs(int hotSwappableCount, int nonHotSwappableCount)
    {
        HotSwappableCount = hotSwappableCount;
        NonHotSwappableCount = nonHotSwappableCount;
    }
}

/// <summary>
/// Event args for non-hot-swappable parameter change.
/// </summary>
public class NonHotSwappableParameterEventArgs : EventArgs
{
    public IReadOnlyList<string> ParameterNames { get; }

    public NonHotSwappableParameterEventArgs(IReadOnlyList<string> parameterNames)
    {
        ParameterNames = parameterNames;
    }
}
