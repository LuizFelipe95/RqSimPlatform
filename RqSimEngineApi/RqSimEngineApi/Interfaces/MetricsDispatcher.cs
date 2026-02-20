using System;
using System.Collections.Generic;
using System.Threading;

namespace RqSimUI.FormSimAPI.Interfaces;

/// <summary>
/// Thread-safe dispatcher for simulation metrics with double-buffering.
/// 
/// Architecture:
/// - Calculation thread writes to "write buffer" with minimal lock
/// - UI thread reads from "read buffer" via TryGetDisplayData (non-blocking)
/// - Automatic decimation for large datasets to prevent UI stalls
/// 
/// Thread priorities:
/// - Calculation thread: AboveNormal (never blocked by UI)
/// - Dispatcher/UI: Normal (responsive but yields to calculation)
/// 
/// UI Pattern:
/// - Call TryGetDisplayData() up to 10x/sec
/// - Returns null if dispatcher busy (calculation thread writing)
/// - UI skips frame and retries after 50ms
/// </summary>
public sealed class MetricsDispatcher : IDisposable
{
    // === Configuration ===
    private const int MaxPointsForFullResolution = 2000;
    private const int DecimatedTargetPoints = 500;
    private const int LiveTailPoints = 100; // Always keep last N at full resolution

    // === Double Buffers (swap atomically) ===
    private DecimatedTimeSeries _readBuffer;
    private DecimatedTimeSeries _writeBuffer;
    private readonly object _writeLock = new();
    private volatile int _swapPending;
    private volatile int _isSwapping; // 1 = swap in progress, UI should skip

    // === Live Metrics (volatile for lock-free read) ===
    public volatile int LiveStep;
    public volatile int LiveExcited;
    public volatile int LiveLargestCluster;
    public volatile int LiveStrongEdges;
    public volatile int LiveTotalSteps;

    // Doubles need Interlocked or volatile wrapper
    private double _liveHeavyMass;
    private double _liveQNorm;
    private double _liveEntanglement;
    private double _liveCorrelation;
    private double _liveSpectralDim; // 0 = not yet computed
    private double _liveTemp;
    private double _liveEffectiveG;
    private double _liveAdaptiveThreshold;
    private double _liveSystemEnergy;

    // Cluster metrics
    public volatile int LiveHeavyClusterCount;
    public volatile int LiveTotalClusters;
    private double _liveAvgClusterMass;
    private double _liveMaxClusterMass;
    private double _liveAvgDegree;

    // Extended graph topology metrics (new)
    public volatile int LiveEdgeCount;
    public volatile int LiveComponentCount;
    public volatile int LiveNodeCount;
    private double _liveGraphDensity;
    private double _liveClusterRatio;  // LargestCluster / N
    private double _liveExcitedRatio;  // Excited / N
    private double _liveStrongEdgeRatio; // StrongEdges / TotalEdges

    public double LiveHeavyMass => Volatile.Read(ref _liveHeavyMass);
    public double LiveQNorm => Volatile.Read(ref _liveQNorm);
    public double LiveEntanglement => Volatile.Read(ref _liveEntanglement);
    public double LiveCorrelation => Volatile.Read(ref _liveCorrelation);
    public double LiveSpectralDim => Volatile.Read(ref _liveSpectralDim);
    public double LiveTemp => Volatile.Read(ref _liveTemp);
    public double LiveEffectiveG => Volatile.Read(ref _liveEffectiveG);
    public double LiveAdaptiveThreshold => Volatile.Read(ref _liveAdaptiveThreshold);
    public double LiveSystemEnergy => Volatile.Read(ref _liveSystemEnergy);
    public double LiveAvgClusterMass => Volatile.Read(ref _liveAvgClusterMass);
    public double LiveMaxClusterMass => Volatile.Read(ref _liveMaxClusterMass);
    public double LiveAvgDegree => Volatile.Read(ref _liveAvgDegree);
    public double LiveGraphDensity => Volatile.Read(ref _liveGraphDensity);
    public double LiveClusterRatio => Volatile.Read(ref _liveClusterRatio);
    public double LiveExcitedRatio => Volatile.Read(ref _liveExcitedRatio);
    public double LiveStrongEdgeRatio => Volatile.Read(ref _liveStrongEdgeRatio);

    /// <summary>
    /// Returns true if new data is available for UI to fetch
    /// </summary>
    public bool HasPendingData => Volatile.Read(ref _swapPending) == 1;

    public MetricsDispatcher()
    {
        _readBuffer = new DecimatedTimeSeries();
        _writeBuffer = new DecimatedTimeSeries();
    }

    /// <summary>
    /// Called by calculation thread to update live metrics (lock-free writes)
    /// </summary>
    public void UpdateLiveMetrics(
        int step, int excited, double heavyMass, int largestCluster,
        int strongEdges, double qNorm, double entanglement, double correlation,
        double spectralDim, double temp, double effectiveG, int totalSteps,
        double adaptiveThreshold = 0.0,
        int heavyClusterCount = 0, int totalClusters = 0, double avgClusterMass = 0.0,
        double maxClusterMass = 0.0, double avgDegree = 0.0,
        int edgeCount = 0, int componentCount = 1, int nodeCount = 0)
    {
        LiveStep = step;
        LiveExcited = excited;
        LiveLargestCluster = largestCluster;
        LiveStrongEdges = strongEdges;
        // Only update LiveTotalSteps if positive (preserves value set by event-based loop)
        if (totalSteps > 0) LiveTotalSteps = totalSteps;
        LiveHeavyClusterCount = heavyClusterCount;
        LiveTotalClusters = totalClusters;
        LiveEdgeCount = edgeCount;
        LiveComponentCount = componentCount;
        LiveNodeCount = nodeCount;

        Volatile.Write(ref _liveHeavyMass, heavyMass);
        Volatile.Write(ref _liveQNorm, qNorm);
        Volatile.Write(ref _liveEntanglement, entanglement);
        Volatile.Write(ref _liveCorrelation, correlation);
        Volatile.Write(ref _liveSpectralDim, spectralDim);
        Volatile.Write(ref _liveTemp, temp);
        Volatile.Write(ref _liveEffectiveG, effectiveG);
        Volatile.Write(ref _liveAdaptiveThreshold, adaptiveThreshold);
        Volatile.Write(ref _liveAvgClusterMass, avgClusterMass);
        Volatile.Write(ref _liveMaxClusterMass, maxClusterMass);
        Volatile.Write(ref _liveAvgDegree, avgDegree);

        // Compute derived ratios
        if (nodeCount > 0)
        {
            Volatile.Write(ref _liveClusterRatio, (double)largestCluster / nodeCount);
            Volatile.Write(ref _liveExcitedRatio, (double)excited / nodeCount);
        }
        if (edgeCount > 0)
        {
            Volatile.Write(ref _liveStrongEdgeRatio, (double)strongEdges / edgeCount);
        }
        if (nodeCount > 1)
        {
            int maxEdges = nodeCount * (nodeCount - 1) / 2;
            Volatile.Write(ref _liveGraphDensity, maxEdges > 0 ? (double)edgeCount / maxEdges : 0.0);
        }
    }

    /// <summary>
    /// Sets system energy (sum of edge weights) — used by console polling.
    /// </summary>
    public void SetLiveSystemEnergy(double energy)
    {
        Volatile.Write(ref _liveSystemEnergy, energy);
    }

    /// <summary>
    /// Called by calculation thread to append time series data.
    /// Uses minimal lock - only blocks if UI is actively reading.
    /// </summary>
    public void AppendTimeSeriesPoint(
        int step, int excited, double heavyMass, int heavyCount,
        int largestCluster, double energy, int strongEdges, double correlation,
        double qNorm, double entanglement, double spectralDim,
        double temp, double effectiveG, double threshold)
    {
        // Quick lock for write buffer only
        lock (_writeLock)
        {
            _writeBuffer.Append(step, excited, heavyMass, heavyCount, largestCluster,
                energy, strongEdges, correlation, qNorm, entanglement,
                spectralDim, temp, effectiveG, threshold);

            // Signal that new data is available
            Interlocked.Exchange(ref _swapPending, 1);
        }
    }

    /// <summary>
    /// Non-blocking attempt to get display data. Returns null if dispatcher is busy.
    /// UI should skip rendering and retry after ~50ms delay.
    /// </summary>
    /// <returns>Decimated data for charts, or null if busy</returns>
    public DecimatedTimeSeries? TryGetDisplayData()
    {
        // FIX: Always try to swap if write buffer has data but read buffer is empty
        // This handles the case where UI starts reading before first swap completes
        bool forceSwap = _readBuffer.TotalCount == 0 && _writeBuffer.Steps.Count > 0;

        // Fast path: no new data AND read buffer has data, return cached read buffer
        if (Volatile.Read(ref _swapPending) == 0 && !forceSwap)
        {
            return _readBuffer;
        }

        // Try to acquire swap lock without blocking
        if (Interlocked.CompareExchange(ref _isSwapping, 1, 0) != 0)
        {
            // Another swap in progress, return existing read buffer (may be empty initially)
            return _readBuffer;
        }

        try
        {
            // Try to acquire write lock without blocking
            if (!Monitor.TryEnter(_writeLock))
            {
                // Write buffer is being modified, return existing read buffer
                return _readBuffer;
            }

            try
            {
                // Clear pending flag
                Interlocked.Exchange(ref _swapPending, 0);

                // Copy write buffer to read buffer with decimation
                _readBuffer.CopyFromWithDecimation(_writeBuffer,
                    MaxPointsForFullResolution, DecimatedTargetPoints, LiveTailPoints);

                return _readBuffer;
            }
            finally
            {
                Monitor.Exit(_writeLock);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isSwapping, 0);
        }
    }

    /// <summary>
    /// Blocking version - waits for data. Use sparingly (e.g., final frame).
    /// </summary>
    public DecimatedTimeSeries GetDisplayData()
    {
        // Check if swap needed (lock-free check)
        if (Interlocked.CompareExchange(ref _swapPending, 0, 1) == 1)
        {
            // Swap buffers atomically
            lock (_writeLock)
            {
                // Copy write buffer to read buffer with decimation
                _readBuffer.CopyFromWithDecimation(_writeBuffer,
                    MaxPointsForFullResolution, DecimatedTargetPoints, LiveTailPoints);
            }
        }

        return _readBuffer;
    }

    /// <summary>
    /// Force immediate data retrieval, blocking calculation thread if necessary.
    /// Use ONLY for manual user-triggered refresh (button click).
    /// This temporarily pauses calculation to ensure consistent snapshot.
    /// </summary>
    /// <param name="timeoutMs">Maximum time to wait for lock (default 500ms)</param>
    /// <returns>Display data, never null</returns>
    public DecimatedTimeSeries ForceGetDisplayDataImmediate(int timeoutMs = 500)
    {
        // Always force a fresh copy, even if no pending data
        bool lockAcquired = false;
        try
        {
            // Try to acquire write lock with timeout
            lockAcquired = Monitor.TryEnter(_writeLock, timeoutMs);

            if (lockAcquired)
            {
                // Clear pending flag
                Interlocked.Exchange(ref _swapPending, 0);

                // Force copy even if no new data (user wants current state)
                _readBuffer.CopyFromWithDecimation(_writeBuffer,
                    MaxPointsForFullResolution, DecimatedTargetPoints, LiveTailPoints);
            }
            // If lock not acquired within timeout, return cached data
        }
        finally
        {
            if (lockAcquired)
                Monitor.Exit(_writeLock);
        }

        return _readBuffer;
    }

    /// <summary>
    /// Clears all data. Called at simulation start.
    /// </summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            _writeBuffer.Clear();
            _readBuffer.Clear();
            Interlocked.Exchange(ref _swapPending, 0);
        }

        LiveStep = 0;
        LiveExcited = 0;
        LiveLargestCluster = 0;
        LiveStrongEdges = 0;
        LiveTotalSteps = 0;
        LiveHeavyClusterCount = 0;
        LiveTotalClusters = 0;
        LiveEdgeCount = 0;
        LiveComponentCount = 1;
        Volatile.Write(ref _liveHeavyMass, 0.0);
        Volatile.Write(ref _liveQNorm, 0.0);
        Volatile.Write(ref _liveEntanglement, 0.0);
        Volatile.Write(ref _liveCorrelation, 0.0);
        Volatile.Write(ref _liveSpectralDim, 0.0);
        Volatile.Write(ref _liveTemp, 0.0);
        Volatile.Write(ref _liveEffectiveG, 0.0);
        Volatile.Write(ref _liveAdaptiveThreshold, 0.0);
        Volatile.Write(ref _liveAvgClusterMass, 0.0);
        Volatile.Write(ref _liveMaxClusterMass, 0.0);
        Volatile.Write(ref _liveAvgDegree, 0.0);
        Volatile.Write(ref _liveGraphDensity, 0.0);
        Volatile.Write(ref _liveClusterRatio, 0.0);
        Volatile.Write(ref _liveExcitedRatio, 0.0);
        Volatile.Write(ref _liveStrongEdgeRatio, 0.0);
    }

    /// <summary>
    /// Gets full resolution data for export. May block briefly.
    /// </summary>
    public DecimatedTimeSeries GetFullDataForExport()
    {
        lock (_writeLock)
        {
            return _writeBuffer.Clone();
        }
    }

    public void Dispose()
    {
        // Nothing to dispose, but implement for future extensibility
    }
}

/// <summary>
/// Time series data with built-in decimation support for UI performance.
/// </summary>
public sealed class DecimatedTimeSeries
{
    // Raw data storage
    public List<int> Steps { get; } = [];
    public List<int> Excited { get; } = [];
    public List<double> HeavyMass { get; } = [];
    public List<int> HeavyCount { get; } = [];
    public List<int> LargestCluster { get; } = [];
    public List<double> Energy { get; } = [];
    public List<int> StrongEdges { get; } = [];
    public List<double> Correlation { get; } = [];
    public List<double> QNorm { get; } = [];
    public List<double> Entanglement { get; } = [];
    public List<double> SpectralDimension { get; } = [];
    public List<double> NetworkTemperature { get; } = [];
    public List<double> EffectiveG { get; } = [];
    public List<double> AdaptiveThreshold { get; } = [];

    // Decimated views for fast chart rendering
    public int[] DecimatedSteps { get; private set; } = [];
    public int[] DecimatedExcited { get; private set; } = [];
    public double[] DecimatedHeavyMass { get; private set; } = [];
    public int[] DecimatedLargestCluster { get; private set; } = [];
    public double[] DecimatedEnergy { get; private set; } = [];
    public double[] DecimatedNetworkTemp { get; private set; } = [];

    public int TotalCount => Steps.Count;
    public int DecimatedCount => DecimatedSteps.Length;

    public void Append(
        int step, int excited, double heavyMass, int heavyCount,
        int largestCluster, double energy, int strongEdges, double correlation,
        double qNorm, double entanglement, double spectralDim,
        double temp, double effectiveG, double threshold)
    {
        Steps.Add(step);
        Excited.Add(excited);
        HeavyMass.Add(heavyMass);
        HeavyCount.Add(heavyCount);
        LargestCluster.Add(largestCluster);
        Energy.Add(energy);
        StrongEdges.Add(strongEdges);
        Correlation.Add(correlation);
        QNorm.Add(qNorm);
        Entanglement.Add(entanglement);
        SpectralDimension.Add(spectralDim);
        NetworkTemperature.Add(temp);
        EffectiveG.Add(effectiveG);
        AdaptiveThreshold.Add(threshold);
    }

    public void Clear()
    {
        Steps.Clear();
        Excited.Clear();
        HeavyMass.Clear();
        HeavyCount.Clear();
        LargestCluster.Clear();
        Energy.Clear();
        StrongEdges.Clear();
        Correlation.Clear();
        QNorm.Clear();
        Entanglement.Clear();
        SpectralDimension.Clear();
        NetworkTemperature.Clear();
        EffectiveG.Clear();
        AdaptiveThreshold.Clear();

        DecimatedSteps = [];
        DecimatedExcited = [];
        DecimatedHeavyMass = [];
        DecimatedLargestCluster = [];
        DecimatedEnergy = [];
        DecimatedNetworkTemp = [];
    }

    /// <summary>
    /// Copies data from source with intelligent decimation for UI rendering.
    /// Preserves: first N points, last M points (tail), samples in between.
    /// </summary>
    public void CopyFromWithDecimation(DecimatedTimeSeries source,
        int maxFullRes, int targetDecimated, int tailPoints)
    {
        int count = source.Steps.Count;

        // Clear current data
        Clear();

        // Copy full data
        Steps.AddRange(source.Steps);
        Excited.AddRange(source.Excited);
        HeavyMass.AddRange(source.HeavyMass);
        HeavyCount.AddRange(source.HeavyCount);
        LargestCluster.AddRange(source.LargestCluster);
        Energy.AddRange(source.Energy);
        StrongEdges.AddRange(source.StrongEdges);
        Correlation.AddRange(source.Correlation);
        QNorm.AddRange(source.QNorm);
        Entanglement.AddRange(source.Entanglement);
        SpectralDimension.AddRange(source.SpectralDimension);
        NetworkTemperature.AddRange(source.NetworkTemperature);
        EffectiveG.AddRange(source.EffectiveG);
        AdaptiveThreshold.AddRange(source.AdaptiveThreshold);

        // Create decimated views
        if (count <= maxFullRes)
        {
            // No decimation needed
            DecimatedSteps = [.. Steps];
            DecimatedExcited = [.. Excited];
            DecimatedHeavyMass = [.. HeavyMass];
            DecimatedLargestCluster = [.. LargestCluster];
            DecimatedEnergy = [.. Energy];
            DecimatedNetworkTemp = [.. NetworkTemperature];
        }
        else
        {
            // Need decimation
            CreateDecimatedViews(count, targetDecimated, tailPoints);
        }
    }

    private void CreateDecimatedViews(int count, int targetPoints, int tailPoints)
    {
        // Strategy: keep first 10%, sample middle 80%, keep last 10% at full res
        int headPoints = Math.Min(tailPoints, count / 10);
        int actualTail = Math.Min(tailPoints, count / 10);
        int middleCount = count - headPoints - actualTail;
        int middleSamples = targetPoints - headPoints - actualTail;

        if (middleSamples <= 0 || middleCount <= 0)
        {
            // Just take evenly spaced samples
            int skip = Math.Max(1, count / targetPoints);
            var indices = new List<int>();
            for (int i = 0; i < count; i += skip)
                indices.Add(i);
            // Always include last point
            if (indices.Count == 0 || indices[^1] != count - 1)
                indices.Add(count - 1);

            BuildDecimatedArrays(indices);
            return;
        }

        // Build index list
        var selectedIndices = new List<int>(targetPoints);

        // Head (first N points)
        for (int i = 0; i < headPoints; i++)
            selectedIndices.Add(i);

        // Middle (sampled)
        int middleStart = headPoints;
        int middleEnd = count - actualTail;
        double middleStep = (double)middleCount / middleSamples;
        for (int i = 0; i < middleSamples; i++)
        {
            int idx = middleStart + (int)(i * middleStep);
            if (idx < middleEnd && (selectedIndices.Count == 0 || selectedIndices[^1] != idx))
                selectedIndices.Add(idx);
        }

        // Tail (last N points at full resolution)
        for (int i = count - actualTail; i < count; i++)
        {
            if (selectedIndices.Count == 0 || selectedIndices[^1] != i)
                selectedIndices.Add(i);
        }

        BuildDecimatedArrays(selectedIndices);
    }

    private void BuildDecimatedArrays(List<int> indices)
    {
        int n = indices.Count;
        DecimatedSteps = new int[n];
        DecimatedExcited = new int[n];
        DecimatedHeavyMass = new double[n];
        DecimatedLargestCluster = new int[n];
        DecimatedEnergy = new double[n];
        DecimatedNetworkTemp = new double[n];

        for (int i = 0; i < n; i++)
        {
            int idx = indices[i];
            DecimatedSteps[i] = Steps[idx];
            DecimatedExcited[i] = Excited[idx];
            DecimatedHeavyMass[i] = HeavyMass[idx];
            DecimatedLargestCluster[i] = LargestCluster[idx];
            DecimatedEnergy[i] = Energy[idx];
            DecimatedNetworkTemp[i] = NetworkTemperature[idx];
        }
    }

    public DecimatedTimeSeries Clone()
    {
        var clone = new DecimatedTimeSeries();
        clone.Steps.AddRange(Steps);
        clone.Excited.AddRange(Excited);
        clone.HeavyMass.AddRange(HeavyMass);
        clone.HeavyCount.AddRange(HeavyCount);
        clone.LargestCluster.AddRange(LargestCluster);
        clone.Energy.AddRange(Energy);
        clone.StrongEdges.AddRange(StrongEdges);
        clone.Correlation.AddRange(Correlation);
        clone.QNorm.AddRange(QNorm);
        clone.Entanglement.AddRange(Entanglement);
        clone.SpectralDimension.AddRange(SpectralDimension);
        clone.NetworkTemperature.AddRange(NetworkTemperature);
        clone.EffectiveG.AddRange(EffectiveG);
        clone.AdaptiveThreshold.AddRange(AdaptiveThreshold);
        return clone;
    }
}
