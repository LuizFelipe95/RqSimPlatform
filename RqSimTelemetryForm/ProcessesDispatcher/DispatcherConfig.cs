using System.Diagnostics;

namespace RqSimForms.ProcessesDispatcher;

public static class DispatcherConfig
{
    public const string SimulationProcessName = "RqSimConsole";
    public const string ControlPipeName = "RqSim_Control_Pipe";
    public const string SharedMemoryMapName = "RqSim_Shared_Memory";

    /// <summary>
    /// Searches for RqSimConsole.exe in multiple possible locations:
    /// 1. Same directory as the running application (deployed side-by-side)
    /// 2. Sibling RqSimConsole output directory (development layout)
    /// 3. Solution root's RqSimConsole project outputs
    /// </summary>
    public static string SimulationExecutablePath { get; } = FindSimulationExecutable();

    public static string SimulationArguments { get; } = "--headless --server-mode";

    public static TimeSpan PipeConnectTimeout { get; } = TimeSpan.FromSeconds(2);

    public static TimeSpan ShutdownGracePeriod { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum age for shared memory data to be considered fresh.
    /// Data older than this threshold is treated as stale.
    /// </summary>
    public static TimeSpan StaleDataThreshold { get; } = TimeSpan.FromSeconds(1);

    public const long SharedMemoryCapacityBytes = 50L * 1024L * 1024L;

    public static ProcessStartInfo BuildStartInfo()
    {
        return new ProcessStartInfo
        {
            FileName = SimulationExecutablePath,
            Arguments = SimulationArguments,
            UseShellExecute = true,
            CreateNoWindow = false, // Show console window for visibility
            WindowStyle = ProcessWindowStyle.Normal
        };
    }

    /// <summary>
    /// Finds RqSimConsole.exe searching multiple locations.
    /// </summary>
    private static string FindSimulationExecutable()
    {
        const string exeName = "RqSimConsole.exe";
        var baseDir = AppContext.BaseDirectory;

        // 1. Same directory (deployed scenario - side-by-side)
        var sameDir = Path.Combine(baseDir, exeName);
        if (File.Exists(sameDir))
        {
            Trace.WriteLine($"[DispatcherConfig] Found {exeName} in same directory: {sameDir}");
            return sameDir;
        }

        // 2. Find solution root first
        var solutionRoot = FindSolutionRoot(baseDir);
        if (solutionRoot != null)
        {
            Trace.WriteLine($"[DispatcherConfig] Solution root found: {solutionRoot}");

            // Determine current configuration (Release/Debug)
            var config = baseDir.Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

            // List of possible TFMs for RqSimConsole
            var possibleTfms = new[]
            {
                "net10.0-windows",
                "net10.0-windows10.0.22000.0",
                "net9.0-windows",
                "net9.0-windows10.0.22000.0",
                "net8.0-windows",
            };

            // Try same configuration first
            foreach (var tfm in possibleTfms)
            {
                var path = Path.Combine(solutionRoot, "RqSimConsole", "bin", config, tfm, exeName);
                if (File.Exists(path))
                {
                    Trace.WriteLine($"[DispatcherConfig] Found {exeName}: {path}");
                    return path;
                }
            }

            // Try opposite configuration as fallback
            var altConfig = config == "Release" ? "Debug" : "Release";
            foreach (var tfm in possibleTfms)
            {
                var path = Path.Combine(solutionRoot, "RqSimConsole", "bin", altConfig, tfm, exeName);
                if (File.Exists(path))
                {
                    Trace.WriteLine($"[DispatcherConfig] Found {exeName} (alt config): {path}");
                    return path;
                }
            }

            // Fallback: search all TFM directories under bin/{config}/
            var consoleBinDir = Path.Combine(solutionRoot, "RqSimConsole", "bin", config);
            if (Directory.Exists(consoleBinDir))
            {
                foreach (var tfmDir in Directory.GetDirectories(consoleBinDir))
                {
                    var path = Path.Combine(tfmDir, exeName);
                    if (File.Exists(path))
                    {
                        Trace.WriteLine($"[DispatcherConfig] Found {exeName} via directory scan: {path}");
                        return path;
                    }
                }
            }
        }
        else
        {
            Trace.WriteLine("[DispatcherConfig] Solution root not found");
        }

        // Return default path (will fail with FileNotFoundException in SimProcessDispatcher)
        Trace.WriteLine($"[DispatcherConfig] {exeName} not found anywhere, returning default path: {sameDir}");
        return sameDir;
    }

    /// <summary>
    /// Finds solution root by looking for .sln file.
    /// </summary>
    private static string? FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
