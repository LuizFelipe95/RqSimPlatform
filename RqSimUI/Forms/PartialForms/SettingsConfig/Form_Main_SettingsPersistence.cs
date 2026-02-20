using RqSimForms.Forms.Interfaces;
using RqSimUI.Forms.PartialForms.SettingsConfig.Interfaces;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main - Physics Settings Persistence and Apply functionality.
/// Handles save/load on startup/exit and Apply Settings button.
/// </summary>
public partial class Form_Main_RqSim
{
    private PhysicsSettingsManager? _settingsManager;
    private Button? _btnApplySettings;
    private Button? _btnResetToDefaults;
    private Button? _btnSavePreset;
    private Button? _btnLoadFromConsole;
    private Label? _lblSettingsStatus;

    /// <summary>
    /// Initializes the physics settings manager and loads saved settings.
    /// Call this after InitializeComponent() and before other settings initialization.
    /// </summary>
    private void InitializePhysicsSettingsManager()
    {
        _settingsManager = new PhysicsSettingsManager(_simApi);
        
        // Subscribe to events
        _settingsManager.SettingsApplied += OnSettingsApplied;
        _settingsManager.NonHotSwappableParameterChanged += OnNonHotSwappableParameterChanged;
        
        // Load saved settings
        _settingsManager.LoadSettings();
        
        AppendSysConsole("[Settings] Physics settings manager initialized\n");
    }

    /// <summary>
    /// Saves settings on form closing.
    /// </summary>
    private void SavePhysicsSettingsOnExit()
    {
        if (_settingsManager is null) return;
        
        try
        {
            _settingsManager.CaptureCurrentState();
            _settingsManager.SaveSettings();
            AppendSysConsole("[Settings] Physics settings saved on exit\n");
        }
        catch (Exception ex)
        {
            AppendSysConsole($"[Settings] Failed to save settings: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Initializes Apply Settings button and related controls.
    /// </summary>
    private void InitializeApplySettingsControls()
    {
        // Create a panel for settings action buttons
        var pnlSettingsActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5),
            BackColor = Color.FromArgb(245, 245, 250)
        };

        // Apply Settings button
        _btnApplySettings = new Button
        {
            Text = "? Apply Settings",
            AutoSize = true,
            BackColor = Color.FromArgb(100, 180, 100),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Margin = new Padding(5)
        };
        _btnApplySettings.FlatAppearance.BorderSize = 0;
        _btnApplySettings.Click += BtnApplySettings_Click;

        // Reset to Defaults button
        _btnResetToDefaults = new Button
        {
            Text = "? Reset Defaults",
            AutoSize = true,
            BackColor = Color.FromArgb(200, 150, 100),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(5)
        };
        _btnResetToDefaults.FlatAppearance.BorderSize = 0;
        _btnResetToDefaults.Click += BtnResetToDefaults_Click;

        // Save Preset button
        _btnSavePreset = new Button
        {
            Text = "\uD83D\uDCBE Export Settings",
            AutoSize = true,
            BackColor = Color.FromArgb(100, 150, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(5)
        };
        _btnSavePreset.FlatAppearance.BorderSize = 0;
        _btnSavePreset.Click += BtnSavePreset_Click;

        // Load from Console JSON button
        _btnLoadFromConsole = new Button
        {
            Text = "\uD83D\uDCC2 Import Settings",
            AutoSize = true,
            BackColor = Color.FromArgb(150, 100, 180),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(5)
        };
        _btnLoadFromConsole.FlatAppearance.BorderSize = 0;
        _btnLoadFromConsole.Click += BtnLoadFromConsole_Click;

        // Status label
        _lblSettingsStatus = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.DarkGreen,
            Margin = new Padding(10, 10, 10, 0)
        };

        pnlSettingsActions.Controls.Add(_btnApplySettings);
        pnlSettingsActions.Controls.Add(_btnResetToDefaults);
        pnlSettingsActions.Controls.Add(_btnSavePreset);
        pnlSettingsActions.Controls.Add(_btnLoadFromConsole);
        pnlSettingsActions.Controls.Add(_lblSettingsStatus);

        // Add to Settings tab
        tabPage_Settings.Controls.Add(pnlSettingsActions);
        pnlSettingsActions.BringToFront();
    }

    private void BtnApplySettings_Click(object? sender, EventArgs e)
    {
        if (_settingsManager is null) return;

        // Capture current UI state
        _settingsManager.CaptureCurrentState();

        // Apply to simulation
        var nonHotSwappable = _settingsManager.ApplyToSimulation(_isModernRunning);

        // Check for unused flags
        var unusedFlags = _settingsManager.GetUnusedFlags(_simApi.Pipeline);
        
        if (unusedFlags.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("?? The following flags are enabled but have no corresponding pipeline module:");
            foreach (var flag in unusedFlags)
            {
                sb.AppendLine($"  • {flag}");
            }
            sb.AppendLine("\nEnable the required physics modules or disable these flags.");
            
            AppendSimConsole(sb.ToString());
            
            if (_lblSettingsStatus != null)
            {
                _lblSettingsStatus.Text = $"?? {unusedFlags.Count} unused flag(s)";
                _lblSettingsStatus.ForeColor = Color.DarkOrange;
            }
        }
        else
        {
            if (_lblSettingsStatus != null)
            {
                _lblSettingsStatus.Text = "? Settings applied";
                _lblSettingsStatus.ForeColor = Color.DarkGreen;
            }
        }

        AppendSimConsole("[Settings] Settings applied to simulation\n");
    }

    private void BtnResetToDefaults_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Reset all physics settings to default values?\n\nThis will also reload the UI controls.",
            "Reset to Defaults",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        // Delete saved settings file
        PhysicsSettingsSerializer.DeleteSettings();

        // Create fresh default config
        if (_settingsManager is not null)
        {
            _settingsManager.LoadSettings();
        }

        // Refresh UI
        SyncUIWithPhysicsConstants();

        if (_lblSettingsStatus != null)
        {
            _lblSettingsStatus.Text = "? Reset to defaults";
            _lblSettingsStatus.ForeColor = Color.DarkBlue;
        }

        AppendSysConsole("[Settings] Reset to default values\n");
    }

    private void BtnSavePreset_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON Preset|*.json",
            Title = "Export Simulation Settings",
            DefaultExt = "json",
            InitialDirectory = ProcessesDispatcher.SessionStoragePaths.PresetsDir
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var settings = BuildServerModeSettingsFromUI();
            ProcessesDispatcher.UnifiedSettingsSerializer.ExportPreset(settings, dialog.FileName);

            string presetName = Path.GetFileNameWithoutExtension(dialog.FileName);

            if (_lblSettingsStatus != null)
            {
                _lblSettingsStatus.Text = $"\u2705 Exported: {presetName}";
                _lblSettingsStatus.ForeColor = Color.DarkGreen;
            }

            AppendSysConsole($"[Settings] Preset exported: {dialog.FileName}\n");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export preset: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnLoadFromConsole_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON Config|*.json|All Files|*.*",
            Title = "Import Simulation Settings",
            InitialDirectory = ProcessesDispatcher.SessionStoragePaths.PresetsDir
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var dto = ProcessesDispatcher.UnifiedSettingsSerializer.ImportPreset(dialog.FileName);
            if (dto is null)
            {
                MessageBox.Show(
                    "Could not detect the settings format.\n\nSupported formats:\n" +
                    "\u2022 Unified simulation settings (ServerModeSettingsDto)\n" +
                    "\u2022 Console configuration (SimulationParameters/PhysicsConstants/RQFlags)\n" +
                    "\u2022 Physics settings preset (PhysicsSettingsConfig)",
                    "Import Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyServerModeSettingsToUI(dto);

            if (_settingsManager is not null)
            {
                _settingsManager.CaptureCurrentState();
            }

            if (_lblSettingsStatus != null)
            {
                _lblSettingsStatus.Text = $"\u2705 Imported: {Path.GetFileName(dialog.FileName)}";
                _lblSettingsStatus.ForeColor = Color.DarkGreen;
            }

            AppendSysConsole($"[Settings] Imported settings: {dialog.FileName}\n");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to import settings: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendSysConsole($"[Settings] Failed to import settings: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Loads a ConsoleConfig JSON file and converts it to PhysicsSettingsConfig.
    /// </summary>
    private PhysicsSettingsConfig? LoadConsoleConfigAndConvert(string filePath)
    {
        var json = File.ReadAllText(filePath);
        
        // Try to deserialize as ConsoleConfig-like structure
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var config = new PhysicsSettingsConfig();

        // Load SimulationParameters section
        if (root.TryGetProperty("SimulationParameters", out var simParams) ||
            root.TryGetProperty("simulationParameters", out simParams))
        {
            config.NodeCount = GetIntOrDefault(simParams, "NodeCount", "nodeCount", 256);
            config.TargetDegree = GetIntOrDefault(simParams, "TargetDegree", "targetDegree", 8);
            config.InitialExcitedProb = GetDoubleOrDefault(simParams, "InitialExcitedProb", "initialExcitedProb", 0.02);
            config.LambdaState = GetDoubleOrDefault(simParams, "LambdaState", "lambdaState", 0.5);
            config.Temperature = GetDoubleOrDefault(simParams, "Temperature", "temperature", 10.0);
            config.EdgeTrialProb = GetDoubleOrDefault(simParams, "EdgeTrialProb", "edgeTrialProb", 0.02);
            config.MeasurementThreshold = GetDoubleOrDefault(simParams, "MeasurementThreshold", "measurementThreshold", 0.3);
            config.TotalSteps = GetIntOrDefault(simParams, "TotalSteps", "totalSteps", 10000);
            config.FractalLevels = GetIntOrDefault(simParams, "FractalLevels", "fractalLevels", 0);
            config.FractalBranchFactor = GetIntOrDefault(simParams, "FractalBranchFactor", "fractalBranchFactor", 0);
        }

        // Load PhysicsConstants section
        if (root.TryGetProperty("PhysicsConstants", out var physConst) ||
            root.TryGetProperty("physicsConstants", out physConst))
        {
            config.InitialEdgeProb = GetDoubleOrDefault(physConst, "InitialEdgeProb", "initialEdgeProb", 0.035);
            config.GravitationalCoupling = GetDoubleOrDefault(physConst, "GravitationalCoupling", "gravitationalCoupling", 0.05);
            config.VacuumEnergyScale = GetDoubleOrDefault(physConst, "VacuumEnergyScale", "vacuumEnergyScale", 0.00005);
            config.DecoherenceRate = GetDoubleOrDefault(physConst, "DecoherenceRate", "decoherenceRate", 0.001);
            config.HotStartTemperature = GetDoubleOrDefault(physConst, "HotStartTemperature", "hotStartTemperature", 3.0);
            config.AdaptiveThresholdSigma = GetDoubleOrDefault(physConst, "AdaptiveThresholdAlpha", "adaptiveThresholdAlpha", 1.5);
            config.WarmupDuration = GetIntOrDefault(physConst, "WarmupDuration", "warmupDuration", 200);
            config.GravityTransitionDuration = GetIntOrDefault(physConst, "GravityTransition", "gravityTransition", 137);
        }

        // Load RQFlags section
        if (root.TryGetProperty("RQFlags", out var rqFlags) ||
            root.TryGetProperty("rqFlags", out rqFlags))
        {
            config.EnableNaturalDimensionEmergence = GetBoolOrDefault(rqFlags, "NaturalDimensionEmergence", "naturalDimensionEmergence", true);
            config.EnableTopologicalParity = GetBoolOrDefault(rqFlags, "TopologicalParity", "topologicalParity", false);
            config.EnableLapseSynchronizedGeometry = GetBoolOrDefault(rqFlags, "LapseSynchronizedGeometry", "lapseSynchronizedGeometry", true);
            config.EnableTopologyEnergyCompensation = GetBoolOrDefault(rqFlags, "TopologyEnergyCompensation", "topologyEnergyCompensation", true);
            config.EnablePlaquetteYangMills = GetBoolOrDefault(rqFlags, "PlaquetteYangMills", "plaquetteYangMills", false);
        }

        return config;
    }

    private static int GetIntOrDefault(JsonElement element, string name1, string name2, int defaultValue)
    {
        if (element.TryGetProperty(name1, out var prop) || element.TryGetProperty(name2, out prop))
        {
            if (prop.TryGetInt32(out int val)) return val;
        }
        return defaultValue;
    }

    private static double GetDoubleOrDefault(JsonElement element, string name1, string name2, double defaultValue)
    {
        if (element.TryGetProperty(name1, out var prop) || element.TryGetProperty(name2, out prop))
        {
            if (prop.TryGetDouble(out double val)) return val;
        }
        return defaultValue;
    }

    private static bool GetBoolOrDefault(JsonElement element, string name1, string name2, bool defaultValue)
    {
        if (element.TryGetProperty(name1, out var prop) || element.TryGetProperty(name2, out prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    /// <summary>
    /// Applies PhysicsSettingsConfig to all UI controls.
    /// </summary>
    public void ApplyConfigToUI(PhysicsSettingsConfig config)
    {
        SuspendControlEvents();

        try
        {
            // Simulation parameters
            SetNumericValueSafe(numNodeCount, config.NodeCount);
            SetNumericValueSafe(numTargetDegree, config.TargetDegree);
            SetNumericValueSafe(numInitialEdgeProb, (decimal)config.InitialEdgeProb);
            SetNumericValueSafe(numInitialExcitedProb, (decimal)config.InitialExcitedProb);
            SetNumericValueSafe(numTotalSteps, config.TotalSteps);
            SetNumericValueSafe(numFractalLevels, config.FractalLevels);
            SetNumericValueSafe(numFractalBranchFactor, config.FractalBranchFactor);

            // Physics constants
            SetNumericValueSafe(numGravitationalCoupling, (decimal)config.GravitationalCoupling);
            SetNumericValueSafe(numVacuumEnergyScale, (decimal)config.VacuumEnergyScale);
            SetNumericValueSafe(numDecoherenceRate, (decimal)config.DecoherenceRate);
            SetNumericValueSafe(numHotStartTemperature, (decimal)config.HotStartTemperature);
            SetNumericValueSafe(numAdaptiveThresholdSigma, (decimal)config.AdaptiveThresholdSigma);
            SetNumericValueSafe(numWarmupDuration, config.WarmupDuration);
            SetNumericValueSafe(numGravityTransitionDuration, config.GravityTransitionDuration);
            SetNumericValueSafe(numLambdaState, (decimal)config.LambdaState);
            SetNumericValueSafe(numTemperature, (decimal)config.Temperature);
            SetNumericValueSafe(numEdgeTrialProb, (decimal)config.EdgeTrialProb);
            SetNumericValueSafe(numMeasurementThreshold, (decimal)config.MeasurementThreshold);

            // RQ Checklist
            SetNumericValueSafe(numEdgeWeightQuantum, (decimal)config.EdgeWeightQuantum);
            SetNumericValueSafe(numRngStepCost, (decimal)config.RngStepCost);
            SetNumericValueSafe(numEdgeCreationCost, (decimal)config.EdgeCreationCost);
            SetNumericValueSafe(numInitialVacuumEnergy, (decimal)config.InitialVacuumEnergy);

            // Graph Health
            SetNumericValueSafe(numGiantClusterThreshold, (decimal)config.GiantClusterThreshold);
            SetNumericValueSafe(numEmergencyGiantClusterThreshold, (decimal)config.EmergencyGiantClusterThreshold);
            SetNumericValueSafe(numGiantClusterDecoherenceRate, (decimal)config.GiantClusterDecoherenceRate);
            SetNumericValueSafe(numMaxDecoherenceEdgesFraction, (decimal)config.MaxDecoherenceEdgesFraction);
            SetNumericValueSafe(numCriticalSpectralDimension, (decimal)config.CriticalSpectralDimension);
            SetNumericValueSafe(numWarningSpectralDimension, (decimal)config.WarningSpectralDimension);

            // Advanced Physics
            SetNumericValueSafe(numLapseFunctionAlpha, (decimal)config.LapseFunctionAlpha);
            SetNumericValueSafe(numTimeDilationAlpha, (decimal)config.TimeDilationAlpha);
            SetNumericValueSafe(numWilsonParameter, (decimal)config.WilsonParameter);
            SetNumericValueSafe(numTopologyDecoherenceInterval, config.TopologyDecoherenceInterval);
            SetNumericValueSafe(numTopologyDecoherenceTemperature, (decimal)config.TopologyDecoherenceTemperature);
            SetNumericValueSafe(numGaugeTolerance, (decimal)config.GaugeTolerance);
            SetNumericValueSafe(numMaxRemovableFlux, (decimal)config.MaxRemovableFlux);
            SetNumericValueSafe(numGeometryInertiaMass, (decimal)config.GeometryInertiaMass);
            SetNumericValueSafe(numGaugeFieldDamping, (decimal)config.GaugeFieldDamping);
            SetNumericValueSafe(numPairCreationMassThreshold, (decimal)config.PairCreationMassThreshold);
            SetNumericValueSafe(numPairCreationEnergy, (decimal)config.PairCreationEnergy);

            // Spectral Action
            SetNumericValueSafe(numSpectralLambdaCutoff, (decimal)config.SpectralLambdaCutoff);
            SetNumericValueSafe(numSpectralTargetDimension, (decimal)config.SpectralTargetDimension);
            SetNumericValueSafe(numSpectralDimensionPotentialStrength, (decimal)config.SpectralDimensionPotentialStrength);

            // RQ Flags checkboxes
            SetCheckboxSafe(chkUseHamiltonianGravity, config.UseHamiltonianGravity);
            SetCheckboxSafe(chkEnableVacuumEnergyReservoir, config.EnableVacuumEnergyReservoir);
            SetCheckboxSafe(chkEnableNaturalDimensionEmergence, config.EnableNaturalDimensionEmergence);
            SetCheckboxSafe(chkEnableTopologicalParity, config.EnableTopologicalParity);
            SetCheckboxSafe(chkEnableLapseSynchronizedGeometry, config.EnableLapseSynchronizedGeometry);
            SetCheckboxSafe(chkEnableTopologyEnergyCompensation, config.EnableTopologyEnergyCompensation);
            SetCheckboxSafe(chkEnablePlaquetteYangMills, config.EnablePlaquetteYangMills);
            SetCheckboxSafe(chkEnableSymplecticGaugeEvolution, config.EnableSymplecticGaugeEvolution);
            SetCheckboxSafe(chkEnableAdaptiveTopologyDecoherence, config.EnableAdaptiveTopologyDecoherence);
            SetCheckboxSafe(chkEnableWilsonLoopProtection, config.EnableWilsonLoopProtection);
            SetCheckboxSafe(chkEnableSpectralActionMode, config.EnableSpectralActionMode);
            SetCheckboxSafe(chkEnableWheelerDeWittStrictMode, config.EnableWheelerDeWittStrictMode);
            SetCheckboxSafe(chkPreferOllivierRicciCurvature, config.PreferOllivierRicciCurvature);
        }
        finally
        {
            ResumeControlEvents();
        }
    }

    private void SetCheckboxSafe(CheckBox? checkbox, bool value)
    {
        if (checkbox is not null)
            checkbox.Checked = value;
    }

    private void OnSettingsApplied(object? sender, SettingsAppliedEventArgs e)
    {
        AppendSimConsole($"[Settings] Applied {e.HotSwappableCount} hot-swappable parameters\n");

        if (e.NonHotSwappableCount > 0 && _isModernRunning)
        {
            AppendSimConsole($"[Settings] ?? {e.NonHotSwappableCount} parameters require simulation restart\n");
        }
    }

    private void OnNonHotSwappableParameterChanged(object? sender, NonHotSwappableParameterEventArgs e)
    {
        if (!_isModernRunning) return;

        var sb = new StringBuilder();
        sb.AppendLine("?? The following parameters cannot be changed during simulation:");
        foreach (var param in e.ParameterNames)
        {
            sb.AppendLine($"  • {param}");
        }
        sb.AppendLine("\nChanges will take effect on next simulation start.");

        AppendSimConsole(sb.ToString());
    }
}
