using System.Diagnostics;
using System.IO;
using RqSimForms.ProcessesDispatcher;

namespace RqSimForms;

/// <summary>
/// Partial class for Form_Main — one-time migration of settings from legacy paths
/// (<c>%APPDATA%</c>, <c>%PROGRAMDATA%</c>, <c>%USERPROFILE%/RqSimPlatform/default/</c>)
/// to the new unified location (<c>&lt;SolutionRoot&gt;/Users/default/</c>).
/// </summary>
partial class Form_Main_RqSim
{
    /// <summary>
    /// Migrates settings files from legacy locations to the new directory structure.
    /// No-ops if migration has already been performed (sentinel file exists).
    /// Creates a backup of each source file before moving.
    /// </summary>
    private static void TryMigrateOldSettings()
    {
        if (File.Exists(SessionStoragePaths.MigrationCompletedMarker))
            return;

        try
        {
            // Migrate from old %USERPROFILE%/RqSimPlatform/default/ (previous migration target)
            string userProfileSettings = Path.Combine(SessionStoragePaths.LegacyUserProfileDir, "settings");
            MigrateFileIfExists(
                Path.Combine(userProfileSettings, "simulation_settings.json"),
                SessionStoragePaths.SimulationSettingsPath);
            MigrateFileIfExists(
                Path.Combine(userProfileSettings, "form_state.json"),
                SessionStoragePaths.FormStatePath);

            // Migrate from %APPDATA%
            MigrateFileIfExists(
                Path.Combine(SessionStoragePaths.LegacyAppDataDir, "form_settings.json"),
                SessionStoragePaths.FormStatePath);

            MigrateFileIfExists(
                Path.Combine(SessionStoragePaths.LegacyAppDataDir, "physics_settings.json"),
                Path.Combine(SessionStoragePaths.SettingsDir, "physics_settings_legacy.json"));

            MigrateFileIfExists(
                Path.Combine(SessionStoragePaths.LegacyAppDataDir, "physics_settings.backup.json"),
                Path.Combine(SessionStoragePaths.SettingsDir, "physics_settings_legacy.bak.json"));

            // Migrate shared settings from ProgramData (primary) or AppData (fallback)
            string programDataShared = Path.Combine(SessionStoragePaths.LegacyProgramDataDir, "shared_settings.json");
            string appDataShared = Path.Combine(SessionStoragePaths.LegacyAppDataDir, "shared_settings.json");

            if (!MigrateFileIfExists(programDataShared, SessionStoragePaths.SimulationSettingsPath))
            {
                MigrateFileIfExists(appDataShared, SessionStoragePaths.SimulationSettingsPath);
            }

            // Migrate presets from all legacy locations
            MigratePresetsFromDir(Path.Combine(userProfileSettings, "presets"));
            MigratePresetsFromDir(Path.Combine(SessionStoragePaths.LegacyAppDataDir, "presets"));

            // Write sentinel so migration is not repeated
            File.WriteAllText(SessionStoragePaths.MigrationCompletedMarker,
                $"Migrated on {DateTime.UtcNow:O}");

            Trace.WriteLine("[StorageMigration] Legacy settings migrated successfully");
        }
        catch (Exception ex)
        {
            // Migration failure is non-fatal — app will use defaults
            Trace.WriteLine($"[StorageMigration] Migration failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Migrates all <c>.json</c> preset files from a legacy directory.
    /// </summary>
    private static void MigratePresetsFromDir(string legacyPresetsDir)
    {
        if (!Directory.Exists(legacyPresetsDir))
            return;

        foreach (string presetFile in Directory.EnumerateFiles(legacyPresetsDir, "*.json"))
        {
            string destPath = Path.Combine(SessionStoragePaths.PresetsDir, Path.GetFileName(presetFile));
            MigrateFileIfExists(presetFile, destPath);
        }
    }

    /// <summary>
    /// Copies a file from <paramref name="sourcePath"/> to <paramref name="destPath"/>
    /// if the source exists and the destination does not.
    /// </summary>
    /// <returns><see langword="true"/> if a file was copied.</returns>
    private static bool MigrateFileIfExists(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath))
            return false;

        // Never overwrite an already-migrated or manually-placed file
        if (File.Exists(destPath))
        {
            Trace.WriteLine($"[StorageMigration] Skipped (dest exists): {destPath}");
            return false;
        }

        try
        {
            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(sourcePath, destPath, overwrite: false);
            Trace.WriteLine($"[StorageMigration] Copied {sourcePath} → {destPath}");
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[StorageMigration] Failed to copy {sourcePath}: {ex.Message}");
            return false;
        }
    }
}
