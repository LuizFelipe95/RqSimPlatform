using System.Diagnostics;
using RqSimTelemetryForm;
using RqSimVisualization;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main — embeds RqSimVisualizationForm and TelemetryForm
/// into tabPage_Visualization and tabPage_Telemetry as child controls.
/// Forms are created lazily on first tab selection.
/// </summary>
partial class Form_Main_RqSim
{
    private RqSimVisualizationForm? _embeddedVisualizationForm;
    private TelemetryForm? _embeddedTelemetryForm;

    /// <summary>
    /// Wires up lazy embedding of visualization and telemetry forms into tab pages.
    /// Call from InitializeUiAfterDesigner.
    /// </summary>
    private void InitializeEmbeddedForms()
    {
        tabControl_Main.SelectedIndexChanged += TabControl_Main_SelectedIndexChanged;
    }

    private void TabControl_Main_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (tabControl_Main.SelectedTab == tabPage_Visualization)
        {
            EnsureVisualizationFormEmbedded();
        }
        else if (tabControl_Main.SelectedTab == tabPage_Telemetry)
        {
            EnsureTelemetryFormEmbedded();
        }
    }

    private void EnsureVisualizationFormEmbedded()
    {
        if (_embeddedVisualizationForm is not null)
            return;

        try
        {
            _embeddedVisualizationForm = new RqSimVisualizationForm();
            _embeddedVisualizationForm.SetSimulationApi(_simApi);

            // Sync current console mode state — the form is created lazily and may
            // have missed earlier SetConsoleMode calls that fired before it existed.
            if (_isConsoleBound || _isExternalSimulation)
            {
                _embeddedVisualizationForm.SetConsoleMode(true);
            }

            EmbedFormIntoTabPage(_embeddedVisualizationForm, tabPage_Visualization);
            Debug.WriteLine("[EmbeddedForms] Visualization form embedded");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EmbeddedForms] Failed to embed Visualization: {ex.Message}");
            ShowEmbeddingError(tabPage_Visualization, "Visualization", ex.Message);
        }
    }

    private void EnsureTelemetryFormEmbedded()
    {
        if (_embeddedTelemetryForm is not null)
            return;

        try
        {
            _embeddedTelemetryForm = new TelemetryForm();
            _embeddedTelemetryForm.SetTelemetryApi(_simApi);
            _embeddedTelemetryForm.ExperimentConfigApplyRequested += OnExperimentConfigApplyRequested;

            // Sync current console mode state — the form is created lazily and may
            // have missed earlier SetConsoleMode calls that fired before it existed.
            if (_isConsoleBound || _isExternalSimulation)
            {
                _embeddedTelemetryForm.SetConsoleMode(true);
            }

            EmbedFormIntoTabPage(_embeddedTelemetryForm, tabPage_Telemetry);
            Debug.WriteLine("[EmbeddedForms] Telemetry form embedded");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EmbeddedForms] Failed to embed Telemetry: {ex.Message}");
            ShowEmbeddingError(tabPage_Telemetry, "Telemetry", ex.Message);
        }
    }

    /// <summary>
    /// Embeds a Form as a borderless child control inside a TabPage.
    /// Removes title bar, min/max/close buttons, and fills the entire tab area.
    /// </summary>
    private static void EmbedFormIntoTabPage(Form form, TabPage tabPage)
    {
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill;
        form.Visible = true;

        tabPage.Controls.Add(form);
        form.Show();
    }

    /// <summary>
    /// Shows an error label inside a tab page when embedding fails.
    /// </summary>
    private static void ShowEmbeddingError(TabPage tabPage, string formName, string errorMessage)
    {
        Label errorLabel = new()
        {
            Text = $"Failed to load {formName} form:\n{errorMessage}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DarkRed
        };
        tabPage.Controls.Add(errorLabel);
    }

    /// <summary>
    /// Notifies the embedded TelemetryForm about console mode change and ensures its timer is running.
    /// </summary>
    private void NotifyTelemetryConsoleMode(bool isConsoleMode)
    {
        if (_embeddedTelemetryForm is null)
            return;

        _embeddedTelemetryForm.SetConsoleMode(isConsoleMode);
        _embeddedTelemetryForm.EnsureTimerRunning();
    }

    /// <summary>
    /// Handles the ExperimentConfigApplyRequested event from the embedded TelemetryForm.
    /// Maps experiment config fields to the corresponding RqSimUI NumericUpDown/CheckBox controls.
    /// </summary>
    private void OnExperimentConfigApplyRequested(object? sender, ExperimentConfigEventArgs e)
    {
        ApplyExperimentConfigToUI(e.Config);
        AppendSysConsole($"[Experiment] Applied '{e.ExperimentName}' config to UI controls\n");
    }

    /// <summary>
    /// Sets all UI controls from an ExperimentConfig, then triggers a LiveConfig sync
    /// so the running simulation picks up the new values immediately.
    /// </summary>
    private void ApplyExperimentConfigToUI(RqSimGraphEngine.Experiments.ExperimentConfig config)
    {
        SuspendControlEvents();
        try
        {
            // Graph parameters
            SetNumericValueSafe(numNodeCount, config.NodeCount);
            SetNumericValueSafe(numInitialEdgeProb, (decimal)config.InitialEdgeProb);
            SetNumericValueSafe(numInitialExcitedProb, (decimal)config.InitialExcitedProb);
            SetNumericValueSafe(numTargetDegree, config.TargetDegree);
            SetNumericValueSafe(numTotalSteps, config.TotalSteps);

            // Simulation parameters
            SetNumericValueSafe(numTemperature, (decimal)config.Temperature);
            SetNumericValueSafe(numLambdaState, (decimal)config.LambdaState);
            SetNumericValueSafe(numEdgeTrialProb, (decimal)config.EdgeTrialProbability);
            SetNumericValueSafe(numMeasurementThreshold, (decimal)config.MeasurementThreshold);

            // Physics parameters
            SetNumericValueSafe(numGravitationalCoupling, (decimal)config.GravitationalCoupling);
            SetNumericValueSafe(numVacuumEnergyScale, (decimal)config.VacuumEnergyScale);
            SetNumericValueSafe(numHotStartTemperature, (decimal)config.HotStartTemperature);
            SetNumericValueSafe(numDecoherenceRate, (decimal)config.DecoherenceRate);
            SetNumericValueSafe(numAdaptiveThresholdSigma, (decimal)config.AdaptiveThresholdSigma);
            SetNumericValueSafe(numWarmupDuration, (decimal)config.WarmupDuration);
            SetNumericValueSafe(numGravityTransitionDuration, (decimal)config.GravityTransitionDuration);

            // Fractal
            SetNumericValueSafe(numFractalLevels, config.FractalLevels);
            SetNumericValueSafe(numFractalBranchFactor, config.FractalBranchFactor);

            // Physics module checkboxes
            chkSpectralGeometry.Checked = config.UseSpectralGeometry;
            chkNetworkGravity.Checked = config.UseNetworkGravity;
            chkQuantumDriven.Checked = config.UseQuantumDrivenStates;
            chkSpinorField.Checked = config.UseSpinorField;
            chkVacuumFluctuations.Checked = config.UseVacuumFluctuations;
            chkTopologicalProtection.Checked = config.UseTopologicalProtection;
        }
        finally
        {
            ResumeControlEvents();
        }

        // Sync the new values to LiveConfig so the running sim picks them up
        OnLiveParameterChanged(this, EventArgs.Empty);
    }
}
