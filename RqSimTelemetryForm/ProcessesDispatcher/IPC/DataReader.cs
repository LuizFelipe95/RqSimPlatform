using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using RqSimForms.ProcessesDispatcher.Contracts;

namespace RqSimForms.ProcessesDispatcher.IPC;

public sealed class DataReader : IDisposable
{
    private MemoryMappedFile? _memory;
    private MemoryMappedViewAccessor? _accessor;

    public bool IsConnected => _accessor is not null;

    public bool TryConnect()
    {
        Dispose();

        try
        {
            _memory = MemoryMappedFile.OpenExisting(
                DispatcherConfig.SharedMemoryMapName,
                MemoryMappedFileRights.Read);

            // Use 0 for capacity to map the entire file, matching Form_Main_RqConsole behavior
            _accessor = _memory.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"[DataReader] Failed to open shared memory: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public bool TryReadHeader(out SharedHeader header)
    {
        header = default;
        if (_accessor is null)
            return false;

        _accessor.Read(0, out header);
        return true;
    }

    public SimState? ReadState()
    {
        if (!TryReadHeader(out var header))
            return null;

        var status = (SimulationStatus)header.StateCode;
        var timestamp = new DateTimeOffset(header.LastUpdateTimestampUtcTicks, TimeSpan.Zero);

        return new SimState(
            header.Iteration,
            header.NodeCount,
            header.EdgeCount,
            header.SystemEnergy,
            status,
            timestamp,
            // Extended metrics
            header.ExcitedCount,
            header.HeavyMass,
            header.LargestCluster,
            header.StrongEdgeCount,
            header.QNorm,
            header.Entanglement,
            header.Correlation,
            header.LatestSpectralDimension,
            header.NetworkTemperature,
            header.EffectiveG,
            header.TotalSteps);
    }

    public bool TryReadNodes(int expectedCount, RenderNode[] buffer)
    {
        if (_accessor is null || expectedCount <= 0 || buffer.Length < expectedCount)
            return false;

        _accessor.ReadArray(SharedMemoryLayout.HeaderSize, buffer, 0, expectedCount);
        return true;
    }

    public RenderNode[] ReadNodesArray(int expectedCount)
    {
        if (_accessor is null || expectedCount <= 0)
            return Array.Empty<RenderNode>();

        RenderNode[] nodes = new RenderNode[expectedCount];
        _accessor.ReadArray(SharedMemoryLayout.HeaderSize, nodes, 0, expectedCount);
        return nodes;
    }

    /// <summary>
    /// Reads edges from shared memory using the offset from header.
    /// </summary>
    public bool TryReadEdges(int edgeDataOffset, int expectedCount, RenderEdge[] buffer)
    {
        if (_accessor is null || expectedCount <= 0 || buffer.Length < expectedCount || edgeDataOffset <= 0)
            return false;

        _accessor.ReadArray(edgeDataOffset, buffer, 0, expectedCount);
        return true;
    }

    /// <summary>
    /// Reads edges from shared memory using header information.
    /// Returns empty array if not connected or no edges.
    /// </summary>
    public RenderEdge[] ReadEdgesArray(int edgeDataOffset, int expectedCount)
    {
        if (_accessor is null || expectedCount <= 0 || edgeDataOffset <= 0)
            return [];

        RenderEdge[] edges = new RenderEdge[expectedCount];
        _accessor.ReadArray(edgeDataOffset, edges, 0, expectedCount);
        return edges;
    }

    /// <summary>
    /// Reads pipeline module stats entries from shared memory.
    /// Entries are stored AFTER edge data at the offset specified in header.ModuleStatsOffset.
    /// Returns empty array if no module stats or not connected.
    /// </summary>
    public ModuleStatsEntry[] ReadModuleStats()
    {
        if (_accessor is null)
            return [];

        if (!TryReadHeader(out var header))
            return [];

        int count = header.ModuleStatsCount;
        int offset = header.ModuleStatsOffset;
        if (count <= 0 || count > 32 || offset <= 0)
            return [];

        var entries = new ModuleStatsEntry[count];
        _accessor.ReadArray(offset, entries, 0, count);
        return entries;
    }

    public void Dispose()
    {
        _accessor?.Dispose();
        _memory?.Dispose();
        _accessor = null;
        _memory = null;
    }
}
