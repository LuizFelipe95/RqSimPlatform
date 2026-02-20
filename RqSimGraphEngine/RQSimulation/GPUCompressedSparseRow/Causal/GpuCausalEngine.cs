using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.Causal;

/// <summary>
/// GPU-accelerated causal structure discovery engine using parallel BFS.
/// 
/// ALGORITHM: Wavefront propagation on CSR graph
/// 1. Initialize source node in frontier bitmask
/// 2. Iteratively expand frontier using parallel kernel
/// 3. Each iteration explores all neighbors of current frontier
/// 4. Continue until frontier is empty or max depth reached
/// 
/// COMPLEXITY: O(D * N/32) where D = depth, N = nodes
/// Each iteration processes N nodes in parallel with bitmask operations.
/// 
/// APPLICATIONS:
/// - Causal cone computation (light cone)
/// - Causality checks between events
/// - Graph distance computation
/// - Cluster boundary detection
/// </summary>
public sealed class GpuCausalEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;
    private bool _initialized;

    private CsrTopology? _topology;

    // GPU Buffers - BFS state (double-buffered frontiers)
    private ReadWriteBuffer<uint>? _frontierA;
    private ReadWriteBuffer<uint>? _frontierB;
    private ReadWriteBuffer<uint>? _visited;
    private ReadWriteBuffer<int>? _distance;
    private ReadWriteBuffer<int>? _hasNodes;
    private ReadWriteBuffer<int>? _inCone;

    // CPU Arrays for readback
    private int[] _distanceCpu = [];
    private int[] _inConeCpu = [];

    // Dimensions
    private int _nodeCount;
    private int _wordCount; // Number of uint words for bitmask

    // Configuration
    /// <summary>Maximum BFS depth (default: unlimited).</summary>
    public int MaxDepth { get; set; } = int.MaxValue;

    // State
    public bool IsInitialized => _initialized;
    public int NodeCount => _nodeCount;

    /// <summary>Distances from last BFS source(s).</summary>
    public ReadOnlySpan<int> Distances => _distanceCpu;

    /// <summary>Causal cone membership from last query.</summary>
    public ReadOnlySpan<int> InCone => _inConeCpu;

    /// <summary>Last computed cone size.</summary>
    public int LastConeSize { get; private set; }

    public GpuCausalEngine()
    {
        _device = GraphicsDevice.GetDefault();
    }

    public GpuCausalEngine(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Initialize engine with CSR topology.
    /// </summary>
    public void Initialize(CsrTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        if (!topology.IsGpuReady)
            throw new InvalidOperationException("CsrTopology must be uploaded to GPU first.");

        _topology = topology;
        _nodeCount = topology.NodeCount;
        _wordCount = (_nodeCount + 31) / 32; // Round up for bitmask

        AllocateBuffers();
        _initialized = true;
    }

    private void AllocateBuffers()
    {
        DisposeBuffers();

        // Bitmask buffers for frontier and visited
        _frontierA = _device.AllocateReadWriteBuffer<uint>(_wordCount);
        _frontierB = _device.AllocateReadWriteBuffer<uint>(_wordCount);
        _visited = _device.AllocateReadWriteBuffer<uint>(_wordCount);
        
        // Distance and result buffers
        _distance = _device.AllocateReadWriteBuffer<int>(_nodeCount);
        _hasNodes = _device.AllocateReadWriteBuffer<int>(1);
        _inCone = _device.AllocateReadWriteBuffer<int>(_nodeCount);

        // CPU arrays
        _distanceCpu = new int[_nodeCount];
        _inConeCpu = new int[_nodeCount];
    }

    /// <summary>
    /// Compute causal cone (all reachable nodes) from a single source.
    /// </summary>
    /// <param name="sourceNode">Starting node for BFS.</param>
    /// <param name="maxDepth">Maximum depth to explore (default: use MaxDepth property).</param>
    /// <returns>Number of nodes in causal cone.</returns>
    public int ComputeCausalCone(int sourceNode, int? maxDepth = null)
    {
        if (!_initialized || _topology == null)
            throw new InvalidOperationException("Engine not initialized.");

        if (sourceNode < 0 || sourceNode >= _nodeCount)
            throw new ArgumentOutOfRangeException(nameof(sourceNode));

        int depth = maxDepth ?? MaxDepth;

        // Initialize BFS
        int maxThreads = System.Math.Max(_wordCount, _nodeCount);
        _device.For(maxThreads, new CausalInitKernel(
            _frontierA!,
            _visited!,
            _distance!,
            sourceNode,
            _wordCount,
            _nodeCount));

        // Iterative wavefront expansion
        bool useFrontierA = true;
        int currentDepth = 0;

        while (currentDepth < depth)
        {
            var currentFrontier = useFrontierA ? _frontierA! : _frontierB!;
            var nextFrontier = useFrontierA ? _frontierB! : _frontierA!;

            // Clear next frontier
            ClearBuffer(nextFrontier);

            // Check if current frontier is empty
            ClearBuffer(_hasNodes!);
            
            var currentCopy = CopyToReadOnly(currentFrontier);
            _device.For(_wordCount, new FrontierCheckKernel(
                currentCopy,
                _hasNodes!,
                _wordCount));
            currentCopy.Dispose();

            int[] hasNodesResult = new int[1];
            _hasNodes!.CopyTo(hasNodesResult);
            if (hasNodesResult[0] == 0)
                break; // Frontier empty, done

            // Expand wavefront
            currentCopy = CopyToReadOnly(currentFrontier);
            _device.For(_nodeCount, new CausalWavefrontKernel(
                _topology.RowOffsetsBuffer,
                _topology.ColIndicesBuffer,
                currentCopy,
                nextFrontier,
                _visited!,
                _distance!,
                currentDepth,
                depth,
                _nodeCount));
            currentCopy.Dispose();

            useFrontierA = !useFrontierA;
            currentDepth++;
        }

        // Extract cone membership
        var visitedCopy = CopyToReadOnly(_visited!);
        _device.For(_nodeCount, new CausalConeExtractKernel(
            visitedCopy,
            _inCone!,
            _nodeCount));
        visitedCopy.Dispose();

        // Sync to CPU
        _distance!.CopyTo(_distanceCpu);
        _inCone!.CopyTo(_inConeCpu);

        // Count cone size
        LastConeSize = 0;
        for (int i = 0; i < _nodeCount; i++)
        {
            if (_inConeCpu[i] != 0) LastConeSize++;
        }

        return LastConeSize;
    }

    /// <summary>
    /// Compute causal cone from multiple source nodes simultaneously.
    /// </summary>
    public int ComputeCausalConeMultiSource(int[] sourceNodes, int? maxDepth = null)
    {
        if (!_initialized || _topology == null)
            throw new InvalidOperationException("Engine not initialized.");

        if (sourceNodes == null || sourceNodes.Length == 0)
            return 0;

        int depth = maxDepth ?? MaxDepth;

        // Clear buffers
        ClearBuffer(_frontierA!);
        ClearBuffer(_visited!);
        
        // Initialize distances to max
        var maxDist = new int[_nodeCount];
        Array.Fill(maxDist, int.MaxValue);
        _distance!.CopyFrom(maxDist);

        // Initialize multiple sources
        using var sourcesBuffer = _device.AllocateReadOnlyBuffer(sourceNodes);
        _device.For(sourceNodes.Length, new MultiSourceInitKernel(
            _frontierA!,
            _visited!,
            _distance!,
            sourcesBuffer,
            sourceNodes.Length,
            _nodeCount));

        // Iterative wavefront expansion (same as single source)
        bool useFrontierA = true;
        int currentDepth = 0;

        while (currentDepth < depth)
        {
            var currentFrontier = useFrontierA ? _frontierA! : _frontierB!;
            var nextFrontier = useFrontierA ? _frontierB! : _frontierA!;

            ClearBuffer(nextFrontier);
            ClearBuffer(_hasNodes!);

            var currentCopy = CopyToReadOnly(currentFrontier);
            _device.For(_wordCount, new FrontierCheckKernel(
                currentCopy,
                _hasNodes!,
                _wordCount));
            currentCopy.Dispose();

            int[] hasNodesResult = new int[1];
            _hasNodes!.CopyTo(hasNodesResult);
            if (hasNodesResult[0] == 0) break;

            currentCopy = CopyToReadOnly(currentFrontier);
            _device.For(_nodeCount, new CausalWavefrontKernel(
                _topology.RowOffsetsBuffer,
                _topology.ColIndicesBuffer,
                currentCopy,
                nextFrontier,
                _visited!,
                _distance!,
                currentDepth,
                depth,
                _nodeCount));
            currentCopy.Dispose();

            useFrontierA = !useFrontierA;
            currentDepth++;
        }

        // Extract and count
        var visitedCopy = CopyToReadOnly(_visited!);
        _device.For(_nodeCount, new CausalConeExtractKernel(
            visitedCopy,
            _inCone!,
            _nodeCount));
        visitedCopy.Dispose();

        _distance!.CopyTo(_distanceCpu);
        _inCone!.CopyTo(_inConeCpu);

        LastConeSize = 0;
        for (int i = 0; i < _nodeCount; i++)
        {
            if (_inConeCpu[i] != 0) LastConeSize++;
        }

        return LastConeSize;
    }

    /// <summary>
    /// Check if two nodes are causally connected within given depth.
    /// </summary>
    public bool AreCausallyConnected(int nodeA, int nodeB, int maxDepth)
    {
        if (nodeA == nodeB) return true;

        ComputeCausalCone(nodeA, maxDepth);
        return _inConeCpu[nodeB] != 0;
    }

    /// <summary>
    /// Get shortest path distance between two nodes.
    /// Returns int.MaxValue if not reachable within MaxDepth.
    /// </summary>
    public int GetDistance(int nodeA, int nodeB)
    {
        if (nodeA == nodeB) return 0;

        ComputeCausalCone(nodeA);
        return _distanceCpu[nodeB];
    }

    /// <summary>
    /// Get all nodes in causal cone after last computation.
    /// </summary>
    public List<int> GetCausalConeNodes()
    {
        var result = new List<int>();
        for (int i = 0; i < _nodeCount; i++)
        {
            if (_inConeCpu[i] != 0)
                result.Add(i);
        }
        return result;
    }

    private void ClearBuffer(ReadWriteBuffer<uint> buffer)
    {
        var zeros = new uint[buffer.Length];
        buffer.CopyFrom(zeros);
    }

    private void ClearBuffer(ReadWriteBuffer<int> buffer)
    {
        var zeros = new int[buffer.Length];
        buffer.CopyFrom(zeros);
    }

    private ReadOnlyBuffer<uint> CopyToReadOnly(ReadWriteBuffer<uint> source)
    {
        var temp = new uint[source.Length];
        source.CopyTo(temp);
        return _device.AllocateReadOnlyBuffer(temp);
    }

    private void DisposeBuffers()
    {
        _frontierA?.Dispose();
        _frontierB?.Dispose();
        _visited?.Dispose();
        _distance?.Dispose();
        _hasNodes?.Dispose();
        _inCone?.Dispose();

        _frontierA = null;
        _frontierB = null;
        _visited = null;
        _distance = null;
        _hasNodes = null;
        _inCone = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}
