using System.Runtime.InteropServices;

namespace RqSimConsole.ServerMode;


/// <summary>
/// Per-module execution statistics for SharedMemory transfer.
/// 16 bytes per entry — fixed layout for blittable IPC.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ModuleStatsEntry
{
    /// <summary>FNV-1a hash of module name for mapping.</summary>
    public int NameHash;
    /// <summary>Average execution time in milliseconds.</summary>
    public float AvgMs;
    /// <summary>Total execution count.</summary>
    public int Count;
    /// <summary>Total error count.</summary>
    public int Errors;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SharedHeader
{
    public long Iteration;
    public int NodeCount;
    public int EdgeCount;
    public double SystemEnergy;
    public int StateCode;
    public long LastUpdateTimestampUtcTicks;

    // Multi-GPU cluster info
    public int GpuClusterSize;
    public int BusySpectralWorkers;
    public int BusyMcmcWorkers;
    public double LatestSpectralDimension;
    public double LatestMcmcEnergy;
    public int TotalSpectralResults;
    public int TotalMcmcResults;

    // Extended simulation metrics
    public int ExcitedCount;
    public double HeavyMass;
    public int LargestCluster;
    public int StrongEdgeCount;
    public double QNorm;
    public double Entanglement;
    public double Correlation;
    public double NetworkTemperature;
    public double EffectiveG;

    // Total steps target (from GUI settings)
    public int TotalSteps;

    // Offset to edge data (after node array)
    // EdgeDataOffset = HeaderSize + NodeCount * sizeof(RenderNode)
    public int EdgeDataOffset;

    // Pipeline module performance stats (Option A)
    // Up to 32 modules × 16 bytes = 512 bytes
    public int ModuleStatsCount;

    /// <summary>
    /// Byte offset where ModuleStatsEntry[] data starts (after edge data).
    /// 0 means no module stats region.
    /// </summary>
    public int ModuleStatsOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct RenderNode
{
    public float X;
    public float Y;
    public float Z;
    public float R;
    public float G;
    public float B;
    public int Id;
}

/// <summary>
/// Edge data for rendering. Packed for shared memory transfer.
/// Total: 12 bytes per edge.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct RenderEdge
{
    public int FromNode;    // 4 bytes - source node index
    public int ToNode;      // 4 bytes - target node index
    public float Weight;    // 4 bytes - edge weight [0.0, 1.0]
}

/// <summary>
/// Multi-GPU cluster status for IPC/serialization.
/// </summary>
internal sealed class MultiGpuStatusDto
{
    public int TotalDevices { get; init; }
    public string PhysicsDeviceName { get; init; } = "";
    public int SpectralWorkerCount { get; init; }
    public int McmcWorkerCount { get; init; }
    public int BusySpectralWorkers { get; init; }
    public int BusyMcmcWorkers { get; init; }
    public bool IsDoublePrecisionSupported { get; init; }
    public long TotalVramMb { get; init; }
    public WorkerStatusDto[] SpectralWorkers { get; init; } = [];
    public WorkerStatusDto[] McmcWorkers { get; init; } = [];
}

/// <summary>
/// Individual worker status for IPC/serialization.
/// </summary>
internal sealed class WorkerStatusDto
{
    public int WorkerId { get; init; }
    public string DeviceName { get; init; } = "";
    public bool IsBusy { get; init; }
    public long LastResultTick { get; init; }
    public double? Beta { get; init; }
    public double? Temperature { get; init; }
}

/// <summary>
/// Spectral dimension result for IPC/serialization.
/// </summary>
internal sealed class SpectralResultDto
{
    public double SpectralDimension { get; init; }
    public long TickId { get; init; }
    public int WorkerId { get; init; }
    public double ComputeTimeMs { get; init; }
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public bool IsValid { get; init; }
}

/// <summary>
/// MCMC result for IPC/serialization.
/// </summary>
internal sealed class McmcResultDto
{
    public double MeanEnergy { get; init; }
    public double StdEnergy { get; init; }
    public double MeanAcceptanceRate { get; init; }
    public long TickId { get; init; }
    public int WorkerId { get; init; }
    public double Beta { get; init; }
    public double Temperature { get; init; }
    public double ComputeTimeMs { get; init; }
}
