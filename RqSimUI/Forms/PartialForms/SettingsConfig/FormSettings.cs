using System.Text.Json;
using System.Text.Json.Serialization;
using RqSimForms.ProcessesDispatcher;

namespace RqSimUI.Forms.PartialForms.SettingsConfig;

/// <summary>
/// Stores all Form_Main UI settings for persistence across sessions.
/// Saved to JSON file on form close, loaded on form load.
/// </summary>
public sealed class FormSettings
{
    private static readonly string SettingsPath = SessionStoragePaths.FormStatePath;

    /// <summary>
    /// Legacy AppData path — used as read fallback when new path has no file yet.
    /// </summary>
    private static readonly string LegacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform", "form_settings.json");

    // === Simulation Parameters ===
    public int NodeCount { get; set; } = 250;
    public int TargetDegree { get; set; } = 8;
    public double InitialExcitedProb { get; set; } = 0.10;
    public double LambdaState { get; set; } = 0.5;
    public double Temperature { get; set; } = 10.0;
    public double EdgeTrialProb { get; set; } = 0.02;
    public double MeasurementThreshold { get; set; } = 0.30;
    public int TotalSteps { get; set; } = 500000;
    public int FractalLevels { get; set; } = 0;
    public int FractalBranchFactor { get; set; } = 0;
    public bool AutoTuning { get; set; } = false;

    // === Physics Constants ===
    public double InitialEdgeProb { get; set; } = 0.035;
    public double GravitationalCoupling { get; set; } = 0.010;
    public double VacuumEnergyScale { get; set; } = 0.00005;
    public double DecoherenceRate { get; set; } = 0.005;
    public double HotStartTemperature { get; set; } = 6.0;
    public double AdaptiveThresholdSigma { get; set; } = 1.5;
    public int WarmupDuration { get; set; } = 200;
    public double GravityTransitionDuration { get; set; } = 137.0;

    // === Physics Modules (checkboxes) ===
    public bool UseQuantumDriven { get; set; } = true;
    public bool UseSpacetimePhysics { get; set; } = true;
    public bool UseSpinorField { get; set; } = true;
    public bool UseVacuumFluctuations { get; set; } = true;
    public bool UseBlackHolePhysics { get; set; } = true;
    public bool UseYangMillsGauge { get; set; } = true;
    public bool UseEnhancedKleinGordon { get; set; } = true;
    public bool UseInternalTime { get; set; } = true;
    public bool UseSpectralGeometry { get; set; } = true;
    public bool UseQuantumGraphity { get; set; } = true;
    public bool UseRelationalTime { get; set; } = true;
    public bool UseRelationalYangMills { get; set; } = true;
    public bool UseNetworkGravity { get; set; } = true;
    public bool UseUnifiedPhysicsStep { get; set; } = true;
    public bool UseEnforceGaugeConstraints { get; set; } = true;
    public bool UseCausalRewiring { get; set; } = true;
    public bool UseTopologicalProtection { get; set; } = true;
    public bool UseValidateEnergyConservation { get; set; } = true;
    public bool UseMexicanHatPotential { get; set; } = true;
    public bool UseGeometryMomenta { get; set; } = true;
    public bool UseTopologicalCensorship { get; set; } = true;

    // === GPU Settings ===
    public bool EnableGPU { get; set; } = true;
    public bool UseMultiGPU { get; set; } = false;
    public int GPUComputeEngineIndex { get; set; } = 0;
    public int GPUIndex { get; set; } = 0;
    public int MultiGpuSpectralWalkers { get; set; } = 10000;

    // === UI Settings ===
    public int MaxFPS { get; set; } = 10;
    public int CPUThreads { get; set; } = 8;
    public bool AutoScrollConsole { get; set; } = true;
    public bool ShowHeavyOnly { get; set; } = false;
    public int WeightThresholdIndex { get; set; } = 0;
    public int PresetIndex { get; set; } = 0;
    public int ExperimentIndex { get; set; } = 0;

    // === Mode Settings ===
    /// <summary>
    /// Science Mode enabled state (strict physical validation).
    /// </summary>
    public bool ScienceMode { get; set; } = false;

    /// <summary>
    /// Use Ollivier-Ricci curvature (true) or Forman-Ricci (false).
    /// </summary>
    public bool UseOllivierRicciCurvature { get; set; } = true;

    /// <summary>
    /// Enable conservation validation in Science Mode.
    /// </summary>
    public bool EnableConservationValidation { get; set; } = false;

    /// <summary>
    /// Use GPU for anisotropy computation.
    /// </summary>
    public bool UseGpuAnisotropy { get; set; } = true;

    // === Window State ===
    public int WindowWidth { get; set; } = 1359;
    public int WindowHeight { get; set; } = 737;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public bool IsMaximized { get; set; } = false;
    public int SelectedTabIndex { get; set; } = 0;

    // === Background Plugins Settings ===
    /// <summary>
    /// List of enabled background plugin type names.
    /// </summary>
    public List<string> EnabledBackgroundPlugins { get; set; } = [];

    /// <summary>
    /// GPU assignments for background plugins (plugin type name -> GPU index).
    /// </summary>
    public Dictionary<string, int> PluginGpuAssignments { get; set; } = [];

    /// <summary>
    /// Kernel counts for background plugins (plugin type name -> kernel count).
    /// </summary>
    public Dictionary<string, int> PluginKernelCounts { get; set; } = [];

    /// <summary>
    /// Path to last used plugin configuration file.
    /// </summary>
    public string? LastPluginConfigPath { get; set; }

    // === SHARED CONSOLE SETTINGS ===
    // Primary path: new user-profile location; also writes legacy fallback for Console compatibility.

    /// <summary>
    /// Primary shared settings path (new unified location).
    /// </summary>
    public static string SharedSettingsPath { get; } = SessionStoragePaths.SimulationSettingsPath;

    /// <summary>
    /// Legacy AppData path — written alongside new path so Console can still find settings.
    /// </summary>
    private static readonly string LegacySharedSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform", "shared_settings.json");

    /// <summary>
    /// Saves current settings to the new shared location for RqSimConsole to read.
    /// Also writes to the legacy AppData path for backward compatibility.
    /// </summary>
    public void SaveSharedSettings()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        string json = JsonSerializer.Serialize(this, options);

        // Write to new primary location
        WriteJsonSafe(SharedSettingsPath, json);

        // Write to legacy AppData location for Console backward compatibility
        WriteJsonSafe(LegacySharedSettingsPath, json);
    }

    private static void WriteJsonSafe(string path, string json)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FormSettings] Failed to write {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads shared settings from the new location, then falls back to legacy paths.
    /// </summary>
    public static FormSettings? LoadSharedSettings()
    {
        try
        {
            // Try new primary path first
            if (File.Exists(SharedSettingsPath))
            {
                string json = File.ReadAllText(SharedSettingsPath);
                return JsonSerializer.Deserialize<FormSettings>(json);
            }

            // Fallback to legacy AppData path
            if (File.Exists(LegacySharedSettingsPath))
            {
                string json = File.ReadAllText(LegacySharedSettingsPath);
                return JsonSerializer.Deserialize<FormSettings>(json);
            }
        }
        catch
        {
            // Silently fail
        }
        return null;
    }

    /// <summary>
    /// Load settings from file. Tries new path first, then legacy path.
    /// Returns default settings if neither exists.
    /// </summary>
    public static FormSettings Load()
    {
        try
        {
            // Try new path first
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<FormSettings>(json) ?? new FormSettings();
            }

            // Fallback to legacy path
            if (File.Exists(LegacySettingsPath))
            {
                string json = File.ReadAllText(LegacySettingsPath);
                return JsonSerializer.Deserialize<FormSettings>(json) ?? new FormSettings();
            }

            return new FormSettings();
        }
        catch
        {
            return new FormSettings();
        }
    }

    /// <summary>
    /// Save settings to file.
    /// </summary>
    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail - settings are not critical
        }
    }
}
