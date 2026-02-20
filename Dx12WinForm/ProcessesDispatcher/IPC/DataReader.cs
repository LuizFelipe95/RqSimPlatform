using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using Dx12WinForm.ProcessesDispatcher.Contracts;

namespace Dx12WinForm.ProcessesDispatcher.IPC;

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

            _accessor = _memory.CreateViewAccessor(0, DispatcherConfig.SharedMemoryCapacityBytes, MemoryMappedFileAccess.Read);
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
            timestamp);
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

    public void Dispose()
    {
        _accessor?.Dispose();
        _memory?.Dispose();
        _accessor = null;
        _memory = null;
    }
}
