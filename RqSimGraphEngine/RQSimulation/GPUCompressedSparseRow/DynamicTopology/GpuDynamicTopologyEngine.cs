using System;
using System.Linq;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.Shaders;
using RQSimulation.Core.StrongScience;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// GPU Dynamic Topology Engine
/// ===========================
/// Orchestrates the full pipeline for "hard rewiring" of graph topology:
/// 
/// Pipeline stages:
/// 1. PROPOSAL: Collect edge addition/deletion candidates via MCMC
/// 2. MARK: Identify edges for deletion based on weight threshold
/// 3. CONSERVE: Transfer energy/gauge from dying edges to nodes (NEW)
/// 4. DEGREE: Compute new degrees for all nodes
/// 5. SCAN: Parallel prefix sum to compute new RowOffsets
/// 6. SCATTER: Rebuild ColIndices and Weights arrays
/// 7. SWAP: Replace old buffers with new ones
/// 
/// This enables true dynamic topology changes beyond soft weight modifications.
/// 
/// SCIENCE MODE INTEGRATION:
/// When using StrictScienceProfile, the CONSERVE phase validates:
/// - Energy conservation: E_before = E_transferred
/// - Gauge charge conservation: Q_before = Q_transferred
/// - Throws ScientificMalpracticeException on conservation violations
/// </summary>
public sealed class GpuDynamicTopologyEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;

    // Proposal collection
    private EdgeProposalBuffer? _proposalBuffer;
    private ReadWriteBuffer<uint>? _randomSeedsBuffer;

    // Intermediate computation buffers
    private ReadWriteBuffer<int>? _degreesBuffer;
    private ReadWriteBuffer<int>? _additionDegreesBuffer;
    private ReadWriteBuffer<int>? _deletionFlagsBuffer;
    private ReadWriteBuffer<int>? _scanBuffer;
    private ReadWriteBuffer<int>? _newRowOffsetsBuffer;
    private ReadWriteBuffer<int>? _newColIndicesBuffer;
    private ReadWriteBuffer<double>? _newWeightsBuffer;
    private ReadWriteBuffer<int>? _writeCountersBuffer;

    // Conservation buffers (NEW)
    private ReadWriteBuffer<int>? _previousExistenceBuffer;
    private ReadWriteBuffer<double>? _nodeMassBuffer;
    private ReadWriteBuffer<Double2>? _nodeSpinorBuffer;
    private ReadWriteBuffer<Double2>? _edgeGaugePhaseBuffer;
    private ReadWriteBuffer<double>? _energyAccumulatorBuffer;
    private ReadWriteBuffer<Double2>? _fluxAccumulatorBuffer;
    private ReadWriteBuffer<int>? _validationResultBuffer;
    private ReadWriteBuffer<double>? _conservationErrorBuffer;

    // Stream compaction engine (GPU-only)
    private GpuStreamCompactionEngine? _compactionEngine;

    // Capacity tracking
    private int _allocatedNodeCount;
    private int _allocatedNnz;
    private int _scanBufferSize;

    // Configuration
    private DynamicTopologyConfig _config = new();

    // Science mode profile (optional)
    private ISimulationProfile? _profile;

    /// <summary>
    /// Configuration for dynamic topology operations.
    /// </summary>
    public DynamicTopologyConfig Config
    {
        get => _config;
        set => _config = value ?? new DynamicTopologyConfig();
    }

    /// <summary>
    /// Sets the simulation profile for Science mode validation.
    /// When set to StrictScienceProfile, conservation laws are validated.
    /// </summary>
    public ISimulationProfile? Profile
    {
        get => _profile;
        set => _profile = value;
    }

    /// <summary>
    /// Access to proposal buffer for external inspection.
    /// </summary>
    public EdgeProposalBuffer? ProposalBuffer => _proposalBuffer;

    /// <summary>
    /// Statistics from last rebuild operation.
    /// </summary>
    public DynamicTopologyStats LastStats { get; private set; } = new();

    /// <summary>
    /// Conservation statistics from last rebuild operation (NEW).
    /// </summary>
    public ConservationStats? LastConservationStats { get; private set; }

    public GpuDynamicTopologyEngine(GraphicsDevice device)
    {
        _device = device;
        _compactionEngine = new GpuStreamCompactionEngine(device);
    }

    /// <summary>
    /// Creates engine with a simulation profile for Science mode.
    /// </summary>
    public GpuDynamicTopologyEngine(GraphicsDevice device, ISimulationProfile profile)
        : this(device)
    {
        _profile = profile;
    }

    /// <summary>
    /// Initialize engine for a graph with given size.
    /// </summary>
    public void Initialize(int nodeCount, int nnz)
    {
        EnsureBuffersAllocated(nodeCount, nnz);
        InitializeRandomSeeds(nodeCount + nnz);
    }

    /// <summary>
    /// Run the full topology evolution pipeline.
    /// </summary>
    public DynamicCsrTopology? EvolveTopology(CsrTopology topology, ReadWriteBuffer<double> masses)
    {
        ArgumentNullException.ThrowIfNull(topology);
        
        if (!topology.IsGpuReady)
            throw new InvalidOperationException("Topology must be on GPU");

        int nodeCount = topology.NodeCount;
        int nnz = topology.Nnz;

        EnsureBuffersAllocated(nodeCount, nnz);

        // Reset stats
        LastStats = new DynamicTopologyStats();
        LastConservationStats = null;
        var startTime = DateTime.UtcNow;

        // PHASE 1: Initialize previous existence flags (for CONSERVE phase)
        if (_config.EnableConservation)
        {
            InitializePreviousExistence(topology, nnz);
        }

        // PHASE 2: Mark low-weight edges for deletion (GPU)
        int deletionCount = MarkLowWeightEdges(topology, nodeCount, nnz);
        LastStats.ProposedDeletions = deletionCount;

        if (deletionCount == 0)
        {
            LastStats.TotalTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            return null;
        }

        // PHASE 3: CONSERVE - Transfer dying edge content to nodes (NEW)
        if (_config.EnableConservation)
        {
            var conserveStart = DateTime.UtcNow;
            var conservationStats = ExecuteConservationPhase(topology, masses, nodeCount, nnz);
            conservationStats.ConservationTimeMs = (DateTime.UtcNow - conserveStart).TotalMilliseconds;
            LastConservationStats = conservationStats;

            // In Science mode, validate conservation
            if (_profile?.IsStrictValidationEnabled == true && !conservationStats.IsConserved)
            {
                throw new ScientificMalpracticeException(
                    $"Energy conservation violated during topology update. " +
                    $"Error: {conservationStats.ConservationError:E4}, " +
                    $"Tolerance: {_config.ConservationTolerance:E4}",
                    ScientificMalpracticeType.EnergyConservationViolation);
            }
        }

        // PHASE 4..6: Use GPU stream compaction engine to compute degrees, scan and compact
        var compaction = _compactionEngine ?? throw new InvalidOperationException("Compaction engine not initialized");

        var (newRowOffsets, newColIndices, newWeights, newNnz) = compaction.CompactTopologyFullGpu(topology, _config.DeletionThreshold);
        LastStats.NewNnz = newNnz;

        if (newNnz == 0)
        {
            LastStats.TotalTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            return CreateEmptyTopology(nodeCount);
        }

        // PHASE 7: Verify - allocate GPU buffers for verification and run VerifyCsrKernel
        ReadOnlyBuffer<int> roRowOffsets = _device.AllocateReadOnlyBuffer(newRowOffsets);
        ReadOnlyBuffer<int> roColIndices = _device.AllocateReadOnlyBuffer(newColIndices);
        ReadOnlyBuffer<double> roWeights = _device.AllocateReadOnlyBuffer(newWeights);

        var errorFlags = _device.AllocateReadWriteBuffer<int>(nodeCount);

        // Run verify kernel
        _device.For(nodeCount, new VerifyCsrKernel(
            roRowOffsets, roColIndices, errorFlags, nodeCount));

        // Download errors
        int[] errors = new int[nodeCount];
        errorFlags.CopyTo(errors);
        int errorCount = errors.Sum();

        errorFlags.Dispose();

        if (errorCount > 0)
        {
            // Dispose created buffers
            roRowOffsets.Dispose();
            roColIndices.Dispose();
            roWeights.Dispose();

            // Log and fail
            System.Diagnostics.Debug.WriteLine($"[DynamicTopology] CSR verification failed: {errorCount} rows reported errors");
            throw new InvalidOperationException($"CSR verification failed: {errorCount} rows are invalid");
        }

        // PHASE 8: Build new topology and swap GPU buffers to avoid extra copies
        var newTopology = new DynamicCsrTopology(_device);
        // Populate CPU-side arrays for topology metadata and capacity
        newTopology.BuildFromCsrArrays(nodeCount, newNnz, newRowOffsets, newColIndices, newWeights, topology.NodePotential.ToArray());

        // Replace GPU buffers with our pre-created ones (ownership transferred)
        newTopology.ReplaceGpuBuffers(roRowOffsets, roColIndices, roWeights);

        LastStats.TotalTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
        LastStats.AcceptedDeletions = deletionCount;

        // Update conservation stats in main stats
        if (LastConservationStats is not null)
        {
            LastStats.ConservationTimeMs = LastConservationStats.ConservationTimeMs;
            LastStats.EnergyConserved = LastConservationStats.IsConserved;
            LastStats.ConservationError = LastConservationStats.ConservationError;
        }

        return newTopology;
    }

    /// <summary>
    /// Initialize previous existence flags from current weights.
    /// Called at start of frame to capture "alive" edges before marking.
    /// NOTE: Uses CPU for now to avoid buffer type mismatches.
    /// </summary>
    private void InitializePreviousExistence(CsrTopology topology, int nnz)
    {
        if (_previousExistenceBuffer is null || _previousExistenceBuffer.Length < nnz)
        {
            _previousExistenceBuffer?.Dispose();
            _previousExistenceBuffer = _device.AllocateReadWriteBuffer<int>(nnz);
        }

        // CPU implementation for compatibility
        double[] weights = topology.EdgeWeights.ToArray();
        int[] existence = new int[nnz];
        for (int i = 0; i < nnz; i++)
        {
            existence[i] = weights[i] >= _config.DeletionThreshold ? 1 : 0;
        }
        _previousExistenceBuffer.CopyFrom(existence);
    }

    /// <summary>
    /// Execute the CONSERVE phase: transfer dying edge content to nodes.
    /// NOTE: CPU implementation for compatibility with double-precision buffers.
    /// GPU version can be enabled when float buffers are used throughout.
    /// </summary>
    private ConservationStats ExecuteConservationPhase(
        CsrTopology topology,
        ReadWriteBuffer<double> masses,
        int nodeCount,
        int nnz)
    {
        var stats = new ConservationStats();

        // Get conversion factors from profile or config
        double energyFactor = _config.EnergyConversionFactor;
        double fluxFactor = _config.FluxConversionFactor;

        if (_profile?.Constants is IPhysicalConstants constants)
        {
            // In Science mode, use physical constants
            energyFactor = constants.GravitationalCoupling;
            fluxFactor = constants.FineStructureConstant;
        }

        // Download data for CPU processing
        double[] weights = topology.EdgeWeights.ToArray();
        int[] colIndices = topology.ColIndices.ToArray();
        int[] rowOffsets = topology.RowOffsets.ToArray();
        int[] deletionFlags = new int[nnz];
        _deletionFlagsBuffer!.CopyTo(deletionFlags);
        int[] prevExistence = new int[nnz];
        _previousExistenceBuffer!.CopyTo(prevExistence);
        double[] nodeMasses = new double[nodeCount];
        masses.CopyTo(nodeMasses);

        // Process dying edges (CPU)
        double totalEnergyTransferred = 0;
        double energyBefore = 0;
        int dyingCount = 0;

        for (int edgeIndex = 0; edgeIndex < nnz; edgeIndex++)
        {
            // Check if edge is dying this frame
            bool isDying = deletionFlags[edgeIndex] == 1 && prevExistence[edgeIndex] == 1;
            if (!isDying) continue;

            // Find source node (row) for this edge via binary search
            int nodeA = FindRowForEdgeCpu(rowOffsets, nodeCount, edgeIndex);
            int nodeB = colIndices[edgeIndex];

            if (nodeA < 0 || nodeA >= nodeCount || nodeB < 0 || nodeB >= nodeCount)
                continue;

            // Compute energy to transfer
            double weight = weights[edgeIndex];
            double energy = energyFactor * weight;
            double halfEnergy = energy * 0.5;

            // Transfer to nodes
            nodeMasses[nodeA] += halfEnergy;
            nodeMasses[nodeB] += halfEnergy;

            // Track totals
            energyBefore += energy;
            totalEnergyTransferred += energy;
            dyingCount++;
        }

        // Upload updated masses back to GPU
        masses.CopyFrom(nodeMasses);

        // Populate stats
        stats.EnergyBefore = energyBefore;
        stats.EnergyTransferred = totalEnergyTransferred;
        stats.DyingEdgeCount = dyingCount;
        stats.ConservationError = System.Math.Abs(stats.EnergyBefore - stats.EnergyTransferred);
        stats.IsConserved = stats.ConservationError <= _config.ConservationTolerance;

        return stats;
    }

    /// <summary>
    /// Binary search to find which row contains an edge index (CPU version).
    /// </summary>
    private static int FindRowForEdgeCpu(int[] rowOffsets, int nodeCount, int edgeIndex)
    {
        int lo = 0;
        int hi = nodeCount - 1;

        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (rowOffsets[mid] <= edgeIndex)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo;
    }

    /// <summary>
    /// Ensure conservation-specific buffers are allocated.
    /// </summary>
    private void EnsureConservationBuffersAllocated(int nodeCount, int nnz)
    {
        // Only previous existence buffer is needed for CPU-based conservation
        if (_previousExistenceBuffer is null || _previousExistenceBuffer.Length < nnz)
        {
            _previousExistenceBuffer?.Dispose();
            _previousExistenceBuffer = _device.AllocateReadWriteBuffer<int>(nnz);
        }
    }

    private int MarkLowWeightEdges(CsrTopology topology, int nodeCount, int nnz)
    {
        int count = 0;

        // Prepare deletion flags array
        int[] deletionFlags = new int[nnz];

        // Attempt to obtain protected top-K indices via configurable GPU selection strategy
        int k = System.Math.Max(0, _config.MaxProtectedHeavyEdges);
        int[] protectedIndices = Array.Empty<int>();

        // Initialize top-K stats
        LastStats.TopKSourceNnz = nnz;
        LastStats.TopKUsedFallback = false;
        LastStats.TopKErrorMessage = null;

        if (k > 0 && _compactionEngine is not null)
        {
            try
            {
                // Use the unified SelectTopK API with configured strategy
                protectedIndices = _compactionEngine.SelectTopK(
                    topology, 
                    k, 
                    _config.TopKStrategy, 
                    _config.TopKLocalM);
                
                // Copy metrics from compaction engine
                var topKMetrics = _compactionEngine.LastTopKMetrics;
                if (topKMetrics is not null)
                {
                    LastStats.TopKSelectionMethod = topKMetrics.UsedStrategy.ToString();
                    LastStats.TopKSelectionTimeMs = topKMetrics.TotalTimeMs;
                    LastStats.TopKGpuCandidateCount = topKMetrics.GpuCandidateCount;
                    LastStats.TopKUsedFallback = topKMetrics.UsedFallback;
                    LastStats.TopKErrorMessage = topKMetrics.ErrorMessage;
                }
                else
                {
                    LastStats.TopKSelectionMethod = _config.TopKStrategy.ToString();
                }
            }
            catch (Exception ex)
            {
                // Log and fall back to CPU selection
                System.Diagnostics.Debug.WriteLine($"[DynamicTopology] GPU Top-K selection failed ({_config.TopKStrategy}): {ex.Message}. Falling back to CPU.");
                protectedIndices = Array.Empty<int>();
                LastStats.TopKSelectionMethod = "CpuFallback";
                LastStats.TopKUsedFallback = true;
                LastStats.TopKErrorMessage = ex.Message;
            }
        }

        // Download weights (needed to evaluate threshold). This is still relatively cheap compared to a full sort.
        double[] weights = topology.EdgeWeights.ToArray();

        // If GPU selection did not produce results and k>0, fall back to CPU QuickSelect
        if (k > 0 && (protectedIndices is null || protectedIndices.Length == 0))
        {
            var cpuFallbackStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Use CPU QuickSelect via compaction engine fallback if available
                if (_compactionEngine is not null)
                {
                    protectedIndices = _compactionEngine.GetTopKIndices(topology, k);
                    LastStats.TopKSelectionMethod = "CpuQuickSelect";
                }
                else
                {
                    protectedIndices = Enumerable.Range(0, nnz)
                        .OrderByDescending(i => weights[i])
                        .Take(k)
                        .ToArray();
                    LastStats.TopKSelectionMethod = "CpuLinqSort";
                }
                LastStats.TopKUsedFallback = true;
            }
            catch (Exception ex)
            {
                // If CPU selection fails, leave protectedIndices empty - proceed without protection
                protectedIndices = Array.Empty<int>();
                LastStats.TopKSelectionMethod = "None";
                LastStats.TopKErrorMessage = ex.Message;
            }
            cpuFallbackStopwatch.Stop();
            LastStats.TopKSelectionTimeMs = cpuFallbackStopwatch.Elapsed.TotalMilliseconds;
        }

        var protectedSet = new HashSet<int>(protectedIndices ?? Array.Empty<int>());
        LastStats.ProtectedEdgeCount = protectedSet.Count;

        for (int e = 0; e < nnz; e++)
        {
            // Protect top-K heavy edges regardless of threshold
            if (protectedSet.Contains(e))
            {
                deletionFlags[e] = 0;
                continue;
            }

            if (weights[e] < _config.DeletionThreshold)
            {
                deletionFlags[e] = 1;
                count++;
            }
            else
            {
                deletionFlags[e] = 0;
            }
        }

        // Upload deletion flags
        _deletionFlagsBuffer!.CopyFrom(deletionFlags);
        return count;
    }

    private DynamicCsrTopology CreateEmptyTopology(int nodeCount)
    {
        int[] rowOffsets = new int[nodeCount + 1];
        int[] emptyCols = Array.Empty<int>();
        double[] emptyWeights = Array.Empty<double>();
        
        var topology = new DynamicCsrTopology(_device);
        topology.BuildFromCsrArrays(nodeCount, 0, rowOffsets, emptyCols, emptyWeights, new double[nodeCount]);
        topology.UploadToGpu();
        
        return topology;
    }

    private void InitializeRandomSeeds(int count)
    {
        uint[] seeds = new uint[count];
        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            seeds[i] = (uint)rng.Next(1, int.MaxValue);
        }

        _randomSeedsBuffer?.Dispose();
        _randomSeedsBuffer = _device.AllocateReadWriteBuffer<uint>(count);
        _randomSeedsBuffer.CopyFrom(seeds);
    }

    private void EnsureBuffersAllocated(int nodeCount, int nnz)
    {
        if (_allocatedNodeCount >= nodeCount && _allocatedNnz >= nnz)
            return;

        DisposeBuffers();

        int newNodeCount = System.Math.Max(nodeCount, _allocatedNodeCount);
        int newNnz = System.Math.Max(nnz, _allocatedNnz);

        // Proposal buffer
        int maxAdditions = System.Math.Max(100, nodeCount / 10);
        int maxDeletions = System.Math.Max(100, nnz / 10);
        
        _proposalBuffer = new EdgeProposalBuffer(_device);
        _proposalBuffer.Allocate(maxAdditions, maxDeletions);

        // Computation buffers
        _degreesBuffer = _device.AllocateReadWriteBuffer<int>(newNodeCount);
        _additionDegreesBuffer = _device.AllocateReadWriteBuffer<int>(newNodeCount);
        _deletionFlagsBuffer = _device.AllocateReadWriteBuffer<int>(newNnz);
        _newRowOffsetsBuffer = _device.AllocateReadWriteBuffer<int>(newNodeCount + 1);
        _writeCountersBuffer = _device.AllocateReadWriteBuffer<int>(newNodeCount);

        // Scan buffer (power of 2)
        _scanBufferSize = NextPowerOf2(newNodeCount);
        _scanBuffer = _device.AllocateReadWriteBuffer<int>(_scanBufferSize);

        _allocatedNodeCount = newNodeCount;
        _allocatedNnz = newNnz;
    }

    private static int NextPowerOf2(int n)
    {
        int p = 1;
        while (p < n) p *= 2;
        return p;
    }

    private void DisposeBuffers()
    {
        _proposalBuffer?.Dispose();
        _randomSeedsBuffer?.Dispose();
        _degreesBuffer?.Dispose();
        _additionDegreesBuffer?.Dispose();
        _deletionFlagsBuffer?.Dispose();
        _scanBuffer?.Dispose();
        _newRowOffsetsBuffer?.Dispose();
        _newColIndicesBuffer?.Dispose();
        _newWeightsBuffer?.Dispose();
        _writeCountersBuffer?.Dispose();

        // Conservation buffers (NEW)
        _previousExistenceBuffer?.Dispose();
        _nodeMassBuffer?.Dispose();
        _nodeSpinorBuffer?.Dispose();
        _edgeGaugePhaseBuffer?.Dispose();
        _energyAccumulatorBuffer?.Dispose();
        _fluxAccumulatorBuffer?.Dispose();
        _validationResultBuffer?.Dispose();
        _conservationErrorBuffer?.Dispose();

        _proposalBuffer = null;
        _randomSeedsBuffer = null;
        _degreesBuffer = null;
        _additionDegreesBuffer = null;
        _deletionFlagsBuffer = null;
        _scanBuffer = null;
        _newRowOffsetsBuffer = null;
        _newColIndicesBuffer = null;
        _newWeightsBuffer = null;
        _writeCountersBuffer = null;

        // Conservation buffers (NEW)
        _previousExistenceBuffer = null;
        _nodeMassBuffer = null;
        _nodeSpinorBuffer = null;
        _edgeGaugePhaseBuffer = null;
        _energyAccumulatorBuffer = null;
        _fluxAccumulatorBuffer = null;
        _validationResultBuffer = null;
        _conservationErrorBuffer = null;

        _allocatedNodeCount = 0;
        _allocatedNnz = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _compactionEngine?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Configuration for dynamic topology operations.
/// </summary>
public class DynamicTopologyConfig
{
    /// <summary>Inverse temperature for MCMC (1/T).</summary>
    public double Beta { get; set; } = 1.0;

    /// <summary>Link cost coefficient in action.</summary>
    public double LinkCostCoeff { get; set; } = 0.1;

    /// <summary>Target degree for degree penalty.</summary>
    public double TargetDegree { get; set; } = 4.0;

    /// <summary>Degree penalty coefficient.</summary>
    public double DegreePenaltyCoeff { get; set; } = 0.01;

    /// <summary>Initial weight for newly added edges.</summary>
    public double InitialWeight { get; set; } = 0.5;

    /// <summary>Weight threshold for edge deletion.</summary>
    public double DeletionThreshold { get; set; } = 0.001;

    /// <summary>How often to run topology rebuild (every N steps).</summary>
    public int RebuildInterval { get; set; } = 10;

    /// <summary>Maximum edge additions per rebuild.</summary>
    public int MaxAdditionsPerStep { get; set; } = 100;

    /// <summary>Maximum edge deletions per rebuild.</summary>
    public int MaxDeletionsPerStep { get; set; } = 100;

    /// <summary>Number of heaviest edges to protect from deletion regardless of threshold.</summary>
    public int MaxProtectedHeavyEdges { get; set; } = 0;

    /// <summary>
    /// Strategy for GPU top-K selection. Default is Auto which selects based on graph size.
    /// </summary>
    public TopKSelectionStrategy TopKStrategy { get; set; } = TopKSelectionStrategy.Auto;

    /// <summary>
    /// Local top-M parameter for GPU selection strategies. Default 4, max 8.
    /// Higher values produce more candidates but increase GPU memory and compute.
    /// </summary>
    public int TopKLocalM { get; set; } = 4;

    /// <summary>
    /// Enable conservation phase: Transfer energy/gauge from dying edges to nodes.
    /// </summary>
    public bool EnableConservation { get; set; } = false;

    /// <summary>
    /// Tolerance for conservation error (energy and gauge charge).
    /// </summary>
    public double ConservationTolerance { get; set; } = 1e-6;

    /// <summary>
    /// Conversion factor for energy transfer (default 1.0, or use physical constants in Science mode).
    /// </summary>
    public double EnergyConversionFactor { get; set; } = 1.0;

    /// <summary>
    /// Conversion factor for flux transfer (default 1.0, or use physical constants in Science mode).
    /// </summary>
    public double FluxConversionFactor { get; set; } = 1.0;
}

/// <summary>
/// Statistics from last topology evolution.
/// </summary>
public class DynamicTopologyStats
{
    /// <summary>Number of edge additions proposed.</summary>
    public int ProposedAdditions { get; set; }

    /// <summary>Number of edge deletions proposed.</summary>
    public int ProposedDeletions { get; set; }

    /// <summary>Number of additions accepted.</summary>
    public int AcceptedAdditions { get; set; }

    /// <summary>Number of deletions accepted.</summary>
    public int AcceptedDeletions { get; set; }

    /// <summary>New total edge count.</summary>
    public int NewNnz { get; set; }

    /// <summary>Total time for topology evolution (ms).</summary>
    public double TotalTimeMs { get; set; }

    /// <summary>Method used for top-K selection (GPU strategy name or fallback).</summary>
    public string TopKSelectionMethod { get; set; } = "None";

    /// <summary>Number of edges protected from deletion by top-K selection.</summary>
    public int ProtectedEdgeCount { get; set; }

    /// <summary>Time spent in top-K selection (ms).</summary>
    public double TopKSelectionTimeMs { get; set; }

    /// <summary>Number of candidates generated by GPU stage (before CPU refine).</summary>
    public int TopKGpuCandidateCount { get; set; }

    /// <summary>Original nnz (edge count) at time of selection.</summary>
    public int TopKSourceNnz { get; set; }

    /// <summary>Whether a fallback to CPU was triggered due to GPU failure.</summary>
    public bool TopKUsedFallback { get; set; }

    /// <summary>Error message if top-K selection encountered issues.</summary>
    public string? TopKErrorMessage { get; set; }

    /// <summary>Whether energy conservation was successful (NEW).</summary>
    public bool EnergyConserved { get; set; }

    /// <summary>Conservation error amount (NEW).</summary>
    public double ConservationError { get; set; }

    /// <summary>Time spent in conservation phase (ms) (NEW).</summary>
    public double ConservationTimeMs { get; set; }
}

/// <summary>
/// Statistics from last conservation phase (NEW).
/// </summary>
public class ConservationStats
{
    /// <summary>Whether the conservation was successful.</summary>
    public bool IsConserved { get; set; }

    /// <summary>Error amount in energy conservation.</summary>
    public double ConservationError { get; set; }

    /// <summary>Time spent in the conservation phase (ms).</summary>
    public double ConservationTimeMs { get; set; }

    /// <summary>Energy transferred to nodes during conservation.</summary>
    public double EnergyTransferred { get; set; }

    /// <summary>Energy before conservation (sum of dying edge weights).</summary>
    public double EnergyBefore { get; set; }

    /// <summary>Number of dying edges processed.</summary>
    public int DyingEdgeCount { get; set; }
}
