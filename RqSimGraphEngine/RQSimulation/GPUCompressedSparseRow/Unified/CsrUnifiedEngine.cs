using System;
using System.Linq;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.Observer;
using RQSimulation.GPUCompressedSparseRow.QuantumEdges;
using RQSimulation.GPUCompressedSparseRow.Solvers;
using RQSimulation.GPUOptimized.Rendering;

namespace RQSimulation.GPUCompressedSparseRow.Unified;

/// <summary>
/// Unified CSR engine for all GPU operations on large sparse graphs.
/// 
/// RQ-HYPOTHESIS STAGE 6: CSR UNIFIED ENGINE
/// =========================================
/// Combines all GPU physics operations with shared CSR topology:
/// - Wheeler-DeWitt constraint computation
/// - Spectral action computation
/// - Quantum edge evolution (UNITARY - no random!)
/// - Lapse function for emergent time
/// - Internal observer measurements
/// 
/// PARADIGM SHIFT (RQG-HYPOTHESIS):
/// ================================
/// - NO System.Random in physics cycle
/// - All randomness comes from initial superposition
/// - Time emerges from Lapse function N = 1/(1 + ?|H|)
/// - Evolution is strictly unitary: ?(t+dt) = exp(-iH·dt_local)·?(t)
/// 
/// All computations use double precision (64-bit).
/// </summary>
public sealed class CsrUnifiedEngine : IDisposable
{
    private readonly GraphicsDevice _device;

    // Shared CSR Topology
    private CsrTopology? _topology;

    // GPU Buffers - Shared state
    private ReadWriteBuffer<double>? _massesBuffer;
    private ReadWriteBuffer<double>? _curvaturesBuffer;
    private ReadWriteBuffer<double>? _violationsBuffer;
    private ReadWriteBuffer<double>? _nodeActionsBuffer;
    private ReadWriteBuffer<double>? _volumeContribsBuffer;
    private ReadWriteBuffer<double>? _weylContribsBuffer;
    private ReadWriteBuffer<double>? _reductionBuffer;

    // GPU Buffers - Quantum edges
    private ReadWriteBuffer<Double2>? _edgeAmplitudesBuffer;
    private ReadOnlyBuffer<double>? _edgeHamiltoniansBuffer;

    // RQG-HYPOTHESIS: Lapse field buffers for emergent time
    private ReadWriteBuffer<double>? _lapseBuffer;
    private ReadWriteBuffer<double>? _hamiltonianViolationBuffer;
    private ReadWriteBuffer<double>? _localDtBuffer;

    // CPU Arrays
    private double[] _massesCpu = [];
    private double[] _curvaturesCpu = [];
    private double[] _violationsCpu = [];
    private double[] _nodeActionsCpu = [];
    private double[] _volumeContribsCpu = [];
    private double[] _weylContribsCpu = [];
    private Double2[] _edgeAmplitudesCpu = [];
    private double[] _edgeHamiltoniansCpu = [];

    // RQG-HYPOTHESIS: CPU arrays for Lapse and Hamiltonian
    private double[] _lapseCpu = [];
    private double[] _hamiltonianViolationCpu = [];
    private double[] _localDtCpu = [];

    // Sub-engines (lazy initialization)
    private CsrObserverEngine? _observerEngine;
    private GpuBiCGStabSolverCsr? _biCGStabSolver;

    // Dimensions
    private int _nodeCount;
    private int _nnz;
    private double _avgDegree;
    private bool _initialized;
    private bool _disposed;
    private int _topologySignature;

    // Physics parameters
    /// <summary>Gravitational coupling ?</summary>
    public double Kappa { get; set; } = 1.0;

    /// <summary>Constraint Lagrange multiplier ?</summary>
    public double Lambda { get; set; } = 10.0;

    /// <summary>Link cost coefficient for MCMC</summary>
    public double LinkCostCoeff { get; set; } = 1.0;

    /// <summary>Mass coefficient</summary>
    public double MassCoeff { get; set; } = 0.1;

    /// <summary>Target degree</summary>
    public double TargetDegree { get; set; } = 4.0;

    /// <summary>Degree penalty coefficient</summary>
    public double DegreePenaltyCoeff { get; set; } = 0.5;

    /// <summary>Spectral cutoff ?</summary>
    public double SpectralCutoff { get; set; } = 1.0;

    /// <summary>Cosmological coefficient f?</summary>
    public double F0 { get; set; } = 1.0;

    /// <summary>Einstein coefficient f?</summary>
    public double F2 { get; set; } = 0.1;

    /// <summary>Weyl coefficient f?</summary>
    public double F4 { get; set; } = 0.01;

    /// <summary>Dimension term coefficient</summary>
    public double DimensionCoeff { get; set; } = 0.05;

    /// <summary>Target spectral dimension</summary>
    public double TargetDimension { get; set; } = 4.0;

    /// <summary>Inverse temperature ? for MCMC</summary>
    public double Beta { get; set; } = 1.0;

    // RQG-HYPOTHESIS: Lapse function parameters
    /// <summary>
    /// Regularization constant ? for Lapse function.
    /// N = 1 / (1 + ?|H|)
    /// </summary>
    public double LapseAlpha { get; set; } = 1.0;

    /// <summary>Minimum allowed Lapse value.</summary>
    public double MinLapse { get; set; } = 1e-10;

    /// <summary>Maximum Lapse value (normal time flow).</summary>
    public double MaxLapse { get; set; } = 1.0;

    // State
    /// <summary>Whether engine is initialized</summary>
    public bool IsInitialized => _initialized;

    /// <summary>Node count</summary>
    public int NodeCount => _nodeCount;

    /// <summary>Non-zero edge count</summary>
    public int Nnz => _nnz;

    /// <summary>Average degree</summary>
    public double AverageDegree => _avgDegree;

    // Cached computation results
    private double _lastConstraintViolation;
    private double _lastSpectralAction;
    private double _lastEuclideanAction;
    private double _lastTotalProbability;

    /// <summary>Last computed constraint violation</summary>
    public double LastConstraintViolation => _lastConstraintViolation;

    /// <summary>Last computed spectral action</summary>
    public double LastSpectralAction => _lastSpectralAction;

    /// <summary>Last computed Euclidean action</summary>
    public double LastEuclideanAction => _lastEuclideanAction;

    /// <summary>RQG-HYPOTHESIS: Last computed total probability |?|?.</summary>
    public double LastTotalProbability => _lastTotalProbability;

    /// <summary>
    /// Create a new CSR unified engine.
    /// RQG-HYPOTHESIS: No Random field - all randomness from initial superposition.
    /// </summary>
    public CsrUnifiedEngine()
    {
        _device = GraphicsDevice.GetDefault();

        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException(
                "GPU does not support double precision. " +
                "CSR Unified Engine requires SM 6.0+ for accurate physics.");
        }

        Kappa = PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling;
    }

    /// <summary>
    /// Create a CSR unified engine with specific device.
    /// </summary>
    public CsrUnifiedEngine(GraphicsDevice device)
    {
        _device = device;

        if (!_device.IsDoublePrecisionSupportAvailable())
        {
            throw new NotSupportedException("GPU does not support double precision.");
        }

        Kappa = PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling;
    }

    /// <summary>
    /// Initialize the unified engine from an RQGraph.
    /// </summary>
    public void Initialize(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        DisposeBuffers();

        _nodeCount = graph.N;
        _sourceGraph = graph; // keep for render updates

        // Build CSR topology
        _topology = new CsrTopology(_device);
        _topology.BuildFromDenseMatrix(graph.Edges, graph.Weights, GetNodePotential(graph));
        _topology.UploadToGpu();

        _nnz = _topology.Nnz;
        _avgDegree = _nodeCount > 0 ? (double)_nnz / _nodeCount : 0.0;

        // Prepare render buffer (float) for visualization
        try
        {
            _renderBuffer = _device.AllocateReadWriteBuffer<RQSimulation.GPUOptimized.Rendering.RenderNodeVertex>(_nodeCount);
        }
        catch
        {
            _renderBuffer = null; // fallback when allocation fails
        }

        // Allocate CPU arrays
        _massesCpu = new double[_nodeCount];
        _curvaturesCpu = new double[_nodeCount];
        _violationsCpu = new double[_nodeCount];
        _nodeActionsCpu = new double[_nodeCount];
        _lapseCpu = new double[_nodeCount];
        _hamiltonianViolationCpu = new double[_nodeCount];
        _localDtCpu = new double[_nodeCount];

        // Initialize Lapse to 1.0 (flat space)
        Array.Fill(_lapseCpu, 1.0);

        // Edge-indexed arrays
        int edgeCount = _nnz / 2;
        int minEdge = System.Math.Max(1, edgeCount);
        int minNnz = System.Math.Max(1, _nnz);

        _volumeContribsCpu = new double[minNnz];
        _weylContribsCpu = new double[_nodeCount];
        _edgeAmplitudesCpu = new Double2[minEdge];
        _edgeHamiltoniansCpu = new double[minEdge];

        // Allocate GPU buffers
        _massesBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _curvaturesBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _violationsBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _nodeActionsBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _volumeContribsBuffer = _device.AllocateReadWriteBuffer<double>(minNnz);
        _weylContribsBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _reductionBuffer = _device.AllocateReadWriteBuffer<double>(System.Math.Max(_nodeCount, _nnz));

        // Quantum edge buffers
        _edgeAmplitudesBuffer = _device.AllocateReadWriteBuffer<Double2>(minEdge);
        _edgeHamiltoniansBuffer = _device.AllocateReadOnlyBuffer<double>(minEdge);

        // RQG-HYPOTHESIS: Lapse field GPU buffers
        _lapseBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _hamiltonianViolationBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        _localDtBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);

        // Upload initial data
        UploadMasses(graph);
        UploadLapseField();
        InitializeEdgeAmplitudes();

        // Upload initial render data from graph into render buffer
        UpdateRenderBufferFromGraph();

        _initialized = true;
        _topologySignature = ComputeTopologySignature(graph);
    }

    /// <summary>
    /// Update CSR topology when graph structure changes.
    /// </summary>
    public void UpdateTopology(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.N != _nodeCount)
        {
            Initialize(graph);
            return;
        }

        _topology!.BuildFromDenseMatrix(graph.Edges, graph.Weights, GetNodePotential(graph));
        _topology.UploadToGpu();

        int oldNnz = _nnz;
        _nnz = _topology.Nnz;
        _avgDegree = _nodeCount > 0 ? (double)_nnz / _nodeCount : 0.0;

        // If edge count increased significantly, we need to resize edge buffers
        int newEdgeCount = _nnz / 2;
        int oldEdgeCount = oldNnz / 2;
        
        if (newEdgeCount > oldEdgeCount)
        {
            // Resize edge-related CPU arrays
            if (_edgeAmplitudesCpu == null || _edgeAmplitudesCpu.Length < newEdgeCount)
            {
                var oldAmplitudes = _edgeAmplitudesCpu;
                var oldHamiltonians = _edgeHamiltoniansCpu;
                
                _edgeAmplitudesCpu = new Double2[newEdgeCount];
                _edgeHamiltoniansCpu = new double[newEdgeCount];
                
                // Copy old data
                if (oldAmplitudes != null)
                {
                    Array.Copy(oldAmplitudes, _edgeAmplitudesCpu, System.Math.Min(oldAmplitudes.Length, newEdgeCount));
                }
                if (oldHamiltonians != null)
                {
                    Array.Copy(oldHamiltonians, _edgeHamiltoniansCpu, System.Math.Min(oldHamiltonians.Length, newEdgeCount));
                }
                
                // Initialize new edges
                double norm = 1.0 / System.Math.Sqrt(newEdgeCount);
                for (int i = oldEdgeCount; i < newEdgeCount; i++)
                {
                    _edgeAmplitudesCpu[i] = new Double2(norm, 0.0);
                    _edgeHamiltoniansCpu[i] = 1.0;
                }
            }
            
            // Resize GPU buffers
            _edgeAmplitudesBuffer?.Dispose();
            _edgeHamiltoniansBuffer?.Dispose();
            _edgeAmplitudesBuffer = _device.AllocateReadWriteBuffer<Double2>(newEdgeCount);
            _edgeHamiltoniansBuffer = _device.AllocateReadOnlyBuffer<double>(newEdgeCount);
            
            // Upload to GPU
            _edgeAmplitudesBuffer.CopyFrom(_edgeAmplitudesCpu.AsSpan(0, newEdgeCount).ToArray());
            _edgeHamiltoniansBuffer.CopyFrom(_edgeHamiltoniansCpu.AsSpan(0, newEdgeCount).ToArray());
            
            // Also resize volume contributions buffer if needed
            if (_volumeContribsCpu == null || _volumeContribsCpu.Length < _nnz)
            {
                _volumeContribsCpu = new double[_nnz];
                _volumeContribsBuffer?.Dispose();
                _volumeContribsBuffer = _device.AllocateReadWriteBuffer<double>(_nnz);
            }
            
            // Resize reduction buffer if needed
            int maxSize = System.Math.Max(_nodeCount, _nnz);
            if (_reductionBuffer == null || _reductionBuffer.Length < maxSize)
            {
                _reductionBuffer?.Dispose();
                _reductionBuffer = _device.AllocateReadWriteBuffer<double>(maxSize);
            }
        }

        UploadMasses(graph);
        _topologySignature = ComputeTopologySignature(graph);
    }

    /// <summary>
    /// Execute unified physics step on GPU (legacy method).
    /// </summary>
    public void PhysicsStepGpu(double dt)
    {
        EnsureInitialized();

        ComputeConstraintGpu();
        ComputeSpectralActionGpu(TargetDimension);
        EvolveQuantumEdgesGpu(dt);

        // Update render buffer after physics step (CPU->GPU upload fallback)
        UpdateRenderBufferFromGraph();
    }

    /// <summary>
    /// RQG-HYPOTHESIS: Execute unitary physics step with emergent time.
    /// NO RANDOM VALUES USED - purely deterministic unitary evolution.
    /// </summary>
    public void PhysicsStepRqgGpu(double deltaLambda)
    {
        EnsureInitialized();

        ComputeHamiltonianConstraintGpu();
        ComputeLapseFieldGpu();
        ComputeLocalTimeStepsGpu(deltaLambda);
        EvolveQuantumEdgesWithLapseGpu();
        _lastTotalProbability = ComputeTotalProbabilityGpu();

        // Update render buffer after RQG physics
        UpdateRenderBufferFromGraph();
    }

    /// <summary>
    /// Compute Wheeler-DeWitt constraint violation on GPU.
    /// </summary>
    public double ComputeConstraintViolationGpu()
    {
        EnsureInitialized();
        return ComputeConstraintGpu();
    }

    private double ComputeConstraintGpu()
    {
        using var massesReadOnly = _device.AllocateReadOnlyBuffer(_massesCpu);

        _device.For(_nodeCount, new CsrUnifiedConstraintKernelDouble(
            _topology!.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer,
            massesReadOnly,
            _avgDegree,
            _curvaturesBuffer!,
            _violationsBuffer!,
            Kappa,
            _nodeCount));

        _lastConstraintViolation = SumBuffer(_violationsBuffer!, _nodeCount) / _nodeCount;
        return _lastConstraintViolation;
    }

    private void ComputeHamiltonianConstraintGpu()
    {
        using var massesReadOnly = _device.AllocateReadOnlyBuffer(_massesCpu);

        _device.For(_nodeCount, new CsrUnifiedConstraintKernelDouble(
            _topology!.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            _topology.EdgeWeightsBuffer,
            massesReadOnly,
            _avgDegree,
            _curvaturesBuffer!,
            _hamiltonianViolationBuffer!,
            Kappa,
            _nodeCount));

        _hamiltonianViolationBuffer!.CopyTo(_hamiltonianViolationCpu);
    }

    private void ComputeLapseFieldGpu()
    {
        using var hamiltonianReadOnly = _device.AllocateReadOnlyBuffer(_hamiltonianViolationCpu);

        _device.For(_nodeCount, new LapseFromHamiltonianKernel(
            hamiltonianReadOnly,
            _lapseBuffer!,
            LapseAlpha,
            MinLapse,
            MaxLapse,
            _nodeCount));

        _lapseBuffer!.CopyTo(_lapseCpu);
    }

    private void ComputeLocalTimeStepsGpu(double deltaLambda)
    {
        using var lapseReadOnly = _device.AllocateReadOnlyBuffer(_lapseCpu);

        _device.For(_nodeCount, new LocalTimeStepKernel(
            lapseReadOnly,
            _localDtBuffer!,
            deltaLambda,
            _nodeCount));

        _localDtBuffer!.CopyTo(_localDtCpu);
    }

    private void EvolveQuantumEdgesWithLapseGpu()
    {
        int edgeCount = _nnz / 2;
        if (edgeCount == 0) return;

        using var lapseReadOnly = _device.AllocateReadOnlyBuffer(_lapseCpu);

        _device.For(edgeCount, new QuantumPhaseEvolutionWithLapseKernel(
            _edgeAmplitudesBuffer!,
            _edgeHamiltoniansBuffer!,
            lapseReadOnly,
            _topology!.RowOffsetsBuffer,
            _topology.ColIndicesBuffer,
            edgeCount,
            _nodeCount));
    }

    /// <summary>
    /// Compute spectral action on GPU.
    /// </summary>
    public double ComputeSpectralActionGpu(double spectralDimension)
    {
        EnsureInitialized();

        _curvaturesBuffer!.CopyTo(_curvaturesCpu);
        double avgCurvature = _curvaturesCpu.Length > 0 ? _curvaturesCpu.Average() : 0.0;

        using var curvaturesReadOnly = _device.AllocateReadOnlyBuffer(_curvaturesCpu);

        int dispatchSize = System.Math.Max(_nodeCount, _nnz);
        _device.For(dispatchSize, new CsrUnifiedSpectralActionKernelDouble(
            _topology!.RowOffsetsBuffer,
            _topology.EdgeWeightsBuffer,
            curvaturesReadOnly,
            avgCurvature,
            _volumeContribsBuffer!,
            _weylContribsBuffer!,
            _nodeCount,
            _nnz));

        double V = SumBuffer(_volumeContribsBuffer!, _nnz);
        double R_total = _curvaturesCpu.Sum();
        double C2 = SumBuffer(_weylContribsBuffer!, _nodeCount);

        double dimDiff = spectralDimension - TargetDimension;
        double S_dim = DimensionCoeff * dimDiff * dimDiff;

        double Lambda4 = SpectralCutoff * SpectralCutoff * SpectralCutoff * SpectralCutoff;
        double Lambda2 = SpectralCutoff * SpectralCutoff;

        _lastSpectralAction = F0 * Lambda4 * V + F2 * Lambda2 * R_total + F4 * C2 + S_dim;
        return _lastSpectralAction;
    }

    /// <summary>
    /// Compute total Euclidean action for MCMC.
    /// </summary>
    public double ComputeEuclideanActionGpu()
    {
        EnsureInitialized();

        _curvaturesBuffer!.CopyTo(_curvaturesCpu);

        using var curvaturesReadOnly = _device.AllocateReadOnlyBuffer(_curvaturesCpu);
        using var massesReadOnly = _device.AllocateReadOnlyBuffer(_massesCpu);

        _device.For(_nodeCount, new CsrUnifiedActionKernelDouble(
            _topology!.RowOffsetsBuffer,
            _topology.EdgeWeightsBuffer,
            massesReadOnly,
            curvaturesReadOnly,
            _nodeActionsBuffer!,
            LinkCostCoeff,
            MassCoeff,
            TargetDegree,
            DegreePenaltyCoeff,
            Kappa,
            Lambda,
            _nodeCount));

        _lastEuclideanAction = SumBuffer(_nodeActionsBuffer!, _nodeCount);
        return _lastEuclideanAction;
    }

    /// <summary>
    /// Evolve quantum edge amplitudes unitarily.
    /// </summary>
    public void EvolveQuantumEdgesGpu(double dt)
    {
        EnsureInitialized();

        int edgeCount = _nnz / 2;
        if (edgeCount == 0) return;

        using var randomValuesBuffer = _device.AllocateReadOnlyBuffer<double>(edgeCount);

        _device.For(edgeCount, new CsrUnifiedQuantumEdgeKernelDouble(
            _edgeAmplitudesBuffer!,
            _edgeHamiltoniansBuffer!,
            dt,
            edgeCount,
            1, // Unitary mode
            randomValuesBuffer));
    }

    /// <summary>
    /// RQG-HYPOTHESIS: Compute total probability ?|?|?.
    /// </summary>
    public double ComputeTotalProbabilityGpu()
    {
        EnsureInitialized();

        int edgeCount = _nnz / 2;
        if (edgeCount == 0) return 0.0;

        _edgeAmplitudesBuffer!.CopyTo(_edgeAmplitudesCpu.AsSpan(0, edgeCount));

        double totalProb = 0.0;
        for (int i = 0; i < edgeCount; i++)
        {
            var amp = _edgeAmplitudesCpu[i];
            totalProb += amp.X * amp.X + amp.Y * amp.Y;
        }

        return totalProb;
    }

    /// <summary>
    /// RQG-HYPOTHESIS: Collapse all quantum edge amplitudes to classical states.
    /// Each edge is measured with probability |?|? of existing.
    /// </summary>
    public void CollapseAllEdgesGpu()
    {
        EnsureInitialized();

        int edgeCount = _nnz / 2;
        if (edgeCount == 0) return;

        // Generate random thresholds for collapse
        var random = new System.Random();
        double[] thresholds = new double[edgeCount];
        for (int i = 0; i < edgeCount; i++)
        {
            thresholds[i] = random.NextDouble();
        }

        using var thresholdsBuffer = _device.AllocateReadOnlyBuffer(thresholds);
        using var weightsBuffer = _device.AllocateReadWriteBuffer<double>(edgeCount);
        using var existsBuffer = _device.AllocateReadWriteBuffer<int>(edgeCount);

        // Initialize weights to 1.0
        double[] initialWeights = new double[edgeCount];
        Array.Fill(initialWeights, 1.0);
        weightsBuffer.CopyFrom(initialWeights);

        _device.For(edgeCount, new QuantumEdges.CsrCollapseKernelDouble(
            _edgeAmplitudesBuffer!,
            weightsBuffer,
            thresholdsBuffer,
            existsBuffer,
            edgeCount,
            0.01)); // minWeight
    }

    /// <summary>
    /// RQG-HYPOTHESIS: Verify unitarity of evolution.
    /// </summary>
    public bool VerifyUnitarity(double tolerance = 1e-10)
    {
        double totalProb = ComputeTotalProbabilityGpu();
        return System.Math.Abs(totalProb - 1.0) < tolerance;
    }

    /// <summary>
    /// Get or create observer engine for internal measurements.
    /// </summary>
    public CsrObserverEngine GetObserverEngine(int[] observerNodes, int gaugeDim = 1)
    {
        EnsureInitialized();

        if (_observerEngine == null ||
            !_observerEngine.IsInitialized ||
            _observerEngine.ObserverCount != observerNodes.Length)
        {
            _observerEngine?.Dispose();
            _observerEngine = new CsrObserverEngine(_device);
            _observerEngine.Initialize(_topology!, observerNodes, gaugeDim);
        }

        return _observerEngine;
    }

    /// <summary>
    /// Get or create BiCGStab solver for Cayley evolution.
    /// </summary>
    public GpuBiCGStabSolverCsr GetBiCGStabSolver(int gaugeDim = 1)
    {
        EnsureInitialized();

        if (_biCGStabSolver == null || !_biCGStabSolver.IsInitialized)
        {
            _biCGStabSolver?.Dispose();
            _biCGStabSolver = new GpuBiCGStabSolverCsr(_device);
            _biCGStabSolver.Initialize(_topology!, gaugeDim);
        }

        return _biCGStabSolver;
    }

    /// <summary>
    /// Download curvatures from GPU.
    /// </summary>
    public double[] GetCurvatures()
    {
        EnsureInitialized();
        _curvaturesBuffer!.CopyTo(_curvaturesCpu);
        return (double[])_curvaturesCpu.Clone();
    }

    /// <summary>
    /// Download constraint violations from GPU.
    /// </summary>
    public double[] GetConstraintViolations()
    {
        EnsureInitialized();
        _violationsBuffer!.CopyTo(_violationsCpu);
        return (double[])_violationsCpu.Clone();
    }

    /// <summary>
    /// Download edge amplitudes from GPU.
    /// </summary>
    public Double2[] GetEdgeAmplitudes()
    {
        EnsureInitialized();
        _edgeAmplitudesBuffer!.CopyTo(_edgeAmplitudesCpu);
        return (Double2[])_edgeAmplitudesCpu.Clone();
    }

    /// <summary>
    /// Get Lapse field values (CPU copy).
    /// </summary>
    public double[] GetLapseField()
    {
        EnsureInitialized();
        return (double[])_lapseCpu.Clone();
    }

    /// <summary>
    /// Get Hamiltonian constraint violations (CPU copy).
    /// </summary>
    public double[] GetHamiltonianViolations()
    {
        EnsureInitialized();
        return (double[])_hamiltonianViolationCpu.Clone();
    }

    private void UploadMasses(RQGraph graph)
    {
        graph.EnsureCorrelationMassComputed();

        double[] correlationMass = graph.CorrelationMass;
        if (correlationMass != null && correlationMass.Length == _nodeCount)
        {
            Array.Copy(correlationMass, _massesCpu, _nodeCount);
        }
        else
        {
            for (int i = 0; i < _nodeCount; i++)
            {
                _massesCpu[i] = graph.GetNodeMass(i);
            }
        }

        _massesBuffer!.CopyFrom(_massesCpu);
    }

    private double[] GetNodePotential(RQGraph graph)
    {
        if (graph.LocalPotential != null && graph.LocalPotential.Length == graph.N)
        {
            return graph.LocalPotential;
        }
        return new double[graph.N];
    }

    private void InitializeEdgeAmplitudes()
    {
        int edgeCount = _nnz / 2;
        if (edgeCount == 0) return;

        // Ensure arrays are large enough
        if (_edgeAmplitudesCpu == null || _edgeAmplitudesCpu.Length < edgeCount)
        {
            _edgeAmplitudesCpu = new Double2[edgeCount];
            _edgeHamiltoniansCpu = new double[edgeCount];
        }

        double norm = 1.0 / System.Math.Sqrt(edgeCount);
        for (int i = 0; i < edgeCount; i++)
        {
            _edgeAmplitudesCpu[i] = new Double2(norm, 0.0);
            _edgeHamiltoniansCpu[i] = 1.0;
        }

        // Ensure GPU buffers are large enough
        if (_edgeAmplitudesBuffer == null || _edgeAmplitudesBuffer.Length < edgeCount)
        {
            _edgeAmplitudesBuffer?.Dispose();
            _edgeHamiltoniansBuffer?.Dispose();
            _edgeAmplitudesBuffer = _device.AllocateReadWriteBuffer<Double2>(edgeCount);
            _edgeHamiltoniansBuffer = _device.AllocateReadOnlyBuffer<double>(edgeCount);
        }

        // Copy only the valid portion
        var amplitudeSlice = new Span<Double2>(_edgeAmplitudesCpu, 0, edgeCount).ToArray();
        var hamiltonianSlice = new Span<double>(_edgeHamiltoniansCpu, 0, edgeCount).ToArray();

        _edgeAmplitudesBuffer.CopyFrom(amplitudeSlice);
        _edgeHamiltoniansBuffer.CopyFrom(hamiltonianSlice);
    }

    private void UploadLapseField()
    {
        _lapseBuffer!.CopyFrom(_lapseCpu);
        _hamiltonianViolationBuffer!.CopyFrom(_hamiltonianViolationCpu);
    }

    // GPU buffer for render vertices (float)
    private ReadWriteBuffer<RQSimulation.GPUOptimized.Rendering.RenderNodeVertex>? _renderBuffer;

    /// <summary>
    /// Return the internal render buffer used for visualization (ComputeSharp buffer).
    /// This can be wrapped by SharedGpuBuffer for DX12 interop.
    /// </summary>
    public ReadWriteBuffer<RQSimulation.GPUOptimized.Rendering.RenderNodeVertex>? GetRenderBufferInterop()
    {
        return _renderBuffer;
    }

    // Keep reference to last source graph for rendering updates
    private RQGraph? _sourceGraph;

    /// <summary>
    /// Update render buffer from the last synchronized graph (CPU -> GPU copy).
    /// This is a fallback mapper: converts graph positions/states to RenderNodeVertex and uploads to _renderBuffer.
    /// </summary>
    public void UpdateRenderBufferFromGraph()
    {
        if (_renderBuffer is null || _sourceGraph is null)
            return;

        int n = global::System.Math.Min(_nodeCount, _renderBuffer.Length);

        // Try GPU mapping via RenderMapperShader if device supports double precision
        bool gpuMapped = false;
        try
        {
            if (_device != null && _device.IsDoublePrecisionSupportAvailable())
            {
                // Build PhysicsNodeState array from source graph
                var phys = new RQSimulation.GPUOptimized.Rendering.PhysicsNodeState[n];

                bool hasSpectral = _sourceGraph.SpectralX is not null && _sourceGraph.SpectralX.Length == _nodeCount;
#pragma warning disable CS0618
                bool hasCoords = _sourceGraph.Coordinates is not null && _sourceGraph.Coordinates.Length == _nodeCount;
#pragma warning restore CS0618

                for (int i = 0; i < n; i++)
                {
                    float x = 0f, y = 0f, z = 0f;
                    if (hasSpectral)
                    {
                        x = (float)_sourceGraph.SpectralX![i];
                        y = (float)_sourceGraph.SpectralY![i];
                        z = (float)_sourceGraph.SpectralZ![i];
                    }
                    else if (hasCoords)
                    {
                        x = (float)_sourceGraph.Coordinates[i].X;
                        y = (float)_sourceGraph.Coordinates[i].Y;
                        z = 0f;
                    }
                    else
                    {
                        int gridSize = (int)global::System.Math.Ceiling(global::System.Math.Sqrt(_nodeCount));
                        float spacing = 2.0f;
                        int gx = i % gridSize;
                        int gy = i / gridSize;
                        x = (gx - gridSize / 2f) * spacing;
                        y = (gy - gridSize / 2f) * spacing;
                        z = 0f;
                    }

                    phys[i].X = x;
                    phys[i].Y = y;
                    phys[i].Z = z;

                    // Fill physics fields with available data (best-effort)
                    phys[i].PsiReal = 0.0;
                    phys[i].PsiImag = 0.0;
                    phys[i].Potential = (_sourceGraph.LocalPotential != null && i < _sourceGraph.LocalPotential.Length) ? _sourceGraph.LocalPotential[i] : 0.0;
                    phys[i].Mass = (_massesCpu != null && i < _massesCpu.Length) ? _massesCpu[i] : 1.0;
                }

                // Allocate readonly buffer and dispatch mapper
                using var physBuf = _device.AllocateReadOnlyBuffer(phys);

                // Dispatch compute shader to convert double-precision physics to float render vertices
                _device.For(n, new RQSimulation.GPUOptimized.Rendering.RenderMapperShader(physBuf, _renderBuffer!, 0, 0.5f, 0.1f));

                gpuMapped = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CSR Render] GPU RenderMapper failed: {ex.Message}");
            gpuMapped = false;
        }

        if (gpuMapped)
            return;

        // CPU fallback: convert positions to RenderNodeVertex and upload
        var temp = new RQSimulation.GPUOptimized.Rendering.RenderNodeVertex[n];

        bool hasSpectral2 = _sourceGraph.SpectralX is not null && _sourceGraph.SpectralX.Length == _nodeCount;
#pragma warning disable CS0618
        bool hasCoords2 = _sourceGraph.Coordinates is not null && _sourceGraph.Coordinates.Length == _nodeCount;
#pragma warning restore CS0618

        for (int i = 0; i < n; i++)
        {
            float x = 0f, y = 0f, z = 0f;
            if (hasSpectral2)
            {
                x = (float)_sourceGraph.SpectralX![i];
                y = (float)_sourceGraph.SpectralY![i];
                z = (float)_sourceGraph.SpectralZ![i];
            }
            else if (hasCoords2)
            {
                x = (float)_sourceGraph.Coordinates[i].X;
                y = (float)_sourceGraph.Coordinates[i].Y;
                z = 0f;
            }
            else
            {
                int gridSize = (int)global::System.Math.Ceiling(global::System.Math.Sqrt(_nodeCount));
                float spacing = 2.0f;
                int gx = i % gridSize;
                int gy = i / gridSize;
                x = (gx - gridSize / 2f) * spacing;
                y = (gy - gridSize / 2f) * spacing;
                z = 0f;
            }

            temp[i].X = x;
            temp[i].Y = y;
            temp[i].Z = z;
            temp[i].R = 0.2f;
            temp[i].G = 0.9f;
            temp[i].B = 0.2f;
            temp[i].A = 1f;
            temp[i].Size = 0.08f;
        }

        try
        {
            _renderBuffer.CopyFrom(temp);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CSR Render] UpdateRenderBufferFromGraph upload failed: {ex.Message}");
        }
    }

    private double SumBuffer(ReadWriteBuffer<double> buffer, int count)
    {
        if (count == 0) return 0.0;

        double[] tempCpu = new double[count];
        buffer.CopyTo(tempCpu);

        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            sum += tempCpu[i];
        }
        return sum;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized. Call Initialize() first.");
    }

    private void DisposeBuffers()
    {
        _topology?.Dispose();
        _massesBuffer?.Dispose();
        _curvaturesBuffer?.Dispose();
        _violationsBuffer?.Dispose();
        _nodeActionsBuffer?.Dispose();
        _volumeContribsBuffer?.Dispose();
        _weylContribsBuffer?.Dispose();
        _reductionBuffer?.Dispose();
        _edgeAmplitudesBuffer?.Dispose();
        _edgeHamiltoniansBuffer?.Dispose();
        _lapseBuffer?.Dispose();
        _hamiltonianViolationBuffer?.Dispose();
        _localDtBuffer?.Dispose();
        _observerEngine?.Dispose();
        _biCGStabSolver?.Dispose();

        // Dispose render buffer
        _renderBuffer?.Dispose();
        _renderBuffer = null;

        _topology = null;
        _massesBuffer = null;
        _curvaturesBuffer = null;
        _violationsBuffer = null;
        _nodeActionsBuffer = null;
        _volumeContribsBuffer = null;
        _weylContribsBuffer = null;
        _reductionBuffer = null;
        _edgeAmplitudesBuffer = null;
        _edgeHamiltoniansBuffer = null;
        _lapseBuffer = null;
        _hamiltonianViolationBuffer = null;
        _localDtBuffer = null;
        _observerEngine = null;
        _biCGStabSolver = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }

    /// <summary>
    /// Update only edge weights in the internal CSR topology from the given graph.
    /// </summary>
    public void SyncWeightsFromGraph(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();

        if (graph.N != _nodeCount)
            throw new ArgumentException("Graph node count does not match initialized topology.");

        int sig = ComputeTopologySignature(graph);
        if (sig != _topologySignature)
        {
            UpdateTopology(graph);
            return;
        }

        _topology!.UpdateEdgeWeightsFromDense(graph.Weights);
        UploadMasses(graph);
    }

    /// <summary>
    /// Copy the CSR edge weights back into the graph's dense weight matrix.
    /// </summary>
    public void CopyWeightsToGraph(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureInitialized();

        if (graph.N != _nodeCount)
            throw new ArgumentException("Graph node count does not match initialized topology.");

        int sig = ComputeTopologySignature(graph);
        if (sig != _topologySignature)
        {
            UpdateTopology(graph);
        }

        var rowOffsets = _topology!.RowOffsets;
        var colIndices = _topology.ColIndices;
        var edgeWeights = _topology.EdgeWeights;

        for (int i = 0; i < _nodeCount; i++)
        {
            int start = rowOffsets[i];
            int end = rowOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                int j = colIndices[k];
                double w = edgeWeights[k];
                graph.Weights[i, j] = w;
            }
        }

        graph.RecomputeCorrelationMass();
    }

    private static int ComputeTopologySignature(RQGraph graph)
    {
        unchecked
        {
            int n = graph.N;

            int directedEdges = 0;

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (graph.Edges[i, j])
                        directedEdges++;
                }
            }

            int h = (n * 397) ^ directedEdges;
            int step = System.Math.Max(1, n / 8);
            for (int i = 0; i < n; i += step)
            {
                for (int j = 0; j < n; j += step)
                {
                    h = (h * 397) ^ (graph.Edges[i, j] ? 1 : 0);
                }
            }

            return h;
        }
    }
}
