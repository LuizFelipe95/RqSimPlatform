using System.Diagnostics;
using RqSimForms.ProcessesDispatcher;
using RqSimPlatform.Contracts;
using RQSimulation;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main — unified settings capture from UI controls
/// and persistence to <c>simulation_settings.json</c>.
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// Builds a complete <see cref="ServerModeSettingsDto"/> from current UI control values.
    /// Must be called on the UI thread.
    /// </summary>
    private ServerModeSettingsDto BuildServerModeSettingsFromUI()
    {
        return new ServerModeSettingsDto
        {
            // === Core Simulation ===
            NodeCount = (int)numNodeCount.Value,
            TargetDegree = (int)numTargetDegree.Value,
            Seed = 42,
            Temperature = (double)numTemperature.Value,
            TotalSteps = (int)numTotalSteps.Value,

            // === Extended Physics ===
            InitialExcitedProb = (double)numInitialExcitedProb.Value,
            LambdaState = (double)numLambdaState.Value,
            EdgeTrialProb = (double)numEdgeTrialProb.Value,
            InitialEdgeProb = (double)numInitialEdgeProb.Value,
            GravitationalCoupling = (double)numGravitationalCoupling.Value,
            VacuumEnergyScale = (double)numVacuumEnergyScale.Value,
            DecoherenceRate = (double)numDecoherenceRate.Value,
            HotStartTemperature = (double)numHotStartTemperature.Value,
            WarmupDuration = (int)numWarmupDuration.Value,
            GravityTransitionDuration = (double)numGravityTransitionDuration.Value,

            // === Lapse & Wilson ===
            LapseFunctionAlpha = (double)numLapseFunctionAlpha.Value,
            WilsonParameter = (double)numWilsonParameter.Value,

            // === Geometry Inertia ===
            GeometryInertiaMass = (double)numGeometryInertiaMass.Value,
            GaugeFieldDamping = (double)numGaugeFieldDamping.Value,

            // === Topology Decoherence ===
            TopologyDecoherenceInterval = (int)numTopologyDecoherenceInterval.Value,
            TopologyDecoherenceTemperature = (double)numTopologyDecoherenceTemperature.Value,

            // === Gauge Protection ===
            GaugeTolerance = (double)numGaugeTolerance.Value,
            MaxRemovableFlux = (double)numMaxRemovableFlux.Value,

            // === Hawking Radiation ===
            PairCreationMassThreshold = (double)numPairCreationMassThreshold.Value,
            PairCreationEnergy = (double)numPairCreationEnergy.Value,

            // === RQ-Hypothesis Checklist ===
            EdgeWeightQuantum = (double)numEdgeWeightQuantum.Value,
            RngStepCost = (double)numRngStepCost.Value,
            EdgeCreationCost = (double)numEdgeCreationCost.Value,
            InitialVacuumEnergy = (double)numInitialVacuumEnergy.Value,

            // === Spectral Action ===
            SpectralLambdaCutoff = (double)numSpectralLambdaCutoff.Value,
            SpectralTargetDimension = (double)numSpectralTargetDimension.Value,
            SpectralDimensionPotentialStrength = (double)numSpectralDimensionPotentialStrength.Value,

            // === Graph Health ===
            GiantClusterThreshold = (double)numGiantClusterThreshold.Value,
            EmergencyGiantClusterThreshold = (double)numEmergencyGiantClusterThreshold.Value,
            GiantClusterDecoherenceRate = (double)numGiantClusterDecoherenceRate.Value,
            MaxDecoherenceEdgesFraction = (double)numMaxDecoherenceEdgesFraction.Value,
            CriticalSpectralDimension = (double)numCriticalSpectralDimension.Value,
            WarningSpectralDimension = (double)numWarningSpectralDimension.Value,

            // === RQ Experimental Flags ===
            EnableNaturalDimensionEmergence = chkEnableNaturalDimensionEmergence?.Checked ?? PhysicsConstants.EnableNaturalDimensionEmergence,
            EnableTopologicalParity = chkEnableTopologicalParity?.Checked ?? PhysicsConstants.EnableTopologicalParity,
            EnableLapseSynchronizedGeometry = chkEnableLapseSynchronizedGeometry?.Checked ?? PhysicsConstants.EnableLapseSynchronizedGeometry,
            EnableTopologyEnergyCompensation = chkEnableTopologyEnergyCompensation?.Checked ?? PhysicsConstants.EnableTopologyEnergyCompensation,
            EnablePlaquetteYangMills = chkEnablePlaquetteYangMills?.Checked ?? PhysicsConstants.EnablePlaquetteYangMills,
            EnableSymplecticGaugeEvolution = chkEnableSymplecticGaugeEvolution?.Checked ?? PhysicsConstants.EnableSymplecticGaugeEvolution,
            EnableAdaptiveTopologyDecoherence = chkEnableAdaptiveTopologyDecoherence?.Checked ?? PhysicsConstants.EnableAdaptiveTopologyDecoherence,
            PreferOllivierRicciCurvature = chkPreferOllivierRicciCurvature?.Checked ?? PhysicsConstants.PreferOllivierRicciCurvature,

            // === Pipeline Module Flags ===
            UseSpacetimePhysics = IsModuleEnabled("Spacetime Physics"),
            UseSpinorField = IsModuleEnabled("Spinor Field"),
            UseVacuumFluctuations = IsModuleEnabled("Vacuum Fluctuations"),
            UseBlackHolePhysics = IsModuleEnabled("Black Hole Physics"),
            UseYangMillsGauge = IsModuleEnabled("Yang-Mills Gauge"),
            UseEnhancedKleinGordon = IsModuleEnabled("Enhanced Klein-Gordon"),
            UseInternalTime = IsModuleEnabled("Internal Time"),
            UseRelationalTime = IsModuleEnabled("Relational Time"),
            UseSpectralGeometry = IsModuleEnabled("Spectral Geometry"),
            UseQuantumGraphity = IsModuleEnabled("Quantum Graphity"),
            UseMexicanHatPotential = IsModuleEnabled("Mexican Hat Potential"),
            UseGeometryMomenta = IsModuleEnabled("Geometry Momenta"),
            UseUnifiedPhysicsStep = IsModuleEnabled("Unified Physics Step"),
            EnforceGaugeConstraints = IsModuleEnabled("Enforce Gauge Constraints"),
            ValidateEnergyConservation = IsModuleEnabled("Validate Energy Conservation"),
            UseMcmc = IsModuleEnabled("MCMC Sampler (CPU)"),

            // === MCMC Metropolis-Hastings ===
            McmcBeta = (double)(numMcmcBeta?.Value ?? 1.0m),
            McmcStepsPerCall = (int)(numMcmcStepsPerCall?.Value ?? 10m),
            McmcWeightPerturbation = (double)(numMcmcWeightPerturbation?.Value ?? 0.1m),
            McmcMinWeight = 0.01,

            // === Sinkhorn Ollivier-Ricci ===
            SinkhornIterations = (int)(numSinkhornIterations?.Value ?? 50m),
            SinkhornEpsilon = (double)(numSinkhornEpsilon?.Value ?? 0.01m),
            SinkhornConvergenceThreshold = (double)(numSinkhornConvergenceThreshold?.Value ?? 0.000001m),
            LazyWalkAlpha = _trkLazyWalkAlpha is not null ? _trkLazyWalkAlpha.Value / 100.0 : 0.1
        };
    }

    /// <summary>
    /// Captures all current UI values into <see cref="ServerModeSettingsDto"/> and
    /// persists to <c>simulation_settings.json</c> + legacy fallback paths.
    /// Safe to call from any context — marshals to UI thread if needed.
    /// </summary>
    private void SaveUnifiedSettingsFile()
    {
        try
        {
            ServerModeSettingsDto settings;

            if (InvokeRequired)
            {
                settings = (ServerModeSettingsDto)Invoke(BuildServerModeSettingsFromUI);
            }
            else
            {
                settings = BuildServerModeSettingsFromUI();
            }

            UnifiedSettingsSerializer.Save(settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedSettings] Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a <see cref="ServerModeSettingsDto"/> to all UI controls.
    /// Sets numeric controls, RQ-hypothesis checkboxes, MCMC/Sinkhorn parameters,
    /// lazy walk trackbar, and pipeline module enable/disable flags.
    /// Must be called on the UI thread.
    /// </summary>
    private void ApplyServerModeSettingsToUI(ServerModeSettingsDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        SuspendControlEvents();

        try
        {
            // === Core Simulation ===
            SetNumericValueSafe(numNodeCount, dto.NodeCount);
            SetNumericValueSafe(numTargetDegree, dto.TargetDegree);
            SetNumericValueSafe(numTemperature, (decimal)dto.Temperature);
            SetNumericValueSafe(numTotalSteps, dto.TotalSteps);

            // === Extended Physics ===
            SetNumericValueSafe(numInitialExcitedProb, (decimal)dto.InitialExcitedProb);
            SetNumericValueSafe(numLambdaState, (decimal)dto.LambdaState);
            SetNumericValueSafe(numEdgeTrialProb, (decimal)dto.EdgeTrialProb);
            SetNumericValueSafe(numInitialEdgeProb, (decimal)dto.InitialEdgeProb);
            SetNumericValueSafe(numGravitationalCoupling, (decimal)dto.GravitationalCoupling);
            SetNumericValueSafe(numVacuumEnergyScale, (decimal)dto.VacuumEnergyScale);
            SetNumericValueSafe(numDecoherenceRate, (decimal)dto.DecoherenceRate);
            SetNumericValueSafe(numHotStartTemperature, (decimal)dto.HotStartTemperature);
            SetNumericValueSafe(numWarmupDuration, dto.WarmupDuration);
            SetNumericValueSafe(numGravityTransitionDuration, (decimal)dto.GravityTransitionDuration);

            // === Lapse & Wilson ===
            SetNumericValueSafe(numLapseFunctionAlpha, (decimal)dto.LapseFunctionAlpha);
            SetNumericValueSafe(numWilsonParameter, (decimal)dto.WilsonParameter);

            // === Geometry Inertia ===
            SetNumericValueSafe(numGeometryInertiaMass, (decimal)dto.GeometryInertiaMass);
            SetNumericValueSafe(numGaugeFieldDamping, (decimal)dto.GaugeFieldDamping);

            // === Topology Decoherence ===
            SetNumericValueSafe(numTopologyDecoherenceInterval, dto.TopologyDecoherenceInterval);
            SetNumericValueSafe(numTopologyDecoherenceTemperature, (decimal)dto.TopologyDecoherenceTemperature);

            // === Gauge Protection ===
            SetNumericValueSafe(numGaugeTolerance, (decimal)dto.GaugeTolerance);
            SetNumericValueSafe(numMaxRemovableFlux, (decimal)dto.MaxRemovableFlux);

            // === Hawking Radiation ===
            SetNumericValueSafe(numPairCreationMassThreshold, (decimal)dto.PairCreationMassThreshold);
            SetNumericValueSafe(numPairCreationEnergy, (decimal)dto.PairCreationEnergy);

            // === RQ-Hypothesis Checklist ===
            SetNumericValueSafe(numEdgeWeightQuantum, (decimal)dto.EdgeWeightQuantum);
            SetNumericValueSafe(numRngStepCost, (decimal)dto.RngStepCost);
            SetNumericValueSafe(numEdgeCreationCost, (decimal)dto.EdgeCreationCost);
            SetNumericValueSafe(numInitialVacuumEnergy, (decimal)dto.InitialVacuumEnergy);

            // === Spectral Action ===
            SetNumericValueSafe(numSpectralLambdaCutoff, (decimal)dto.SpectralLambdaCutoff);
            SetNumericValueSafe(numSpectralTargetDimension, (decimal)dto.SpectralTargetDimension);
            SetNumericValueSafe(numSpectralDimensionPotentialStrength, (decimal)dto.SpectralDimensionPotentialStrength);

            // === Graph Health ===
            SetNumericValueSafe(numGiantClusterThreshold, (decimal)dto.GiantClusterThreshold);
            SetNumericValueSafe(numEmergencyGiantClusterThreshold, (decimal)dto.EmergencyGiantClusterThreshold);
            SetNumericValueSafe(numGiantClusterDecoherenceRate, (decimal)dto.GiantClusterDecoherenceRate);
            SetNumericValueSafe(numMaxDecoherenceEdgesFraction, (decimal)dto.MaxDecoherenceEdgesFraction);
            SetNumericValueSafe(numCriticalSpectralDimension, (decimal)dto.CriticalSpectralDimension);
            SetNumericValueSafe(numWarningSpectralDimension, (decimal)dto.WarningSpectralDimension);

            // === RQ Experimental Flags (checkboxes) ===
            SetCheckboxSafe(chkEnableNaturalDimensionEmergence, dto.EnableNaturalDimensionEmergence);
            SetCheckboxSafe(chkEnableTopologicalParity, dto.EnableTopologicalParity);
            SetCheckboxSafe(chkEnableLapseSynchronizedGeometry, dto.EnableLapseSynchronizedGeometry);
            SetCheckboxSafe(chkEnableTopologyEnergyCompensation, dto.EnableTopologyEnergyCompensation);
            SetCheckboxSafe(chkEnablePlaquetteYangMills, dto.EnablePlaquetteYangMills);
            SetCheckboxSafe(chkEnableSymplecticGaugeEvolution, dto.EnableSymplecticGaugeEvolution);
            SetCheckboxSafe(chkEnableAdaptiveTopologyDecoherence, dto.EnableAdaptiveTopologyDecoherence);
            SetCheckboxSafe(chkPreferOllivierRicciCurvature, dto.PreferOllivierRicciCurvature);

            // === MCMC Metropolis-Hastings ===
            SetNumericValueSafe(numMcmcBeta, (decimal)dto.McmcBeta);
            SetNumericValueSafe(numMcmcStepsPerCall, dto.McmcStepsPerCall);
            SetNumericValueSafe(numMcmcWeightPerturbation, (decimal)dto.McmcWeightPerturbation);

            // === Sinkhorn Ollivier-Ricci ===
            SetNumericValueSafe(numSinkhornIterations, dto.SinkhornIterations);
            SetNumericValueSafe(numSinkhornEpsilon, (decimal)dto.SinkhornEpsilon);
            SetNumericValueSafe(numSinkhornConvergenceThreshold, (decimal)dto.SinkhornConvergenceThreshold);

            // === Lazy Walk Alpha (TrackBar: 0-100 maps to 0.0-1.0) ===
            if (_trkLazyWalkAlpha is not null)
            {
                int trackValue = (int)Math.Clamp(dto.LazyWalkAlpha * 100.0, 0, 100);
                _trkLazyWalkAlpha.Value = trackValue;
            }

            // === Pipeline Module Flags ===
            ApplyPipelineModuleFlags(dto);
        }
        finally
        {
            ResumeControlEvents();
        }
    }

    /// <summary>
    /// Sets pipeline module IsEnabled flags from the DTO.
    /// Only applies if the pipeline is initialized.
    /// </summary>
    private void ApplyPipelineModuleFlags(ServerModeSettingsDto dto)
    {
        var pipeline = _simApi?.Pipeline;
        if (pipeline is null)
            return;

        SetPipelineModule(pipeline, "Spacetime Physics", dto.UseSpacetimePhysics);
        SetPipelineModule(pipeline, "Spinor Field", dto.UseSpinorField);
        SetPipelineModule(pipeline, "Vacuum Fluctuations", dto.UseVacuumFluctuations);
        SetPipelineModule(pipeline, "Black Hole Physics", dto.UseBlackHolePhysics);
        SetPipelineModule(pipeline, "Yang-Mills Gauge", dto.UseYangMillsGauge);
        SetPipelineModule(pipeline, "Enhanced Klein-Gordon", dto.UseEnhancedKleinGordon);
        SetPipelineModule(pipeline, "Internal Time", dto.UseInternalTime);
        SetPipelineModule(pipeline, "Relational Time", dto.UseRelationalTime);
        SetPipelineModule(pipeline, "Spectral Geometry", dto.UseSpectralGeometry);
        SetPipelineModule(pipeline, "Quantum Graphity", dto.UseQuantumGraphity);
        SetPipelineModule(pipeline, "Mexican Hat Potential", dto.UseMexicanHatPotential);
        SetPipelineModule(pipeline, "Geometry Momenta", dto.UseGeometryMomenta);
        SetPipelineModule(pipeline, "Unified Physics Step", dto.UseUnifiedPhysicsStep);
        SetPipelineModule(pipeline, "Enforce Gauge Constraints", dto.EnforceGaugeConstraints);
        SetPipelineModule(pipeline, "Validate Energy Conservation", dto.ValidateEnergyConservation);
        SetPipelineModule(pipeline, "MCMC Sampler (CPU)", dto.UseMcmc);
    }

    private static void SetPipelineModule(RQSimulation.Core.Plugins.PhysicsPipeline pipeline, string moduleName, bool enabled)
    {
        var module = pipeline.GetModule(moduleName);
        if (module is not null)
        {
            module.IsEnabled = enabled;
        }
    }
}
