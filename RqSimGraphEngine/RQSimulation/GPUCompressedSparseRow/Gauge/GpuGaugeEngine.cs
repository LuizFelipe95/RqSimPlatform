using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.Gauge;

/// <summary>
/// GPU-accelerated gauge invariant checking engine.
/// </summary>
public sealed class GpuGaugeEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;
    private bool _initialized;

    private CsrTopology? _topology;

    // GPU Buffers
    private ReadWriteBuffer<int>? _triangleCountBuffer;
    private ReadWriteBuffer<int>? _triangleVerticesBuffer;
    private ReadWriteBuffer<double>? _wilsonRealBuffer;
    private ReadWriteBuffer<double>? _wilsonImagBuffer;
    private ReadWriteBuffer<double>? _fluxContributionBuffer;
    private ReadWriteBuffer<double>? _gaugeViolationBuffer;
    private ReadOnlyBuffer<double>? _edgePhasesBuffer;

    // Triangle data (CPU)
    private int[]? _triangleDataCpu;
    private int _triangleCount;

    // CPU Arrays for readback
    private double[] _wilsonRealCpu = [];
    private double[] _wilsonImagCpu = [];
    private double[] _fluxCpu = [];
    private double[] _gaugeViolationCpu = [];

    // Dimensions
    private int _nodeCount;
    private int _edgeCount;
    private const int MaxTrianglesPerEdge = 16;

    // Configuration
    public bool RefreshTrianglesEachStep { get; set; } = false;

    // Results
    public bool IsInitialized => _initialized;
    public int NodeCount => _nodeCount;
    public int TriangleCount => _triangleCount;
    public double TotalTopologicalCharge { get; private set; }
    public double MeanWilsonMagnitude { get; private set; }
    public double MaxGaugeViolation { get; private set; }
    public ReadOnlySpan<double> GaugeViolations => _gaugeViolationCpu;

    public GpuGaugeEngine()
    {
        _device = GraphicsDevice.GetDefault();
    }

    public GpuGaugeEngine(GraphicsDevice device)
    {
        _device = device;
    }

    public void Initialize(CsrTopology topology, double[,] edgePhases)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(edgePhases);

        if (!topology.IsGpuReady)
            throw new InvalidOperationException("CsrTopology must be uploaded to GPU first.");

        _topology = topology;
        _nodeCount = topology.NodeCount;
        _edgeCount = topology.Nnz;

        AllocateBuffers();
        UpdateEdgePhases(edgePhases);
        DetectTriangles();

        _initialized = true;
    }

    private void AllocateBuffers()
    {
        DisposeBuffers();

        _triangleCountBuffer = _device.AllocateReadWriteBuffer<int>(_edgeCount);
        _triangleVerticesBuffer = _device.AllocateReadWriteBuffer<int>(_edgeCount * MaxTrianglesPerEdge);

        int maxTriangles = _edgeCount * MaxTrianglesPerEdge;
        _wilsonRealBuffer = _device.AllocateReadWriteBuffer<double>(maxTriangles);
        _wilsonImagBuffer = _device.AllocateReadWriteBuffer<double>(maxTriangles);
        _fluxContributionBuffer = _device.AllocateReadWriteBuffer<double>(maxTriangles);
        _gaugeViolationBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);

        _wilsonRealCpu = new double[maxTriangles];
        _wilsonImagCpu = new double[maxTriangles];
        _fluxCpu = new double[maxTriangles];
        _gaugeViolationCpu = new double[_nodeCount];
    }

    public void UpdateEdgePhases(double[,] edgePhases)
    {
        if (edgePhases.GetLength(0) != _nodeCount || edgePhases.GetLength(1) != _nodeCount)
            throw new ArgumentException("Edge phases matrix size mismatch.");

        var flat = new double[_nodeCount * _nodeCount];
        for (int i = 0; i < _nodeCount; i++)
        {
            for (int j = 0; j < _nodeCount; j++)
            {
                flat[i * _nodeCount + j] = edgePhases[i, j];
            }
        }

        _edgePhasesBuffer?.Dispose();
        _edgePhasesBuffer = _device.AllocateReadOnlyBuffer(flat);
    }

    public void DetectTriangles()
    {
        if (_topology == null)
            throw new InvalidOperationException("Topology not set.");

        _device.For(_edgeCount, new TriangleDetectionKernel(
            _topology.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _triangleCountBuffer!,
            _triangleVerticesBuffer!,
            MaxTrianglesPerEdge,
            _edgeCount,
            _nodeCount));

        var triangleCounts = new int[_edgeCount];
        _triangleCountBuffer!.CopyTo(triangleCounts);

        var triangles = new List<(int i, int j, int k)>();
        var triangleVertices = new int[_edgeCount * MaxTrianglesPerEdge];
        _triangleVerticesBuffer!.CopyTo(triangleVertices);

        var rowOffsets = _topology.RowOffsets.ToArray();
        var colIndices = _topology.ColIndices.ToArray();

        for (int edgeIdx = 0; edgeIdx < _edgeCount; edgeIdx++)
        {
            int count = triangleCounts[edgeIdx];
            if (count == 0) continue;

            int i = 0;
            for (int n = 0; n < _nodeCount; n++)
            {
                if (edgeIdx >= rowOffsets[n] && edgeIdx < rowOffsets[n + 1])
                {
                    i = n;
                    break;
                }
            }
            int j = colIndices[edgeIdx];

            int baseIdx = edgeIdx * MaxTrianglesPerEdge;
            for (int t = 0; t < count; t++)
            {
                int k = triangleVertices[baseIdx + t];
                if (i < j && j < k)
                {
                    triangles.Add((i, j, k));
                }
            }
        }

        _triangleCount = triangles.Count;

        _triangleDataCpu = new int[_triangleCount * 3];
        for (int t = 0; t < _triangleCount; t++)
        {
            _triangleDataCpu[t * 3] = triangles[t].i;
            _triangleDataCpu[t * 3 + 1] = triangles[t].j;
            _triangleDataCpu[t * 3 + 2] = triangles[t].k;
        }
    }

    public void ComputeGaugeInvariants()
    {
        if (!_initialized || _triangleCount == 0)
            return;

        using var triangleDataBuffer = _device.AllocateReadOnlyBuffer(_triangleDataCpu!);

        _device.For(_triangleCount, new WilsonLoopKernel(
            triangleDataBuffer,
            _edgePhasesBuffer!,
            _wilsonRealBuffer!,
            _wilsonImagBuffer!,
            _triangleCount,
            _nodeCount));

        var wilsonRealCopy = new double[_triangleCount];
        var wilsonImagCopy = new double[_triangleCount];
        _wilsonRealBuffer!.CopyTo(wilsonRealCopy);
        _wilsonImagBuffer!.CopyTo(wilsonImagCopy);

        using var wilsonRealReadOnly = _device.AllocateReadOnlyBuffer(wilsonRealCopy);
        using var wilsonImagReadOnly = _device.AllocateReadOnlyBuffer(wilsonImagCopy);

        _device.For(_triangleCount, new TopologicalChargeKernel(
            wilsonRealReadOnly,
            wilsonImagReadOnly,
            _fluxContributionBuffer!,
            _triangleCount));

        _wilsonRealBuffer!.CopyTo(_wilsonRealCpu.AsSpan(0, _triangleCount));
        _wilsonImagBuffer!.CopyTo(_wilsonImagCpu.AsSpan(0, _triangleCount));
        _fluxContributionBuffer!.CopyTo(_fluxCpu.AsSpan(0, _triangleCount));

        ComputeStatistics();
    }

    private void ComputeStatistics()
    {
        double totalFlux = 0;
        double sumMagnitude = 0;

        for (int t = 0; t < _triangleCount; t++)
        {
            totalFlux += _fluxCpu[t];
            double re = _wilsonRealCpu[t];
            double im = _wilsonImagCpu[t];
            sumMagnitude += System.Math.Sqrt(re * re + im * im);
        }

        TotalTopologicalCharge = totalFlux / (2 * System.Math.PI);
        MeanWilsonMagnitude = _triangleCount > 0 ? sumMagnitude / _triangleCount : 0;

        MaxGaugeViolation = 0;
        for (int t = 0; t < _triangleCount; t++)
        {
            double re = _wilsonRealCpu[t];
            double im = _wilsonImagCpu[t];
            double deviation = System.Math.Abs(System.Math.Sqrt(re * re + im * im) - 1.0);
            if (deviation > MaxGaugeViolation)
                MaxGaugeViolation = deviation;
        }
    }

    public void Step(double[,]? newEdgePhases = null)
    {
        if (newEdgePhases != null)
        {
            UpdateEdgePhases(newEdgePhases);
        }

        if (RefreshTrianglesEachStep)
        {
            DetectTriangles();
        }

        ComputeGaugeInvariants();
    }

    public (double real, double imag) GetWilsonLoop(int triangleIndex)
    {
        if (triangleIndex < 0 || triangleIndex >= _triangleCount)
            throw new ArgumentOutOfRangeException(nameof(triangleIndex));

        return (_wilsonRealCpu[triangleIndex], _wilsonImagCpu[triangleIndex]);
    }

    public double GetTriangleFlux(int triangleIndex)
    {
        if (triangleIndex < 0 || triangleIndex >= _triangleCount)
            throw new ArgumentOutOfRangeException(nameof(triangleIndex));

        return _fluxCpu[triangleIndex];
    }

    public List<(int i, int j, int k)> GetTriangles()
    {
        var result = new List<(int, int, int)>();
        if (_triangleDataCpu == null) return result;

        for (int t = 0; t < _triangleCount; t++)
        {
            result.Add((
                _triangleDataCpu[t * 3],
                _triangleDataCpu[t * 3 + 1],
                _triangleDataCpu[t * 3 + 2]
            ));
        }
        return result;
    }

    private void DisposeBuffers()
    {
        _triangleCountBuffer?.Dispose();
        _triangleVerticesBuffer?.Dispose();
        _wilsonRealBuffer?.Dispose();
        _wilsonImagBuffer?.Dispose();
        _fluxContributionBuffer?.Dispose();
        _gaugeViolationBuffer?.Dispose();
        _edgePhasesBuffer?.Dispose();

        _triangleCountBuffer = null;
        _triangleVerticesBuffer = null;
        _wilsonRealBuffer = null;
        _wilsonImagBuffer = null;
        _fluxContributionBuffer = null;
        _gaugeViolationBuffer = null;
        _edgePhasesBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}
