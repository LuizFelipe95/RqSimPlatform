using System.Text.Json;
using System.Text.Json.Serialization;

namespace RqSimForms.Forms.Interfaces;

/// <summary>
/// Serializer for physics settings configurations.
/// Handles saving/loading physics parameters to/from JSON files.
/// Auto-saves on application exit and auto-loads on startup.
/// </summary>
public static class PhysicsSettingsSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Default physics settings file path in user's AppData folder.
    /// </summary>
    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform",
        "physics_settings.json");

    /// <summary>
    /// Backup settings path for recovery.
    /// </summary>
    public static string BackupSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform",
        "physics_settings.backup.json");

    /// <summary>
    /// Saves the physics settings configuration to a JSON file.
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="filePath">Target file path (uses default if null)</param>
    public static void Save(PhysicsSettingsConfig config, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        filePath ??= DefaultSettingsPath;

        // Ensure directory exists
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Create backup of existing file
        if (File.Exists(filePath))
        {
            try
            {
                File.Copy(filePath, BackupSettingsPath, overwrite: true);
            }
            catch
            {
                // Backup failure is not critical
            }
        }

        config.LastModified = DateTime.UtcNow;

        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Saves the physics settings asynchronously.
    /// </summary>
    public static async Task SaveAsync(PhysicsSettingsConfig config, string? filePath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        filePath ??= DefaultSettingsPath;

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(filePath))
        {
            try
            {
                File.Copy(filePath, BackupSettingsPath, overwrite: true);
            }
            catch
            {
                // Backup failure is not critical
            }
        }

        config.LastModified = DateTime.UtcNow;

        await using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(fs, config, JsonOptions, ct);
    }

    /// <summary>
    /// Loads a physics settings configuration from a JSON file.
    /// </summary>
    /// <param name="filePath">Source file path (uses default if null)</param>
    /// <returns>Loaded configuration, or null if file doesn't exist</returns>
    public static PhysicsSettingsConfig? Load(string? filePath = null)
    {
        filePath ??= DefaultSettingsPath;

        if (!File.Exists(filePath))
        {
            // Try backup file
            if (File.Exists(BackupSettingsPath))
            {
                filePath = BackupSettingsPath;
            }
            else
            {
                return null;
            }
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<PhysicsSettingsConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Corrupted file, try backup
            if (filePath != BackupSettingsPath && File.Exists(BackupSettingsPath))
            {
                try
                {
                    string backupJson = File.ReadAllText(BackupSettingsPath);
                    return JsonSerializer.Deserialize<PhysicsSettingsConfig>(backupJson, JsonOptions);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Loads physics settings asynchronously.
    /// </summary>
    public static async Task<PhysicsSettingsConfig?> LoadAsync(string? filePath = null, CancellationToken ct = default)
    {
        filePath ??= DefaultSettingsPath;

        if (!File.Exists(filePath))
        {
            if (File.Exists(BackupSettingsPath))
            {
                filePath = BackupSettingsPath;
            }
            else
            {
                return null;
            }
        }

        try
        {
            await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<PhysicsSettingsConfig>(fs, JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads settings or creates default if none exist.
    /// </summary>
    public static PhysicsSettingsConfig LoadOrCreateDefault(string? filePath = null)
    {
        return Load(filePath) ?? PhysicsSettingsConfig.CreateDefault();
    }

    /// <summary>
    /// Checks if a settings file exists.
    /// </summary>
    public static bool SettingsExist(string? filePath = null)
    {
        filePath ??= DefaultSettingsPath;
        return File.Exists(filePath) || File.Exists(BackupSettingsPath);
    }

    /// <summary>
    /// Deletes the settings file (for reset to defaults).
    /// </summary>
    public static bool DeleteSettings(string? filePath = null)
    {
        filePath ??= DefaultSettingsPath;

        bool deleted = false;
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            deleted = true;
        }
        if (File.Exists(BackupSettingsPath))
        {
            File.Delete(BackupSettingsPath);
            deleted = true;
        }
        return deleted;
    }

    /// <summary>
    /// Exports settings to a specific file (for presets).
    /// </summary>
    public static void ExportPreset(PhysicsSettingsConfig config, string presetName, string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RqSimPlatform",
            "presets");

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        config.PresetName = presetName;
        string filePath = Path.Combine(directory, $"{presetName}.json");
        Save(config, filePath);
    }

    /// <summary>
    /// Imports a preset from file.
    /// </summary>
    public static PhysicsSettingsConfig? ImportPreset(string presetName, string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RqSimPlatform",
            "presets");

        string filePath = Path.Combine(directory, $"{presetName}.json");
        return Load(filePath);
    }

    /// <summary>
    /// Lists available preset names.
    /// </summary>
    public static IReadOnlyList<string> ListPresets(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RqSimPlatform",
            "presets");

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList()!;
    }
}
