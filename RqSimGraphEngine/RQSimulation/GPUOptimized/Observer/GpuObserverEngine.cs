using System;
using System.Collections.Generic;
using System.Numerics;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.Observer;

/// <summary>
/// GPU-accelerated internal observer engine for relational measurements.
/// 
/// RQ-HYPOTHESIS STAGE 5: GPU INTERNAL OBSERVER
/// =============================================
/// Implements the relational quantum mechanics approach where measurement
/// is performed by a subsystem (observer) becoming entangled with the target,
/// rather than by an external "God's eye view" readout.
/// 
/// KEY PRINCIPLES:
/// - Observer is part of the quantum system (subset of graph nodes)
/// - Measurement creates correlations/entanglement, not classical collapse
/// - Results are encoded in observer's phase, not extracted externally
/// - Mutual information quantifies measurement quality
/// 
/// PARALLELIZATION:
/// - Phase shifts: parallel over observer nodes
/// - Correlations: parallel over observer-target pairs
/// - Probabilities: parallel over all nodes
/// - Entropy/MI: parallel computation + reduction
/// 
/// OPTIMAL USE CASE:
/// - Dense graphs: N &lt; 10? nodes
/// - For sparse graphs: use CsrObserverEngine
/// 
/// All computations use double precision (64-bit).
/// </summary>
public sealed class GpuObserverEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Random _rng;

    // GPU Buffers - Wavefunction
    private ReadWriteBuffer<Double2>? _wavefunctionBuffer;
    
    // GPU Buffers - Observer
    private ReadOnlyBuffer<int>? _observerNodesBuffer;
    private ReadOnlyBuffer<double>? _phaseShiftsBuffer;
    private ReadWriteBuffer<double>? _contributionsBuffer;
    
    // GPU Buffers - Targets and Pairs
    private ReadOnlyBuffer<int>? _targetNodesBuffer;
    private ReadOnlyBuffer<double>? _connectionWeightsBuffer;
    private ReadWriteBuffer<double>? _correlationsBuffer;
    private ReadOnlyBuffer<double>? _couplingsBuffer;
    
    // GPU Buffers - Probability and Entropy
    private ReadWriteBuffer<double>? _probDensityBuffer;
    private ReadWriteBuffer<double>? _entropyContribsBuffer;
    private ReadOnlyBuffer<double>? _observableBuffer;
    
    // GPU Buffers - Reduction
    private ReadWriteBuffer<double>? _reductionBuffer;
    
    // CPU Arrays
    private int[] _observerNodesCpu = [];
    private double[] _phaseShiftsCpu = [];
    private int[] _targetNodesCpu = [];
    private double[] _connectionWeightsCpu = [];
    private double[] _correlationsCpu = [];
    private double[] _couplingsCpu = [];
    private double[] _probDensityCpu = [];
    private double[] _entropyContribsCpu = [];
    private double[] _contributionsCpu = [];
    private double[] _observableCpu = [];
    private Double2[] _wavefunctionCpu = [];
    
    // Dimensions
    private int _nodeCount;
    private int _gaugeDim;
    private int _observerCount;
    private int _maxPairs;
    private bool _initialized;
    private bool _disposed;
    
    // Observation tracking
    private readonly List<ObservationRecord> _observations = [];
    
    /// <summary>
    /// Coupling strength for measurement interaction.
    /// Controls entanglement strength between observer and target.
    /// </summary>
    public double MeasurementCoupling { get; set; } = 0.1;
    
    /// <summary>
    /// Minimum correlation threshold for measurement to register.
    /// </summary>
    public double MinCorrelation { get; set; } = 0.01;
    
    /// <summary>
    /// Epsilon for entropy calculation (avoid log(0)).
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
    /// Gauge dimension (components per node).
    /// </summary>
    public int GaugeDimension => _gaugeDim;
    
    /// <summary>
    /// All observation records accumulated by this engine.
    /// </summary>
    public IReadOnlyList<ObservationRecord> Observations => _observations;
    
    /// <summary>
    /// Total number of measurements performed.
    /// </summary>
    public int MeasurementCount => _observations.Count;

    /// <summary>
    /// Create a new GPU observer engine.
    /// </summary>
    public GpuObserverEngine()
    {
        _device = GraphicsDevice.GetDefault();
        _rng = new Random();
        
        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException(
                "GPU does not support double precision. " +
                "Observer engine requires SM 6.0+ for accurate phase calculations.");
        }
    }
    
    /// <summary>
    /// Create a GPU observer engine with specific device and seed.
    /// </summary>
    public GpuObserverEngine(GraphicsDevice device, int? seed = null)
    {
        _device = device;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        
        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException("GPU does not support double precision.");
        }
    }
    
    /// <summary>
    /// Initialize the engine with graph and observer configuration.
    /// </summary>
    /// <param name="nodeCount">Total number of nodes in the graph</param>
    /// <param name="observerNodes">Node indices forming the observer subsystem</param>
    /// <param name="gaugeDim">Gauge dimension (components per node, default 1)</param>
    public void Initialize(int nodeCount, IEnumerable<int> observerNodes, int gaugeDim = 1)
    {
        ArgumentNullException.ThrowIfNull(observerNodes);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(gaugeDim, 1);
        
        DisposeBuffers();
        
        _nodeCount = nodeCount;
        _gaugeDim = gaugeDim;
        
        // Convert observer nodes to array
        var obsList = new List<int>();
        foreach (int node in observerNodes)
        {
            if (node < 0 || node >= nodeCount)
                throw new ArgumentOutOfRangeException(nameof(observerNodes),
                    $"Observer node {node} is out of range [0, {nodeCount})");
            obsList.Add(node);
        }
        
        if (obsList.Count == 0)
            throw new ArgumentException("Observer must contain at least one node", nameof(observerNodes));
        
        _observerCount = obsList.Count;
        _observerNodesCpu = [.. obsList];
        
        // Max pairs: each observer can measure any non-observer node
        _maxPairs = _observerCount * (nodeCount - _observerCount);
        _maxPairs = Math.Max(1, _maxPairs);
        
        // Allocate CPU arrays
        int wfSize = nodeCount * gaugeDim;
        _wavefunctionCpu = new Double2[wfSize];
        _phaseShiftsCpu = new double[_observerCount];
        _targetNodesCpu = new int[_maxPairs];
        _connectionWeightsCpu = new double[_maxPairs];
        _correlationsCpu = new double[_maxPairs];
        _couplingsCpu = new double[_maxPairs];
        _probDensityCpu = new double[nodeCount];
        _entropyContribsCpu = new double[nodeCount];
        _contributionsCpu = new double[_observerCount];
        _observableCpu = new double[nodeCount];
        
        // Allocate GPU buffers
        _wavefunctionBuffer = _device.AllocateReadWriteBuffer<Double2>(wfSize);
        _observerNodesBuffer = _device.AllocateReadOnlyBuffer(_observerNodesCpu);
        _phaseShiftsBuffer = _device.AllocateReadOnlyBuffer<double>(_observerCount);
        _contributionsBuffer = _device.AllocateReadWriteBuffer<double>(_observerCount);
        _targetNodesBuffer = _device.AllocateReadOnlyBuffer<int>(_maxPairs);
        _connectionWeightsBuffer = _device.AllocateReadOnlyBuffer<double>(_maxPairs);
        _correlationsBuffer = _device.AllocateReadWriteBuffer<double>(_maxPairs);
        _couplingsBuffer = _device.AllocateReadOnlyBuffer<double>(_maxPairs);
        _probDensityBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _entropyContribsBuffer = _device.AllocateReadWriteBuffer<double>(nodeCount);
        _observableBuffer = _device.AllocateReadOnlyBuffer<double>(nodeCount);
        _reductionBuffer = _device.AllocateReadWriteBuffer<double>(Math.Max(nodeCount, _maxPairs));
        
        _observations.Clear();
        _initialized = true;
    }
    
    /// <summary>
    /// Upload wavefunction from graph to GPU.
    /// </summary>
    /// <param name="graph">RQGraph with wavefunction data</param>
    public void UploadWavefunction(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();
        
        // Extract wavefunction from graph
        Complex[]? waveMulti = graph.GetWaveMulti();
        Complex[]? waveSingle = graph.GetWavefunctionForObserver();
        
        if (waveMulti != null && waveMulti.Length > 0)
        {
            // Use multi-component wavefunction
            int expected = _nodeCount * _gaugeDim;
            int actual = Math.Min(waveMulti.Length, expected);
            
            for (int i = 0; i < actual; i++)
            {
                _wavefunctionCpu[i] = new Double2(waveMulti[i].Real, waveMulti[i].Imaginary);
            }
            // Zero-pad if necessary
            for (int i = actual; i < expected; i++)
            {
                _wavefunctionCpu[i] = new Double2(0.0, 0.0);
            }
        }
        else if (waveSingle != null && waveSingle.Length > 0)
        {
            // Use single-component wavefunction, replicate to gauge dim
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
            // Initialize to uniform superposition
            double norm = 1.0 / Math.Sqrt(_nodeCount * _gaugeDim);
            for (int i = 0; i < _wavefunctionCpu.Length; i++)
            {
                _wavefunctionCpu[i] = new Double2(norm, 0.0);
            }
        }
        
        _wavefunctionBuffer!.CopyFrom(_wavefunctionCpu);
    }
    
    /// <summary>
    /// Download wavefunction from GPU back to graph.
    /// </summary>
    /// <param name="graph">RQGraph to update</param>
    public void DownloadWavefunction(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();
        
        _wavefunctionBuffer!.CopyTo(_wavefunctionCpu);
        
        // Convert back to Complex arrays
        Complex[]? waveMulti = graph.GetWaveMulti();
        if (waveMulti != null && waveMulti.Length > 0)
        {
            int len = Math.Min(waveMulti.Length, _wavefunctionCpu.Length);
            for (int i = 0; i < len; i++)
            {
                waveMulti[i] = new Complex(_wavefunctionCpu[i].X, _wavefunctionCpu[i].Y);
            }
        }
    }
    
    /// <summary>
    /// Perform measurement sweep: observer measures all connected target nodes.
    /// Creates entanglement via controlled phase gates.
    /// </summary>
    /// <param name="graph">RQGraph providing connectivity and weights</param>
    /// <param name="targetNodes">Optional specific targets (null = all non-observer nodes)</param>
    /// <returns>Number of measurement interactions performed</returns>
    public int MeasureSweepGpu(RQGraph graph, IEnumerable<int>? targetNodes = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();
        
        var observerSet = new HashSet<int>(_observerNodesCpu);
        
        // Build list of observer-target pairs with connections
        var pairs = new List<(int obs, int tgt, double weight)>();
        
        IEnumerable<int> targets = targetNodes ?? Enumerable.Range(0, _nodeCount).Where(n => !observerSet.Contains(n));
        
        foreach (int obsNode in _observerNodesCpu)
        {
            foreach (int tgtNode in targets)
            {
                if (observerSet.Contains(tgtNode)) continue;
                if (tgtNode < 0 || tgtNode >= _nodeCount) continue;
                
                // Check connectivity and get weight
                if (graph.Edges[obsNode, tgtNode])
                {
                    double w = graph.Weights[obsNode, tgtNode];
                    if (w >= MinCorrelation)
                    {
                        pairs.Add((obsNode, tgtNode, w));
                    }
                }
            }
        }
        
        if (pairs.Count == 0)
            return 0;
        
        int pairCount = Math.Min(pairs.Count, _maxPairs);
        
        // Prepare arrays for GPU
        for (int i = 0; i < pairCount; i++)
        {
            var (obs, tgt, w) = pairs[i];
            _observerNodesCpu[i % _observerCount] = obs; // Reuse for control nodes
            _targetNodesCpu[i] = tgt;
            _connectionWeightsCpu[i] = w;
            _couplingsCpu[i] = MeasurementCoupling * w;
        }
        
        // Upload pair data
        using var controlNodesBuffer = _device.AllocateReadOnlyBuffer<int>(pairCount);
        controlNodesBuffer.CopyFrom(pairs.Select(p => p.tgt).Take(pairCount).ToArray());
        
        using var targetNodesForPhase = _device.AllocateReadOnlyBuffer<int>(pairCount);
        targetNodesForPhase.CopyFrom(pairs.Select(p => p.obs).Take(pairCount).ToArray());
        
        using var couplingsTemp = _device.AllocateReadOnlyBuffer<double>(pairCount);
        couplingsTemp.CopyFrom(_couplingsCpu.Take(pairCount).ToArray());
        
        // Execute controlled phase kernel
        _device.For(pairCount, new ControlledPhaseKernelDouble(
            _wavefunctionBuffer!,
            controlNodesBuffer,
            targetNodesForPhase,
            couplingsTemp,
            _gaugeDim,
            pairCount));
        
        // Record observations
        foreach (var (obs, tgt, w) in pairs.Take(pairCount))
        {
            _observations.Add(new ObservationRecord
            {
                ObserverNode = obs,
                TargetNode = tgt,
                ConnectionWeight = w,
                CorrelatedPhase = MeasurementCoupling * w,
                Timestamp = DateTime.UtcNow
            });
        }
        
        return pairCount;
    }
    
    /// <summary>
    /// Apply phase shifts to observer nodes on GPU.
    /// </summary>
    /// <param name="phaseShifts">Phase shifts for each observer node</param>
    public void ApplyPhaseShiftsGpu(double[] phaseShifts)
    {
        ArgumentNullException.ThrowIfNull(phaseShifts);
        EnsureInitialized();
        
        int count = Math.Min(phaseShifts.Length, _observerCount);
        Array.Copy(phaseShifts, _phaseShiftsCpu, count);
        
        using var shiftsBuffer = _device.AllocateReadOnlyBuffer<double>(count);
        shiftsBuffer.CopyFrom(_phaseShiftsCpu.Take(count).ToArray());
        
        _device.For(count, new PhaseShiftKernelDouble(
            _wavefunctionBuffer!,
            _observerNodesBuffer!,
            shiftsBuffer,
            _gaugeDim,
            count));
    }
    
    /// <summary>
    /// Compute probability density for all nodes on GPU.
    /// </summary>
    /// <returns>Array of probability densities</returns>
    public double[] ComputeProbabilityDensityGpu()
    {
        EnsureInitialized();
        
        _device.For(_nodeCount, new ProbabilityDensityKernelDouble(
            _wavefunctionBuffer!,
            _probDensityBuffer!,
            _gaugeDim,
            _nodeCount));
        
        _probDensityBuffer!.CopyTo(_probDensityCpu);
        return (double[])_probDensityCpu.Clone();
    }
    
    /// <summary>
    /// Compute correlations between observer and target nodes on GPU.
    /// </summary>
    /// <param name="graph">RQGraph providing connectivity</param>
    /// <returns>Dictionary of (obsNode, tgtNode) -> correlation</returns>
    public Dictionary<(int, int), double> ComputeCorrelationsGpu(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();
        
        var observerSet = new HashSet<int>(_observerNodesCpu);
        var pairs = new List<(int obs, int tgt, double w)>();
        
        foreach (int obs in _observerNodesCpu)
        {
            for (int tgt = 0; tgt < _nodeCount; tgt++)
            {
                if (observerSet.Contains(tgt)) continue;
                if (graph.Edges[obs, tgt])
                {
                    double w = graph.Weights[obs, tgt];
                    if (w >= MinCorrelation)
                    {
                        pairs.Add((obs, tgt, w));
                    }
                }
            }
        }
        
        var result = new Dictionary<(int, int), double>();
        if (pairs.Count == 0) return result;
        
        int pairCount = Math.Min(pairs.Count, _maxPairs);
        
        var obsArr = pairs.Select(p => p.obs).Take(pairCount).ToArray();
        var tgtArr = pairs.Select(p => p.tgt).Take(pairCount).ToArray();
        var wArr = pairs.Select(p => p.w).Take(pairCount).ToArray();
        
        using var obsBuffer = _device.AllocateReadOnlyBuffer(obsArr);
        using var tgtBuffer = _device.AllocateReadOnlyBuffer(tgtArr);
        using var wBuffer = _device.AllocateReadOnlyBuffer(wArr);
        using var corrBuffer = _device.AllocateReadWriteBuffer<double>(pairCount);
        
        _device.For(pairCount, new CorrelationKernelDouble(
            _wavefunctionBuffer!,
            obsBuffer,
            tgtBuffer,
            wBuffer,
            corrBuffer,
            _gaugeDim,
            pairCount));
        
        double[] corrCpu = new double[pairCount];
        corrBuffer.CopyTo(corrCpu);
        
        for (int i = 0; i < pairCount; i++)
        {
            result[(obsArr[i], tgtArr[i])] = corrCpu[i];
        }
        
        return result;
    }
    
    /// <summary>
    /// Compute mutual information I(Observer : Rest) on GPU.
    /// I(O:R) = H(O) + H(R) - H(O,R) where H is Shannon entropy.
    /// </summary>
    /// <returns>Mutual information in bits</returns>
    public double ComputeMutualInformationGpu()
    {
        EnsureInitialized();
        
        // First compute probability density
        _device.For(_nodeCount, new ProbabilityDensityKernelDouble(
            _wavefunctionBuffer!,
            _probDensityBuffer!,
            _gaugeDim,
            _nodeCount));
        
        _probDensityBuffer!.CopyTo(_probDensityCpu);
        
        // Normalize probabilities
        double totalProb = _probDensityCpu.Sum();
        if (totalProb < 1e-30) return 0.0;
        
        double[] normalizedProbs = new double[_nodeCount];
        for (int i = 0; i < _nodeCount; i++)
        {
            normalizedProbs[i] = _probDensityCpu[i] / totalProb;
        }
        
        // Compute entropies on CPU (small arrays, GPU overhead not worth it)
        var observerSet = new HashSet<int>(_observerNodesCpu);
        
        // H(O) - observer subsystem entropy
        double probO = 0.0;
        foreach (int obs in _observerNodesCpu)
        {
            probO += normalizedProbs[obs];
        }
        double H_O = probO > EntropyEpsilon ? -probO * Math.Log2(probO) : 0.0;
        
        // H(R) - rest subsystem entropy  
        double probR = 1.0 - probO;
        double H_R = probR > EntropyEpsilon ? -probR * Math.Log2(probR) : 0.0;
        
        // H(O,R) = H(total) - computed from fine-grained distribution
        double H_total = 0.0;
        for (int i = 0; i < _nodeCount; i++)
        {
            double p = normalizedProbs[i];
            if (p > EntropyEpsilon)
            {
                H_total -= p * Math.Log2(p);
            }
        }
        
        // I(O:R) = H(O) + H(R) - H(O,R)
        // For product states I=0, for maximally entangled states I is maximal
        double mutualInfo = H_O + H_R - H_total;
        
        // Mutual information is always non-negative
        return Math.Max(0.0, mutualInfo);
    }
    
    /// <summary>
    /// Compute observer expectation value ?O? for a given observable.
    /// </summary>
    /// <param name="observable">Observable values per node (e.g., local energy)</param>
    /// <returns>Expectation value ?O? = ? |?_i|? O_i for observer nodes</returns>
    public double ComputeObserverExpectationGpu(double[] observable)
    {
        ArgumentNullException.ThrowIfNull(observable);
        EnsureInitialized();
        
        if (observable.Length != _nodeCount)
            throw new ArgumentException($"Observable array must have {_nodeCount} elements", nameof(observable));
        
        // Compute probability density first
        _device.For(_nodeCount, new ProbabilityDensityKernelDouble(
            _wavefunctionBuffer!,
            _probDensityBuffer!,
            _gaugeDim,
            _nodeCount));
        
        // Upload observable
        Array.Copy(observable, _observableCpu, _nodeCount);
        _observableBuffer!.CopyFrom(_observableCpu);
        
        // Compute expectation contributions
        _device.For(_observerCount, new ObserverExpectationKernelDouble(
            _probDensityBuffer!,
            _observableBuffer!,
            _observerNodesBuffer!,
            _contributionsBuffer!,
            _observerCount));
        
        // Sum on CPU (small array)
        _contributionsBuffer!.CopyTo(_contributionsCpu);
        
        double sum = 0.0;
        double normalization = 0.0;
        
        // Also get prob density for normalization
        _probDensityBuffer!.CopyTo(_probDensityCpu);
        
        for (int i = 0; i < _observerCount; i++)
        {
            sum += _contributionsCpu[i];
            normalization += _probDensityCpu[_observerNodesCpu[i]];
        }
        
        return normalization > 1e-30 ? sum / normalization : 0.0;
    }
    
    /// <summary>
    /// Get correlation with a specific region (set of target nodes).
    /// </summary>
    /// <param name="graph">RQGraph providing connectivity</param>
    /// <param name="targetNodes">Target region nodes</param>
    /// <returns>Total correlation (sum over all observer-target pairs)</returns>
    public double GetCorrelationWithRegion(RQGraph graph, IEnumerable<int> targetNodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(targetNodes);
        
        var correlations = ComputeCorrelationsGpu(graph);
        var targetSet = new HashSet<int>(targetNodes);
        
        double total = 0.0;
        foreach (var kvp in correlations)
        {
            if (targetSet.Contains(kvp.Key.Item2))
            {
                total += kvp.Value;
            }
        }
        
        return total;
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
        _phaseShiftsBuffer?.Dispose();
        _contributionsBuffer?.Dispose();
        _targetNodesBuffer?.Dispose();
        _connectionWeightsBuffer?.Dispose();
        _correlationsBuffer?.Dispose();
        _couplingsBuffer?.Dispose();
        _probDensityBuffer?.Dispose();
        _entropyContribsBuffer?.Dispose();
        _observableBuffer?.Dispose();
        _reductionBuffer?.Dispose();
        
        _wavefunctionBuffer = null;
        _observerNodesBuffer = null;
        _phaseShiftsBuffer = null;
        _contributionsBuffer = null;
        _targetNodesBuffer = null;
        _connectionWeightsBuffer = null;
        _correlationsBuffer = null;
        _couplingsBuffer = null;
        _probDensityBuffer = null;
        _entropyContribsBuffer = null;
        _observableBuffer = null;
        _reductionBuffer = null;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}

/// <summary>
/// Record of a single observation event.
/// </summary>
public class ObservationRecord
{
    /// <summary>
    /// Node in the observer subsystem that performed measurement.
    /// </summary>
    public int ObserverNode { get; init; }
    
    /// <summary>
    /// Target node that was measured.
    /// </summary>
    public int TargetNode { get; init; }
    
    /// <summary>
    /// Phase shift correlated with target's state.
    /// </summary>
    public double CorrelatedPhase { get; init; }
    
    /// <summary>
    /// Connection weight between observer and target.
    /// </summary>
    public double ConnectionWeight { get; init; }
    
    /// <summary>
    /// Timestamp of the observation.
    /// </summary>
    public DateTime Timestamp { get; init; }
}
