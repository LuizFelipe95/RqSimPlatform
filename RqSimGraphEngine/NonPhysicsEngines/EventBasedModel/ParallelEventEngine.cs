using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RQSimulation.EventBasedModel;

/// <summary>
/// RQ-HYPOTHESIS COMPLIANT: Parallel Event-Based Simulation Engine.
/// 
/// Key insight: In relational quantum mechanics, there is no global "now".
/// Nodes with non-overlapping causal cones can evolve in parallel without
/// violating causality or physics.
/// 
/// Parallelization strategy:
/// 1. Graph coloring: Partition nodes into independent sets (no edges between nodes in same set)
/// 2. Process each color in parallel (all nodes of same color are causally independent)
/// 3. Synchronize between colors (barrier)
/// 
/// Thread pool strategy:
/// - Fixed number of worker threads (Environment.ProcessorCount - 1)
/// - Work-stealing queues to balance load
/// - No thread creation overhead per batch
/// - Minimal context switching via batched work items
/// </summary>
public sealed class ParallelEventEngine : IDisposable
{
    private readonly RQGraph _graph;
    private readonly int _workerCount;
    private readonly Thread[] _workers;
    private readonly BlockingCollection<WorkItem>[] _workQueues;
    private readonly ManualResetEventSlim _shutdownEvent;
    private readonly CountdownEvent _batchComplete;
    private readonly Random _rng;
    
    // Node coloring for parallel execution (computed once, updated on topology change)
    private int[]? _nodeColors;
    private int _colorCount;
    private List<int>[]? _colorGroups;
    private int[][]? _colorArrays;  // Pre-allocated node arrays per color (avoids ToArray allocation)
    private int _lastTopologyVersion;
    
    // Statistics
    private long _totalEventsProcessed;
    private long _totalBatchesProcessed;
    private long _parallelUpdates;
    private long _sequentialUpdates;

    /// <summary>
    /// Work item for thread pool - minimal allocation
    /// </summary>
    private readonly struct WorkItem
    {
        public readonly int[] NodeIds;
        public readonly int StartIndex;
        public readonly int Count;
        public readonly double Dt;
        public readonly Action<int, double>? CustomAction;

        public WorkItem(int[] nodeIds, int start, int count, double dt, Action<int, double>? action = null)
        {
            NodeIds = nodeIds;
            StartIndex = start;
            Count = count;
            Dt = dt;
            CustomAction = action;
        }

        public static WorkItem Shutdown => new([], 0, -1, 0);
        public bool IsShutdown => Count < 0;
    }

    public ParallelEventEngine(RQGraph graph, int? workerCount = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _workerCount = workerCount ?? Math.Max(1, Environment.ProcessorCount - 1);
        _workers = new Thread[_workerCount];
        _workQueues = new BlockingCollection<WorkItem>[_workerCount];
        _shutdownEvent = new ManualResetEventSlim(false);
        _batchComplete = new CountdownEvent(1);
        _rng = new Random(42);
        _lastTopologyVersion = -1;

        // Initialize work queues
        for (int i = 0; i < _workerCount; i++)
        {
            _workQueues[i] = new BlockingCollection<WorkItem>(new ConcurrentQueue<WorkItem>(), boundedCapacity: 64);
        }

        // Start worker threads (once, reused for entire simulation)
        for (int i = 0; i < _workerCount; i++)
        {
            int workerId = i;
            _workers[i] = new Thread(() => WorkerLoop(workerId))
            {
                Name = $"RQ-ParallelEvent-{i}",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workers[i].Start();
        }
    }

    /// <summary>
    /// Worker thread loop - processes work items until shutdown
    /// Uses work-stealing to balance load between threads
    /// </summary>
    private void WorkerLoop(int workerId)
    {
        var myQueue = _workQueues[workerId];

        while (!_shutdownEvent.IsSet)
        {
            WorkItem item;

            // Try to get work from own queue first
            if (myQueue.TryTake(out item, millisecondsTimeout: 1))
            {
                if (item.IsShutdown)
                    break;

                ProcessWorkItem(item);
                continue;
            }

            // Work stealing: try other queues
            for (int i = 0; i < _workerCount; i++)
            {
                if (i == workerId) continue;

                if (_workQueues[i].TryTake(out item))
                {
                    if (item.IsShutdown)
                    {
                        // Put shutdown back for the owner
                        _workQueues[i].TryAdd(item);
                        continue;
                    }

                    ProcessWorkItem(item);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Process a single work item (batch of nodes)
    /// </summary>
    private void ProcessWorkItem(WorkItem item)
    {
        try
        {
            for (int i = 0; i < item.Count; i++)
            {
                int nodeId = item.NodeIds[item.StartIndex + i];

                if (item.CustomAction != null)
                {
                    item.CustomAction(nodeId, item.Dt);
                }
                else
                {
                    // Default: update node physics
                    _graph.UpdateNodePhysics(nodeId, item.Dt);
                }

                Interlocked.Increment(ref _totalEventsProcessed);
            }
        }
        finally
        {
            _batchComplete.Signal();
        }
    }

    /// <summary>
    /// Compute graph coloring for parallel execution.
    /// Nodes with same color have no edges between them (causally independent).
    /// 
    /// Uses greedy coloring - O(N + E) complexity.
    /// Result: nodes in same color group can be updated in parallel.
    /// </summary>
    public void ComputeGraphColoring()
    {
        int n = _graph.N;
        _nodeColors = new int[n];
        Array.Fill(_nodeColors, -1);

        int maxColor = 0;
        var usedColors = new HashSet<int>();

        for (int node = 0; node < n; node++)
        {
            usedColors.Clear();

            // Find colors used by neighbors
            foreach (int neighbor in _graph.Neighbors(node))
            {
                if (_nodeColors[neighbor] >= 0)
                {
                    usedColors.Add(_nodeColors[neighbor]);
                }
            }

            // Also consider 2-hop neighbors for stronger independence
            // (ensures no shared neighbors between parallel nodes)
            foreach (int neighbor in _graph.Neighbors(node))
            {
                foreach (int neighbor2 in _graph.Neighbors(neighbor))
                {
                    if (neighbor2 != node && _nodeColors[neighbor2] >= 0)
                    {
                        usedColors.Add(_nodeColors[neighbor2]);
                    }
                }
            }

            // Assign smallest available color
            int color = 0;
            while (usedColors.Contains(color))
                color++;

            _nodeColors[node] = color;
            maxColor = Math.Max(maxColor, color);
        }

        _colorCount = maxColor + 1;

        // Group nodes by color
        _colorGroups = new List<int>[_colorCount];
        for (int c = 0; c < _colorCount; c++)
        {
            _colorGroups[c] = [];
        }

        for (int node = 0; node < n; node++)
        {
            _colorGroups[_nodeColors[node]].Add(node);
        }

        // Pre-allocate node arrays for each color (avoid ToArray allocation each sweep)
        _colorArrays = new int[_colorCount][];
        for (int c = 0; c < _colorCount; c++)
        {
            _colorArrays[c] = [.. _colorGroups[c]];
        }

        _lastTopologyVersion = _graph.TopologyVersion;
    }

    /// <summary>
    /// Check if coloring needs to be recomputed (topology changed)
    /// </summary>
    public bool NeedsRecoloring => _nodeColors == null || 
                                    _graph.TopologyVersion != _lastTopologyVersion;

    /// <summary>
    /// Process one "sweep" of all nodes using parallel coloring.
    /// 
    /// Each color group is processed in parallel (causally independent).
    /// Barrier synchronization between colors ensures causality.
    /// 
    /// This is the main entry point for parallel event processing.
    /// </summary>
    /// <param name="dt">Time step for physics updates</param>
    /// <param name="nodeAction">Optional custom action per node (default: UpdateNodePhysics)</param>
    /// <returns>Number of events processed</returns>
    public int ProcessParallelSweep(double dt, Action<int, double>? nodeAction = null)
    {
        if (NeedsRecoloring)
        {
            ComputeGraphColoring();
        }

        if (_colorGroups == null || _colorArrays == null || _colorCount == 0)
            return 0;

        int totalProcessed = 0;

        // Process each color group in sequence (barrier between colors)
        for (int color = 0; color < _colorCount; color++)
        {
            int[] nodeArray = _colorArrays[color];  // Use pre-allocated array
            int nodesInColor = nodeArray.Length;
            if (nodesInColor == 0) continue;

            // FIX: Lower threshold for parallel processing
            // With 250 nodes and 32 colors, avg ~7.8 nodes per color
            // Original threshold: workerCount * 4 = 60 (always sequential!)
            // New threshold: min(8, workerCount) to enable parallelism for small groups
            int parallelThreshold = Math.Min(8, _workerCount);
            
            if (nodesInColor < parallelThreshold)
            {
                // Sequential for very small groups
                for (int i = 0; i < nodesInColor; i++)
                {
                    if (nodeAction != null)
                        nodeAction(nodeArray[i], dt);
                    else
                        _graph.UpdateNodePhysics(nodeArray[i], dt);
                }
                totalProcessed += nodesInColor;
                Interlocked.Add(ref _sequentialUpdates, nodesInColor);
                continue;
            }

            // Distribute work across threads
            // FIX: Use smaller batch size for better load balancing with small groups
            int batchSize = Math.Max(1, (nodesInColor + _workerCount - 1) / _workerCount);
            int batchCount = (nodesInColor + batchSize - 1) / batchSize;

            _batchComplete.Reset(batchCount);

            for (int b = 0; b < batchCount; b++)
            {
                int start = b * batchSize;
                int count = Math.Min(batchSize, nodesInColor - start);
                int targetQueue = b % _workerCount;

                var workItem = new WorkItem(nodeArray, start, count, dt, nodeAction);
                
                // Try to add to queue, fallback to any available queue
                if (!_workQueues[targetQueue].TryAdd(workItem, millisecondsTimeout: 10))
                {
                    // Find any queue with space
                    for (int q = 0; q < _workerCount; q++)
                    {
                        if (_workQueues[q].TryAdd(workItem, millisecondsTimeout: 1))
                            break;
                    }
                }
            }

            // Wait for all batches of this color to complete (barrier)
            _batchComplete.Wait();

            totalProcessed += nodesInColor;
            Interlocked.Add(ref _parallelUpdates, nodesInColor);
        }

        Interlocked.Increment(ref _totalBatchesProcessed);
        return totalProcessed;
    }

    /// <summary>
    /// Process multiple sweeps in parallel (for event-based simulation)
    /// </summary>
    /// <param name="sweepCount">Number of sweeps to perform</param>
    /// <param name="dt">Time step</param>
    /// <returns>Total events processed</returns>
    public int ProcessMultipleSweeps(int sweepCount, double dt)
    {
        int total = 0;
        for (int s = 0; s < sweepCount; s++)
        {
            total += ProcessParallelSweep(dt);
        }
        return total;
    }

    /// <summary>
    /// Process multiple sweeps with reduced barrier overhead.
    /// 
    /// Optimization: For sweeps within the same batch, only sync between colors
    /// within each sweep, not between sweeps. This allows better pipeline utilization.
    /// 
    /// Use when you need high throughput and can tolerate slightly less strict
    /// causality guarantees between sweeps (still correct within each sweep).
    /// </summary>
    /// <param name="sweepCount">Number of sweeps to batch</param>
    /// <param name="dt">Time step per sweep</param>
    /// <param name="syncInterval">Sync after every N sweeps (0 = sync after each sweep)</param>
    /// <returns>Total events processed</returns>
    public int ProcessBatchedSweeps(int sweepCount, double dt, int syncInterval = 5)
    {
        if (NeedsRecoloring)
        {
            ComputeGraphColoring();
        }

        if (_colorGroups == null || _colorArrays == null || _colorCount == 0)
            return 0;

        int total = 0;
        int effectiveSyncInterval = syncInterval > 0 ? syncInterval : 1;

        // FIX: Lower threshold for parallel processing
        int parallelThreshold = Math.Min(8, _workerCount);

        for (int sweep = 0; sweep < sweepCount; sweep++)
        {
            // Process all colors for this sweep
            for (int color = 0; color < _colorCount; color++)
            {
                int[] nodeArray = _colorArrays[color];
                int nodesInColor = nodeArray.Length;
                if (nodesInColor == 0) continue;

                if (nodesInColor < parallelThreshold)
                {
                    // Sequential for very small groups
                    for (int i = 0; i < nodesInColor; i++)
                    {
                        _graph.UpdateNodePhysics(nodeArray[i], dt);
                    }
                    total += nodesInColor;
                    Interlocked.Add(ref _sequentialUpdates, nodesInColor);
                }
                else
                {
                    // Parallel processing with improved batch sizing
                    int batchSize = Math.Max(1, (nodesInColor + _workerCount - 1) / _workerCount);
                    int batchCount = (nodesInColor + batchSize - 1) / batchSize;
                    _batchComplete.Reset(batchCount);

                    for (int b = 0; b < batchCount; b++)
                    {
                        int start = b * batchSize;
                        int count = Math.Min(batchSize, nodesInColor - start);
                        int targetQueue = b % _workerCount;

                        var workItem = new WorkItem(nodeArray, start, count, dt, null);
                        _workQueues[targetQueue].TryAdd(workItem, millisecondsTimeout: 5);
                    }

                    _batchComplete.Wait();
                    total += nodesInColor;
                    Interlocked.Add(ref _parallelUpdates, nodesInColor);
                }
            }

            // Sync point every N sweeps for safety
            if ((sweep + 1) % effectiveSyncInterval == 0)
            {
                Thread.MemoryBarrier();
            }

            Interlocked.Increment(ref _totalBatchesProcessed);
        }

        return total;
    }

    /// <summary>
    /// Statistics
    /// </summary>
    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);
    public long TotalBatchesProcessed => Interlocked.Read(ref _totalBatchesProcessed);
    public long ParallelUpdates => Interlocked.Read(ref _parallelUpdates);
    public long SequentialUpdates => Interlocked.Read(ref _sequentialUpdates);
    public int ColorCount => _colorCount;
    public int WorkerCount => _workerCount;

    public string GetStatsSummary()
    {
        double parallelRatio = _parallelUpdates + _sequentialUpdates > 0
            ? (double)_parallelUpdates / (_parallelUpdates + _sequentialUpdates)
            : 0;

        return $"ParallelEventEngine: workers={_workerCount}, colors={_colorCount}, " +
               $"events={_totalEventsProcessed}, batches={_totalBatchesProcessed}, " +
               $"parallel={parallelRatio:P1}";
    }

    public void Dispose()
    {
        _shutdownEvent.Set();

        // Send shutdown signal to all workers
        foreach (var queue in _workQueues)
        {
            queue.TryAdd(WorkItem.Shutdown, millisecondsTimeout: 100);
        }

        // Wait for workers to finish
        foreach (var worker in _workers)
        {
            worker.Join(millisecondsTimeout: 1000);
        }

        // Cleanup
        foreach (var queue in _workQueues)
        {
            queue.Dispose();
        }

        _shutdownEvent.Dispose();
        _batchComplete.Dispose();
    }
}

/// <summary>
/// Extension methods for RQGraph parallel operations
/// </summary>
public static class ParallelEventExtensions
{
    /// <summary>
    /// Process event batch in parallel using graph coloring.
    /// Safe for RQ-hypothesis: only causally independent nodes update together.
    /// </summary>
    public static int StepEventBasedParallel(this RQGraph graph, ParallelEventEngine engine, int eventCount)
    {
        ArgumentNullException.ThrowIfNull(engine);

        // Each "sweep" processes all nodes once on average
        // eventCount events ? eventCount/N sweeps
        int sweepCount = Math.Max(1, eventCount / Math.Max(1, graph.N));
        double dt = 0.01; // Base timestep

        return engine.ProcessMultipleSweeps(sweepCount, dt);
    }
}
