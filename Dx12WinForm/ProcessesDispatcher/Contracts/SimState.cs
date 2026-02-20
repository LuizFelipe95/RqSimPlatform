namespace Dx12WinForm.ProcessesDispatcher.Contracts;

public enum SimulationStatus
{
    Unknown = 0,
    Running = 1,
    Paused = 2,
    Stopped = 3,
    Faulted = 4
}

public readonly record struct SimState(
    long Iteration,
    int NodeCount,
    int EdgeCount,
    double SystemEnergy,
    SimulationStatus Status,
    DateTimeOffset LastUpdatedUtc)
{
    public bool IsStale(TimeSpan maxAge, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        return reference - LastUpdatedUtc > maxAge;
    }
}
