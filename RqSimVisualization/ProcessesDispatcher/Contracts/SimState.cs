namespace RqSimForms.ProcessesDispatcher.Contracts;

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
    DateTimeOffset LastUpdatedUtc,
    // Extended metrics from SharedHeader
    int ExcitedCount = 0,
    double HeavyMass = 0.0,
    int LargestCluster = 0,
    int StrongEdgeCount = 0,
    double QNorm = 0.0,
    double Entanglement = 0.0,
    double Correlation = 0.0,
    double SpectralDimension = 0.0,
    double NetworkTemperature = 0.0,
    double EffectiveG = 0.0)
{
    public bool IsStale(TimeSpan maxAge, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        return reference - LastUpdatedUtc > maxAge;
    }
}
