using RqSimEngineApi.Contracts;
using RqSimForms.Forms.Interfaces;

namespace RqSimForms;

/// <summary>
/// Partial class for Dynamic Physics Parameters UI integration.
/// Part of Phase 4 of uni-pipeline implementation.
/// Handles physics config, Lazy Walk Alpha slider, and Apply Physics button.
/// </summary>
partial class Form_Main_RqSim
{
    // === Dynamic Physics Fields ===

    /// <summary>
    /// Current physics settings configuration from UI.
    /// Used to convert to SimulationParameters for GPU.
    /// </summary>
    private PhysicsSettingsConfig? _currentPhysicsConfig;

    /// <summary>
    /// TrackBar for Lazy Walk Alpha parameter (Ollivier-Ricci curvature).
    /// Range: 0-100, maps to 0.0-1.0.
    /// </summary>
    private TrackBar? _trkLazyWalkAlpha;

    /// <summary>
    /// Label for Lazy Walk Alpha value display.
    /// </summary>
    private Label? _lblLazyWalkAlphaValue;

    /// <summary>
    /// Button to apply physics parameters to running simulation.
    /// </summary>
    private Button? _btnApplyPhysics;

    /// <summary>
    /// Status label for physics parameter application.
    /// </summary>
    private Label? _lblPhysicsApplyStatus;

    /// <summary>
    /// Initializes dynamic physics parameter controls.
    /// Call this after form initialization.
    /// </summary>
    private void InitializeDynamicPhysicsControls()
    {
        // Load or create default physics config
        _currentPhysicsConfig = PhysicsSettingsConfig.CreateDefault();

        // Initialize Lazy Walk Alpha slider
        if (_trkLazyWalkAlpha is not null)
        {
            _trkLazyWalkAlpha.Minimum = 0;
            _trkLazyWalkAlpha.Maximum = 100;
            _trkLazyWalkAlpha.Value = 10; // Default 0.1
            _trkLazyWalkAlpha.ValueChanged += TrkLazyWalkAlpha_ValueChanged;
        }

        if (_lblLazyWalkAlphaValue is not null)
        {
            _lblLazyWalkAlphaValue.Text = "0.10";
        }

        // Wire Apply Physics button
        if (_btnApplyPhysics is not null)
        {
            _btnApplyPhysics.Click += BtnApplyPhysics_Click;
        }
    }

    /// <summary>
    /// Handler for Lazy Walk Alpha slider changes.
    /// </summary>
    private void TrkLazyWalkAlpha_ValueChanged(object? sender, EventArgs e)
    {
        if (_trkLazyWalkAlpha is null) return;

        double value = _trkLazyWalkAlpha.Value / 100.0;

        if (_lblLazyWalkAlphaValue is not null)
        {
            _lblLazyWalkAlphaValue.Text = value.ToString("F2");
        }
    }

    /// <summary>
    /// Handler for Apply Physics button click.
    /// </summary>
    private void BtnApplyPhysics_Click(object? sender, EventArgs e)
    {
        ApplyPhysicsParametersToPipeline();
    }

    /// <summary>
    /// Applies current physics parameters to the pipeline.
    /// Called from Apply button or programmatically.
    /// </summary>
    private void ApplyPhysicsParametersToPipeline()
    {
        try
        {
            if (_simApi is null)
            {
                UpdatePhysicsApplyStatus("No simulation API available", isError: true);
                return;
            }

            // Sync _currentPhysicsConfig from UI before conversion to GPU parameters
            SyncPhysicsConfigFromUI();

            // Get parameters from UI
            SimulationParameters parameters = GetCurrentPhysicsParametersFromUI();

            // Apply to pipeline
            _simApi.UpdatePhysicsParameters(in parameters);

            // Update status
            UpdatePhysicsApplyStatus($"Applied: G={parameters.GravitationalCoupling:F4}, α={parameters.LazyWalkAlpha:F2}");

            AppendSysConsole($"[DynamicPhysics] Parameters applied to pipeline\n");
        }
        catch (Exception ex)
        {
            UpdatePhysicsApplyStatus($"Error: {ex.Message}", isError: true);
            AppendSysConsole($"[DynamicPhysics] Error applying parameters: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Syncs _currentPhysicsConfig fields from UI NumericUpDown controls
    /// so that ToGpuParameters() reflects the actual user-set values.
    /// </summary>
    private void SyncPhysicsConfigFromUI()
    {
        if (_currentPhysicsConfig is null)
            return;

        // Core physics constants (from grpSimParams / grpPhysicsConstants)
        _currentPhysicsConfig.GravitationalCoupling = (double)numGravitationalCoupling.Value;
        _currentPhysicsConfig.VacuumEnergyScale = (double)numVacuumEnergyScale.Value;
        _currentPhysicsConfig.DecoherenceRate = (double)numDecoherenceRate.Value;
        _currentPhysicsConfig.Temperature = (double)numTemperature.Value;
        _currentPhysicsConfig.LambdaState = (double)numLambdaState.Value;
        _currentPhysicsConfig.EdgeTrialProb = (double)numEdgeTrialProb.Value;
        _currentPhysicsConfig.MeasurementThreshold = (double)numMeasurementThreshold.Value;
        _currentPhysicsConfig.HotStartTemperature = (double)numHotStartTemperature.Value;
        _currentPhysicsConfig.WarmupDuration = (int)numWarmupDuration.Value;
        _currentPhysicsConfig.GravityTransitionDuration = (int)numGravityTransitionDuration.Value;
        _currentPhysicsConfig.InitialEdgeProb = (double)numInitialEdgeProb.Value;
        _currentPhysicsConfig.AnnealingCoolingRate = (double)numAdaptiveThresholdSigma.Value;

        // Advanced physics (from dynamically-created controls in Form_Main_SettingsSync.cs)
        if (numLapseFunctionAlpha is not null)
            _currentPhysicsConfig.LapseFunctionAlpha = (double)numLapseFunctionAlpha.Value;
        if (numTimeDilationAlpha is not null)
            _currentPhysicsConfig.TimeDilationAlpha = (double)numTimeDilationAlpha.Value;
        if (numWilsonParameter is not null)
            _currentPhysicsConfig.WilsonParameter = (double)numWilsonParameter.Value;
        if (numTopologyDecoherenceInterval is not null)
            _currentPhysicsConfig.TopologyDecoherenceInterval = (int)numTopologyDecoherenceInterval.Value;
        if (numTopologyDecoherenceTemperature is not null)
            _currentPhysicsConfig.TopologyDecoherenceTemperature = (double)numTopologyDecoherenceTemperature.Value;
        if (numGaugeTolerance is not null)
            _currentPhysicsConfig.GaugeTolerance = (double)numGaugeTolerance.Value;
        if (numMaxRemovableFlux is not null)
            _currentPhysicsConfig.MaxRemovableFlux = (double)numMaxRemovableFlux.Value;
        if (numGeometryInertiaMass is not null)
            _currentPhysicsConfig.GeometryInertiaMass = (double)numGeometryInertiaMass.Value;
        if (numGaugeFieldDamping is not null)
            _currentPhysicsConfig.GaugeFieldDamping = (double)numGaugeFieldDamping.Value;
        if (numPairCreationMassThreshold is not null)
            _currentPhysicsConfig.PairCreationMassThreshold = (double)numPairCreationMassThreshold.Value;
        if (numPairCreationEnergy is not null)
            _currentPhysicsConfig.PairCreationEnergy = (double)numPairCreationEnergy.Value;

        // Spectral action params
        if (numSpectralLambdaCutoff is not null)
            _currentPhysicsConfig.SpectralLambdaCutoff = (double)numSpectralLambdaCutoff.Value;
        if (numSpectralTargetDimension is not null)
            _currentPhysicsConfig.SpectralTargetDimension = (double)numSpectralTargetDimension.Value;
        if (numSpectralDimensionPotentialStrength is not null)
            _currentPhysicsConfig.SpectralDimensionPotentialStrength = (double)numSpectralDimensionPotentialStrength.Value;

        // MCMC Sampler params
        if (numMcmcBeta is not null)
            _currentPhysicsConfig.McmcBeta = (double)numMcmcBeta.Value;
        if (numMcmcStepsPerCall is not null)
            _currentPhysicsConfig.McmcStepsPerCall = (int)numMcmcStepsPerCall.Value;
        if (numMcmcWeightPerturbation is not null)
            _currentPhysicsConfig.McmcWeightPerturbation = (double)numMcmcWeightPerturbation.Value;

        // Sinkhorn Ollivier-Ricci params
        if (numSinkhornIterations is not null)
            _currentPhysicsConfig.SinkhornIterations = (int)numSinkhornIterations.Value;
        if (numSinkhornEpsilon is not null)
            _currentPhysicsConfig.SinkhornEpsilon = (double)numSinkhornEpsilon.Value;
        if (numSinkhornConvergenceThreshold is not null)
            _currentPhysicsConfig.SinkhornConvergenceThreshold = (double)numSinkhornConvergenceThreshold.Value;
    }

    /// <summary>
    /// Updates the physics apply status label.
    /// </summary>
    private void UpdatePhysicsApplyStatus(string message, bool isError = false)
    {
        if (_lblPhysicsApplyStatus is null) return;

        _lblPhysicsApplyStatus.Text = message;
        _lblPhysicsApplyStatus.ForeColor = isError
            ? System.Drawing.Color.Red
            : System.Drawing.Color.DarkGreen;
    }
}
