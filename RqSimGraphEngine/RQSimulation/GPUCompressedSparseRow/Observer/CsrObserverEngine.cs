using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUOptimized.Observer;
using SysMath = global::System.Math;

namespace RQSimulation.GPUCompressedSparseRow.Observer;

/// <summary>
/// CSR-optimized GPU observer engine for large sparse graphs.
/// 
/// RQ-HYPOTHESIS STAGE 5: GPU INTERNAL OBSERVER (CSR VERSION)
/// ==========================================================
/// Same functionality as GpuObserverEngine but optimized for CSR format.
/// 
/// Use this engine when:
/// - N &gt; 10? nodes (large graphs)
/// - Sparse connectivity (E &lt;&lt; N?)
/// - Memory is a constraint
/// 
/// MEMORY COMPARISON:
/// - Dense: O(N?) for adjacency + O(N * gaugeDim) for wavefunction
/// - CSR: O(E) for edges + O(N) for row pointers + O(N * gaugeDim) for wavefunction
/// 
/// All computations use double precision (64-bit).
/// </summary>
public sealed class CsrObserverEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Random _rng;
    
    // CSR Topology
    private CsrTopology? _topology;
    
    // GPU Buffers - Wavefunction
    private ReadWriteBuffer<Double2>? _wavefunctionBuffer;
    
    // GPU Buffers - Observer
    private ReadOnlyBuffer<int>? _observerNodesBuffer;
    private ReadWriteBuffer<double>? _correlationsBuffer;
    private ReadWriteBuffer<double>? _contributionsBuffer;
    
    // GPU Buffers - Probability and Entropy
    private ReadWriteBuffer<double>? _probDensityBuffer;
    private ReadWriteBuffer<double>? _entropyContribsBuffer;
    private ReadOnlyBuffer<double>? _observableBuffer;
    
    // CPU Arrays
    private int[] _observerNodesCpu = [];
    private double[] _correlationsCpu = [];
    private double[] _probDensityCpu = [];
    private double[] _entropyContribsCpu = [];
    private double[] _contributionsCpu = [];
    private double[] _observableCpu = [];
    private Double2[] _wavefunctionCpu = [];
    
    // Dimensions
    private int _nodeCount;
    private int _gaugeDim;
    private int _observerCount;
    private bool _initialized;
    private bool _disposed;
    
    // Observation tracking
    private readonly List<ObservationRecord> _observations = [];
    
    /// <summary>
    /// Coupling strength for measurement interaction.
    /// </summary>
    public double MeasurementCoupling { get; set; } = 0.1;
    
    /// <summary>
    /// Minimum correlation threshold for measurement.
    /// </summary>
    public double MinCorrelation { get; set; } = 0.01;
    
    /// <summary>
    /// Epsilon for entropy calculation.
    /// </summary>
    public double EntropyEpsilon { get; set; } = 1e-15;
    
    /// <summary>
    /// Whether the engine is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;
    
    /// <summary>
    /// Number of nodes in the graph.
    /// </summary>
    public int NodeCount => _nodeCount;
    
    /// <summary>
    /// Number of observer nodes.
    /// </summary>
    public int ObserverCount => _observerCount;
    
    /// <summary>
    /// Gauge dimension.
    /// </summary>
    public int GaugeDimension => _gaugeDim;
    
    /// <summary>
    /// Observation records.
    /// </summary>
    public IReadOnlyList<ObservationRecord> Observations => _observations;
    
    /// <summary>
    /// Create a new CSR observer engine.
    /// </summary>
    public CsrObserverEngine()
    {
        _device = GraphicsDevice.GetDefault();
        _rng = new Random();
        
        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException(
                "GPU does not support double precision. CSR Observer engine requires SM 6.0+.");
        }
    }
    
    /// <summary>
    /// Create a CSR observer engine with specific device and seed.
    /// </summary>
    public CsrObserverEngine(GraphicsDevice device, int? seed = null)
    {
        _device = device;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        
        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException("GPU does not support double precision.");
        }
    }
    
    /// <summary>
    /// Initialize the engine with CSR topology and observer configuration.
    /// </summary>
    /// <param name="topology">CSR topology (must be uploaded to GPU)</param>
    /// <param name="observerNodes">Node indices forming the observer subsystem</param>
    /// <param name="gaugeDim">Gauge dimension (default 1)</param>
    public void Initialize(CsrTopology topology, IEnumerable<int> observerNodes, int gaugeDim = 1)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(observerNodes);
        ArgumentOutOfRangeException.ThrowIfLessThan(gaugeDim, 1);
        
        if (!topology.IsGpuReady)
        {
            throw new InvalidOperationException("Topology must be uploaded to GPU first.");
        }
        
        DisposeBuffers();
        
        _topology = topology;
        _nodeCount = topology.NodeCount;
        _gaugeDim = gaugeDim;
        
        // Convert observer nodes to array
        var obsList = new List<int>();
        foreach (int node in observerNodes)
        {
            if (node < 0 || node >= _nodeCount)
                throw new ArgumentOutOfRangeException(nameof(observerNodes),
                    $"Observer node {node} out of range [0, {_nodeCount})");
            obsList.Add(node);
        }
        
        if (obsList.Count == 0)
            throw new ArgumentException("Observer must have at least one node", nameof(observerNodes));
        
        _observerCount = obsList.Count;
        _observerNodesCpu = [.. obsList];
        
        // Allocate CPU arrays
        int wfSize = _nodeCount * gaugeDim;
        _wavefunctionCpu = new Double2[wfSize];
        _correlationsCpu = new double[_observerCount];
        _probDensityCpu = new double[_nodeCount];
        _entropyContribsCpu = new double[_nodeCount];
        _contributionsCpu = new double[_observerCount];
        _observableCpu = new double[_nodeCount];
        
        // Allocate GPU buffers
        _wavefunctionBuffer = _device.AllocateReadWriteBuffer<Double2>(wfSize);
        _observerNodesBuffer = _device.AllocateReadOnlyBuffer(_observerNodesCpu);
        _correlationsBuffer = _device.AllocateReadWriteBuffer<double>(_observerCount);
        _contributionsBuffer = _device.AllocateReadWriteBuffer<double>(_observerCount);
        _probDensityBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _entropyContribsBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _observableBuffer = _device.AllocateReadOnlyBuffer<double>(_nodeCount);
        
        _observations.Clear();
        _initialized = true;
    }
    
    /// <summary>
    /// Upload wavefunction from graph to GPU.
    /// </summary>
    public void UploadWavefunction(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();
        
        Complex[]? waveMulti = graph.GetWaveMulti();
        Complex[]? waveSingle = graph.GetWavefunctionForObserver();
        
        if (waveMulti != null && waveMulti.Length > 0)
        {
            int expected = _nodeCount * _gaugeDim;
            int actual = SysMath.Min(waveMulti.Length, expected);
            
            for (int i = 0; i < actual; i++)
            {
                _wavefunctionCpu[i] = new Double2(waveMulti[i].Real, waveMulti[i].Imaginary);
            }
            for (int i = actual; i < expected; i++)
            {
                _wavefunctionCpu[i] = new Double2(0.0, 0.0);
            }
        }
        else if (waveSingle != null && waveSingle.Length > 0)
        {
            for (int n = 0; n < _nodeCount; n++)
            {
                Complex psi = n < waveSingle.Length ? waveSingle[n] : Complex.Zero;
                for (int a = 0; a < _gaugeDim; a++)
                {
                    _wavefunctionCpu[n * _gaugeDim + a] = new Double2(psi.Real, psi.Imaginary);
                }
            }
        }
        else
        {
            double norm = 1.0 / SysMath.Sqrt(_nodeCount * _gaugeDim);
            for (int i = 0; i < _wavefunctionCpu.Length; i++)
            {
                _wavefunctionCpu[i] = new Double2(norm, 0.0);
            }
        }
        
        _wavefunctionBuffer!.CopyFrom(_wavefunctionCpu);
    }
    
    /// <summary>
    /// Download wavefunction from GPU to graph.
    /// </summary>
    public void DownloadWavefunction(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();
        
        _wavefunctionBuffer!.CopyTo(_wavefunctionCpu);
        
        Complex[]? waveMulti = graph.GetWaveMulti();
        if (waveMulti != null && waveMulti.Length > 0)
        {
            int len = SysMath.Min(waveMulti.Length, _wavefunctionCpu.Length);
            for (int i = 0; i < len; i++)
            {
                waveMulti[i] = new Complex(_wavefunctionCpu[i].X, _wavefunctionCpu[i].Y);
            }
        }
    }
    
    /// <summary>
    /// Perform measurement sweep using CSR topology.
    /// Observer nodes measure all connected neighbors via controlled phase.
    /// </summary>
    /// <returns>Number of measurement interactions</returns>
    public int MeasureSweepGpu()
    {
        EnsureInitialized();
        
        // Execute CSR controlled phase kernel
        _device.For(_observerCount, new CsrControlledPhaseKernelDouble(
            _wavefunctionBuffer!,
            _topology!.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer,
            _observerNodesBuffer!,
            MeasurementCoupling,
            _gaugeDim,
            _observerCount,
            MinCorrelation));
        
        // Count interactions (based on topology)
        int interactions = 0;
        ReadOnlySpan<int> rowOffsets = _topology.RowOffsets;
        
        foreach (int obs in _observerNodesCpu)
        {
            int neighbors = rowOffsets[obs + 1] - rowOffsets[obs];
            interactions += neighbors;
            
            _observations.Add(new ObservationRecord
            {
                ObserverNode = obs,
                TargetNode = -1, // CSR measures all neighbors
                ConnectionWeight = MeasurementCoupling,
                CorrelatedPhase = MeasurementCoupling,
                Timestamp = DateTime.UtcNow
            });
        }
        
        return interactions;
    }
    
    /// <summary>
    /// Apply phase shifts to observer nodes.
    /// </summary>
    public void ApplyPhaseShiftsGpu(double[] phaseShifts)
    {
        ArgumentNullException.ThrowIfNull(phaseShifts);
        EnsureInitialized();
        
        int count = SysMath.Min(phaseShifts.Length, _observerCount);
        
        using var shiftsBuffer = _device.AllocateReadOnlyBuffer<double>(count);
        shiftsBuffer.CopyFrom(phaseShifts.Take(count).ToArray());
        
        _device.For(count, new CsrPhaseShiftKernelDouble(
            _wavefunctionBuffer!,
            _observerNodesBuffer!,
            shiftsBuffer,
            _gaugeDim,
            count));
    }
    
    /// <summary>
    /// Compute probability density for all nodes.
    /// </summary>
    public double[] ComputeProbabilityDensityGpu()
    {
        EnsureInitialized();
        
        _device.For(_nodeCount, new CsrProbabilityDensityKernelDouble(
            _wavefunctionBuffer!,
            _probDensityBuffer!,
            _gaugeDim,
            _nodeCount));
        
        _probDensityBuffer!.CopyTo(_probDensityCpu);
        return (double[])_probDensityCpu.Clone();
    }
    
    /// <summary>
    /// Compute correlations between observer nodes and their neighbors using CSR.
    /// </summary>
    /// <returns>Total correlation per observer node</returns>
    public double[] ComputeCorrelationsGpu()
    {
        EnsureInitialized();
        
        _device.For(_observerCount, new CsrCorrelationKernelDouble(
            _wavefunctionBuffer!,
            _topology!.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer,
            _observerNodesBuffer!,
            _correlationsBuffer!,
            _gaugeDim,
            _observerCount,
            MinCorrelation));
        
        _correlationsBuffer!.CopyTo(_correlationsCpu);
        return (double[])_correlationsCpu.Clone();
    }
    
    /// <summary>
    /// Compute mutual information I(Observer : Rest).
    /// </summary>
    public double ComputeMutualInformationGpu()
    {
        EnsureInitialized();
        
        // Compute probability density
        _device.For(_nodeCount, new CsrProbabilityDensityKernelDouble(
            _wavefunctionBuffer!,
            _probDensityBuffer!,
            _gaugeDim,
            _nodeCount));
        
        _probDensityBuffer!.CopyTo(_probDensityCpu);
        
        // Normalize
        double totalProb = _probDensityCpu.Sum();
        if (totalProb < 1e-30) return 0.0;
        
        double[] normalizedProbs = new double[_nodeCount];
        for (int i = 0; i < _nodeCount; i++)
        {
            normalizedProbs[i] = _probDensityCpu[i] / totalProb;
        }
        
        var observerSet = new HashSet<int>(_observerNodesCpu);
        
        // H(O) - observer entropy
        double probO = 0.0;
        foreach (int obs in _observerNodesCpu)
        {
            probO += normalizedProbs[obs];
        }
        double H_O = probO > EntropyEpsilon ? -probO * SysMath.Log2(probO) : 0.0;
        
        // H(R) - rest entropy
        double probR = 1.0 - probO;
        double H_R = probR > EntropyEpsilon ? -probR * SysMath.Log2(probR) : 0.0;
        
        // H(total) from fine-grained distribution
        double H_total = 0.0;
        for (int i = 0; i < _nodeCount; i++)
        {
            double p = normalizedProbs[i];
            if (p > EntropyEpsilon)
            {
                H_total -= p * SysMath.Log2(p);
            }
        }
        
        return SysMath.Max(0.0, H_O + H_R - H_total);
    }
    
    /// <summary>
    /// Compute observer expectation value for an observable.
    /// </summary>
    public double ComputeObserverExpectationGpu(double[] observable)
    {
        ArgumentNullException.ThrowIfNull(observable);
        EnsureInitialized();
        
        if (observable.Length != _nodeCount)
            throw new ArgumentException($"Observable must have {_nodeCount} elements");
        
        // Compute probability density
        _device.For(_nodeCount, new CsrProbabilityDensityKernelDouble(
            _wavefunctionBuffer!,
            _probDensityBuffer!,
            _gaugeDim,
            _nodeCount));
        
        // Upload observable
        Array.Copy(observable, _observableCpu, _nodeCount);
        _observableBuffer!.CopyFrom(_observableCpu);
        
        // Compute expectation contributions
        _device.For(_observerCount, new CsrObserverExpectationKernelDouble(
            _probDensityBuffer!,
            _observableBuffer!,
            _observerNodesBuffer!,
            _contributionsBuffer!,
            _observerCount));
        
        _contributionsBuffer!.CopyTo(_contributionsCpu);
        _probDensityBuffer!.CopyTo(_probDensityCpu);
        
        double sum = 0.0;
        double normalization = 0.0;
        
        for (int i = 0; i < _observerCount; i++)
        {
            sum += _contributionsCpu[i];
            normalization += _probDensityCpu[_observerNodesCpu[i]];
        }
        
        return normalization > 1e-30 ? sum / normalization : 0.0;
    }
    
    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized. Call Initialize() first.");
    }
    
    private void DisposeBuffers()
    {
        _wavefunctionBuffer?.Dispose();
        _observerNodesBuffer?.Dispose();
        _correlationsBuffer?.Dispose();
        _contributionsBuffer?.Dispose();
        _probDensityBuffer?.Dispose();
        _entropyContribsBuffer?.Dispose();
        _observableBuffer?.Dispose();
        
        _wavefunctionBuffer = null;
        _observerNodesBuffer = null;
        _correlationsBuffer = null;
        _contributionsBuffer = null;
        _probDensityBuffer = null;
        _entropyContribsBuffer = null;
        _observableBuffer = null;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}
