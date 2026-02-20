using System.Runtime.InteropServices;

namespace Dx12WinForm.ProcessesDispatcher.Contracts;

/// <summary>
/// Per-module execution statistics from console pipeline.
/// Must match Console's ModuleStatsEntry exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ModuleStatsEntry
{
    public int NameHash;
    public float AvgMs;
    public int Count;
    public int Errors;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SharedHeader
{
    public long Iteration;
    public int NodeCount;
    public int EdgeCount;
    public double SystemEnergy;
    public int StateCode; // cast to SimulationStatus
    public long LastUpdateTimestampUtcTicks;

    // Multi-GPU cluster info - must match Console's SharedHeader
    public int GpuClusterSize;
    public int BusySpectralWorkers;
    public int BusyMcmcWorkers;
    public double LatestSpectralDimension;
    public double LatestMcmcEnergy;
    public int TotalSpectralResults;
    public int TotalMcmcResults;

    // Extended simulation metrics - must match Console's SharedHeader
    public int ExcitedCount;
    public double HeavyMass;
    public int LargestCluster;
    public int StrongEdgeCount;
    public double QNorm;
    public double Entanglement;
    public double Correlation;
    public double NetworkTemperature;
    public double EffectiveG;

    // Total steps target (from GUI settings) - must match Console's SharedHeader
    public int TotalSteps;

    // Offset to edge data (after node array)
    public int EdgeDataOffset;

    // Pipeline module performance stats (Option A)
    public int ModuleStatsCount;
    public int ModuleStatsOffset;

    /// <summary>
    /// Gets the simulation status from the state code.
    /// </summary>
    public readonly SimulationStatus Status => (SimulationStatus)StateCode;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RenderNode
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
/// Total: 12 bytes per edge. Must match Console's RenderEdge struct exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RenderEdge
{
    public int FromNode;    // 4 bytes - source node index
    public int ToNode;      // 4 bytes - target node index
    public float Weight;    // 4 bytes - edge weight [0.0, 1.0]
}

public static class SharedMemoryLayout
{
    public static int HeaderSize { get; } = Marshal.SizeOf<SharedHeader>();
    public static int RenderNodeSize { get; } = Marshal.SizeOf<RenderNode>();
    public static int RenderEdgeSize { get; } = Marshal.SizeOf<RenderEdge>();
}
