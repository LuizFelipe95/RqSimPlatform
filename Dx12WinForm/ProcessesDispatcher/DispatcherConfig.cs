using System.Diagnostics;
using System.IO;

namespace Dx12WinForm.ProcessesDispatcher;

public static class DispatcherConfig
{
    public const string SimulationProcessName = "RqSimConsole";
    public const string ControlPipeName = "RqSim_Control_Pipe";
    public const string SharedMemoryMapName = "RqSim_Shared_Memory";

    public static string SimulationExecutablePath { get; } = Path.Combine(AppContext.BaseDirectory, "RqSimConsole.exe");

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
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Minimized
        };
    }
}
