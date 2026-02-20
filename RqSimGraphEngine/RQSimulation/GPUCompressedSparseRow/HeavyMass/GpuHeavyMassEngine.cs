using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.HeavyMass;

/// <summary>
/// GPU-accelerated heavy mass analysis engine.
/// 
/// PHYSICS: Analyzes cluster mass and inertia for stable structure detection.
/// - Correlation mass = sum of edge weights (connectivity strength)
/// - Geometry inertia = resistance to topology changes
/// - Heavy clusters = stable "baryonic" matter analogs
/// 
/// ALGORITHM:
/// 1. Compute correlation mass per node via CSR
/// 2. Track mass history for inertia computation
/// 3. Detect heavy clusters above threshold
/// 4. Compute cluster centers and velocities
/// </summary>
public sealed class GpuHeavyMassEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;
    private bool _initialized;

    private CsrTopology? _topology;

    // GPU Buffers
    private ReadWriteBuffer<double>? _correlationMassBuffer;
    private ReadWriteBuffer<double>? _previousMassBuffer;
    private ReadWriteBuffer<double>? _inertiaBuffer;
    private ReadWriteBuffer<int>? _isHeavyBuffer;
    private ReadWriteBuffer<double>? _clusterEnergyBuffer;

    // CPU Arrays
    private double[] _correlationMassCpu = [];
    private double[] _previousMassCpu = [];
    private double[] _inertiaCpu = [];
    private int[] _isHeavyCpu = [];

    // Dimensions
    private int _nodeCount;
    private bool _hasHistory;

    // Configuration
    public double HeavyMassThreshold { get; set; } = 1.0;

    // Results
    public bool IsInitialized => _initialized;
    public int NodeCount => _nodeCount;

    public double TotalCorrelationMass { get; private set; }
    public double MeanCorrelationMass { get; private set; }
    public double MaxCorrelationMass { get; private set; }
    public int HeavyNodeCount { get; private set; }
    public double TotalInertia { get; private set; }

    public ReadOnlySpan<double> CorrelationMass => _correlationMassCpu;
    public ReadOnlySpan<double> Inertia => _inertiaCpu;
    public ReadOnlySpan<int> IsHeavy => _isHeavyCpu;

    public GpuHeavyMassEngine()
    {
        _device = GraphicsDevice.GetDefault();
    }

    public GpuHeavyMassEngine(GraphicsDevice device)
    {
        _device = device;
    }

    public void Initialize(CsrTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        if (!topology.IsGpuReady)
            throw new InvalidOperationException("CsrTopology must be uploaded to GPU first.");

        _topology = topology;
        _nodeCount = topology.NodeCount;

        AllocateBuffers();
        _hasHistory = false;
        _initialized = true;
    }

    private void AllocateBuffers()
    {
        DisposeBuffers();

        _correlationMassBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _previousMassBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _inertiaBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _isHeavyBuffer = _device.AllocateReadWriteBuffer<int>(_nodeCount);
        _clusterEnergyBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);

        _correlationMassCpu = new double[_nodeCount];
        _previousMassCpu = new double[_nodeCount];
        _inertiaCpu = new double[_nodeCount];
        _isHeavyCpu = new int[_nodeCount];
    }

    /// <summary>
    /// Compute correlation mass for all nodes.
    /// </summary>
    public void ComputeCorrelationMass()
    {
        if (!_initialized || _topology == null)
            throw new InvalidOperationException("Engine not initialized.");

        // Save previous mass for inertia computation
        if (_hasHistory)
        {
            _correlationMassBuffer!.CopyTo(_previousMassCpu);
            _previousMassBuffer!.CopyFrom(_previousMassCpu);
        }

        // Compute current correlation mass
        _device.For(_nodeCount, new CorrelationMassKernel(
            _topology.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer,
            _correlationMassBuffer!,
            _nodeCount));

        _hasHistory = true;
    }

    /// <summary>
    /// Compute geometry inertia (resistance to change).
    /// </summary>
    public void ComputeInertia(double dt)
    {
        if (!_initialized || !_hasHistory)
            return;

        var currentCopy = new double[_nodeCount];
        _correlationMassBuffer!.CopyTo(currentCopy);
        using var currentReadOnly = _device.AllocateReadOnlyBuffer(currentCopy);

        var previousCopy = new double[_nodeCount];
        _previousMassBuffer!.CopyTo(previousCopy);
        using var previousReadOnly = _device.AllocateReadOnlyBuffer(previousCopy);

        _device.For(_nodeCount, new GeometryInertiaKernel(
            currentReadOnly,
            previousReadOnly,
            _inertiaBuffer!,
            dt,
            _nodeCount));
    }

    /// <summary>
    /// Detect heavy nodes above threshold.
    /// </summary>
    public void DetectHeavyNodes()
    {
        if (!_initialized)
            return;

        var massCopy = new double[_nodeCount];
        _correlationMassBuffer!.CopyTo(massCopy);
        using var massReadOnly = _device.AllocateReadOnlyBuffer(massCopy);

        _device.For(_nodeCount, new HeavyClusterDetectionKernel(
            massReadOnly,
            HeavyMassThreshold,
            _isHeavyBuffer!,
            _nodeCount));
    }

    /// <summary>
    /// Sync GPU results to CPU and compute statistics.
    /// </summary>
    public void SyncToCpu()
    {
        if (!_initialized)
            return;

        _correlationMassBuffer!.CopyTo(_correlationMassCpu);
        _inertiaBuffer!.CopyTo(_inertiaCpu);
        _isHeavyBuffer!.CopyTo(_isHeavyCpu);

        ComputeStatistics();
    }

    private void ComputeStatistics()
    {
        TotalCorrelationMass = 0;
        MaxCorrelationMass = 0;
        HeavyNodeCount = 0;
        TotalInertia = 0;

        for (int i = 0; i < _nodeCount; i++)
        {
            double m = _correlationMassCpu[i];
            TotalCorrelationMass += m;
            if (m > MaxCorrelationMass) MaxCorrelationMass = m;
            if (_isHeavyCpu[i] != 0) HeavyNodeCount++;
            TotalInertia += _inertiaCpu[i];
        }

        MeanCorrelationMass = _nodeCount > 0 ? TotalCorrelationMass / _nodeCount : 0;
    }

    /// <summary>
    /// Full step: compute mass, inertia, detect heavy nodes.
    /// </summary>
    public void Step(double dt)
    {
        ComputeCorrelationMass();
        ComputeInertia(dt);
        DetectHeavyNodes();
        SyncToCpu();
    }

    /// <summary>
    /// Get list of heavy node indices.
    /// </summary>
    public List<int> GetHeavyNodes()
    {
        var result = new List<int>();
        for (int i = 0; i < _nodeCount; i++)
        {
            if (_isHeavyCpu[i] != 0)
                result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Get correlation mass for a specific node.
    /// </summary>
    public double GetNodeMass(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _nodeCount)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        return _correlationMassCpu[nodeIndex];
    }

    private void DisposeBuffers()
    {
        _correlationMassBuffer?.Dispose();
        _previousMassBuffer?.Dispose();
        _inertiaBuffer?.Dispose();
        _isHeavyBuffer?.Dispose();
        _clusterEnergyBuffer?.Dispose();

        _correlationMassBuffer = null;
        _previousMassBuffer = null;
        _inertiaBuffer = null;
        _isHeavyBuffer = null;
        _clusterEnergyBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}
