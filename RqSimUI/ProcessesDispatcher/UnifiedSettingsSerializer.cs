using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RqSimForms.Forms.Interfaces;
using RqSimPlatform.Contracts;

namespace RqSimForms.ProcessesDispatcher;

/// <summary>
/// Serializes <see cref="ServerModeSettingsDto"/> to <c>simulation_settings.json</c>
/// (the unified settings file) and writes legacy fallback copies so
/// <c>RqSimConsole</c> can still discover settings at the old paths.
/// Also provides preset Export/Import with auto-format detection for legacy files.
/// </summary>
internal static class UnifiedSettingsSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Legacy AppData path where <c>RqSimConsole.TryLoadSharedSettings()</c> looks.
    /// </summary>
    private static readonly string LegacyAppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform", "shared_settings.json");

    /// <summary>
    /// Saves <paramref name="settings"/> to the new unified location
    /// (<see cref="SessionStoragePaths.SimulationSettingsPath"/>) and to
    /// the legacy AppData path for Console backward compatibility.
    /// Creates a backup of the previous file before overwriting.
    /// </summary>
    public static void Save(ServerModeSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string json = JsonSerializer.Serialize(settings, WriteOptions);

        // Backup existing file before overwriting
        CreateBackupIfExists(SessionStoragePaths.SimulationSettingsPath,
                             SessionStoragePaths.SettingsBackupPath);

        // Primary location
        WriteJsonSafe(SessionStoragePaths.SimulationSettingsPath, json);

        // Legacy fallback so Console finds settings at old path
        WriteJsonSafe(LegacyAppDataPath, json);

        Trace.WriteLine($"[UnifiedSettings] Saved ({settings.NodeCount} nodes, {settings.TotalSteps} steps)");
    }

    /// <summary>
    /// Loads <see cref="ServerModeSettingsDto"/> from the unified location,
    /// falling back to the legacy AppData path.
    /// Returns <see cref="ServerModeSettingsDto.Default"/> if nothing is found.
    /// </summary>
    public static ServerModeSettingsDto Load()
    {
        string[] candidates =
        [
            SessionStoragePaths.SimulationSettingsPath,
            LegacyAppDataPath
        ];

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                string json = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize<ServerModeSettingsDto>(json);
                if (dto is not null)
                {
                    Trace.WriteLine($"[UnifiedSettings] Loaded from {path}");
                    return dto;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[UnifiedSettings] Failed to load {path}: {ex.Message}");
            }
        }

        Trace.WriteLine("[UnifiedSettings] No settings found, using defaults");
        return ServerModeSettingsDto.Default;
    }

    private static void WriteJsonSafe(string path, string json)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UnifiedSettings] Write failed for {path}: {ex.Message}");
        }
    }

    private static void CreateBackupIfExists(string sourcePath, string backupPath)
    {
        try
        {
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, backupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UnifiedSettings] Backup failed: {ex.Message}");
        }
    }

    // ================================================================
    // PRESET EXPORT / IMPORT
    // ================================================================

    /// <summary>
    /// Exports <paramref name="settings"/> to the specified file path.
    /// </summary>
    public static void ExportPreset(ServerModeSettingsDto settings, string filePath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filePath);

        string json = JsonSerializer.Serialize(settings, WriteOptions);
        WriteJsonSafe(filePath, json);

        Trace.WriteLine($"[UnifiedSettings] Exported preset to {filePath}");
    }

    /// <summary>
    /// Imports settings from a JSON file with auto-format detection.
    /// Supports unified <see cref="ServerModeSettingsDto"/>, legacy <see cref="PhysicsSettingsConfig"/>,
    /// and ConsoleConfig (nested SimulationParameters/PhysicsConstants/RQFlags) formats.
    /// </summary>
    /// <returns>Loaded settings, or <c>null</c> if the file cannot be parsed.</returns>
    public static ServerModeSettingsDto? ImportPreset(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            return null;

        string json = File.ReadAllText(filePath);

        // 1. Try direct deserialization as ServerModeSettingsDto (unified format)
        var dto = TryDeserializeAsUnified(json);
        if (dto is not null)
        {
            Trace.WriteLine($"[UnifiedSettings] Imported unified format from {filePath}");
            return dto;
        }

        // 2. Try ConsoleConfig format (nested sections: SimulationParameters, PhysicsConstants, RQFlags)
        dto = TryParseConsoleConfigFormat(json);
        if (dto is not null)
        {
            Trace.WriteLine($"[UnifiedSettings] Imported ConsoleConfig format from {filePath}");
            return dto;
        }

        // 3. Try PhysicsSettingsConfig (legacy presets)
        dto = TryDeserializeAsPhysicsConfig(json);
        if (dto is not null)
        {
            Trace.WriteLine($"[UnifiedSettings] Imported PhysicsSettingsConfig format from {filePath}");
            return dto;
        }

        Trace.WriteLine($"[UnifiedSettings] Failed to detect format for {filePath}");
        return null;
    }

    /// <summary>
    /// Attempts to deserialize JSON as <see cref="ServerModeSettingsDto"/>.
    /// Returns <c>null</c> if the JSON does not match the unified format.
    /// </summary>
    private static ServerModeSettingsDto? TryDeserializeAsUnified(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Unified format has flat NodeCount, TargetDegree, McmcBeta at root level
            bool hasNodeCount = root.TryGetProperty("NodeCount", out _) ||
                                root.TryGetProperty("nodeCount", out _);
            bool hasMcmcBeta = root.TryGetProperty("McmcBeta", out _) ||
                               root.TryGetProperty("mcmcBeta", out _);

            // Must not have nested sections (ConsoleConfig format)
            bool hasNestedSections = root.TryGetProperty("SimulationParameters", out _) ||
                                     root.TryGetProperty("simulationParameters", out _);

            if (hasNodeCount && hasMcmcBeta && !hasNestedSections)
            {
                return JsonSerializer.Deserialize<ServerModeSettingsDto>(json, ReadOptions);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UnifiedSettings] Unified parse attempt failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Attempts to parse JSON in the ConsoleConfig format with nested sections
    /// (SimulationParameters, PhysicsConstants, RQFlags).
    /// </summary>
    private static ServerModeSettingsDto? TryParseConsoleConfigFormat(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool hasSimParams = root.TryGetProperty("SimulationParameters", out var simParams) ||
                                root.TryGetProperty("simulationParameters", out simParams);

            if (!hasSimParams)
                return null;

            var dto = ServerModeSettingsDto.Default;

            // SimulationParameters
            dto = dto with
            {
                NodeCount = GetInt(simParams, "NodeCount", "nodeCount", dto.NodeCount),
                TargetDegree = GetInt(simParams, "TargetDegree", "targetDegree", dto.TargetDegree),
                InitialExcitedProb = GetDouble(simParams, "InitialExcitedProb", "initialExcitedProb", dto.InitialExcitedProb),
                LambdaState = GetDouble(simParams, "LambdaState", "lambdaState", dto.LambdaState),
                Temperature = GetDouble(simParams, "Temperature", "temperature", dto.Temperature),
                EdgeTrialProb = GetDouble(simParams, "EdgeTrialProb", "edgeTrialProb", dto.EdgeTrialProb),
                TotalSteps = GetInt(simParams, "TotalSteps", "totalSteps", dto.TotalSteps),
            };

            // PhysicsConstants
            if (root.TryGetProperty("PhysicsConstants", out var physConst) ||
                root.TryGetProperty("physicsConstants", out physConst))
            {
                dto = dto with
                {
                    InitialEdgeProb = GetDouble(physConst, "InitialEdgeProb", "initialEdgeProb", dto.InitialEdgeProb),
                    GravitationalCoupling = GetDouble(physConst, "GravitationalCoupling", "gravitationalCoupling", dto.GravitationalCoupling),
                    VacuumEnergyScale = GetDouble(physConst, "VacuumEnergyScale", "vacuumEnergyScale", dto.VacuumEnergyScale),
                    DecoherenceRate = GetDouble(physConst, "DecoherenceRate", "decoherenceRate", dto.DecoherenceRate),
                    HotStartTemperature = GetDouble(physConst, "HotStartTemperature", "hotStartTemperature", dto.HotStartTemperature),
                    WarmupDuration = GetInt(physConst, "WarmupDuration", "warmupDuration", dto.WarmupDuration),
                    GravityTransitionDuration = GetDouble(physConst, "GravityTransition", "gravityTransition", dto.GravityTransitionDuration),
                };
            }

            // RQFlags
            if (root.TryGetProperty("RQFlags", out var rqFlags) ||
                root.TryGetProperty("rqFlags", out rqFlags))
            {
                dto = dto with
                {
                    EnableNaturalDimensionEmergence = GetBool(rqFlags, "NaturalDimensionEmergence", "naturalDimensionEmergence", dto.EnableNaturalDimensionEmergence),
                    EnableTopologicalParity = GetBool(rqFlags, "TopologicalParity", "topologicalParity", dto.EnableTopologicalParity),
                    EnableLapseSynchronizedGeometry = GetBool(rqFlags, "LapseSynchronizedGeometry", "lapseSynchronizedGeometry", dto.EnableLapseSynchronizedGeometry),
                    EnableTopologyEnergyCompensation = GetBool(rqFlags, "TopologyEnergyCompensation", "topologyEnergyCompensation", dto.EnableTopologyEnergyCompensation),
                    EnablePlaquetteYangMills = GetBool(rqFlags, "PlaquetteYangMills", "plaquetteYangMills", dto.EnablePlaquetteYangMills),
                };
            }

            return dto;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UnifiedSettings] ConsoleConfig parse attempt failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Attempts to deserialize JSON as <see cref="PhysicsSettingsConfig"/> and convert
    /// to <see cref="ServerModeSettingsDto"/>.
    /// </summary>
    private static ServerModeSettingsDto? TryDeserializeAsPhysicsConfig(string json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            var config = JsonSerializer.Deserialize<PhysicsSettingsConfig>(json, options);
            if (config is null)
                return null;

            return ConvertPhysicsConfigToDto(config);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UnifiedSettings] PhysicsSettingsConfig parse attempt failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Converts a legacy <see cref="PhysicsSettingsConfig"/> to <see cref="ServerModeSettingsDto"/>.
    /// Fields present only in <c>PhysicsSettingsConfig</c> but not in the DTO are dropped.
    /// Fields present only in the DTO retain their defaults.
    /// </summary>
    private static ServerModeSettingsDto ConvertPhysicsConfigToDto(PhysicsSettingsConfig config)
    {
        return new ServerModeSettingsDto
        {
            // Core
            NodeCount = config.NodeCount,
            TargetDegree = config.TargetDegree,
            Temperature = config.Temperature,
            TotalSteps = config.TotalSteps,

            // Extended physics
            InitialExcitedProb = config.InitialExcitedProb,
            LambdaState = config.LambdaState,
            EdgeTrialProb = config.EdgeTrialProb,
            InitialEdgeProb = config.InitialEdgeProb,
            GravitationalCoupling = config.GravitationalCoupling,
            VacuumEnergyScale = config.VacuumEnergyScale,
            DecoherenceRate = config.DecoherenceRate,
            HotStartTemperature = config.HotStartTemperature,
            WarmupDuration = config.WarmupDuration,
            GravityTransitionDuration = config.GravityTransitionDuration,

            // Lapse & Wilson
            LapseFunctionAlpha = config.LapseFunctionAlpha,
            WilsonParameter = config.WilsonParameter,

            // Geometry Inertia
            GeometryInertiaMass = config.GeometryInertiaMass,
            GaugeFieldDamping = config.GaugeFieldDamping,

            // Topology Decoherence
            TopologyDecoherenceInterval = config.TopologyDecoherenceInterval,
            TopologyDecoherenceTemperature = config.TopologyDecoherenceTemperature,

            // Gauge Protection
            GaugeTolerance = config.GaugeTolerance,
            MaxRemovableFlux = config.MaxRemovableFlux,

            // Hawking Radiation
            PairCreationMassThreshold = config.PairCreationMassThreshold,
            PairCreationEnergy = config.PairCreationEnergy,

            // RQ-Hypothesis Checklist
            EdgeWeightQuantum = config.EdgeWeightQuantum,
            RngStepCost = config.RngStepCost,
            EdgeCreationCost = config.EdgeCreationCost,
            InitialVacuumEnergy = config.InitialVacuumEnergy,

            // Spectral Action
            SpectralLambdaCutoff = config.SpectralLambdaCutoff,
            SpectralTargetDimension = config.SpectralTargetDimension,
            SpectralDimensionPotentialStrength = config.SpectralDimensionPotentialStrength,

            // Graph Health
            GiantClusterThreshold = config.GiantClusterThreshold,
            EmergencyGiantClusterThreshold = config.EmergencyGiantClusterThreshold,
            GiantClusterDecoherenceRate = config.GiantClusterDecoherenceRate,
            MaxDecoherenceEdgesFraction = config.MaxDecoherenceEdgesFraction,
            CriticalSpectralDimension = config.CriticalSpectralDimension,
            WarningSpectralDimension = config.WarningSpectralDimension,

            // RQ Experimental Flags
            EnableNaturalDimensionEmergence = config.EnableNaturalDimensionEmergence,
            EnableTopologicalParity = config.EnableTopologicalParity,
            EnableLapseSynchronizedGeometry = config.EnableLapseSynchronizedGeometry,
            EnableTopologyEnergyCompensation = config.EnableTopologyEnergyCompensation,
            EnablePlaquetteYangMills = config.EnablePlaquetteYangMills,
            EnableSymplecticGaugeEvolution = config.EnableSymplecticGaugeEvolution,
            EnableAdaptiveTopologyDecoherence = config.EnableAdaptiveTopologyDecoherence,
            PreferOllivierRicciCurvature = config.PreferOllivierRicciCurvature,

            // MCMC
            McmcBeta = config.McmcBeta,
            McmcStepsPerCall = config.McmcStepsPerCall,
            McmcWeightPerturbation = config.McmcWeightPerturbation,
            McmcMinWeight = config.McmcMinWeight,

            // Sinkhorn
            SinkhornIterations = config.SinkhornIterations,
            SinkhornEpsilon = config.SinkhornEpsilon,
            SinkhornConvergenceThreshold = config.SinkhornConvergenceThreshold,
            LazyWalkAlpha = config.LazyWalkAlpha,
        };
    }

    // ================================================================
    // JSON HELPERS
    // ================================================================

    private static int GetInt(JsonElement el, string name1, string name2, int defaultValue)
    {
        if ((el.TryGetProperty(name1, out var p) || el.TryGetProperty(name2, out p)) && p.TryGetInt32(out int v))
            return v;
        return defaultValue;
    }

    private static double GetDouble(JsonElement el, string name1, string name2, double defaultValue)
    {
        if ((el.TryGetProperty(name1, out var p) || el.TryGetProperty(name2, out p)) && p.TryGetDouble(out double v))
            return v;
        return defaultValue;
    }

    private static bool GetBool(JsonElement el, string name1, string name2, bool defaultValue)
    {
        if (el.TryGetProperty(name1, out var p) || el.TryGetProperty(name2, out p))
        {
            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }
}
