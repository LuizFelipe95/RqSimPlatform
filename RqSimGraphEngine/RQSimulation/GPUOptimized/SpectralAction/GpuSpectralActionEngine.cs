using System;
using ComputeSharp;
using RQSimulation.GPUOptimized; // for OllivierRicciCurvature

namespace RQSimulation.GPUOptimized.SpectralAction;

/// <summary>
/// GPU-accelerated Spectral Action computation engine.
/// 
/// PHYSICS:
/// ========
/// The Chamseddine-Connes spectral action principle provides a unified
/// approach to gravity and matter from spectral geometry:
/// 
///   S = Tr(f(D/?)) ? f???V + f????R + f??C? + S_dimension
/// 
/// Terms:
/// - f???V: Cosmological term (volume = total edge weight)
/// - f????R: Einstein-Hilbert term (curvature integral)
/// - f??C?: Weyl conformal term (curvature variance)
/// - S_dimension: Mexican hat potential for d_S = 4 stabilization
/// 
/// GPU Parallelization Strategy:
/// ============================
/// 1. Volume: parallel sum over edges
/// 2. Curvature: parallel over nodes ? reduction for average
/// 3. Weyl: variance via parallel (R-?R?)? ? reduction
/// 4. Dimension: degree computation ? spectral dimension estimate
/// 
/// All computations use double precision (64-bit) for physical accuracy.
/// </summary>
public sealed class GpuSpectralActionEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    
    // CPU arrays for computation
    private int[] _rowOffsets = [];
    private int[] _colIndices = [];
    private double[] _edgeWeights = [];
    private int[] _nodeDegreesInt = [];
    private double[] _curvaturesCpu = [];

    // Optional: per-CSR-entry edge curvatures (Ollivier-Ricci) used for anisotropy
    private double[] _edgeCurvatures = [];

    private int _nodeCount;
    private int _nnz;
    private int _edgeCount;
    private bool _initialized;
    private bool _disposed;
    
    // Cached values for efficiency
    private double _cachedTotalDegree;
    private double _cachedAvgCurvature;
    private bool _curvatureCacheValid;
    
    /// <summary>
    /// If true, when uploading topology the engine will compute edge-level
    /// Ollivier-Ricci curvatures (Jaccard approximation) and use them to form
    /// an anisotropy (Weyl-proxy) per node. This behavior can be toggled from UI.
    /// Default: false (use existing node-degree-based curvature).
    /// </summary>
    public bool UseOllivierRicciForWeyl { get; set; } = false;

    /// <summary>
    /// UV cutoff ?. Default from PhysicsConstants.
    /// </summary>
    public double LambdaCutoff { get; set; }

    /// <summary>
    /// Cosmological coefficient f?. Default from PhysicsConstants.
    /// </summary>
    public double F0_Cosmological { get; set; }

    /// <summary>
    /// Einstein-Hilbert coefficient f?. Default from PhysicsConstants.
    /// </summary>
    public double F2_EinsteinHilbert { get; set; }

    /// <summary>
    /// Weyl coefficient f?. Default from PhysicsConstants.
    /// </summary>
    public double F4_Weyl { get; set; }

    /// <summary>
    /// Target spectral dimension. Default = 4.0.
    /// </summary>
    public double TargetDimension { get; set; }

    /// <summary>
    /// Dimension potential strength. Default from PhysicsConstants.
    /// </summary>
    public double DimensionPotentialStrength { get; set; }

    /// <summary>
    /// Dimension potential width. Default from PhysicsConstants.
    /// </summary>
    public double DimensionPotentialWidth { get; set; }

    /// <summary>
    /// Whether the engine is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;
    
    /// <summary>
    /// Number of nodes in the current graph.
    /// </summary>
    public int NodeCount => _nodeCount;
    
    /// <summary>
    /// Number of edges in the current graph.
    /// </summary>
    public int EdgeCount => _edgeCount;
    
    /// <summary>
    /// Create a new GPU spectral action engine.
    /// </summary>
    public GpuSpectralActionEngine()
    {
        _device = GraphicsDevice.GetDefault();
        LoadDefaultConstants();
    }
    
    /// <summary>
    /// Create a GPU spectral action engine with a specific device.
    /// </summary>
    public GpuSpectralActionEngine(GraphicsDevice device)
    {
        _device = device;
        LoadDefaultConstants();
    }
    
    private void LoadDefaultConstants()
    {
        LambdaCutoff = PhysicsConstants.SpectralActionConstants.LambdaCutoff;
        F0_Cosmological = PhysicsConstants.SpectralActionConstants.F0_Cosmological;
        F2_EinsteinHilbert = PhysicsConstants.SpectralActionConstants.F2_EinsteinHilbert;
        F4_Weyl = PhysicsConstants.SpectralActionConstants.F4_Weyl;
        TargetDimension = PhysicsConstants.SpectralActionConstants.TargetSpectralDimension;
        DimensionPotentialStrength = PhysicsConstants.SpectralActionConstants.DimensionPotentialStrength;
        DimensionPotentialWidth = PhysicsConstants.SpectralActionConstants.DimensionPotentialWidth;
    }
    
    /// <summary>
    /// Initialize the engine for a graph of given size.
    /// </summary>
    /// <param name="nodeCount">Number of nodes</param>
    /// <param name="maxNnz">Maximum non-zero elements (directed edges)</param>
    public void Initialize(int nodeCount, int maxNnz)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 1);
        
        _nodeCount = nodeCount;
        _nnz = maxNnz;
        _edgeCount = maxNnz / 2;
        
        // Allocate CPU arrays
        _rowOffsets = new int[nodeCount + 1];
        _colIndices = new int[Math.Max(1, maxNnz)];
        _edgeWeights = new double[Math.Max(1, maxNnz)];
        _nodeDegreesInt = new int[nodeCount];
        _curvaturesCpu = new double[nodeCount];
        _edgeCurvatures = new double[Math.Max(1, maxNnz)];
        
        _initialized = true;
        _curvatureCacheValid = false;
    }
    
    /// <summary>
    /// Upload graph topology to GPU.
    /// </summary>
    /// <param name="graph">The RQGraph to upload</param>
    public void UploadTopology(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        
        if (!_initialized || graph.N != _nodeCount)
        {
            int nnz = CountNonZeros(graph);
            Initialize(graph.N, nnz);
        }
        
        BuildCsrFromGraph(graph);
        _curvatureCacheValid = false;
    }
    
    /// <summary>
    /// Compute the total spectral action.
    /// 
    /// S = f???V + f????R?g + f??C??g + S_dimension
    /// </summary>
    /// <returns>Total spectral action value</returns>
    public double ComputeSpectralAction()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized.");
        
        if (_nodeCount < 3)
            return 0.0;
        
        // Precompute powers of cutoff
        double lambda2 = LambdaCutoff * LambdaCutoff;
        double lambda4 = lambda2 * lambda2;
        
        // Compute components
        double volume = ComputeEffectiveVolume();
        double avgCurvature = ComputeAverageCurvature();
        double weylSquared = ComputeWeylSquared();
        double spectralDim = EstimateSpectralDimensionFast();
        
        // S = f???V + f????R?V + f?|C|?V + S_dim
        double S_cosmological = F0_Cosmological * lambda4 * volume;
        double S_einstein = F2_EinsteinHilbert * lambda2 * avgCurvature * volume;
        double S_weyl = F4_Weyl * weylSquared * volume;
        double S_dimension = ComputeDimensionPotential(spectralDim);
        
        return S_cosmological + S_einstein + S_weyl + S_dimension;
    }
    
    /// <summary>
    /// Compute effective volume (sum of edge weights).
    /// </summary>
    public double ComputeEffectiveVolume()
    {
        if (!_initialized || _edgeCount == 0)
            return 0.0;
        
        // Sum all edge weights and divide by 2 (each edge appears twice in CSR)
        double totalWeight = 0.0;
        for (int i = 0; i < _nnz; i++)
        {
            totalWeight += _edgeWeights[i];
        }
        
        return totalWeight / 2.0;
    }
    
    /// <summary>
    /// Compute average curvature over all nodes.
    /// </summary>
    public double ComputeAverageCurvature()
    {
        if (!_initialized || _nodeCount == 0)
            return 0.0;
        
        // Compute curvatures if not cached
        if (!_curvatureCacheValid)
        {
            ComputeNodeCurvatures();
        }
        
        return _cachedAvgCurvature;
    }
    
    /// <summary>
    /// Compute Weyl curvature squared (variance proxy).
    /// |C|? ? Var(R) = ?R?? - ?R??
    /// 
    /// If UseOllivierRicciForWeyl is true, uses per-edge Ollivier-Ricci curvatures to
    /// compute a per-node anisotropy (variance of incident edge curvatures) and
    /// returns the average anisotropy across nodes. Otherwise falls back to node
    /// curvature variance based on degree.
    /// </summary>
    public double ComputeWeylSquared()
    {
        if (!_initialized || _nodeCount == 0)
            return 0.0;
        
        if (UseOllivierRicciForWeyl)
        {
            // Ensure edge curvatures are present; if not, attempt to compute using CPU Ollivier methods
            // Note: BuildCsrFromGraph may have populated _edgeCurvatures when topology was uploaded.
            // If not, we cannot compute here without the original RQGraph reference, so fall back gracefully.
            bool hasEdgeCurvatures = _edgeCurvatures != null && _edgeCurvatures.Length >= _nnz;
            if (!hasEdgeCurvatures)
                return 0.0;

            double sumAnisotropy = 0.0;

            for (int i = 0; i < _nodeCount; i++)
            {
                int start = _rowOffsets[i];
                int end = _rowOffsets[i + 1];

                double sumRicci = 0.0;
                double sumSqRicci = 0.0;
                int degree = 0;

                for (int idx = start; idx < end; idx++)
                {
                    double kij = _edgeCurvatures[idx];
                    sumRicci += kij;
                    sumSqRicci += kij * kij;
                    degree++;
                }

                double denom = degree > 0 ? (double)degree : 1.0;
                double avgR = sumRicci / denom;
                double anisotropy = (sumSqRicci / denom) - (avgR * avgR);

                sumAnisotropy += anisotropy;
            }

            return Math.Max(0.0, sumAnisotropy / _nodeCount);
        }

        // Ensure curvatures are computed
        if (!_curvatureCacheValid)
        {
            ComputeNodeCurvatures();
        }
        
        // Compute variance: ?(R? - ?R?)? / N
        double sumVariance = 0.0;
        for (int i = 0; i < _nodeCount; i++)
        {
            double diff = _curvaturesCpu[i] - _cachedAvgCurvature;
            sumVariance += diff * diff;
        }
        
        return Math.Max(0.0, sumVariance / _nodeCount);
    }
    
    /// <summary>
    /// Fast estimation of spectral dimension from average degree.
    /// d_S ? 2 * log(k_avg) / log(2 * k_avg - 1)
    /// </summary>
    public double EstimateSpectralDimensionFast()
    {
        if (!_initialized || _nodeCount < 3)
            return 1.0;
        
        // Ensure curvatures (and degrees) are computed
        if (!_curvatureCacheValid)
        {
            ComputeNodeCurvatures();
        }
        
        // Compute average degree
        double avgDegree = _cachedTotalDegree / _nodeCount;
        
        if (avgDegree < 2.0)
            return 1.0;
        
        // Approximate spectral dimension
        double denominator = Math.Log(2.0 * avgDegree - 1.0);
        if (Math.Abs(denominator) < 1e-10)
            return 2.0;
        
        double d_S = 2.0 * Math.Log(avgDegree) / denominator;
        
        return Math.Clamp(d_S, 1.0, 8.0);
    }
    
    /// <summary>
    /// Compute dimension stabilization potential (Mexican hat).
    /// V(d) = ? * (d - d?)? * ((d - d?)? - w?)
    /// </summary>
    public double ComputeDimensionPotential(double spectralDimension)
    {
        double deviation = spectralDimension - TargetDimension;
        double dev2 = deviation * deviation;
        double w2 = DimensionPotentialWidth * DimensionPotentialWidth;
        
        return DimensionPotentialStrength * dev2 * (dev2 - w2);
    }
    
    /// <summary>
    /// Download computed curvatures to CPU array.
    /// </summary>
    public void DownloadCurvatures(double[] output)
    {
        ArgumentNullException.ThrowIfNull(output);
        
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized.");
        
        if (output.Length != _nodeCount)
            throw new ArgumentException($"Output length ({output.Length}) must match node count ({_nodeCount}).");
        
        if (!_curvatureCacheValid)
        {
            ComputeNodeCurvatures();
        }
        
        Array.Copy(_curvaturesCpu, output, _nodeCount);
    }
    
    /// <summary>
    /// Get detailed breakdown of spectral action components.
    /// </summary>
    public SpectralActionComponents GetComponents()
    {
        if (!_initialized)
            throw new InvalidOperationException("Engine not initialized.");
        
        double lambda2 = LambdaCutoff * LambdaCutoff;
        double lambda4 = lambda2 * lambda2;
        
        double volume = ComputeEffectiveVolume();
        double avgCurvature = ComputeAverageCurvature();
        double weylSquared = ComputeWeylSquared();
        double spectralDim = EstimateSpectralDimensionFast();
        
        return new SpectralActionComponents
        {
            Volume = volume,
            AverageCurvature = avgCurvature,
            WeylSquared = weylSquared,
            SpectralDimension = spectralDim,
            S_Cosmological = F0_Cosmological * lambda4 * volume,
            S_EinsteinHilbert = F2_EinsteinHilbert * lambda2 * avgCurvature * volume,
            S_Weyl = F4_Weyl * weylSquared * volume,
            S_Dimension = ComputeDimensionPotential(spectralDim)
        };
    }

    // ============================================================================
    // Private Methods
    // ============================================================================
    
    private void ComputeNodeCurvatures()
    {
        // Compute total degree
        _cachedTotalDegree = 0.0;
        for (int i = 0; i < _nodeCount; i++)
        {
            _nodeDegreesInt[i] = _rowOffsets[i + 1] - _rowOffsets[i];
            _cachedTotalDegree += _nodeDegreesInt[i];
        }
        
        double avgDegree = _cachedTotalDegree / _nodeCount;
        
        // Compute curvatures: R[i] = (deg[i] - avgDeg) / avgDeg
        double sumCurvature = 0.0;
        for (int i = 0; i < _nodeCount; i++)
        {
            double localDeg = _nodeDegreesInt[i];
            _curvaturesCpu[i] = avgDegree > 1e-10 ? (localDeg - avgDegree) / avgDegree : 0.0;
            sumCurvature += _curvaturesCpu[i];
        }
        
        _cachedAvgCurvature = sumCurvature / _nodeCount;
        _curvatureCacheValid = true;
    }

    private static int CountNonZeros(RQGraph graph)
    {
        int nnz = 0;
        for (int i = 0; i < graph.N; i++)
        {
            foreach (int _ in graph.Neighbors(i))
            {
                nnz++;
            }
        }
        return nnz;
    }
    
    private void BuildCsrFromGraph(RQGraph graph)
    {
        int n = graph.N;
        int offset = 0;
        
        for (int i = 0; i < n; i++)
        {
            _rowOffsets[i] = offset;
            
            foreach (int j in graph.Neighbors(i))
            {
                if (offset < _colIndices.Length)
                {
                    _colIndices[offset] = j;
                    _edgeWeights[offset] = graph.Weights[i, j];
                    offset++;
                }
            }
        }
        
        _rowOffsets[n] = offset;
        _nnz = offset;
        _edgeCount = offset / 2;

        // Optionally compute edge curvatures using Ollivier-Ricci (Jaccard approx) and store in CSR order
        if (UseOllivierRicciForWeyl)
        {
            try
            {
                for (int i = 0; i < n; i++)
                {
                    int start = _rowOffsets[i];
                    int end = _rowOffsets[i + 1];
                    for (int idx = start; idx < end; idx++)
                    {
                        int j = _colIndices[idx];
                        // Use Sinkhorn optimal transport for accurate W₁
                        double k_ij = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(graph, i, j);
                        _edgeCurvatures[idx] = k_ij;
                    }
                }
            }
            catch (Exception ex)
            {
                // Do not swallow exceptions silently; invalidate automation and rethrow
                UseOllivierRicciForWeyl = false;
                throw new InvalidOperationException("Failed to compute Ollivier-Ricci edge curvatures during topology upload.", ex);
            }
        }
    }
    
    // ============================================================================
    // IDisposable
    // ============================================================================
    
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    
    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            _initialized = false;
        }
        
        _disposed = true;
    }
}

/// <summary>
/// Detailed breakdown of spectral action components.
/// </summary>
public readonly struct SpectralActionComponents
{
    /// <summary>Effective volume (sum of edge weights)</summary>
    public double Volume { get; init; }
    
    /// <summary>Average curvature ?R?</summary>
    public double AverageCurvature { get; init; }
    
    /// <summary>Weyl squared |C|? (curvature variance)</summary>
    public double WeylSquared { get; init; }
    
    /// <summary>Estimated spectral dimension d_S</summary>
    public double SpectralDimension { get; init; }
    
    /// <summary>Cosmological term f???V</summary>
    public double S_Cosmological { get; init; }
    
    /// <summary>Einstein-Hilbert term f????R</summary>
    public double S_EinsteinHilbert { get; init; }
    
    /// <summary>Weyl conformal term f??C?</summary>
    public double S_Weyl { get; init; }
    
    /// <summary>Dimension stabilization potential</summary>
    public double S_Dimension { get; init; }
    
    /// <summary>Total spectral action</summary>
    public double Total => S_Cosmological + S_EinsteinHilbert + S_Weyl + S_Dimension;
}
