using RqSimForms.Forms.Interfaces;
using RQSimulation;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RqSimForms;

public partial class Form_Main_RqSim
{

    /// <summary>
    /// Connects ValueChanged handlers to all NumericUpDown controls in grpSimParams and grpPhysicsConstants
    /// for live parameter updates during simulation run.
    /// </summary>
    private void WireLiveParameterHandlers()
    {
        // grpPhysicsConstants - NumericUpDown controls
        numInitialEdgeProb.ValueChanged += OnLiveParameterChanged;
        numGravitationalCoupling.ValueChanged += OnLiveParameterChanged;
        numVacuumEnergyScale.ValueChanged += OnLiveParameterChanged;
        numDecoherenceRate.ValueChanged += OnLiveParameterChanged;
        numHotStartTemperature.ValueChanged += OnLiveParameterChanged;
        numAdaptiveThresholdSigma.ValueChanged += OnLiveParameterChanged;
        numWarmupDuration.ValueChanged += OnLiveParameterChanged;
        numGravityTransitionDuration.ValueChanged += OnLiveParameterChanged;

        // grpSimParams - NumericUpDown controls
        numNodeCount.ValueChanged += OnLiveParameterChanged;
        numTargetDegree.ValueChanged += OnLiveParameterChanged;
        numInitialExcitedProb.ValueChanged += OnLiveParameterChanged;
        numLambdaState.ValueChanged += OnLiveParameterChanged;
        numTemperature.ValueChanged += OnLiveParameterChanged;
        numEdgeTrialProb.ValueChanged += OnLiveParameterChanged;
        numMeasurementThreshold.ValueChanged += OnLiveParameterChanged;
        numTotalSteps.ValueChanged += OnLiveParameterChanged;
        numFractalLevels.ValueChanged += OnLiveParameterChanged;
        numFractalBranchFactor.ValueChanged += OnLiveParameterChanged;

        // RQ-Hypothesis checklist controls (live updates)
        numEdgeWeightQuantum.ValueChanged += OnRQChecklistParameterChanged;
        numRngStepCost.ValueChanged += OnRQChecklistParameterChanged;
        numEdgeCreationCost.ValueChanged += OnRQChecklistParameterChanged;
        numInitialVacuumEnergy.ValueChanged += OnRQChecklistParameterChanged;
    }

    /// <summary>
    /// Handler for live parameter changes - updates LiveConfig for running simulation.
    /// Thread-safe: writes to fields that calculation thread reads.
    /// </summary>
    private void OnLiveParameterChanged(object? sender, EventArgs e)
    {
        // Skip if controls not yet initialized
        if (numInitialEdgeProb is null) return;

        var liveConfig = _simApi.LiveConfig;

        // grpPhysicsConstants values
        liveConfig.InitialEdgeProb = (double)numInitialEdgeProb.Value;
        liveConfig.GravitationalCoupling = (double)numGravitationalCoupling.Value;
        liveConfig.VacuumEnergyScale = (double)numVacuumEnergyScale.Value;
        liveConfig.DecoherenceRate = (double)numDecoherenceRate.Value;
        liveConfig.HotStartTemperature = (double)numHotStartTemperature.Value;
        liveConfig.AdaptiveThresholdSigma = (double)numAdaptiveThresholdSigma.Value;
        liveConfig.WarmupDuration = (double)numWarmupDuration.Value;
        liveConfig.GravityTransitionDuration = (double)numGravityTransitionDuration.Value;

        // grpSimParams values
        liveConfig.TargetDegree = (int)numTargetDegree.Value;
        liveConfig.InitialExcitedProb = (double)numInitialExcitedProb.Value;
        liveConfig.LambdaState = (double)numLambdaState.Value;
        liveConfig.Temperature = (double)numTemperature.Value;
        liveConfig.EdgeTrialProb = (double)numEdgeTrialProb.Value;
        liveConfig.MeasurementThreshold = (double)numMeasurementThreshold.Value;
        liveConfig.FractalLevels = (int)numFractalLevels.Value;
        liveConfig.FractalBranchFactor = (int)numFractalBranchFactor.Value;

        // Advanced physics params (from dynamically-created controls)
        if (numLapseFunctionAlpha is not null)
            liveConfig.LapseFunctionAlpha = (double)numLapseFunctionAlpha.Value;
        if (numTimeDilationAlpha is not null)
            liveConfig.TimeDilationAlpha = (double)numTimeDilationAlpha.Value;
        if (numWilsonParameter is not null)
            liveConfig.WilsonParameter = (double)numWilsonParameter.Value;
        if (numTopologyDecoherenceInterval is not null)
            liveConfig.TopologyDecoherenceInterval = (int)numTopologyDecoherenceInterval.Value;
        if (numTopologyDecoherenceTemperature is not null)
            liveConfig.TopologyDecoherenceTemperature = (double)numTopologyDecoherenceTemperature.Value;
        if (numGaugeTolerance is not null)
            liveConfig.GaugeTolerance = (double)numGaugeTolerance.Value;
        if (numMaxRemovableFlux is not null)
            liveConfig.MaxRemovableFlux = (double)numMaxRemovableFlux.Value;
        if (numGeometryInertiaMass is not null)
            liveConfig.GeometryInertiaMass = (double)numGeometryInertiaMass.Value;
        if (numGaugeFieldDamping is not null)
            liveConfig.GaugeFieldDamping = (double)numGaugeFieldDamping.Value;
        if (numPairCreationMassThreshold is not null)
            liveConfig.PairCreationMassThreshold = (double)numPairCreationMassThreshold.Value;
        if (numPairCreationEnergy is not null)
            liveConfig.PairCreationEnergy = (double)numPairCreationEnergy.Value;
        if (numSpectralLambdaCutoff is not null)
            liveConfig.SpectralLambdaCutoff = (double)numSpectralLambdaCutoff.Value;
        if (numSpectralTargetDimension is not null)
            liveConfig.SpectralTargetDimension = (double)numSpectralTargetDimension.Value;
        if (numSpectralDimensionPotentialStrength is not null)
            liveConfig.SpectralDimensionPotentialStrength = (double)numSpectralDimensionPotentialStrength.Value;

        // Mirror RQ-Hypothesis checklist controls into simulation API RQChecklist (if initialized)
        if (numEdgeWeightQuantum is not null)
        {
            var rq = _simApi.RQChecklist;
            rq.EdgeWeightQuantum = (double)numEdgeWeightQuantum.Value;
            rq.RngStepCost = (double)numRngStepCost.Value;
            rq.EdgeCreationCost = (double)numEdgeCreationCost.Value;
            rq.InitialVacuumEnergy = (double)numInitialVacuumEnergy.Value;
            rq.MarkUpdated();
        }

        liveConfig.MarkUpdated();

        // Log if simulation is running
        if (_isModernRunning && sender is NumericUpDown num)
        {
            AppendSimConsole($"[Live] {num.Name}: {num.Value}\n");
        }
    }

    // В методе OnRQChecklistParameterChanged замените строки с _simApi.RQChecklist на _rqChecklist
    private void OnRQChecklistParameterChanged(object? sender, EventArgs e)
    {
        // Skip if controls not yet initialized
        if (numEdgeWeightQuantum is null) return;

        var rq = _simApi.RQChecklist;

        rq.EdgeWeightQuantum = (double)numEdgeWeightQuantum.Value;
        rq.RngStepCost = (double)numRngStepCost.Value;
        rq.EdgeCreationCost = (double)numEdgeCreationCost.Value;
        rq.InitialVacuumEnergy = (double)numInitialVacuumEnergy.Value;
        rq.MarkUpdated();
        if (_isModernRunning && sender is NumericUpDown num)
        {
            AppendSimConsole($"[RQ Checklist] {num.Name}: {num.Value} (live update applied)\n");
        }
    }

    /// <summary>
    /// Handler for Graph Health parameter changes.
    /// Updates GraphHealthLive in _simApi for auto-tuning to use.
    /// </summary>
    private void OnGraphHealthParameterChanged(object? sender, EventArgs e)
    {
        // Skip if controls not yet initialized
        if (numGiantClusterThreshold is null) return;

        // Update GraphHealthLive config for auto-tuning
        _simApi.GraphHealthLive.GiantClusterThreshold = (double)numGiantClusterThreshold.Value;
        _simApi.GraphHealthLive.EmergencyGiantClusterThreshold = (double)numEmergencyGiantClusterThreshold.Value;
        _simApi.GraphHealthLive.GiantClusterDecoherenceRate = (double)numGiantClusterDecoherenceRate.Value;
        _simApi.GraphHealthLive.MaxDecoherenceEdgesFraction = (double)numMaxDecoherenceEdgesFraction.Value;
        _simApi.GraphHealthLive.CriticalSpectralDimension = (double)numCriticalSpectralDimension.Value;
        _simApi.GraphHealthLive.WarningSpectralDimension = (double)numWarningSpectralDimension.Value;

        if (_isModernRunning && sender is NumericUpDown num)
        {
            AppendSimConsole($"[GraphHealth] {num.Name}: {num.Value} (live update applied)\n");
        }
    }

    /// <summary>
    /// Initializes Graph Health UI controls on the Settings tab.
    /// These controls allow runtime configuration of fragmentation detection
    /// and giant cluster decoherence parameters (RQ-Hypothesis compliance).
    /// </summary>
    private void InitializeGraphHealthControls()
    {
        // Expand tlpPhysicsConstants to add Graph Health section
        // Current row count is 10, we need 6 more rows (1 header + 6 params)
        int startRow = tlpPhysicsConstants.RowCount;
        tlpPhysicsConstants.RowCount = startRow + 7;

        // Add row styles for new rows
        for (int i = 0; i < 7; i++)
        {
            tlpPhysicsConstants.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        }

        // === Header Label ===
        var lblGraphHealthHeader = new Label
        {
            Text = "??? Graph Health (RQ) ???",
            AutoSize = true,
            ForeColor = Color.DarkBlue,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        tlpPhysicsConstants.Controls.Add(lblGraphHealthHeader, 0, startRow);
        tlpPhysicsConstants.SetColumnSpan(lblGraphHealthHeader, 2);

        // === Giant Cluster Threshold ===
        var lblGiantClusterThreshold = new Label
        {
            Text = "Giant Cluster (% of N):",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numGiantClusterThreshold = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.05m,
            Minimum = 0.10m,
            Maximum = 0.90m,
            Value = (decimal)PhysicsConstants.GiantClusterThreshold,
            Dock = DockStyle.Fill
        };
        numGiantClusterThreshold.ValueChanged += OnGraphHealthParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblGiantClusterThreshold, 0, startRow + 1);
        tlpPhysicsConstants.Controls.Add(numGiantClusterThreshold, 1, startRow + 1);

        // === Emergency Giant Cluster Threshold ===
        var lblEmergencyThreshold = new Label
        {
            Text = "Emergency Cluster (% of N):",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numEmergencyGiantClusterThreshold = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.05m,
            Minimum = 0.20m,
            Maximum = 0.95m,
            Value = (decimal)PhysicsConstants.EmergencyGiantClusterThreshold,
            Dock = DockStyle.Fill
        };
        numEmergencyGiantClusterThreshold.ValueChanged += OnGraphHealthParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblEmergencyThreshold, 0, startRow + 2);
        tlpPhysicsConstants.Controls.Add(numEmergencyGiantClusterThreshold, 1, startRow + 2);

        // === Decoherence Rate ===
        var lblDecoherenceRate = new Label
        {
            Text = "Cluster Decoherence Rate:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numGiantClusterDecoherenceRate = new NumericUpDown
        {
            DecimalPlaces = 3,
            Increment = 0.01m,
            Minimum = 0.01m,
            Maximum = 0.50m,
            Value = (decimal)PhysicsConstants.GiantClusterDecoherenceRate,
            Dock = DockStyle.Fill
        };
        numGiantClusterDecoherenceRate.ValueChanged += OnGraphHealthParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblDecoherenceRate, 0, startRow + 3);
        tlpPhysicsConstants.Controls.Add(numGiantClusterDecoherenceRate, 1, startRow + 3);

        // === Max Decoherence Edges Fraction ===
        var lblMaxEdgesFraction = new Label
        {
            Text = "Max Edges Weakened (%):",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numMaxDecoherenceEdgesFraction = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.02m,
            Minimum = 0.02m,
            Maximum = 0.50m,
            Value = (decimal)PhysicsConstants.MaxDecoherenceEdgesFraction,
            Dock = DockStyle.Fill
        };
        numMaxDecoherenceEdgesFraction.ValueChanged += OnGraphHealthParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblMaxEdgesFraction, 0, startRow + 4);
        tlpPhysicsConstants.Controls.Add(numMaxDecoherenceEdgesFraction, 1, startRow + 4);

        // === Critical Spectral Dimension ===
        var lblCriticalSpectralDim = new Label
        {
            Text = "Critical d_S (fragmentation):",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numCriticalSpectralDimension = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.1m,
            Minimum = 0.5m,
            Maximum = 2.0m,
            Value = (decimal)PhysicsConstants.CriticalSpectralDimension,
            Dock = DockStyle.Fill
        };
        numCriticalSpectralDimension.ValueChanged += OnGraphHealthParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblCriticalSpectralDim, 0, startRow + 5);
        tlpPhysicsConstants.Controls.Add(numCriticalSpectralDimension, 1, startRow + 5);

        // === Warning Spectral Dimension ===
        var lblWarningSpectralDim = new Label
        {
            Text = "Warning d_S (correction):",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numWarningSpectralDimension = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.1m,
            Minimum = 1.0m,
            Maximum = 3.0m,
            Value = (decimal)PhysicsConstants.WarningSpectralDimension,
            Dock = DockStyle.Fill
        };
        numWarningSpectralDimension.ValueChanged += OnGraphHealthParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblWarningSpectralDim, 0, startRow + 6);
        tlpPhysicsConstants.Controls.Add(numWarningSpectralDimension, 1, startRow + 6);

        // === RQ-Hypothesis Checklist Constants (Energy/Quantization) ===
        // Добавляем разделитель для группировки контролов
        var lblChecklistHeader = new Label
        {
            Text = "??? RQ-Hypothesis Checklist ???",
            AutoSize = true,
            ForeColor = Color.DarkGreen,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        tlpPhysicsConstants.Controls.Add(lblChecklistHeader, 0, startRow + 7);
        tlpPhysicsConstants.SetColumnSpan(lblChecklistHeader, 2);

        // === Edge Weight Quantum ===
        var lblEdgeWeightQuantum = new Label
        {
            Text = "Edge Weight Quantum:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numEdgeWeightQuantum = new NumericUpDown
        {
            DecimalPlaces = 4,
            Increment = 0.0001m,
            Minimum = 0.0001m,
            Maximum = 0.1000m,
            Value = (decimal)PhysicsConstants.EdgeWeightQuantum,
            Dock = DockStyle.Fill
        };
        numEdgeWeightQuantum.ValueChanged += OnRQChecklistParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblEdgeWeightQuantum, 0, startRow + 8);
        tlpPhysicsConstants.Controls.Add(numEdgeWeightQuantum, 1, startRow + 8);

        // === RNG Step Cost ===
        var lblRngStepCost = new Label
        {
            Text = "RNG Step Cost:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numRngStepCost = new NumericUpDown
        {
            DecimalPlaces = 7,
            Increment = 0.000001m,
            Minimum = 0.0000001m,
            Maximum = 0.0100m,
            Value = (decimal)PhysicsConstants.RngStepCost,
            Dock = DockStyle.Fill
        };
        numRngStepCost.ValueChanged += OnRQChecklistParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblRngStepCost, 0, startRow + 9);
        tlpPhysicsConstants.Controls.Add(numRngStepCost, 1, startRow + 9);

        // === Edge Creation Cost ===
        var lblEdgeCreationCost = new Label
        {
            Text = "Edge Creation Cost:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numEdgeCreationCost = new NumericUpDown
        {
            DecimalPlaces = 4,
            Increment = 0.0001m,
            Minimum = 0.0001m,
            Maximum = 0.1000m,
            Value = (decimal)PhysicsConstants.EdgeCreationCost,
            Dock = DockStyle.Fill
        };
        numEdgeCreationCost.ValueChanged += OnRQChecklistParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblEdgeCreationCost, 0, startRow + 10);
        tlpPhysicsConstants.Controls.Add(numEdgeCreationCost, 1, startRow + 10);

        // === Initial Vacuum Energy ===
        var lblInitialVacuumEnergy = new Label
        {
            Text = "Initial Vacuum Energy:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        numInitialVacuumEnergy = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 10.0m,
            Minimum = 0.0001m,
            Maximum = 10000.0m,
            Value = (decimal)PhysicsConstants.InitialVacuumEnergy,
            Dock = DockStyle.Fill
        };
        numInitialVacuumEnergy.ValueChanged += OnRQChecklistParameterChanged;
        tlpPhysicsConstants.Controls.Add(lblInitialVacuumEnergy, 0, startRow + 11);
        tlpPhysicsConstants.Controls.Add(numInitialVacuumEnergy, 1, startRow + 11);
    }

    /// <summary>
    /// Handler for Advanced Physics parameter changes - updates LiveConfig for running simulation.
    /// Registered as event handler in InitializeAdvancedPhysicsControls.
    /// </summary>
    private void OnAdvancedPhysicsParameterChanged(object? sender, EventArgs e)
    {
        if (numLapseFunctionAlpha is null) return;

        var liveConfig = _simApi.LiveConfig;

        liveConfig.LapseFunctionAlpha = (double)numLapseFunctionAlpha.Value;
        liveConfig.TimeDilationAlpha = (double)numTimeDilationAlpha.Value;
        liveConfig.WilsonParameter = (double)numWilsonParameter.Value;
        liveConfig.TopologyDecoherenceInterval = (int)numTopologyDecoherenceInterval.Value;
        liveConfig.TopologyDecoherenceTemperature = (double)numTopologyDecoherenceTemperature.Value;
        liveConfig.GaugeTolerance = (double)numGaugeTolerance.Value;
        liveConfig.MaxRemovableFlux = (double)numMaxRemovableFlux.Value;
        liveConfig.GeometryInertiaMass = (double)numGeometryInertiaMass.Value;
        liveConfig.GaugeFieldDamping = (double)numGaugeFieldDamping.Value;
        liveConfig.PairCreationMassThreshold = (double)numPairCreationMassThreshold.Value;
        liveConfig.PairCreationEnergy = (double)numPairCreationEnergy.Value;

        if (numSpectralLambdaCutoff is not null)
            liveConfig.SpectralLambdaCutoff = (double)numSpectralLambdaCutoff.Value;
        if (numSpectralTargetDimension is not null)
            liveConfig.SpectralTargetDimension = (double)numSpectralTargetDimension.Value;
        if (numSpectralDimensionPotentialStrength is not null)
            liveConfig.SpectralDimensionPotentialStrength = (double)numSpectralDimensionPotentialStrength.Value;

        liveConfig.MarkUpdated();

        if (_isModernRunning && sender is NumericUpDown num)
        {
            AppendSimConsole($"[AdvancedPhysics] {num.Name}: {num.Value}\n");
        }
    }
}
