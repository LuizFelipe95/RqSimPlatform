using System;
using System.Linq;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.DynamicTopology;
using RQSimulation.GPUCompressedSparseRow.Solvers;

namespace RQSimulation.GPUCompressedSparseRow;

/// <summary>
/// GPU-accelerated Cayley evolution engine using CSR (Compressed Sparse Row) format.
/// 
/// This is the NEW implementation that uses optimized CSR data layout for sparse
/// matrix-vector operations, providing better memory locality and cache efficiency
/// compared to the original dense-style GPU implementation.
/// 
/// USAGE:
/// - Use this engine for large sparse graphs (100k+ nodes)
/// - Falls back to original GPUOptimized engine for small/dense graphs
/// 
/// COMPATIBILITY:
/// - Can work alongside RQSimulation.GPUOptimized.CayleyEvolution.GpuCayleyEvolutionEngineDouble
/// - Both engines produce identical physics results (unitarity preserved)
/// - Choice depends on graph structure and performance needs
/// 
/// TOPOLOGY MODES:
/// - CsrStatic: No topology changes
/// - StreamCompaction: Soft rewiring (edge removal only)
/// - StreamCompactionFullGpu: Full GPU compaction
/// - DynamicHardRewiring: Full hard rewiring with edge addition and deletion
/// </summary>
public sealed class GpuCayleyEvolutionEngineCsr : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly bool _useDoublePrecision;
    
    private CsrTopology? _topology;
    private DynamicCsrTopology? _dynamicTopology;
    private GpuBiCGStabSolverCsr? _solver;
    private PingPongBuffer<Double2>? _psiBuffer;
    
    // Stream compaction engine for soft rewiring
    private GpuStreamCompactionEngine? _compactionEngine;
    
    // Dynamic topology engine for hard rewiring
    private GpuDynamicTopologyEngine? _dynamicTopologyEngine;
    private ReadWriteBuffer<double>? _massesBuffer;
    
    // Problem dimensions
    private int _nodeCount;
    private int _gaugeDim;
    private int _dim;
    
    // Step counter for periodic topology rebuilds
    private int _stepCounter;
    
    private bool _initialized;
    private bool _disposed;
    
    /// <summary>
    /// Whether double precision is supported and used.
    /// </summary>
    public bool IsDoublePrecision => _useDoublePrecision;
    
    /// <summary>
    /// Whether the engine is initialized and ready.
    /// </summary>
    public bool IsInitialized => _initialized;
    
    /// <summary>
    /// Number of BiCGStab iterations in last evolution step.
    /// </summary>
    public int LastIterations => _solver?.LastIterations ?? 0;
    
    /// <summary>
    /// Final residual from last evolution step.
    /// </summary>
    public double LastResidual => _solver?.LastResidual ?? 0;
    
    /// <summary>
    /// Maximum BiCGStab iterations allowed.
    /// </summary>
    public int MaxIterations
    {
        get => _solver?.MaxIterations ?? 100;
        set { if (_solver is not null) _solver.MaxIterations = value; }
    }
    
    /// <summary>
    /// Convergence tolerance for BiCGStab.
    /// </summary>
    public double Tolerance
    {
        get => _solver?.Tolerance ?? 1e-12;
        set { if (_solver is not null) _solver.Tolerance = value; }
    }
    
    /// <summary>
    /// Access to underlying CSR topology for updates.
    /// </summary>
    public CsrTopology? Topology => _topology;

    /// <summary>
    /// Access to dynamic topology if using hard rewiring mode.
    /// </summary>
    public DynamicCsrTopology? DynamicTopology => _dynamicTopology;

    /// <summary>
    /// Configuration for dynamic topology operations.
    /// </summary>
    public DynamicTopologyConfig? DynamicConfig => _dynamicTopologyEngine?.Config;

    /// <summary>
    /// Statistics from last dynamic topology operation.
    /// </summary>
    public DynamicTopologyStats? LastDynamicStats => _dynamicTopologyEngine?.LastStats;

    // New: allow choosing topology update mode
    public enum TopologyMode
    {
        /// <summary>Static CSR - no topology changes.</summary>
        CsrStatic,
        /// <summary>GPU stream compaction for dynamic edge removal (soft rewiring).</summary>
        StreamCompaction,
        /// <summary>Full GPU Blelloch scan + scatter compaction (soft rewiring).</summary>
        StreamCompactionFullGpu,
        /// <summary>Dynamic hard rewiring with edge addition and deletion.</summary>
        DynamicHardRewiring
    }

    public TopologyMode CurrentTopologyMode { get; set; } = TopologyMode.CsrStatic;

    public GpuCayleyEvolutionEngineCsr()
    {
        _device = GraphicsDevice.GetDefault();
        _useDoublePrecision = _device.IsDoublePrecisionSupportAvailable();
        
        if (!_useDoublePrecision)
        {
            Console.WriteLine("WARNING: GPU does not support double precision (SM 6.0+).");
            Console.WriteLine("CSR Cayley evolution requires double precision for accuracy.");
            throw new NotSupportedException("Double precision not available.");
        }
        
        Console.WriteLine($"GPU: {_device.Name} - CSR Cayley Engine initialized (double precision)");
    }

    public GpuCayleyEvolutionEngineCsr(GraphicsDevice device)
    {
        _device = device;
        _useDoublePrecision = _device.IsDoublePrecisionSupportAvailable();
        
        if (!_useDoublePrecision)
        {
            throw new NotSupportedException("Double precision not available.");
        }
    }

    /// <summary>
    /// Initialize engine from dense adjacency matrix.
    /// Converts to CSR format internally.
    /// </summary>
    /// <param name="edges">Dense boolean adjacency matrix [N x N]</param>
    /// <param name="weights">Dense weight matrix [N x N]</param>
    /// <param name="potential">Node potentials [N] (optional)</param>
    /// <param name="gaugeDim">Gauge dimension (components per node)</param>
    public void InitializeFromDense(bool[,] edges, double[,] weights, double[]? potential = null, int gaugeDim = 1)
    {
        DisposeInternal();
        
        _topology = new CsrTopology(_device);
        _topology.BuildFromDenseMatrix(edges, weights, potential);
        _topology.UploadToGpu();
        
        InitializeWithTopology(_topology, gaugeDim);
    }

    /// <summary>
    /// Initialize engine from edge list (more efficient for sparse graphs).
    /// </summary>
    /// <param name="nodeCount">Number of nodes</param>
    /// <param name="sourceNodes">Source node for each edge</param>
    /// <param name="targetNodes">Target node for each edge</param>
    /// <param name="weights">Weight for each edge</param>
    /// <param name="potential">Node potentials (optional)</param>
    /// <param name="gaugeDim">Gauge dimension</param>
    public void InitializeFromEdgeList(
        int nodeCount,
        int[] sourceNodes,
        int[] targetNodes,
        double[] weights,
        double[]? potential = null,
        int gaugeDim = 1)
    {
        DisposeInternal();
        
        _topology = new CsrTopology(_device);
        _topology.BuildFromEdgeList(nodeCount, sourceNodes, targetNodes, weights, 
                                    potential ?? Array.Empty<double>());
        _topology.UploadToGpu();
        
        InitializeWithTopology(_topology, gaugeDim);
    }

    /// <summary>
    /// Initialize with pre-built CSR topology.
    /// </summary>
    public void InitializeWithTopology(CsrTopology topology, int gaugeDim = 1)
    {
        ArgumentNullException.ThrowIfNull(topology);
        
        if (!topology.IsGpuReady)
        {
            topology.UploadToGpu();
        }
        
        _topology = topology;
        _nodeCount = topology.NodeCount;
        _gaugeDim = gaugeDim;
        _dim = _nodeCount * _gaugeDim;
        
        // Initialize solver
        _solver = new GpuBiCGStabSolverCsr(_device);
        _solver.Initialize(topology, gaugeDim);
        
        // Initialize wavefunction buffer (ping-pong)
        _psiBuffer = new PingPongBuffer<Double2>();
        _psiBuffer.Allocate(_device, _dim);
        
        // Initialize stream compaction engine
        _compactionEngine = new GpuStreamCompactionEngine(_device);
        
        // Initialize dynamic topology engine
        _dynamicTopologyEngine = new GpuDynamicTopologyEngine(_device);
        _dynamicTopologyEngine.Initialize(_nodeCount, topology.Nnz);
        
        // Initialize masses buffer for dynamic topology
        _massesBuffer = _device.AllocateReadWriteBuffer<double>(_nodeCount);
        
        _stepCounter = 0;
        _initialized = true;
    }

    /// <summary>
    /// Upload wavefunction from CPU arrays.
    /// </summary>
    public void UploadWavefunction(ReadOnlySpan<double> psiReal, ReadOnlySpan<double> psiImag)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");
        
        if (psiReal.Length != _dim || psiImag.Length != _dim)
            throw new ArgumentException($"Wavefunction size {psiReal.Length} != expected {_dim}");
        
        // Convert to Double2 array
        Double2[] psiArray = new Double2[_dim];
        
        for (int i = 0; i < _dim; i++)
        {
            psiArray[i] = new Double2(psiReal[i], psiImag[i]);
        }
        
        _psiBuffer!.UploadToCurrent(psiArray);
    }

    /// <summary>
    /// Download wavefunction to CPU arrays.
    /// </summary>
    public void DownloadWavefunction(Span<double> psiReal, Span<double> psiImag)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");
        
        if (psiReal.Length != _dim || psiImag.Length != _dim)
            throw new ArgumentException($"Wavefunction size {psiReal.Length} != expected {_dim}");
        
        Double2[] psiArray = new Double2[_dim];
        _psiBuffer!.DownloadFromCurrent(psiArray);
        
        for (int i = 0; i < _dim; i++)
        {
            psiReal[i] = psiArray[i].X;
            psiImag[i] = psiArray[i].Y;
        }
    }

    /// <summary>
    /// Perform one Cayley-form unitary evolution step.
    /// ?_new = (1 - i*H*dt/2) * (1 + i*H*dt/2)^{-1} * ?_old
    /// </summary>
    /// <param name="dt">Time step</param>
    /// <returns>Number of BiCGStab iterations used</returns>
    public int EvolveStep(double dt)
    {
        if (!_initialized || _solver is null || _psiBuffer is null)
            throw new InvalidOperationException("Engine not initialized");
        
        double alpha = dt / 2.0;
        
        // Solve (1 + i*?*H)*?_new = (1 - i*?*H)*?_old
        int iterations = _solver.Solve(_psiBuffer.Current, alpha);
        
        _stepCounter++;
        
        // Check if periodic topology rebuild is needed
        if (CurrentTopologyMode == TopologyMode.DynamicHardRewiring)
        {
            int rebuildInterval = _dynamicTopologyEngine?.Config.RebuildInterval ?? 10;
            if (_stepCounter % rebuildInterval == 0)
            {
                EvolveTopologyDynamic();
            }
        }
        
        return iterations;
    }

    /// <summary>
    /// Perform multiple evolution steps.
    /// </summary>
    /// <param name="dt">Time step per iteration</param>
    /// <param name="numSteps">Number of steps</param>
    /// <returns>Total iterations across all steps</returns>
    public int Evolve(double dt, int numSteps)
    {
        int totalIterations = 0;
        
        for (int step = 0; step < numSteps; step++)
        {
            totalIterations += EvolveStep(dt);
        }
        
        return totalIterations;
    }

    /// <summary>
    /// Update node potentials without re-initializing.
    /// </summary>
    public void UpdatePotential(ReadOnlySpan<double> potential)
    {
        if (!_initialized || _topology is null)
            throw new InvalidOperationException("Engine not initialized");
        
        _topology.UpdatePotential(potential);
    }

    /// <summary>
    /// Update node masses for dynamic topology evolution.
    /// </summary>
    public void UpdateMasses(ReadOnlySpan<double> masses)
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized");
        
        if (masses.Length != _nodeCount)
            throw new ArgumentException($"Masses length {masses.Length} != NodeCount {_nodeCount}");
        
        _massesBuffer?.CopyFrom(masses);
    }

    /// <summary>
    /// Compute current wavefunction norm (should be 1.0 if unitary).
    /// </summary>
    public double ComputeNorm()
    {
        if (!_initialized || _psiBuffer is null)
            throw new InvalidOperationException("Engine not initialized");
        
        Double2[] psi = new Double2[_dim];
        _psiBuffer.DownloadFromCurrent(psi);
        
        double normSq = 0;
        for (int i = 0; i < _dim; i++)
        {
            normSq += psi[i].X * psi[i].X + psi[i].Y * psi[i].Y;
        }
        
        return System.Math.Sqrt(normSq);
    }

    /// <summary>
    /// Get direct access to GPU wavefunction buffer (for advanced use).
    /// </summary>
    public ReadWriteBuffer<Double2> GetPsiBuffer()
    {
        if (!_initialized || _psiBuffer is null)
            throw new InvalidOperationException("Engine not initialized");
        
        return _psiBuffer.Current;
    }

    /// <summary>
    /// Evolve topology using dynamic hard rewiring.
    /// This allows adding new edges that weren't in the original topology.
    /// </summary>
    public void EvolveTopologyDynamic()
    {
        if (_topology is null || _dynamicTopologyEngine is null || _massesBuffer is null)
            throw new InvalidOperationException("Engine not initialized for dynamic topology");

        if (!_topology.IsGpuReady)
            throw new InvalidOperationException("Topology must be on GPU");

        // Run dynamic topology evolution
        var newTopology = _dynamicTopologyEngine.EvolveTopology(_topology, _massesBuffer);
        
        if (newTopology is null)
        {
            // No changes proposed
            return;
        }

        // Store dynamic topology
        _dynamicTopology?.Dispose();
        _dynamicTopology = newTopology;

        // Convert to standard topology and reinitialize solver
        var standardTopology = newTopology.ToStandardTopology();
        standardTopology.UploadToGpu();

        // Dispose old topology
        _topology.Dispose();
        _topology = standardTopology;

        // Reinitialize solver with new topology
        _solver?.Dispose();
        _solver = new GpuBiCGStabSolverCsr(_device);
        _solver.Initialize(_topology, _gaugeDim);

        // Reallocate masses buffer if node count changed
        if (_massesBuffer.Length != _topology.NodeCount)
        {
            _massesBuffer.Dispose();
            _massesBuffer = _device.AllocateReadWriteBuffer<double>(_topology.NodeCount);
        }
    }

    /// <summary>
    /// Evolve topology using stream compaction (soft rewiring - removal only).
    /// </summary>
    public void EvolveTopology(double weightThreshold = 0.001)
    {
        if (_topology is null) 
            throw new InvalidOperationException("Topology not initialized");
        
        if (!_topology.IsGpuReady)
            throw new InvalidOperationException("Topology must be on GPU");

        if (CurrentTopologyMode == TopologyMode.CsrStatic)
        {
            return;
        }

        if (CurrentTopologyMode == TopologyMode.DynamicHardRewiring)
        {
            EvolveTopologyDynamic();
            return;
        }

        int nodeCount = _topology.NodeCount;
        int[] newRowOffsets;
        int[] newColIndices;
        double[] newWeights;
        int newNnz;

        if (CurrentTopologyMode == TopologyMode.StreamCompactionFullGpu && _compactionEngine is not null)
        {
            (newRowOffsets, newColIndices, newWeights, newNnz) = 
                _compactionEngine.CompactTopologyFullGpu(_topology, weightThreshold);
        }
        else if (_compactionEngine is not null)
        {
            (newRowOffsets, newColIndices, newWeights, newNnz) = 
                _compactionEngine.CompactTopology(_topology, weightThreshold);
        }
        else
        {
            (newRowOffsets, newColIndices, newWeights, newNnz) = 
                CompactTopologyCpu(weightThreshold);
        }

        if (newNnz == _topology.Nnz)
        {
            return;
        }

        // Build source nodes from CSR structure
        int[] sourceNodes = new int[newNnz];
        for (int i = 0; i < nodeCount; i++)
        {
            int start = newRowOffsets[i];
            int end = newRowOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                sourceNodes[k] = i;
            }
        }

        // Dispose old topology and rebuild
        double[] potential = _topology.NodePotential.ToArray();
        _topology.Dispose();

        _topology = new CsrTopology(_device);
        _topology.BuildFromEdgeList(nodeCount, sourceNodes, newColIndices, newWeights, potential);
        _topology.UploadToGpu();

        // Reinitialize solver with new topology
        _solver?.Dispose();
        _solver = new GpuBiCGStabSolverCsr(_device);
        _solver.Initialize(_topology, _gaugeDim);
    }

    private (int[] newRowOffsets, int[] newColIndices, double[] newWeights, int newNnz) 
        CompactTopologyCpu(double weightThreshold)
    {
        int nodeCount = _topology!.NodeCount;

        // 1) Compute new degrees
        int[] newDegrees = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            int start = _topology.RowOffsets[i];
            int end = _topology.RowOffsets[i + 1];
            int deg = 0;
            for (int k = start; k < end; k++)
            {
                if (_topology.EdgeWeights[k] >= weightThreshold) deg++;
            }
            newDegrees[i] = deg;
        }

        // 2) Prefix sum for new row offsets
        int[] newRowOffsets = new int[nodeCount + 1];
        newRowOffsets[0] = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            newRowOffsets[i + 1] = newRowOffsets[i] + newDegrees[i];
        }
        int newNnz = newRowOffsets[nodeCount];

        // 3) Compact
        int[] newColIndices = new int[newNnz];
        double[] newWeights = new double[newNnz];

        for (int i = 0; i < nodeCount; i++)
        {
            int writePos = newRowOffsets[i];
            int start = _topology.RowOffsets[i];
            int end = _topology.RowOffsets[i + 1];
            for (int k = start; k < end; k++)
            {
                double w = _topology.EdgeWeights[k];
                if (w >= weightThreshold)
                {
                    newColIndices[writePos] = _topology.ColIndices[k];
                    newWeights[writePos] = w;
                    writePos++;
                }
            }
        }

        return (newRowOffsets, newColIndices, newWeights, newNnz);
    }

    /// <summary>
    /// Configure dynamic topology parameters.
    /// </summary>
    public void ConfigureDynamicTopology(Action<DynamicTopologyConfig> configure)
    {
        if (_dynamicTopologyEngine is null)
            throw new InvalidOperationException("Dynamic topology engine not initialized");
        
        configure(_dynamicTopologyEngine.Config);
    }

    /// <summary>
    /// Force a topology rebuild on next step.
    /// </summary>
    public void ForceTopologyRebuild()
    {
        if (CurrentTopologyMode == TopologyMode.DynamicHardRewiring)
        {
            EvolveTopologyDynamic();
        }
        else if (CurrentTopologyMode != TopologyMode.CsrStatic)
        {
            EvolveTopology();
        }
    }

    private void DisposeInternal()
    {
        _solver?.Dispose();
        _psiBuffer?.Dispose();
        _topology?.Dispose();
        _dynamicTopology?.Dispose();
        _compactionEngine?.Dispose();
        _dynamicTopologyEngine?.Dispose();
        _massesBuffer?.Dispose();
        
        _solver = null;
        _psiBuffer = null;
        _topology = null;
        _dynamicTopology = null;
        _compactionEngine = null;
        _dynamicTopologyEngine = null;
        _massesBuffer = null;
        
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        DisposeInternal();
        _disposed = true;
    }
}

/// <summary>
/// Factory for choosing between GPU evolution engine implementations.
/// </summary>
public static class GpuCayleyEngineFactory
{
    /// <summary>
    /// GPU engine implementation type.
    /// </summary>
    public enum EngineType
    {
        /// <summary>
        /// Original implementation in GPUOptimized folder.
        /// Uses direct buffer-based matrix storage.
        /// </summary>
        Original,
        
        /// <summary>
        /// New CSR-based implementation.
        /// Uses Compressed Sparse Row format for better cache efficiency.
        /// </summary>
        Csr
    }
    
    /// <summary>
    /// Recommended engine type based on graph characteristics.
    /// </summary>
    /// <param name="nodeCount">Number of nodes</param>
    /// <param name="nnz">Number of non-zero entries in adjacency</param>
    /// <returns>Recommended engine type</returns>
    public static EngineType RecommendEngineType(int nodeCount, int nnz)
    {
        // Sparsity ratio
        double sparsity = 1.0 - (double)nnz / (nodeCount * nodeCount);
        
        // CSR is better for:
        // - Large graphs (>10k nodes)
        // - Sparse graphs (>90% sparsity)
        if (nodeCount > 10000 || sparsity > 0.9)
        {
            return EngineType.Csr;
        }
        
        return EngineType.Original;
    }
    
    /// <summary>
    /// Check if GPU supports required features.
    /// </summary>
    public static bool IsGpuSupported()
    {
        try
        {
            var device = GraphicsDevice.GetDefault();
            return device.IsDoublePrecisionSupportAvailable();
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Get GPU device info string.
    /// </summary>
    public static string GetGpuInfo()
    {
        try
        {
            var device = GraphicsDevice.GetDefault();
            bool doublePrecision = device.IsDoublePrecisionSupportAvailable();
            
            return $"{device.Name} (Double precision: {(doublePrecision ? "Yes" : "No")})";
        }
        catch (Exception ex)
        {
            return $"GPU not available: {ex.Message}";
        }
    }
}
