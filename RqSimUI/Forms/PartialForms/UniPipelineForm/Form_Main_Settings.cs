using RqSimUI.Forms.PartialForms.SettingsConfig;
using System.Diagnostics;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Settings persistence methods.
/// Handles loading settings on startup and saving on close.
/// </summary>
partial class Form_Main_RqSim
{
    private FormSettings? _formSettings;

    /// <summary>
    /// Load saved settings and apply to UI controls.
    /// Call this at the END of Form_Main_Load after all controls are initialized.
    /// </summary>


    private static void SetNumericValueSafe(NumericUpDown? control, decimal value)
    {
        if (control is null) return;

        var originalMinimum = control.Minimum;
        var originalMaximum = control.Maximum;

        try
        {
            // Round value to match DecimalPlaces setting to avoid validation errors
            if (control.DecimalPlaces >= 0)
            {
                value = Math.Round(value, control.DecimalPlaces, MidpointRounding.AwayFromZero);
            }

            // Временно расширяем диапазон, чтобы Value присвоился без ArgumentOutOfRangeException,
            // даже если Designer/другой код позже пересчитает Minimum/Maximum.
            control.Minimum = decimal.MinValue;
            control.Maximum = decimal.MaxValue;

            control.Value = value;
        }
        catch (ArgumentOutOfRangeException)
        {
            // На всякий случай (например, NaN/Infinity через конверсию),
            // выбираем безопасный дефолт.
            control.Value = originalMinimum;
        }
        catch (Exception)
        {
            // Catch any other exceptions and use minimum as safe default
            control.Value = originalMinimum;
        }
        finally
        {
            control.Minimum = originalMinimum;
            control.Maximum = originalMaximum;

            // Финальный кламп под реальный диапазон.
            var clamped = Math.Clamp(control.Value, control.Minimum, control.Maximum);
            if (control.Value != clamped)
                control.Value = clamped;
        }
    }
    /// <summary>
    /// Stub: logs to Debug output (console tab removed).
    /// </summary>
    private void AppendSysConsole(string text) => Debug.Write(text);

    /// <summary>
    /// Stub: logs to Debug output (console tab removed).
    /// </summary>
    private void AppendSimConsole(string text) => Debug.Write(text);

    // Science mode fields
    private bool _scienceModeEnabled;
    private bool _useOllivierRicci;
    private bool _enableConservation;
    private bool _useGpuAnisotropy;

    // Simulation run state
    private CancellationTokenSource? _modernCts;
    private Task? _modernSimTask;



    /// <summary>
    /// Stub: physics constants display panel removed with visualization tabs.
    /// </summary>
    private void InitializeAllPhysicsConstantsDisplay()
    {
        // No-op: constants tree view removed
    }

    private void LoadAndApplySettings()
    {
        _formSettings = FormSettings.Load();

        try
        {
            // === Simulation Parameters ===
            SetNumericValueSafe(numNodeCount, _formSettings.NodeCount);
            SetNumericValueSafe(numTargetDegree, _formSettings.TargetDegree);
            SetNumericValueSafe(numInitialExcitedProb, (decimal)_formSettings.InitialExcitedProb);
            SetNumericValueSafe(numLambdaState, (decimal)_formSettings.LambdaState);
            SetNumericValueSafe(numTemperature, (decimal)_formSettings.Temperature);
            SetNumericValueSafe(numEdgeTrialProb, (decimal)_formSettings.EdgeTrialProb);
            SetNumericValueSafe(numMeasurementThreshold, (decimal)_formSettings.MeasurementThreshold);
            SetNumericValueSafe(numTotalSteps, _formSettings.TotalSteps);
            SetNumericValueSafe(numFractalLevels, _formSettings.FractalLevels);
            SetNumericValueSafe(numFractalBranchFactor, _formSettings.FractalBranchFactor);
            // === Physics Constants ===
            SetNumericValueSafe(numInitialEdgeProb, (decimal)_formSettings.InitialEdgeProb);
            SetNumericValueSafe(numGravitationalCoupling, (decimal)_formSettings.GravitationalCoupling);
            SetNumericValueSafe(numVacuumEnergyScale, (decimal)_formSettings.VacuumEnergyScale);
            SetNumericValueSafe(numDecoherenceRate, (decimal)_formSettings.DecoherenceRate);
            SetNumericValueSafe(numHotStartTemperature, (decimal)_formSettings.HotStartTemperature);
            SetNumericValueSafe(numAdaptiveThresholdSigma, (decimal)_formSettings.AdaptiveThresholdSigma);
            SetNumericValueSafe(numWarmupDuration, _formSettings.WarmupDuration);
            SetNumericValueSafe(numGravityTransitionDuration, (decimal)_formSettings.GravityTransitionDuration);

            // === GPU Settings ===
            if (comboBox_GPUIndex.Items.Count > _formSettings.GPUIndex)
                comboBox_GPUIndex.SelectedIndex = _formSettings.GPUIndex;

            // === UI Settings ===
            SetNumericValueSafe(numericUpDown1, _formSettings.CPUThreads);

            // === Mode Settings (Science Mode) ===
            if (checkBox_ScienceSimMode is not null)
            {
                checkBox_ScienceSimMode.Checked = _formSettings.ScienceMode;
            }
            _scienceModeEnabled = _formSettings.ScienceMode;
            _useOllivierRicci = _formSettings.UseOllivierRicciCurvature;
            _enableConservation = _formSettings.EnableConservationValidation;
            _useGpuAnisotropy = _formSettings.UseGpuAnisotropy;

            // === Window State ===
            if (!_formSettings.IsMaximized)
            {
                var screen = Screen.FromPoint(new Point(_formSettings.WindowX, _formSettings.WindowY));
                if (screen.WorkingArea.Contains(_formSettings.WindowX, _formSettings.WindowY))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(_formSettings.WindowX, _formSettings.WindowY);
                }
                Width = Math.Max(800, _formSettings.WindowWidth);
                Height = Math.Max(600, _formSettings.WindowHeight);
            }
            else
            {
                WindowState = FormWindowState.Maximized;
            }

            if (tabControl_Main.TabCount > _formSettings.SelectedTabIndex)
                tabControl_Main.SelectedIndex = _formSettings.SelectedTabIndex;

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Warning: Could not fully restore settings: {ex.Message}");
        }
    }


    /// <summary>
    /// Save current UI settings to file.
    /// Call this in OnFormClosing before base call.
    /// </summary>
    private void SaveCurrentSettings()
    {
        _formSettings ??= new FormSettings();

        try
        {
            // === Simulation Parameters ===
            _formSettings.NodeCount = (int)numNodeCount.Value;
            _formSettings.TargetDegree = (int)numTargetDegree.Value;
            _formSettings.InitialExcitedProb = (double)numInitialExcitedProb.Value;
            _formSettings.LambdaState = (double)numLambdaState.Value;
            _formSettings.Temperature = (double)numTemperature.Value;
            _formSettings.EdgeTrialProb = (double)numEdgeTrialProb.Value;
            _formSettings.MeasurementThreshold = (double)numMeasurementThreshold.Value;
            _formSettings.TotalSteps = (int)numTotalSteps.Value;
            _formSettings.FractalLevels = (int)numFractalLevels.Value;
            _formSettings.FractalBranchFactor = (int)numFractalBranchFactor.Value;
            // === Physics Constants ===
            _formSettings.InitialEdgeProb = (double)numInitialEdgeProb.Value;
            _formSettings.GravitationalCoupling = (double)numGravitationalCoupling.Value;
            _formSettings.VacuumEnergyScale = (double)numVacuumEnergyScale.Value;
            _formSettings.DecoherenceRate = (double)numDecoherenceRate.Value;
            _formSettings.HotStartTemperature = (double)numHotStartTemperature.Value;
            _formSettings.AdaptiveThresholdSigma = (double)numAdaptiveThresholdSigma.Value;
            _formSettings.WarmupDuration = (int)numWarmupDuration.Value;
            _formSettings.GravityTransitionDuration = (double)numGravityTransitionDuration.Value;

            // === Physics Modules ===
            _formSettings.UseQuantumDriven = chkQuantumDriven.Checked;
            _formSettings.UseSpacetimePhysics = chkSpacetimePhysics.Checked;
            _formSettings.UseSpinorField = chkSpinorField.Checked;
            _formSettings.UseVacuumFluctuations = chkVacuumFluctuations.Checked;
            _formSettings.UseBlackHolePhysics = chkBlackHolePhysics.Checked;
            _formSettings.UseYangMillsGauge = chkYangMillsGauge.Checked;
            _formSettings.UseEnhancedKleinGordon = chkEnhancedKleinGordon.Checked;
            _formSettings.UseInternalTime = chkInternalTime.Checked;
            _formSettings.UseSpectralGeometry = chkSpectralGeometry.Checked;
            _formSettings.UseQuantumGraphity = chkQuantumGraphity.Checked;
            _formSettings.UseRelationalTime = chkRelationalTime.Checked;
            _formSettings.UseRelationalYangMills = chkRelationalYangMills.Checked;
            _formSettings.UseNetworkGravity = chkNetworkGravity.Checked;
            _formSettings.UseUnifiedPhysicsStep = chkUnifiedPhysicsStep.Checked;
            _formSettings.UseEnforceGaugeConstraints = chkEnforceGaugeConstraints.Checked;
            _formSettings.UseCausalRewiring = chkCausalRewiring.Checked;
            _formSettings.UseTopologicalProtection = chkTopologicalProtection.Checked;
            _formSettings.UseValidateEnergyConservation = chkValidateEnergyConservation.Checked;
            _formSettings.UseMexicanHatPotential = chkMexicanHatPotential.Checked;
            _formSettings.UseGeometryMomenta = chkGeometryMomenta.Checked;
            _formSettings.UseTopologicalCensorship = chkTopologicalCensorship.Checked;

            // === GPU Settings ===
            _formSettings.GPUIndex = comboBox_GPUIndex.SelectedIndex >= 0 ? comboBox_GPUIndex.SelectedIndex : 0;

            // === UI Settings ===
            _formSettings.CPUThreads = (int)numericUpDown1.Value;

            // === Mode Settings (Science Mode) ===
            _formSettings.ScienceMode = checkBox_ScienceSimMode?.Checked ?? false;
            _formSettings.UseOllivierRicciCurvature = _useOllivierRicci;
            _formSettings.EnableConservationValidation = _enableConservation;
            _formSettings.UseGpuAnisotropy = _useGpuAnisotropy;

            // === Window State ===
            _formSettings.IsMaximized = WindowState == FormWindowState.Maximized;
            if (!_formSettings.IsMaximized)
            {
                _formSettings.WindowWidth = Width;
                _formSettings.WindowHeight = Height;
                _formSettings.WindowX = Location.X;
                _formSettings.WindowY = Location.Y;
            }
            _formSettings.SelectedTabIndex = tabControl_Main.SelectedIndex;

            _formSettings.Save();
        }
        catch
        {
            // Silently fail - settings persistence is not critical
        }
    }
}
