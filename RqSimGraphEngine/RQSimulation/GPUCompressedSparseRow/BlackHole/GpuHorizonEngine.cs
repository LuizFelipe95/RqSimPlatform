using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RQSimulation.GPUCompressedSparseRow.BlackHole;

/// <summary>
/// GPU-accelerated black hole horizon detection engine using CSR topology.
/// </summary>
public sealed class GpuHorizonEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;
    private bool _initialized;

    private CsrTopology? _topology;

    private ReadWriteBuffer<double>? _localMassBuffer;
    private ReadWriteBuffer<double>? _effectiveRadiusBuffer;
    private ReadWriteBuffer<int>? _horizonFlagsBuffer;
    private ReadWriteBuffer<double>? _schwarzschildRadiusBuffer;
    private ReadWriteBuffer<double>? _densityBuffer;
    private ReadWriteBuffer<double>? _hawkingTemperatureBuffer;
    private ReadWriteBuffer<double>? _entropyBuffer;
    private ReadOnlyBuffer<double>? _nodeEnergiesBuffer;
    private ReadOnlyBuffer<double>? _edgeDistancesBuffer;

    private double[] _localMassCpu = [];
    private double[] _effectiveRadiusCpu = [];
    private int[] _horizonFlagsCpu = [];
    private double[] _schwarzschildRadiusCpu = [];
    private double[] _densityCpu = [];
    private double[] _hawkingTemperatureCpu = [];
    private double[] _entropyCpu = [];

    private int _nodeCount;
    private int _nnz;

    public double DensityThreshold { get; set; } = 10.0;
    public double MinMassThreshold { get; set; } = 0.01;
    public double SelfMassFactor { get; set; } = 1.0;
    public double MinWeightThreshold { get; set; } = 1e-10;
    public double MaxDistance { get; set; } = 10.0;
    public double EvaporationConstant { get; set; } = 1e-4;

    public bool IsInitialized => _initialized;
    public int NodeCount => _nodeCount;
    public ReadOnlySpan<int> HorizonFlags => _horizonFlagsCpu;
    public ReadOnlySpan<double> LocalMass => _localMassCpu;
    public ReadOnlySpan<double> SchwarzschildRadius => _schwarzschildRadiusCpu;
    public ReadOnlySpan<double> HawkingTemperature => _hawkingTemperatureCpu;
    public int HorizonCount { get; private set; }
    public int SingularityCount { get; private set; }

    public GpuHorizonEngine()
    {
        _device = GraphicsDevice.GetDefault();
    }

    public GpuHorizonEngine(GraphicsDevice device)
    {
        _device = device;
    }

    public void Initialize(CsrTopology topology, double[] nodeEnergies)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(nodeEnergies);

        if (!topology.IsGpuReady)
            throw new InvalidOperationException("CsrTopology must be uploaded to GPU first.");

        _topology = topology;
        _nodeCount = topology.NodeCount;
        _nnz = topology.Nnz;

        if (nodeEnergies.Length != _nodeCount)
            throw new ArgumentException($"nodeEnergies length mismatch.");

        AllocateBuffers(nodeEnergies);
        _initialized = true;
    }

    private void AllocateBuffers(double[] nodeEnergies)
    {
        DisposeBuffers();

        _localMassBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _effectiveRadiusBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _horizonFlagsBuffer = _device.AllocateReadWriteBuffer<int>(_nodeCount);
        _schwarzschildRadiusBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _densityBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _hawkingTemperatureBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _entropyBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _nodeEnergiesBuffer = _device.AllocateReadOnlyBuffer(nodeEnergies);

        PrecomputeEdgeDistances();

        _localMassCpu = new double[_nodeCount];
        _effectiveRadiusCpu = new double[_nodeCount];
        _horizonFlagsCpu = new int[_nodeCount];
        _schwarzschildRadiusCpu = new double[_nodeCount];
        _densityCpu = new double[_nodeCount];
        _hawkingTemperatureCpu = new double[_nodeCount];
        _entropyCpu = new double[_nodeCount];
    }

    private void PrecomputeEdgeDistances()
    {
        if (_topology == null || _nnz == 0) return;

        var edgeWeights = _topology.EdgeWeights;
        var edgeDistances = new double[_nnz];

        for (int k = 0; k < _nnz; k++)
        {
            double weight = edgeWeights[k];
            if (weight > MinWeightThreshold)
            {
                edgeDistances[k] = -System.Math.Log(weight);
            }
            else
            {
                edgeDistances[k] = MaxDistance;
            }
        }

        _edgeDistancesBuffer = _device.AllocateReadOnlyBuffer(edgeDistances);
    }

    public void UpdateNodeEnergies(double[] nodeEnergies)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized.");

        if (nodeEnergies.Length != _nodeCount)
            throw new ArgumentException($"nodeEnergies length mismatch.");

        _nodeEnergiesBuffer?.Dispose();
        _nodeEnergiesBuffer = _device.AllocateReadOnlyBuffer(nodeEnergies);
    }

    public void DetectHorizons()
    {
        if (!_initialized || _topology == null)
            throw new InvalidOperationException("Engine not initialized.");

        // Step 1: Compute local mass via CSR neighbor sum
        _device.For(_nodeCount, new LocalMassKernel(
            _topology.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer,
            _nodeEnergiesBuffer!,
            _localMassBuffer!,
            SelfMassFactor,
            _nodeCount));

        // Step 2: Compute effective radius from precomputed edge distances
        _device.For(_nodeCount, new EffectiveRadiusKernel(
            _topology.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _edgeDistancesBuffer!,
            _effectiveRadiusBuffer!,
            MaxDistance,
            _nodeCount));

        // Step 3: Copy to ReadOnlyBuffers for detection kernel
        var localMassCopy = new double[_nodeCount];
        var effectiveRadiusCopy = new double[_nodeCount];
        _localMassBuffer!.CopyTo(localMassCopy);
        _effectiveRadiusBuffer!.CopyTo(effectiveRadiusCopy);
        
        using var localMassReadOnly = _device.AllocateReadOnlyBuffer(localMassCopy);
        using var effectiveRadiusReadOnly = _device.AllocateReadOnlyBuffer(effectiveRadiusCopy);

        // Step 4: Horizon detection
        _device.For(_nodeCount, new HorizonDetectionKernel(
            localMassReadOnly,
            effectiveRadiusReadOnly,
            _horizonFlagsBuffer!,
            _schwarzschildRadiusBuffer!,
            _densityBuffer!,
            _hawkingTemperatureBuffer!,
            _entropyBuffer!,
            DensityThreshold,
            MinMassThreshold,
            _nodeCount));
    }

    public void ApplyEvaporation(double dt)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized.");

        var tempCopy = new double[_nodeCount];
        var flagsCopy = new int[_nodeCount];
        _hawkingTemperatureBuffer!.CopyTo(tempCopy);
        _horizonFlagsBuffer!.CopyTo(flagsCopy);
        
        using var tempReadOnly = _device.AllocateReadOnlyBuffer(tempCopy);
        using var flagsReadOnly = _device.AllocateReadOnlyBuffer(flagsCopy);

        _device.For(_nodeCount, new HawkingEvaporationKernel(
            _localMassBuffer!,
            tempReadOnly,
            flagsReadOnly,
            EvaporationConstant,
            dt,
            _nodeCount));
    }

    public void SyncToCpu()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized.");

        _horizonFlagsBuffer!.CopyTo(_horizonFlagsCpu);
        _localMassBuffer!.CopyTo(_localMassCpu);
        _schwarzschildRadiusBuffer!.CopyTo(_schwarzschildRadiusCpu);
        _hawkingTemperatureBuffer!.CopyTo(_hawkingTemperatureCpu);
        _densityBuffer!.CopyTo(_densityCpu);
        _entropyBuffer!.CopyTo(_entropyCpu);

        HorizonCount = 0;
        SingularityCount = 0;
        for (int i = 0; i < _nodeCount; i++)
        {
            if ((_horizonFlagsCpu[i] & 1) != 0) HorizonCount++;
            if ((_horizonFlagsCpu[i] & 2) != 0) SingularityCount++;
        }
    }

    public void Step(double dt, bool applyEvaporation = true)
    {
        DetectHorizons();
        if (applyEvaporation)
        {
            ApplyEvaporation(dt);
        }
        SyncToCpu();
    }

    public HorizonStateGpu GetNodeState(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _nodeCount)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        return new HorizonStateGpu(
            _localMassCpu[nodeIndex],
            _effectiveRadiusCpu[nodeIndex],
            _densityCpu[nodeIndex],
            _horizonFlagsCpu[nodeIndex],
            _hawkingTemperatureCpu[nodeIndex],
            _entropyCpu[nodeIndex]);
    }

    public List<int> GetHorizonNodes()
    {
        var result = new List<int>();
        for (int i = 0; i < _nodeCount; i++)
        {
            if ((_horizonFlagsCpu[i] & 1) != 0)
                result.Add(i);
        }
        return result;
    }

    public List<int> GetSingularityNodes()
    {
        var result = new List<int>();
        for (int i = 0; i < _nodeCount; i++)
        {
            if ((_horizonFlagsCpu[i] & 2) != 0)
                result.Add(i);
        }
        return result;
    }

    private void DisposeBuffers()
    {
        _localMassBuffer?.Dispose();
        _effectiveRadiusBuffer?.Dispose();
        _horizonFlagsBuffer?.Dispose();
        _schwarzschildRadiusBuffer?.Dispose();
        _densityBuffer?.Dispose();
        _hawkingTemperatureBuffer?.Dispose();
        _entropyBuffer?.Dispose();
        _nodeEnergiesBuffer?.Dispose();
        _edgeDistancesBuffer?.Dispose();

        _localMassBuffer = null;
        _effectiveRadiusBuffer = null;
        _horizonFlagsBuffer = null;
        _schwarzschildRadiusBuffer = null;
        _densityBuffer = null;
        _hawkingTemperatureBuffer = null;
        _entropyBuffer = null;
        _nodeEnergiesBuffer = null;
        _edgeDistancesBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}
