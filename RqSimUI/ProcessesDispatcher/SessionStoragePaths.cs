using System.Diagnostics;
using System.IO;

namespace RqSimForms.ProcessesDispatcher;

/// <summary>
/// Centralizes all storage paths for settings, sessions, logs, and presets.
/// Base directory: <c>&lt;SolutionRoot&gt;/Users/default/</c>.
/// Falls back to <c>AppContext.BaseDirectory/Users/default/</c> if solution root is not found.
/// </summary>
internal static class SessionStoragePaths
{
    // === Base directory ===

    /// <summary>
    /// Root folder for all RqSimPlatform user data.
    /// Resolved relative to the solution/application root: <c>Users/default/</c>.
    /// </summary>
    public static string BaseDir { get; } = ResolveBaseDirectory();

    // === Subdirectories ===

    public static string SettingsDir { get; } = Path.Combine(BaseDir, "settings");
    public static string SessionsDir { get; } = Path.Combine(BaseDir, "sessions");
    public static string LogsDir { get; } = Path.Combine(BaseDir, "logs");
    public static string PresetsDir { get; } = Path.Combine(SettingsDir, "presets");
    public static string DebugDir { get; } = Path.Combine(BaseDir, "debug");

    // === Settings files ===

    /// <summary>
    /// Unified simulation settings file (replaces scattered form/physics/shared files).
    /// </summary>
    public static string SimulationSettingsPath { get; } = Path.Combine(SettingsDir, "simulation_settings.json");

    /// <summary>
    /// Window position, tab index, GPU selection — pure UI state.
    /// </summary>
    public static string FormStatePath { get; } = Path.Combine(SettingsDir, "form_state.json");

    /// <summary>
    /// Backup of the unified settings file created before each save.
    /// </summary>
    public static string SettingsBackupPath { get; } = Path.Combine(SettingsDir, "simulation_settings.bak.json");

    // === Legacy paths (for migration) ===

    /// <summary>
    /// Old AppData-based settings directory used before migration.
    /// </summary>
    public static string LegacyAppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform");

    /// <summary>
    /// Old ProgramData-based shared settings directory (requires elevation).
    /// </summary>
    public static string LegacyProgramDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RqSimPlatform");

    /// <summary>
    /// Old UserProfile-based directory from previous migration.
    /// </summary>
    public static string LegacyUserProfileDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "RqSimPlatform", "default");

    /// <summary>
    /// Sentinel file indicating migration has already been performed.
    /// </summary>
    public static string MigrationCompletedMarker { get; } = Path.Combine(BaseDir, ".migrated");

    // === Session-specific paths ===

    /// <summary>
    /// Returns the per-session folder for the given session ID.
    /// </summary>
    public static string GetSessionDir(string sessionId) => Path.Combine(SessionsDir, sessionId);

    public static string GetMetricsDir(string sessionId) => Path.Combine(GetSessionDir(sessionId), "metrics");
    public static string GetTelemetryDir(string sessionId) => Path.Combine(GetSessionDir(sessionId), "telemetry");
    public static string GetSnapshotsDir(string sessionId) => Path.Combine(GetSessionDir(sessionId), "snapshots");

    /// <summary>
    /// Path to <c>session_info.json</c> inside a session folder.
    /// </summary>
    public static string GetSessionInfoPath(string sessionId) =>
        Path.Combine(GetSessionDir(sessionId), "session_info.json");

    /// <summary>
    /// Path to the streaming metrics JSONL file inside a session folder.
    /// </summary>
    public static string GetMetricsLogPath(string sessionId) =>
        Path.Combine(GetMetricsDir(sessionId), "metrics_log.jsonl");

    /// <summary>
    /// Path to the streaming telemetry JSONL file inside a session folder.
    /// </summary>
    public static string GetTelemetryLogPath(string sessionId) =>
        Path.Combine(GetTelemetryDir(sessionId), "telemetry.jsonl");

    // === Session ID generation ===

    /// <summary>
    /// Generates a unique session ID in the format <c>yyyy-MM-dd_HH-mm-ss_{mode}</c>.
    /// </summary>
    public static string GenerateSessionId(string mode) =>
        $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}_{mode}";

    /// <summary>
    /// Creates the full directory tree for a specific session.
    /// </summary>
    public static void EnsureSessionDirectoryStructure(string sessionId)
    {
        Directory.CreateDirectory(GetMetricsDir(sessionId));
        Directory.CreateDirectory(GetTelemetryDir(sessionId));
        Directory.CreateDirectory(GetSnapshotsDir(sessionId));
    }

    // === Directory initialization ===

    /// <summary>
    /// Ensures the default directory tree exists. Safe to call multiple times.
    /// </summary>
    public static void EnsureDefaultDirectoryStructure()
    {
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(SessionsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(PresetsDir);
        Directory.CreateDirectory(DebugDir);

        Trace.WriteLine($"[SessionStoragePaths] Directory structure ensured at {BaseDir}");
    }

    // === Base directory resolution ===

    /// <summary>
    /// Resolves the base directory by finding the solution root (directory containing <c>.sln</c>)
    /// and returning <c>&lt;SolutionRoot&gt;/Users/default/</c>.
    /// Falls back to <c>AppContext.BaseDirectory/Users/default/</c>.
    /// </summary>
    private static string ResolveBaseDirectory()
    {
        string startDir = AppContext.BaseDirectory;
        string? solutionRoot = FindSolutionRoot(startDir);

        string platformRoot;
        if (solutionRoot is not null)
        {
            platformRoot = solutionRoot;
        }
        else
        {
            // Fallback: use the executable directory as the platform root
            platformRoot = startDir;
            Trace.WriteLine("[SessionStoragePaths] Solution root not found, using AppContext.BaseDirectory");
        }

        string baseDir = Path.Combine(platformRoot, "Users", "default");
        Trace.WriteLine($"[SessionStoragePaths] BaseDir resolved to: {baseDir}");
        return baseDir;
    }

    /// <summary>
    /// Finds solution root by walking up from <paramref name="startDir"/>
    /// looking for a directory containing a <c>.sln</c> file.
    /// </summary>
    private static string? FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
