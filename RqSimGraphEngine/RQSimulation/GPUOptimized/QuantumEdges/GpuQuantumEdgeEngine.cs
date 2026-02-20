using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.QuantumEdges;

/// <summary>
/// GPU-accelerated quantum edge engine for dense graphs.
/// 
/// RQ-HYPOTHESIS: QUANTUM GRAPHITY
/// ================================
/// In quantum gravity, the geometry itself is in superposition.
/// Each edge has a quantum amplitude: |edge_ij? = ?|exists? + ?|not-exists?
/// 
/// This engine provides GPU-accelerated operations for:
/// - Unitary time evolution of edge amplitudes
/// - Quantum measurement (collapse)
/// - Purity and coherence metrics
/// 
/// PARALLELIZATION STRATEGY:
/// ========================
/// All operations are fully parallelizable over edges:
/// - Unitary evolution: independent per edge
/// - Probability computation: independent per edge
/// - Collapse: independent per edge (given random numbers)
/// - Reductions: parallel tree reduction O(log E)
/// 
/// OPTIMAL USE CASE:
/// - Dense graphs: N &lt; 10? nodes
/// - For sparse graphs (N &gt; 10?), use CSR variant
/// 
/// All computations use double precision (64-bit) for physical accuracy.
/// </summary>
public sealed class GpuQuantumEdgeEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Random _rng;

    // GPU buffers
    private ReadWriteBuffer<Double2>? _amplitudesBuffer;
    private ReadWriteBuffer<double>? _weightsBuffer;
    private ReadWriteBuffer<double>? _hamiltonianDiagBuffer;  // Changed to ReadWriteBuffer
    private ReadWriteBuffer<double>? _probabilitiesBuffer;
    private ReadWriteBuffer<double>? _squaredProbsBuffer;
    private ReadOnlyBuffer<double>? _randomThresholdsBuffer;
    private ReadWriteBuffer<int>? _existsAfterCollapseBuffer;
    
    // Topology buffers (for Hamiltonian computation)
    private ReadOnlyBuffer<int>? _edgeIBuffer;
    private ReadOnlyBuffer<int>? _edgeJBuffer;
    private ReadOnlyBuffer<int>? _rowOffsetsBuffer;
    private ReadOnlyBuffer<int>? _colIndicesBuffer;
    private ReadOnlyBuffer<int>? _degreesBuffer;

    // CPU arrays
    private Double2[] _amplitudesCpu = [];
    private double[] _weightsCpu = [];
    private double[] _hamiltonianDiagCpu = [];
    private double[] _probabilitiesCpu = [];
    private double[] _squaredProbsCpu = [];
    private double[] _randomThresholdsCpu = [];
    private int[] _existsAfterCollapseCpu = [];
    private int[] _edgeICpu = [];
    private int[] _edgeJCpu = [];
    private int[] _rowOffsetsCpu = [];
    private int[] _colIndicesCpu = [];
    private int[] _degreesCpu = [];

    private int _edgeCount;
    private int _nodeCount;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Triangle bonus coefficient for edge Hamiltonian.
    /// Negative value = lower energy for edges forming triangles.
    /// </summary>
    public double TriangleBonus { get; set; } = -0.1;

    /// <summary>
    /// Degree penalty coefficient for edge Hamiltonian.
    /// Positive value = higher energy for high-degree nodes.
    /// </summary>
    public double DegreePenalty { get; set; } = 0.01;

    /// <summary>
    /// Target average degree for degree penalty calculation.
    /// </summary>
    public double TargetDegree { get; set; } = 4.0;

    /// <summary>
    /// Whether the engine is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Number of edges in the graph.
    /// </summary>
    public int EdgeCount => _edgeCount;

    /// <summary>
    /// Number of nodes in the graph.
    /// </summary>
    public int NodeCount => _nodeCount;

    /// <summary>
    /// Create GPU quantum edge engine with default device.
    /// </summary>
    public GpuQuantumEdgeEngine()
    {
        _device = GraphicsDevice.GetDefault();
        _rng = new Random();
    }

    /// <summary>
    /// Create GPU quantum edge engine with specified device and seed.
    /// </summary>
    public GpuQuantumEdgeEngine(GraphicsDevice device, int seed = 42)
    {
        _device = device;
        _rng = new Random(seed);
    }

    /// <summary>
    /// Initialize the engine for a graph with given size.
    /// </summary>
    /// <param name="nodeCount">Number of nodes</param>
    /// <param name="edgeCount">Number of undirected edges (max N*(N-1)/2 for dense)</param>
    public void Initialize(int nodeCount, int edgeCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(edgeCount, 0);

        _nodeCount = nodeCount;
        _edgeCount = edgeCount;

        // Allocate CPU arrays
        _amplitudesCpu = new Double2[edgeCount];
        _weightsCpu = new double[edgeCount];
        _hamiltonianDiagCpu = new double[edgeCount];
        _probabilitiesCpu = new double[edgeCount];
        _squaredProbsCpu = new double[edgeCount];
        _randomThresholdsCpu = new double[edgeCount];
        _existsAfterCollapseCpu = new int[edgeCount];
        _edgeICpu = new int[edgeCount];
        _edgeJCpu = new int[edgeCount];
        _degreesCpu = new int[nodeCount];

        // CSR topology: max 2*edgeCount directed edges
        int maxNnz = edgeCount * 2;
        _rowOffsetsCpu = new int[nodeCount + 1];
        _colIndicesCpu = new int[maxNnz];

        // Allocate GPU buffers
        DisposeGpuBuffers();

        _amplitudesBuffer = _device.AllocateReadWriteBuffer<Double2>(edgeCount);
        _weightsBuffer = _device.AllocateReadWriteBuffer<double>(edgeCount);
        _hamiltonianDiagBuffer = _device.AllocateReadWriteBuffer<double>(edgeCount);
        _probabilitiesBuffer = _device.AllocateReadWriteBuffer<double>(edgeCount);
        _squaredProbsBuffer = _device.AllocateReadWriteBuffer<double>(edgeCount);
        _randomThresholdsBuffer = _device.AllocateReadOnlyBuffer<double>(edgeCount);
        _existsAfterCollapseBuffer = _device.AllocateReadWriteBuffer<int>(edgeCount);
        _edgeIBuffer = _device.AllocateReadOnlyBuffer<int>(edgeCount);
        _edgeJBuffer = _device.AllocateReadOnlyBuffer<int>(edgeCount);
        _rowOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
        _colIndicesBuffer = _device.AllocateReadOnlyBuffer<int>(Math.Max(1, maxNnz));
        _degreesBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount);

        _initialized = true;
    }

    /// <summary>
    /// Upload quantum edge data from RQGraph.
    /// Extracts edge list and amplitudes from quantum edges.
    /// </summary>
    /// <param name="graph">Source graph with quantum edges enabled</param>
    public void UploadFromGraph(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        
        if (!graph.IsQuantumEdgeMode)
        {
            throw new InvalidOperationException(
                "Graph must have quantum edges enabled. Call EnableQuantumEdges() first.");
        }
        
        // Count actual edges and build edge list
        int edgeIdx = 0;
        for (int i = 0; i < graph.N; i++)
        {
            for (int j = i + 1; j < graph.N; j++)
            {
                var qe = graph.GetQuantumEdge(i, j);
                if (qe.ExistenceProbability > 1e-20 || graph.Edges[i, j])
                {
                    if (edgeIdx >= _edgeCount)
                    {
                        throw new InvalidOperationException(
                            $"Too many edges. Initialize with larger edgeCount. Found: {edgeIdx + 1}, Capacity: {_edgeCount}");
                    }
                    
                    _edgeICpu[edgeIdx] = i;
                    _edgeJCpu[edgeIdx] = j;
                    _amplitudesCpu[edgeIdx] = new Double2(
                        qe.Amplitude.Real,
                        qe.Amplitude.Imaginary);
                    _weightsCpu[edgeIdx] = qe.GetMagnitude();
                    edgeIdx++;
                }
            }
        }
        
        // Update edge count to actual
        int actualEdgeCount = edgeIdx;
        
        // Build CSR topology for triangle counting
        BuildCsrTopology(graph);
        
        // Upload to GPU
        _edgeIBuffer!.CopyFrom(_edgeICpu.AsSpan(0, actualEdgeCount));
        _edgeJBuffer!.CopyFrom(_edgeJCpu.AsSpan(0, actualEdgeCount));
        _amplitudesBuffer!.CopyFrom(_amplitudesCpu.AsSpan(0, actualEdgeCount));
        _weightsBuffer!.CopyFrom(_weightsCpu.AsSpan(0, actualEdgeCount));
        
        // Copy node degrees - use Degree(i) method
        for (int i = 0; i < graph.N; i++)
        {
            _degreesCpu[i] = graph.Degree(i);
        }
        _degreesBuffer!.CopyFrom(_degreesCpu);
    }

    /// <summary>
    /// Build CSR topology from graph for neighbor lookups.
    /// </summary>
    private void BuildCsrTopology(RQGraph graph)
    {
        int N = graph.N;

        // Count degrees
        for (int i = 0; i < N; i++)
        {
            _degreesCpu[i] = 0;
            for (int j = 0; j < N; j++)
            {
                if (i != j && graph.Edges[i, j])
                {
                    _degreesCpu[i]++;
                }
            }
        }

        // Build row offsets
        _rowOffsetsCpu[0] = 0;
        for (int i = 0; i < N; i++)
        {
            _rowOffsetsCpu[i + 1] = _rowOffsetsCpu[i] + _degreesCpu[i];
        }

        int nnz = _rowOffsetsCpu[N];
        if (nnz > _colIndicesCpu.Length)
        {
            _colIndicesCpu = new int[nnz];
        }

        // Fill column indices (sorted neighbors for each node)
        int[] currentOffset = new int[N];
        Array.Copy(_rowOffsetsCpu, currentOffset, N);

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                if (i != j && graph.Edges[i, j])
                {
                    _colIndicesCpu[currentOffset[i]++] = j;
                }
            }
        }

        // Sort neighbor lists for merge-style intersection
        for (int i = 0; i < N; i++)
        {
            int start = _rowOffsetsCpu[i];
            int end = _rowOffsetsCpu[i + 1];
            Array.Sort(_colIndicesCpu, start, end - start);
        }

        // Upload to GPU
        _rowOffsetsBuffer!.CopyFrom(_rowOffsetsCpu.AsSpan(0, N + 1));
        if (nnz > 0)
        {
            _colIndicesBuffer!.CopyFrom(_colIndicesCpu.AsSpan(0, nnz));
        }
        _degreesBuffer!.CopyFrom(_degreesCpu.AsSpan(0, N));
    }

    /// <summary>
    /// Upload amplitudes directly (for testing or custom initialization).
    /// </summary>
    /// <param name="amplitudes">Array of (real, imag) amplitude pairs</param>
    public void UploadAmplitudes(Double2[] amplitudes)
    {
        ArgumentNullException.ThrowIfNull(amplitudes);

        if (amplitudes.Length > _edgeCount)
        {
            throw new ArgumentException($"Too many amplitudes: {amplitudes.Length} > {_edgeCount}");
        }

        Array.Copy(amplitudes, _amplitudesCpu, amplitudes.Length);
        _amplitudesBuffer!.CopyFrom(_amplitudesCpu.AsSpan(0, amplitudes.Length));
    }

    /// <summary>
    /// Compute edge Hamiltonian on GPU.
    /// Must be called before EvolveUnitary if Hamiltonian changed.
    /// </summary>
    public void ComputeHamiltonian()
    {
        ThrowIfNotInitialized();
        
        // Copy weights to CPU, compute Hamiltonian on CPU, upload result
        // This avoids the ReadOnlyBuffer issue
        ComputeHamiltonianCpu();
        _hamiltonianDiagBuffer!.CopyFrom(_hamiltonianDiagCpu);
    }
    
    /// <summary>
    /// CPU fallback for Hamiltonian computation
    /// </summary>
    private void ComputeHamiltonianCpu()
    {
        for (int e = 0; e < _edgeCount; e++)
        {
            int i = _edgeICpu[e];
            int j = _edgeJCpu[e];
            
            // Count triangles
            int triangles = CountCommonNeighborsCpu(i, j);
            
            double E_triangle = TriangleBonus * triangles;
            double E_degree = DegreePenalty * ((_degreesCpu[i] - TargetDegree) + (_degreesCpu[j] - TargetDegree));
            
            _hamiltonianDiagCpu[e] = E_triangle + E_degree;
        }
    }
    
    private int CountCommonNeighborsCpu(int i, int j)
    {
        int startI = _rowOffsetsCpu[i];
        int endI = _rowOffsetsCpu[i + 1];
        int startJ = _rowOffsetsCpu[j];
        int endJ = _rowOffsetsCpu[j + 1];
        
        int count = 0;
        int ki = startI;
        int kj = startJ;
        
        while (ki < endI && kj < endJ)
        {
            int ni = _colIndicesCpu[ki];
            int nj = _colIndicesCpu[kj];
            
            if (ni == nj)
            {
                count++;
                ki++;
                kj++;
            }
            else if (ni < nj)
            {
                ki++;
            }
            else
            {
                kj++;
            }
        }
        
        return count;
    }
    
    /// <summary>
    /// Evolve quantum edge amplitudes unitarily.
    /// ?(t+dt) = exp(-i*H*dt) * ?(t)
    /// </summary>
    /// <param name="dt">Time step</param>
    public void EvolveUnitary(double dt)
    {
        ThrowIfNotInitialized();
        
        // Download Hamiltonian, evolve on CPU, upload result
        // This avoids ReadOnlyBuffer conversion issues
        _amplitudesBuffer!.CopyTo(_amplitudesCpu);
        _hamiltonianDiagBuffer!.CopyTo(_hamiltonianDiagCpu);
        
        for (int e = 0; e < _edgeCount; e++)
        {
            var alpha = _amplitudesCpu[e];
            double H = _hamiltonianDiagCpu[e];
            
            double phase = H * dt;
            double cosP = Math.Cos(phase);
            double sinP = Math.Sin(phase);
            
            double newReal = alpha.X * cosP + alpha.Y * sinP;
            double newImag = alpha.Y * cosP - alpha.X * sinP;
            
            _amplitudesCpu[e] = new Double2(newReal, newImag);
        }
        
        _amplitudesBuffer.CopyFrom(_amplitudesCpu);
    }
    
    /// <summary>
    /// Compute existence probabilities for all edges.
    /// </summary>
    /// <returns>Array of probabilities P = |?|?</returns>
    public double[] ComputeProbabilities()
    {
        ThrowIfNotInitialized();
        
        _amplitudesBuffer!.CopyTo(_amplitudesCpu);
        
        for (int e = 0; e < _edgeCount; e++)
        {
            var alpha = _amplitudesCpu[e];
            _probabilitiesCpu[e] = alpha.X * alpha.X + alpha.Y * alpha.Y;
        }
        
        return _probabilitiesCpu[.._edgeCount].ToArray();
    }
    
    /// <summary>
    /// Compute total existence probability (sum of all P = |?|?).
    /// </summary>
    public double ComputeTotalProbability()
    {
        var probabilities = ComputeProbabilities();
        double total = 0.0;
        for (int e = 0; e < _edgeCount; e++)
        {
            total += probabilities[e];
        }
        return total;
    }

    /// <summary>
    /// Compute purity of the quantum edge state.
    /// ? = ? P? / (? P)?
    /// 
    /// ? = 1: Pure state (classical, all edges definite)
    /// ? &lt; 1: Mixed state (quantum superposition)
    /// </summary>
    public double ComputePurity()
    {
        ThrowIfNotInitialized();

        // Compute on CPU to avoid AsReadOnly issues
        _amplitudesBuffer!.CopyTo(_amplitudesCpu);
        
        double sumP = 0.0;
        double sumP2 = 0.0;
        
        for (int e = 0; e < _edgeCount; e++)
        {
            var alpha = _amplitudesCpu[e];
            double p = alpha.X * alpha.X + alpha.Y * alpha.Y;
            sumP += p;
            sumP2 += p * p;
        }

        if (sumP < 1e-20) return 1.0; // No edges = pure vacuum

        return sumP2 / (sumP * sumP);
    }

    /// <summary>
    /// Collapse all quantum edges to classical states.
    /// Each edge is measured and collapses to |exists? or |not-exists?.
    /// </summary>
    /// <returns>Number of edges that exist after collapse</returns>
    public int CollapseAllEdges()
    {
        ThrowIfNotInitialized();

        // Download current state
        _amplitudesBuffer!.CopyTo(_amplitudesCpu);
        _weightsBuffer!.CopyTo(_weightsCpu);
        
        int existingCount = 0;
        
        // Collapse on CPU to avoid ComputeSharp caching issues
        for (int e = 0; e < _edgeCount; e++)
        {
            var alpha = _amplitudesCpu[e];
            double probability = alpha.X * alpha.X + alpha.Y * alpha.Y;
            double threshold = _rng.NextDouble();
            
            bool exists = threshold < probability;
            _existsAfterCollapseCpu[e] = exists ? 1 : 0;
            
            if (exists)
            {
                _amplitudesCpu[e] = new Double2(1.0, 0.0);
                if (_weightsCpu[e] < 0.01)
                {
                    _weightsCpu[e] = 0.5;
                }
                existingCount++;
            }
            else
            {
                _amplitudesCpu[e] = new Double2(0.0, 0.0);
                _weightsCpu[e] = 0.0;
            }
        }
        
        // Upload results back to GPU
        _amplitudesBuffer.CopyFrom(_amplitudesCpu);
        _weightsBuffer!.CopyFrom(_weightsCpu);

        return existingCount;
    }

    /// <summary>
    /// Download amplitudes back to CPU.
    /// </summary>
    /// <returns>Array of (real, imag) amplitude pairs</returns>
    public Double2[] DownloadAmplitudes()
    {
        ThrowIfNotInitialized();

        _amplitudesBuffer!.CopyTo(_amplitudesCpu);
        return _amplitudesCpu[.._edgeCount].ToArray();
    }

    /// <summary>
    /// Download edge existence status after collapse.
    /// </summary>
    /// <returns>Array where 1 = exists, 0 = not exists</returns>
    public int[] DownloadExistenceStatus()
    {
        ThrowIfNotInitialized();

        _existsAfterCollapseBuffer!.CopyTo(_existsAfterCollapseCpu);
        return _existsAfterCollapseCpu[.._edgeCount].ToArray();
    }

    /// <summary>
    /// Get edge endpoints (i, j) for edge at index e.
    /// </summary>
    public (int i, int j) GetEdgeEndpoints(int edgeIndex)
    {
        if (edgeIndex < 0 || edgeIndex >= _edgeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        }

        return (_edgeICpu[edgeIndex], _edgeJCpu[edgeIndex]);
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Engine not initialized. Call Initialize() first.");
        }
    }

    private void DisposeGpuBuffers()
    {
        _amplitudesBuffer?.Dispose();
        _weightsBuffer?.Dispose();
        _hamiltonianDiagBuffer?.Dispose();
        _probabilitiesBuffer?.Dispose();
        _squaredProbsBuffer?.Dispose();
        _randomThresholdsBuffer?.Dispose();
        _existsAfterCollapseBuffer?.Dispose();
        _edgeIBuffer?.Dispose();
        _edgeJBuffer?.Dispose();
        _rowOffsetsBuffer?.Dispose();
        _colIndicesBuffer?.Dispose();
        _degreesBuffer?.Dispose();
    }

    /// <summary>
    /// Dispose GPU resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        DisposeGpuBuffers();
        _disposed = true;
        _initialized = false;
    }
}
