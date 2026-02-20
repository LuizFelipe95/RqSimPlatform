using System;
using System.Linq;
using ComputeSharp;
using RQSimulation.GPUCompressedSparseRow.Data;
using RQSimulation.GPUCompressedSparseRow.Shaders;

namespace RQSimulation.GPUCompressedSparseRow;

/// <summary>
/// GPU Stream Compaction Engine
/// ============================
/// Orchestrates the full GPU pipeline for dynamic CSR topology updates:
/// 
/// 1. MARK: Identify active edges (weight >= threshold)
/// 2. SCAN: Compute new row offsets via Blelloch prefix sum
/// 3. COMPACT: Scatter active edges to new positions
/// 4. SWAP: Replace old CSR buffers with compacted ones
/// 
/// This enables dynamic topology changes (edge dissolution) entirely on GPU.
/// </summary>
public sealed class GpuStreamCompactionEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private bool _disposed;

    // Reusable GPU buffers for compaction
    private ReadWriteBuffer<int>? _flagsBuffer;
    private ReadWriteBuffer<int>? _scatterIndicesBuffer;
    private ReadWriteBuffer<int>? _newDegreesBuffer;
    private ReadWriteBuffer<int>? _newRowOffsetsBuffer;
    private ReadWriteBuffer<int>? _newColIndicesBuffer;
    private ReadWriteBuffer<double>? _newWeightsBuffer;
    private ReadWriteBuffer<int>? _writeCountersBuffer;
    
    // For Blelloch scan
    private ReadWriteBuffer<int>? _scanTempBuffer;
    private ReadWriteBuffer<int>? _blockSumsBuffer;

    private int _allocatedNodeCount;
    private int _allocatedNnz;

    /// <summary>
    /// Block size for Blelloch scan (power of 2).
    /// </summary>
    public int ScanBlockSize { get; set; } = 256;

    /// <summary>
    /// Metrics from the last top-K selection operation.
    /// </summary>
    public TopKSelectionMetrics? LastTopKMetrics { get; private set; }

    public GpuStreamCompactionEngine(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Perform stream compaction on CSR topology.
    /// Removes edges with weight below threshold.
    /// </summary>
    /// <param name="topology">CSR topology to compact (modified in place conceptually)</param>
    /// <param name="weightThreshold">Minimum weight to keep an edge</param>
    /// <returns>New compacted CSR arrays (caller should swap into topology)</returns>
    public (int[] newRowOffsets, int[] newColIndices, double[] newWeights, int newNnz) 
        CompactTopology(CsrTopology topology, double weightThreshold)
    {
        if (!topology.IsGpuReady)
            throw new InvalidOperationException("Topology must be uploaded to GPU first.");

        int nodeCount = topology.NodeCount;
        int nnz = topology.Nnz;

        // Ensure buffers are allocated
        EnsureBuffersAllocated(nodeCount, nnz);

        // PHASE 1: Compute new degrees per node
        _device.For(nodeCount, new ComputeNewDegreesKernel(
            topology.RowOffsetsBuffer,
            topology.EdgeWeightsBuffer,
            _newDegreesBuffer!,
            weightThreshold,
            nodeCount));

        // Download degrees to compute prefix sum on CPU (simpler for now)
        // Full GPU Blelloch would require multi-pass kernel launches
        int[] newDegrees = new int[nodeCount];
        _newDegreesBuffer!.CopyTo(newDegrees);

        // PHASE 2: Compute new row offsets (exclusive prefix sum on CPU)
        int[] newRowOffsets = new int[nodeCount + 1];
        newRowOffsets[0] = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            newRowOffsets[i + 1] = newRowOffsets[i] + newDegrees[i];
        }
        int newNnz = newRowOffsets[nodeCount];

        if (newNnz == 0)
        {
            // All edges removed
            return (newRowOffsets, Array.Empty<int>(), Array.Empty<double>(), 0);
        }

        // Upload new row offsets
        EnsureNewBuffersAllocated(newNnz);
        _newRowOffsetsBuffer!.CopyFrom(newRowOffsets);

        // Zero write counters
        _device.For(nodeCount, new ZeroCountersKernel(_writeCountersBuffer!, nodeCount));

        // PHASE 3: Compact using atomic kernel
        _device.For(nodeCount, new CompactCsrAtomicKernel(
            topology.RowOffsetsBuffer,
            topology.ColIndicesBuffer,
            topology.EdgeWeightsBuffer,
            _newRowOffsetsBuffer!,
            _newColIndicesBuffer!,
            _newWeightsBuffer!,
            _writeCountersBuffer!,
            weightThreshold,
            nodeCount));

        // Download results
        int[] newColIndices = new int[newNnz];
        double[] newWeights = new double[newNnz];
        _newColIndicesBuffer!.CopyTo(newColIndices.AsSpan());
        _newWeightsBuffer!.CopyTo(newWeights.AsSpan());

        return (newRowOffsets, newColIndices, newWeights, newNnz);
    }

    /// <summary>
    /// Perform full GPU Blelloch scan on an array.
    /// </summary>
    public void BlellochScan(ReadWriteBuffer<int> data, int n)
    {
        if (n <= 0) return;

        // Round up to power of 2
        int paddedN = NextPowerOf2(n);
        
        // UP-SWEEP PHASE
        for (int stride = 1; stride < paddedN; stride *= 2)
        {
            int numThreads = paddedN / (2 * stride);
            if (numThreads > 0)
            {
                _device.For(numThreads, new BlellochUpSweepKernel(
                    data, paddedN, stride, 2 * stride - 1));
            }
        }

        // Set root to zero for exclusive scan
        _device.For(1, new BlellochSetRootZeroKernel(data, paddedN - 1));

        // DOWN-SWEEP PHASE
        for (int stride = paddedN / 2; stride >= 1; stride /= 2)
        {
            int numThreads = paddedN / (2 * stride);
            if (numThreads > 0)
            {
                _device.For(numThreads, new BlellochDownSweepKernel(data, paddedN, stride));
            }
        }
    }

    /// <summary>
    /// Perform stream compaction entirely on GPU using Blelloch scan.
    /// More efficient for large graphs.
    /// </summary>
    public (int[] newRowOffsets, int[] newColIndices, double[] newWeights, int newNnz)
        CompactTopologyFullGpu(CsrTopology topology, double weightThreshold)
    {
        if (!topology.IsGpuReady)
            throw new InvalidOperationException("Topology must be uploaded to GPU first.");

        int nodeCount = topology.NodeCount;
        int nnz = topology.Nnz;

        EnsureBuffersAllocated(nodeCount, nnz);

        // PHASE 1: Mark active edges
        _device.For(nnz, new MarkActiveEdgesKernel(
            topology.EdgeWeightsBuffer,
            _flagsBuffer!,
            weightThreshold,
            nnz));

        // Copy flags to scatter indices buffer for in-place scan
        int[] flags = new int[nnz];
        _flagsBuffer!.CopyTo(flags);
        _scatterIndicesBuffer!.CopyFrom(flags);

        // PHASE 2: Exclusive prefix sum of flags (Blelloch)
        BlellochScan(_scatterIndicesBuffer!, nnz);

        // Get total count (sum of flags)
        int totalActive = flags.Sum();
        if (totalActive == 0)
        {
            int[] emptyOffsets = new int[nodeCount + 1];
            return (emptyOffsets, Array.Empty<int>(), Array.Empty<double>(), 0);
        }

        // Allocate output buffers
        EnsureNewBuffersAllocated(totalActive);

        // PHASE 3: Scatter compact
        _device.For(nnz, new CompactCsrScatterKernel(
            topology.ColIndicesBuffer,
            topology.EdgeWeightsBuffer,
            _flagsBuffer!,
            _scatterIndicesBuffer!,
            _newColIndicesBuffer!,
            _newWeightsBuffer!,
            nnz));

        // PHASE 4: Compute new row offsets from degrees
        _device.For(nodeCount, new ComputeNewDegreesKernel(
            topology.RowOffsetsBuffer,
            topology.EdgeWeightsBuffer,
            _newDegreesBuffer!,
            weightThreshold,
            nodeCount));

        int[] newDegrees = new int[nodeCount];
        _newDegreesBuffer!.CopyTo(newDegrees);

        int[] newRowOffsets = new int[nodeCount + 1];
        newRowOffsets[0] = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            newRowOffsets[i + 1] = newRowOffsets[i] + newDegrees[i];
        }

        // Download results
        int[] newColIndices = new int[totalActive];
        double[] newWeights = new double[totalActive];
        _newColIndicesBuffer!.CopyTo(newColIndices.AsSpan());
        _newWeightsBuffer!.CopyTo(newWeights.AsSpan());

        return (newRowOffsets, newColIndices, newWeights, totalActive);
    }

    /// <summary>
    /// Get indices of top-K heaviest edges (by weight).
    /// Currently implemented as an optimized CPU QuickSelect-based selection as a fallback.
    /// TODO: implement full GPU top-K selection to avoid downloading large buffers.
    /// </summary>
    /// <param name="topology">CSR topology (must be GPU-ready)</param>
    /// <param name="k">Number of heaviest edges to return</param>
    /// <returns>Array of edge indices (length <= k)</returns>
    public int[] GetTopKIndices(CsrTopology topology, int k)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        int nnz = topology.Nnz;
        if (k <= 0) return Array.Empty<int>();
        if (k >= nnz) return Enumerable.Range(0, nnz).ToArray();

        // Download weights (current fallback)
        double[] weights = topology.EdgeWeights.ToArray();

        // Create index array
        int[] idx = Enumerable.Range(0, nnz).ToArray();

        // Use QuickSelect to partition top-k to front (descending)
        QuickSelect(indices: idx, weights: weights, left: 0, right: nnz - 1, k);

        // Take first k indices
        int[] result = new int[k];
        Array.Copy(idx, 0, result, 0, k);
        return result;
    }

    /// <summary>
    /// Two-stage GPU-assisted Top-K: 1) per-block maxima via GPU; 2) CPU refine top-k from block maxima candidates.
    /// This reduces CPU work and memory for large nnz.
    /// </summary>
    public int[] GetTopKIndicesTwoStage(CsrTopology topology, int k)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        int nnz = topology.Nnz;
        if (k <= 0) return Array.Empty<int>();
        if (k >= nnz) return Enumerable.Range(0, nnz).ToArray();

        // Choose block size based on device/threadgroup; simple heuristic
        int blockSize = 1024; // tune as needed
        int numBlocks = (nnz + blockSize - 1) / blockSize;

        // Ensure buffers allocated for flags and temp
        EnsureBuffersAllocated(topology.NodeCount, nnz);

        // Allocate block maxima buffer
        var blockMaxBuffer = _device.AllocateReadWriteBuffer<int>(numBlocks);

        // Run TopBlockMaxKernel
        _device.For(numBlocks, new TopBlockMaxKernel(
            topology.EdgeWeightsBuffer,
            blockMaxBuffer,
            nnz,
            blockSize));

        // Copy block max indices to CPU
        int[] blockMax = new int[numBlocks];
        blockMaxBuffer.CopyTo(blockMax);

        // Release GPU temp buffer
        blockMaxBuffer.Dispose();

        // Collect candidate indices (filter out -1)
        var candidates = blockMax.Where(i => i >= 0).ToArray();
        if (candidates.Length == 0)
            return Array.Empty<int>();

        // Refine top-k among candidates using CPU QuickSelect on their weights
        double[] weights = topology.EdgeWeights.ToArray();
        int[] candIdx = candidates.ToArray();
        QuickSelect(indices: candIdx, weights: weights, left: 0, right: candIdx.Length - 1, k: System.Math.Min(k, candIdx.Length));
        int take = System.Math.Min(k, candIdx.Length);
        int[] result = new int[take];
        Array.Copy(candIdx, 0, result, 0, take);
        return result;
    }

    /// <summary>
    /// Two-stage GPU-assisted Top-K with local top-M per block.
    /// Stage1: per-block top-M via GPU shader TopBlockTopMKernel
    /// Stage2: CPU refine among M*numBlocks candidates using QuickSelect
    /// </summary>
    public int[] GetTopKIndicesBlockTopM(CsrTopology topology, int k, int M = 4)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        int nnz = topology.Nnz;
        if (k <= 0) return Array.Empty<int>();
        if (k >= nnz) return Enumerable.Range(0, nnz).ToArray();

        int blockSize = 1024;
        int numBlocks = (nnz + blockSize - 1) / blockSize;

        EnsureBuffersAllocated(topology.NodeCount, nnz);

        int totalSlots = numBlocks * M;
        var blockTopBuffer = _device.AllocateReadWriteBuffer<int>(totalSlots);

        _device.For(numBlocks, new TopBlockTopMKernel(
            topology.EdgeWeightsBuffer,
            blockTopBuffer,
            nnz,
            blockSize,
            M));

        int[] blockTop = new int[totalSlots];
        blockTopBuffer.CopyTo(blockTop);
        blockTopBuffer.Dispose();

        // Filter valid indices
        var candidates = blockTop.Where(i => i >= 0).Distinct().ToArray();
        if (candidates.Length == 0) return Array.Empty<int>();

        double[] weights = topology.EdgeWeights.ToArray();
        int[] candIdx = candidates.ToArray();
        int take = System.Math.Min(k, candIdx.Length);
        QuickSelect(indices: candIdx, weights: weights, left: 0, right: candIdx.Length - 1, k: take);

        int[] result = new int[take];
        Array.Copy(candIdx, 0, result, 0, take);
        return result;
    }

    /// <summary>
    /// Three-stage GPU-assisted parallel Top-K selection.
    /// Stage 1: Multiple threads per block compute local top-M in parallel
    /// Stage 2: CPU merge of partial top-M results per block  
    /// Stage 3: CPU QuickSelect on merged candidates to extract final top-K
    /// 
    /// This method provides better GPU utilization than single-thread-per-block approaches
    /// and supports M up to 8 per thread partition.
    /// </summary>
    /// <param name="topology">CSR topology (must be GPU-ready)</param>
    /// <param name="k">Number of heaviest edges to return</param>
    /// <param name="threadsPerBlock">Parallelism within each block (default 4)</param>
    /// <param name="M">Local top-M per thread partition (default 4, max 8)</param>
    /// <returns>Array of edge indices (length <= k)</returns>
    public int[] GetTopKIndicesParallel(CsrTopology topology, int k, int threadsPerBlock = 4, int M = 4)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        int nnz = topology.Nnz;
        if (k <= 0) return Array.Empty<int>();
        if (k >= nnz) return Enumerable.Range(0, nnz).ToArray();

        // Clamp M to supported range
        M = System.Math.Clamp(M, 1, 8);
        threadsPerBlock = System.Math.Max(1, threadsPerBlock);

        int blockSize = 1024;
        int numBlocks = (nnz + blockSize - 1) / blockSize;
        int totalThreads = numBlocks * threadsPerBlock;
        int totalSlots = totalThreads * M;

        EnsureBuffersAllocated(topology.NodeCount, nnz);

        // Allocate output buffer for parallel kernel
        var partialTopBuffer = _device.AllocateReadWriteBuffer<int>(totalSlots);

        // Stage 1: Run parallel top-M kernel
        _device.For(totalThreads, new ParallelBlockTopMKernel(
            topology.EdgeWeightsBuffer,
            partialTopBuffer,
            nnz,
            blockSize,
            threadsPerBlock,
            M));

        // Download partial results
        int[] partialTop = new int[totalSlots];
        partialTopBuffer.CopyTo(partialTop);
        partialTopBuffer.Dispose();

        // Stage 2: Collect valid candidates (filter -1 and deduplicate)
        var candidates = partialTop.Where(i => i >= 0).Distinct().ToArray();
        if (candidates.Length == 0) return Array.Empty<int>();
        if (candidates.Length <= k) return candidates;

        // Stage 3: Refine top-K among candidates using CPU QuickSelect
        double[] weights = topology.EdgeWeights.ToArray();
        int[] candIdx = candidates.ToArray();
        QuickSelect(indices: candIdx, weights: weights, left: 0, right: candIdx.Length - 1, k: k);

        int[] result = new int[k];
        Array.Copy(candIdx, 0, result, 0, k);
        return result;
    }

    /// <summary>
    /// Select top-K indices using the specified strategy.
    /// Provides a unified API for top-K selection with configurable strategy.
    /// Populates LastTopKMetrics with performance data.
    /// </summary>
    /// <param name="topology">CSR topology</param>
    /// <param name="k">Number of top elements</param>
    /// <param name="strategy">Selection strategy</param>
    /// <param name="M">Local top-M per partition (for GPU strategies)</param>
    /// <returns>Array of top-K indices by weight (descending)</returns>
    public int[] SelectTopK(CsrTopology topology, int k, TopKSelectionStrategy strategy = TopKSelectionStrategy.Auto, int M = 4)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        int nnz = topology.Nnz;
        
        // Initialize metrics
        var metrics = new TopKSelectionMetrics
        {
            RequestedK = k,
            SourceNnz = nnz
        };
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (k <= 0)
            {
                metrics.ResultCount = 0;
                metrics.UsedStrategy = TopKSelectionStrategy.CpuOnly;
                return Array.Empty<int>();
            }
            
            if (k >= nnz)
            {
                var allIndices = Enumerable.Range(0, nnz).ToArray();
                metrics.ResultCount = nnz;
                metrics.UsedStrategy = TopKSelectionStrategy.CpuOnly;
                return allIndices;
            }

            // Auto-select strategy based on nnz
            var originalStrategy = strategy;
            if (strategy == TopKSelectionStrategy.Auto)
            {
                // For small graphs, CPU is faster due to no GPU overhead
                // For medium graphs, single-thread block top-M is sufficient
                // For large graphs, parallel block top-M provides better utilization
                if (nnz < 10_000)
                    strategy = TopKSelectionStrategy.CpuOnly;
                else if (nnz < 100_000)
                    strategy = TopKSelectionStrategy.BlockTopM;
                else
                    strategy = TopKSelectionStrategy.ParallelBlockTopM;
            }
            
            metrics.UsedStrategy = strategy;

            int[] result = strategy switch
            {
                TopKSelectionStrategy.CpuOnly => GetTopKIndicesWithMetrics(topology, k, metrics),
                TopKSelectionStrategy.BlockTopM => GetTopKIndicesBlockTopMWithMetrics(topology, k, M, metrics),
                TopKSelectionStrategy.ParallelBlockTopM => GetTopKIndicesParallelWithMetrics(topology, k, 4, M, metrics),
                _ => GetTopKIndicesWithMetrics(topology, k, metrics)
            };

            metrics.ResultCount = result.Length;
            return result;
        }
        catch (Exception ex)
        {
            metrics.ErrorMessage = ex.Message;
            metrics.UsedFallback = true;
            
            // Fallback to CPU on any GPU error
            System.Diagnostics.Debug.WriteLine($"[GpuStreamCompaction] SelectTopK failed with {strategy}: {ex.Message}. Falling back to CPU.");
            
            var cpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var fallbackResult = GetTopKIndices(topology, k);
            cpuStopwatch.Stop();
            
            metrics.CpuRefineTimeMs = cpuStopwatch.Elapsed.TotalMilliseconds;
            metrics.ResultCount = fallbackResult.Length;
            metrics.UsedStrategy = TopKSelectionStrategy.CpuOnly;
            
            return fallbackResult;
        }
        finally
        {
            totalStopwatch.Stop();
            metrics.TotalTimeMs = totalStopwatch.Elapsed.TotalMilliseconds;
            LastTopKMetrics = metrics;
            
            // Log metrics
            System.Diagnostics.Debug.WriteLine(
                $"[TopK] Strategy={metrics.UsedStrategy}, k={k}, nnz={nnz}, " +
                $"candidates={metrics.GpuCandidateCount}, result={metrics.ResultCount}, " +
                $"gpu={metrics.GpuTimeMs:F2}ms, cpu={metrics.CpuRefineTimeMs:F2}ms, total={metrics.TotalTimeMs:F2}ms" +
                (metrics.UsedFallback ? " [FALLBACK]" : ""));
        }
    }

    private int[] GetTopKIndicesWithMetrics(CsrTopology topology, int k, TopKSelectionMetrics metrics)
    {
        var cpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = GetTopKIndices(topology, k);
        cpuStopwatch.Stop();
        
        metrics.CpuRefineTimeMs = cpuStopwatch.Elapsed.TotalMilliseconds;
        metrics.GpuCandidateCount = 0; // Pure CPU, no GPU candidates
        return result;
    }

    private int[] GetTopKIndicesBlockTopMWithMetrics(CsrTopology topology, int k, int M, TopKSelectionMetrics metrics)
    {
        int nnz = topology.Nnz;
        int blockSize = 1024;
        int numBlocks = (nnz + blockSize - 1) / blockSize;

        EnsureBuffersAllocated(topology.NodeCount, nnz);

        int totalSlots = numBlocks * M;
        var blockTopBuffer = _device.AllocateReadWriteBuffer<int>(totalSlots);

        var gpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _device.For(numBlocks, new TopBlockTopMKernel(
            topology.EdgeWeightsBuffer,
            blockTopBuffer,
            nnz,
            blockSize,
            M));

        int[] blockTop = new int[totalSlots];
        blockTopBuffer.CopyTo(blockTop);
        gpuStopwatch.Stop();
        
        blockTopBuffer.Dispose();
        metrics.GpuTimeMs = gpuStopwatch.Elapsed.TotalMilliseconds;

        // Filter valid indices
        var candidates = blockTop.Where(i => i >= 0).Distinct().ToArray();
        metrics.GpuCandidateCount = candidates.Length;
        
        if (candidates.Length == 0) return Array.Empty<int>();

        var cpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        double[] weights = topology.EdgeWeights.ToArray();
        int[] candIdx = candidates.ToArray();
        int take = System.Math.Min(k, candIdx.Length);
        QuickSelect(indices: candIdx, weights: weights, left: 0, right: candIdx.Length - 1, k: take);
        cpuStopwatch.Stop();
        
        metrics.CpuRefineTimeMs = cpuStopwatch.Elapsed.TotalMilliseconds;

        int[] result = new int[take];
        Array.Copy(candIdx, 0, result, 0, take);
        return result;
    }

    private int[] GetTopKIndicesParallelWithMetrics(CsrTopology topology, int k, int threadsPerBlock, int M, TopKSelectionMetrics metrics)
    {
        int nnz = topology.Nnz;
        M = System.Math.Clamp(M, 1, 8);
        threadsPerBlock = System.Math.Max(1, threadsPerBlock);

        int blockSize = 1024;
        int numBlocks = (nnz + blockSize - 1) / blockSize;
        int totalThreads = numBlocks * threadsPerBlock;
        int totalSlots = totalThreads * M;

        EnsureBuffersAllocated(topology.NodeCount, nnz);

        var partialTopBuffer = _device.AllocateReadWriteBuffer<int>(totalSlots);

        var gpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _device.For(totalThreads, new ParallelBlockTopMKernel(
            topology.EdgeWeightsBuffer,
            partialTopBuffer,
            nnz,
            blockSize,
            threadsPerBlock,
            M));

        int[] partialTop = new int[totalSlots];
        partialTopBuffer.CopyTo(partialTop);
        gpuStopwatch.Stop();
        
        partialTopBuffer.Dispose();
        metrics.GpuTimeMs = gpuStopwatch.Elapsed.TotalMilliseconds;

        // Collect valid candidates
        var candidates = partialTop.Where(i => i >= 0).Distinct().ToArray();
        metrics.GpuCandidateCount = candidates.Length;
        
        if (candidates.Length == 0) return Array.Empty<int>();
        if (candidates.Length <= k) return candidates;

        var cpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        double[] weights = topology.EdgeWeights.ToArray();
        int[] candIdx = candidates.ToArray();
        QuickSelect(indices: candIdx, weights: weights, left: 0, right: candIdx.Length - 1, k: k);
        cpuStopwatch.Stop();
        
        metrics.CpuRefineTimeMs = cpuStopwatch.Elapsed.TotalMilliseconds;

        int[] result = new int[k];
        Array.Copy(candIdx, 0, result, 0, k);
        return result;
    }

    /// <summary>
    /// QuickSelect: partitions indices array so that first k elements are the k largest by weights
    /// </summary>
    /// <param name="indices">The indices array to partition</param>
    /// <param name="weights">The weights array used for comparison</param>
    /// <param name="left">Left bound of the partitioning range</param>
    /// <param name="right">Right bound of the partitioning range</param>
    /// <param name="k">The number of largest elements to keep</param>
    private static void QuickSelect(int[] indices, double[] weights, int left, int right, int k)
    {
        var rand = new Random(123456);
        while (left <= right)
        {
            int pivotIndex = rand.Next(left, right + 1);
            int pivotNewIndex = Partition(indices, weights, left, right, pivotIndex);
            int leftCount = pivotNewIndex - left + 1; // number of elements >= pivot in [left..pivotNewIndex]

            if (leftCount == k)
                return;
            else if (k < leftCount)
                right = pivotNewIndex - 1;
            else
            {
                k -= leftCount;
                left = pivotNewIndex + 1;
            }
        }
    }

    /// <summary>
    /// Partition so that elements with weight >= pivot are placed to the left.
    /// </summary>
    /// <param name="indices">The indices array to partition</param>
    /// <param name="weights">The weights array used for comparison</param>
    /// <param name="left">Left bound of the partitioning range</param>
    /// <param name="right">Right bound of the partitioning range</param>
    /// <param name="pivotIndex">The index of the pivot element</param>
    /// <returns>The new index of the pivot element after partitioning</returns>
    private static int Partition(int[] indices, double[] weights, int left, int right, int pivotIndex)
    {
        double pivotValue = weights[indices[pivotIndex]];
        // Move pivot to end
        Swap(indices, pivotIndex, right);
        int storeIndex = left;

        for (int i = left; i < right; i++)
        {
            if (weights[indices[i]] >= pivotValue)
            {
                Swap(indices, storeIndex, i);
                storeIndex++;
            }
        }

        // Move pivot to its final place
        Swap(indices, storeIndex, right);
        return storeIndex;
    }

    private static void Swap(int[] a, int i, int j)
    {
        if (i == j) return;
        int tmp = a[i];
        a[i] = a[j];
        a[j] = tmp;
    }

    private void EnsureBuffersAllocated(int nodeCount, int nnz)
    {
        if (_allocatedNodeCount < nodeCount || _allocatedNnz < nnz)
        {
            DisposeBuffers();

            _flagsBuffer = _device.AllocateReadWriteBuffer<int>(nnz);
            _scatterIndicesBuffer = _device.AllocateReadWriteBuffer<int>(NextPowerOf2(nnz));
            _newDegreesBuffer = _device.AllocateReadWriteBuffer<int>(nodeCount);
            _newRowOffsetsBuffer = _device.AllocateReadWriteBuffer<int>(nodeCount + 1);
            _writeCountersBuffer = _device.AllocateReadWriteBuffer<int>(nodeCount);

            _allocatedNodeCount = nodeCount;
            _allocatedNnz = nnz;
        }
    }

    private void EnsureNewBuffersAllocated(int newNnz)
    {
        if (_newColIndicesBuffer is null || _newColIndicesBuffer.Length < newNnz)
        {
            _newColIndicesBuffer?.Dispose();
            _newWeightsBuffer?.Dispose();

            _newColIndicesBuffer = _device.AllocateReadWriteBuffer<int>(newNnz);
            _newWeightsBuffer = _device.AllocateReadWriteBuffer<double>(newNnz);
        }
    }

    private static int NextPowerOf2(int n)
    {
        int p = 1;
        while (p < n) p *= 2;
        return p;
    }

    private void DisposeBuffers()
    {
        _flagsBuffer?.Dispose();
        _scatterIndicesBuffer?.Dispose();
        _newDegreesBuffer?.Dispose();
        _newRowOffsetsBuffer?.Dispose();
        _newColIndicesBuffer?.Dispose();
        _newWeightsBuffer?.Dispose();
        _writeCountersBuffer?.Dispose();
        _scanTempBuffer?.Dispose();
        _blockSumsBuffer?.Dispose();

        _flagsBuffer = null;
        _scatterIndicesBuffer = null;
        _newDegreesBuffer = null;
        _newRowOffsetsBuffer = null;
        _newColIndicesBuffer = null;
        _newWeightsBuffer = null;
        _writeCountersBuffer = null;
        _scanTempBuffer = null;
        _blockSumsBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeBuffers();
        _disposed = true;
    }
}

/// <summary>
/// Selection strategy for top-K GPU operations.
/// </summary>
public enum TopKSelectionStrategy
{
    /// <summary>CPU-only QuickSelect fallback.</summary>
    CpuOnly,
    /// <summary>Single-thread per block top-M (existing GetTopKIndicesBlockTopM).</summary>
    BlockTopM,
    /// <summary>Parallel threads per block (new GetTopKIndicesParallel).</summary>
    ParallelBlockTopM,
    /// <summary>Auto-select best strategy based on nnz.</summary>
    Auto
}

/// <summary>
/// Metrics from a single top-K selection operation.
/// </summary>
public sealed class TopKSelectionMetrics
{
    /// <summary>Strategy that was actually used.</summary>
    public TopKSelectionStrategy UsedStrategy { get; set; }

    /// <summary>Total time for top-K selection (ms).</summary>
    public double TotalTimeMs { get; set; }

    /// <summary>Time spent in GPU kernel dispatch (ms).</summary>
    public double GpuTimeMs { get; set; }

    /// <summary>Time spent in CPU refinement (ms).</summary>
    public double CpuRefineTimeMs { get; set; }

    /// <summary>Number of candidates produced by GPU stage.</summary>
    public int GpuCandidateCount { get; set; }

    /// <summary>Final result count.</summary>
    public int ResultCount { get; set; }

    /// <summary>Source nnz (number of edges).</summary>
    public int SourceNnz { get; set; }

    /// <summary>Requested k value.</summary>
    public int RequestedK { get; set; }

    /// <summary>Whether fallback to CPU was used.</summary>
    public bool UsedFallback { get; set; }

    /// <summary>Error message if any.</summary>
    public string? ErrorMessage { get; set; }
}
