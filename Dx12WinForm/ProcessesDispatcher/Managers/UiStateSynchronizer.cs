using Dx12WinForm.ProcessesDispatcher.Contracts;
using Dx12WinForm.ProcessesDispatcher.IPC;

namespace Dx12WinForm.ProcessesDispatcher.Managers;

internal sealed class UiStateSynchronizer : IDisposable
{
    private readonly DataReader _reader = new();

    // Cached render nodes buffer for reuse
    private RenderNode[]? _renderNodesBuffer;

    public bool TryDetectExternalSimulationRunning(TimeSpan maxAge)
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
            return false;

        var state = _reader.ReadState();
        if (state is null)
            return false;

        return !state.Value.IsStale(maxAge);
    }

    /// <summary>
    /// Gets the current simulation state from shared memory.
    /// Returns null if not connected or unable to read.
    /// </summary>
    public SimState? GetCurrentState()
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
            return null;

        return _reader.ReadState();
    }

    /// <summary>
    /// Gets the current render nodes from shared memory.
    /// Returns null if not connected, no nodes, or unable to read.
    /// </summary>
    public RenderNode[]? GetRenderNodes()
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
            return null;

        if (!_reader.TryReadHeader(out var header))
            return null;

        int nodeCount = header.NodeCount;
        if (nodeCount <= 0)
            return null;

        // Resize buffer if needed
        if (_renderNodesBuffer is null || _renderNodesBuffer.Length < nodeCount)
        {
            _renderNodesBuffer = new RenderNode[nodeCount];
        }

        if (!_reader.TryReadNodes(nodeCount, _renderNodesBuffer))
            return null;

        // Return a slice of the buffer with actual node count
        if (_renderNodesBuffer.Length == nodeCount)
            return _renderNodesBuffer;

        // Create correctly sized array for caller
        var result = new RenderNode[nodeCount];
        Array.Copy(_renderNodesBuffer, result, nodeCount);
        return result;
    }

    /// <summary>
    /// Gets node count from shared memory header without reading full node data.
    /// </summary>
    public int GetNodeCount()
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
            return 0;

        if (!_reader.TryReadHeader(out var header))
            return 0;

        return header.NodeCount;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
