using RqSimUI.FormSimAPI.Interfaces;

namespace RqSimTelemetryForm;

/// <summary>
/// Console state: enums, ConsoleLine record, and buffer fields.
/// </summary>
public partial class TelemetryForm
{
    private ConsoleBuffer? _sysConsoleBuffer;
    private ConsoleBuffer? _simConsoleBuffer;

    private SysConsoleOutType _sysConsoleOutType = SysConsoleOutType.All;
    private SimConsoleOutType _simConsoleOutType = SimConsoleOutType.All;

    private readonly List<ConsoleLine> _sysConsoleLines = new(capacity: 2048);
    private readonly List<ConsoleLine> _simConsoleLines = new(capacity: 2048);

    private const int MaxConsoleLines = 5000;

    private enum SysConsoleOutType
    {
        All = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Dispatcher = 4,
        GPU = 5,
        IO = 6,
    }

    private enum SimConsoleOutType
    {
        All = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Pipeline = 4,
        Physics = 5,
        Metrics = 6,
    }

    private readonly record struct ConsoleLine(DateTime TimestampUtc, string Category, string Message);
}
