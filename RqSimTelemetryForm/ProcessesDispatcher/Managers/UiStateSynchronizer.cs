using RqSimForms.ProcessesDispatcher.Contracts;
using RqSimForms.ProcessesDispatcher.IPC;

namespace RqSimForms.ProcessesDispatcher.Managers;

internal sealed class UiStateSynchronizer : IDisposable
{
    private readonly DataReader _reader = new();

    // Cached render nodes buffer for reuse
    private RenderNode[]? _renderNodesBuffer;
    
    // Cached render edges buffer for reuse
    private RenderEdge[]? _renderEdgesBuffer;

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
    /// Gets the current render edges from shared memory.
    /// Returns null if not connected, no edges, or unable to read.
    /// </summary>
    public RenderEdge[]? GetRenderEdges()
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
            return null;

        if (!_reader.TryReadHeader(out var header))
            return null;

        int edgeCount = header.EdgeCount;
        int edgeDataOffset = header.EdgeDataOffset;
        
        if (edgeCount <= 0 || edgeDataOffset <= 0)
            return null;

        // Resize buffer if needed
        if (_renderEdgesBuffer is null || _renderEdgesBuffer.Length < edgeCount)
        {
            _renderEdgesBuffer = new RenderEdge[edgeCount];
        }

        if (!_reader.TryReadEdges(edgeDataOffset, edgeCount, _renderEdgesBuffer))
            return null;

        // Return a slice of the buffer with actual edge count
        if (_renderEdgesBuffer.Length == edgeCount)
            return _renderEdgesBuffer;

        // Create correctly sized array for caller
        var result = new RenderEdge[edgeCount];
        Array.Copy(_renderEdgesBuffer, result, edgeCount);
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

    /// <summary>
    /// Reads pipeline module stats from shared memory.
    /// Returns empty array if not connected or no module stats available.
    /// </summary>
    public ModuleStatsEntry[] GetModuleStats()
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
            return [];

        return _reader.ReadModuleStats();
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
