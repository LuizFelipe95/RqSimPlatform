using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// GPU-accelerated random walk engine for spectral dimension computation.
    /// Launches thousands of independent random walkers in parallel.
    /// 
    /// Spectral dimension d_s is computed from return probability P(t):
    /// P(t) ~ t^(-d_s/2) for large t
    /// 
    /// Each GPU thread manages one walker, enabling 10,000+ walkers
    /// to be simulated simultaneously.
    /// 
    /// MULTI-GPU SUPPORT:
    /// ==================
    /// Supports device injection for multi-GPU clusters.
    /// Use SpectralWalkEngine(device) constructor to run on a specific GPU.
    /// The default constructor uses GraphicsDevice.GetDefault().
    /// 
    /// IMPORTANT: ReadWriteBuffer objects are bound to the GraphicsDevice that created them.
    /// Cannot transfer buffers between devices. Data must go through CPU arrays.
    /// 
    /// FIX: Added topology version tracking to prevent stale topology issues.
    /// FIX: Added UpdateTopologyFromGraph method for easier synchronization.
    /// </summary>
    public class SpectralWalkEngine : IDisposable
    {
        private readonly GraphicsDevice _device;

        // Walker state
        private ReadWriteBuffer<int>? _walkerPositions;
        private ReadOnlyBuffer<int>? _startPositions;
        private ReadWriteBuffer<int>? _returnCounts;

        // Topology in CSR format
        private ReadOnlyBuffer<int>? _adjOffsets;
        private ReadOnlyBuffer<int>? _adjNeighbors;
        private ReadOnlyBuffer<float>? _cumulativeWeights;

        private int _walkerCount;
        private int _nodeCount;
        private uint _currentSeed;
        private bool _initialized;

        // Topology version tracking to detect stale data
        private int _topologyVersion = -1;

        /// <summary>
        /// Current cached topology version. Compare with graph.TopologyVersion to detect staleness.
        /// </summary>
        public int TopologyVersion => _topologyVersion;

        /// <summary>
        /// Whether the engine is initialized and ready for computation.
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Number of walker threads.
        /// </summary>
        public int WalkerCount => _walkerCount;

        /// <summary>
        /// Number of nodes in the cached topology.
        /// </summary>
        public int NodeCount => _nodeCount;

        /// <summary>
        /// The GPU device this engine is bound to.
        /// </summary>
        public GraphicsDevice Device => _device;

        /// <summary>
        /// Create SpectralWalkEngine using the default GPU device.
        /// </summary>
        public SpectralWalkEngine()
            : this(GraphicsDevice.GetDefault())
        {
        }

        /// <summary>
        /// Create SpectralWalkEngine on a specific GPU device.
        /// Use this constructor for multi-GPU clusters where each worker
        /// needs to run on a dedicated device.
        /// </summary>
        /// <param name="device">Target GPU device for all buffer allocations and shader execution</param>
        /// <exception cref="ArgumentNullException">Device is null</exception>
        public SpectralWalkEngine(GraphicsDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            _device = device;
            _currentSeed = (uint)Environment.TickCount;
        }

        /// <summary>
        /// Create SpectralWalkEngine with custom seed for reproducibility.
        /// </summary>
        /// <param name="device">Target GPU device</param>
        /// <param name="seed">Initial RNG seed</param>
        public SpectralWalkEngine(GraphicsDevice device, uint seed)
        {
            ArgumentNullException.ThrowIfNull(device);
            _device = device;
            _currentSeed = seed;
        }

        /// <summary>
        /// Initialize the random walk engine.
        /// </summary>
        /// <param name="walkerCount">Number of parallel random walkers</param>
        /// <param name="nodeCount">Number of nodes in the graph</param>
        /// <param name="totalEdges">Total number of directed edges (2 * undirected)</param>
        public void Initialize(int walkerCount, int nodeCount, int totalEdges)
        {
            _walkerCount = walkerCount;
            _nodeCount = nodeCount;

            // Dispose old buffers
            _walkerPositions?.Dispose();
            _startPositions?.Dispose();
            _returnCounts?.Dispose();
            _adjOffsets?.Dispose();
            _adjNeighbors?.Dispose();
            _cumulativeWeights?.Dispose();

            _walkerPositions = _device.AllocateReadWriteBuffer<int>(walkerCount);
            _startPositions = _device.AllocateReadOnlyBuffer<int>(walkerCount);
            _returnCounts = _device.AllocateReadWriteBuffer<int>(1);

            _adjOffsets = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
            _adjNeighbors = _device.AllocateReadOnlyBuffer<int>(totalEdges);
            _cumulativeWeights = _device.AllocateReadOnlyBuffer<float>(totalEdges);
        }

        /// <summary>
        /// Update topology buffers from RQGraph.
        /// Call this when graph.TopologyVersion changes or before first computation.
        /// 
        /// This method builds CSR topology with weighted edges, matching the
        /// CPU random walk implementation.
        /// </summary>
        /// <param name="graph">The RQGraph to read topology from</param>
        /// <param name="walkerCount">Number of parallel walkers (default 10000)</param>
        public void UpdateTopologyFromGraph(RQGraph graph, int walkerCount = 10000)
        {
            ArgumentNullException.ThrowIfNull(graph);

            // Build CSR format
            graph.BuildSoAViews();

            int nodeCount = graph.N;
            int[] offsets = graph.CsrOffsets;
            int[] neighbors = graph.CsrIndices;
            int totalEdges = neighbors.Length;

            // Build weights array matching CSR order
            float[] weights = new float[totalEdges];
            for (int i = 0; i < nodeCount; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                for (int k = start; k < end; k++)
                {
                    int j = neighbors[k];
                    weights[k] = (float)graph.Weights[i, j];
                }
            }

            // Initialize buffers if needed
            if (_walkerCount != walkerCount || _nodeCount != nodeCount || _adjNeighbors == null ||
                _adjNeighbors.Length != totalEdges)
            {
                Initialize(walkerCount, nodeCount, totalEdges);
            }

            // Update topology
            UpdateTopology(offsets, neighbors, weights);

            // Store topology version
            _topologyVersion = graph.TopologyVersion;

            //Console.WriteLine($"[SpectralWalkEngine] Updated topology: N={nodeCount}, E={totalEdges/2}, version={_topologyVersion}");
        }

        /// <summary>
        /// Update topology buffers.
        /// Also precomputes cumulative weights for efficient weighted sampling.
        /// </summary>
        public void UpdateTopology(int[] offsets, int[] neighbors, float[] weights)
        {
            if (_adjOffsets == null || _adjNeighbors == null || _cumulativeWeights == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            // Precompute cumulative weights for each node's neighbors
            float[] cumulative = new float[weights.Length];
            for (int i = 0; i < _nodeCount; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                float sum = 0;

                for (int k = start; k < end; k++)
                {
                    sum += weights[k];
                    cumulative[k] = sum;
                }
            }

            _adjOffsets.CopyFrom(offsets);
            _adjNeighbors.CopyFrom(neighbors);
            _cumulativeWeights.CopyFrom(cumulative);
            _initialized = true;
        }

        /// <summary>
        /// Initialize walkers at random starting positions.
        /// </summary>
        /// <param name="random">Random number generator</param>
        public void InitializeWalkersRandom(Random random)
        {
            if (_walkerPositions == null || _startPositions == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            int[] positions = new int[_walkerCount];
            for (int i = 0; i < _walkerCount; i++)
            {
                positions[i] = random.Next(_nodeCount);
            }

            _walkerPositions.CopyFrom(positions);

            // Need to recreate start positions buffer with new data
            _startPositions.Dispose();
            _startPositions = _device.AllocateReadOnlyBuffer(positions);
        }

        /// <summary>
        /// Initialize all walkers at the same starting node.
        /// Useful for measuring return probability from a specific node.
        /// </summary>
        public void InitializeWalkersAt(int startNode)
        {
            if (_walkerPositions == null || _startPositions == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            int[] positions = new int[_walkerCount];
            Array.Fill(positions, startNode);

            _walkerPositions.CopyFrom(positions);

            _startPositions.Dispose();
            _startPositions = _device.AllocateReadOnlyBuffer(positions);
        }

        /// <summary>
        /// Initialize walkers uniformly distributed across all nodes.
        /// </summary>
        public void InitializeWalkersUniform()
        {
            if (_walkerPositions == null || _startPositions == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            int[] positions = new int[_walkerCount];
            for (int i = 0; i < _walkerCount; i++)
            {
                positions[i] = i % _nodeCount;
            }

            _walkerPositions.CopyFrom(positions);

            _startPositions.Dispose();
            _startPositions = _device.AllocateReadOnlyBuffer(positions);
        }

        /// <summary>
        /// Perform one step of random walk for all walkers.
        /// Returns the number of walkers that returned to their starting position.
        /// </summary>
        public int Step()
        {
            if (!_initialized || _walkerPositions == null || _startPositions == null ||
                _returnCounts == null || _adjOffsets == null || _adjNeighbors == null ||
                _cumulativeWeights == null)
            {
                throw new InvalidOperationException("Engine not properly initialized.");
            }

            // Reset return counter
            int[] zero = [0];
            _returnCounts.CopyFrom(zero);

            // Advance seed for this step
            _currentSeed = _currentSeed * 1103515245u + 12345u;

            var shader = new RandomWalkShader(
                _walkerPositions,
                _startPositions,
                _returnCounts,
                _adjOffsets,
                _adjNeighbors,
                _cumulativeWeights,
                _currentSeed,
                _walkerCount,
                _nodeCount);

            _device.For(_walkerCount, shader);

            // Read return count
            int[] result = new int[1];
            _returnCounts.CopyTo(result);

            return result[0];
        }

        /// <summary>
        /// Perform multiple steps and collect return statistics.
        /// Returns array of return counts at each step.
        /// </summary>
        public int[] RunSteps(int numSteps)
        {
            int[] returns = new int[numSteps];

            for (int t = 0; t < numSteps; t++)
            {
                returns[t] = Step();
            }

            return returns;
        }

        /// <summary>
        /// Compute spectral dimension from return probability data.
        /// Uses log-log linear fit of P(t) ~ t^(-d_s/2)
        /// </summary>
        /// <param name="returns">Return counts at each time step</param>
        /// <param name="skipInitial">Number of initial steps to skip (thermalization)</param>
        public double ComputeSpectralDimension(int[] returns, int skipInitial = 10)
        {
            // P(t) = returns[t] / walkerCount
            // log(P(t)) = -d_s/2 * log(t) + const
            // Slope of log-log plot gives -d_s/2

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            int count = 0;

            // Skip late steps too (noisy)
            int skipLate = Math.Min(10, returns.Length / 5);

            for (int t = skipInitial; t < returns.Length - skipLate; t++)
            {
                if (returns[t] <= 0)
                {
                    continue;
                }

                double logT = Math.Log(t);
                double logP = Math.Log((double)returns[t] / _walkerCount);

                sumX += logT;
                sumY += logP;
                sumXY += logT * logP;
                sumX2 += logT * logT;
                count++;
            }

            if (count < 2)
            {
                //Console.WriteLine("[SpectralWalkEngine] Insufficient data points for regression");
                return double.NaN; // Signal invalid computation
            }

            // Linear regression: slope = (n*?xy - ?x*?y) / (n*?x? - (?x)?)
            double denominator = count * sumX2 - sumX * sumX;
            if (Math.Abs(denominator) < 1e-10)
            {
                //Console.WriteLine("[SpectralWalkEngine] Degenerate regression (zero denominator)");
                return double.NaN;
            }

            double slope = (count * sumXY - sumX * sumY) / denominator;

            //Console.WriteLine($"[SpectralWalkEngine] Fit: count={count}, slope={slope:F6}");

            // If slope is non-negative, walkers are not diffusing (trapped or disconnected)
            if (slope >= -1e-4)
            {
                //Console.WriteLine("[SpectralWalkEngine] Non-negative slope detected. Graph may be disconnected.");
                return double.NaN;
            }

            // d_s = -2 * slope
            double spectralDim = -2.0 * slope;

            // Clamp to reasonable range [1, 10]
            return Math.Clamp(spectralDim, 1.0, 10.0);
        }

        /// <summary>
        /// Compute spectral dimension with automatic topology synchronization.
        /// This is the recommended entry point for computing d_s on GPU.
        /// 
        /// FIX: Automatically checks topology version and updates if stale.
        /// CRITICAL FIX: Re-syncs weights even if topology hasn't changed,
        /// because gravity evolution modifies weights without changing topology version.
        /// </summary>
        /// <param name="graph">The RQGraph to compute d_s for</param>
        /// <param name="numSteps">Number of random walk steps</param>
        /// <param name="walkerCount">Number of parallel walkers</param>
        /// <param name="skipInitial">Number of initial steps to skip</param>
        /// <returns>Spectral dimension estimate, or NaN if computation failed</returns>
        public double ComputeSpectralDimensionWithSyncCheck(
            RQGraph graph,
            int numSteps = 100,
            int walkerCount = 10000,
            int skipInitial = 10)
        {
            ArgumentNullException.ThrowIfNull(graph);

            // Check for stale topology
            if (!_initialized || _topologyVersion != graph.TopologyVersion)
            {
                //Console.WriteLine($"[SpectralWalkEngine] Topology stale (cached={_topologyVersion}, graph={graph.TopologyVersion}). Updating...");
                UpdateTopologyFromGraph(graph, walkerCount);
            }
            else
            {
                // CRITICAL FIX: Even if topology version is same, weights may have changed
                // from gravity evolution. Re-sync weights to ensure correct spectral dimension.
                SyncWeightsFromGraph(graph);
            }

            // Initialize walkers at random positions
            InitializeWalkersRandom(new Random());

            // Run random walks
            int[] returns = RunSteps(numSteps);

            // Compute spectral dimension
            double ds = ComputeSpectralDimension(returns, skipInitial);

            //Console.WriteLine($"[SpectralWalkEngine] d_S = {ds:F4}");

            return ds;
        }

        /// <summary>
        /// Sync weights from graph without rebuilding full topology.
        /// Call this when weights have changed but topology (edges) hasn't.
        /// </summary>
        private void SyncWeightsFromGraph(RQGraph graph)
        {
            if (_adjOffsets == null || _adjNeighbors == null || _cumulativeWeights == null)
            {
                return;
            }

            int[] offsets = new int[_nodeCount + 1];
            int[] neighbors = new int[_cumulativeWeights.Length];

            _adjOffsets.CopyTo(offsets);
            _adjNeighbors.CopyTo(neighbors);

            // Build weights array matching CSR order
            float[] weights = new float[neighbors.Length];
            for (int i = 0; i < _nodeCount; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                for (int k = start; k < end; k++)
                {
                    int j = neighbors[k];
                    weights[k] = (float)graph.Weights[i, j];
                }
            }

            // Recompute cumulative weights for weighted sampling
            float[] cumulative = new float[weights.Length];
            for (int i = 0; i < _nodeCount; i++)
            {
                int start = offsets[i];
                int end = offsets[i + 1];
                float sum = 0;

                for (int k = start; k < end; k++)
                {
                    sum += weights[k];
                    cumulative[k] = sum;
                }
            }

            // Re-upload to GPU without reallocating buffers
            _cumulativeWeights.CopyFrom(cumulative);
        }

        /// <summary>
        /// Get current positions of all walkers.
        /// </summary>
        public int[] GetWalkerPositions()
        {
            if (_walkerPositions == null)
            {
                throw new InvalidOperationException("Buffers not initialized.");
            }

            int[] positions = new int[_walkerCount];
            _walkerPositions.CopyTo(positions);

            return positions;
        }

        /// <summary>
        /// Measures signal propagation velocity from source to target node.
        /// 
        /// This tests Lieb-Robinson bounds: information should propagate at
        /// a finite emergent "speed of light" through the graph.
        /// 
        /// Method: Inject signal at source, measure time to reach target.
        /// Velocity = geodesic_distance / arrival_time
        /// </summary>
        /// <param name="graph">The RQGraph to analyze</param>
        /// <param name="sourceNode">Node where signal originates</param>
        /// <param name="targetNode">Node to detect signal arrival</param>
        /// <param name="maxSteps">Maximum steps before timeout</param>
        /// <param name="detectionThreshold">Fraction of walkers required at target for "detection"</param>
        /// <returns>Signal velocity (geodesic distance / time steps), or 0 if not reached</returns>
        public double MeasureSignalVelocity(
            RQGraph graph,
            int sourceNode,
            int targetNode,
            int maxSteps = 1000,
            double detectionThreshold = 0.01)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (sourceNode == targetNode || sourceNode < 0 || targetNode < 0 ||
                sourceNode >= graph.N || targetNode >= graph.N)
            {
                return 0;
            }

            // Update topology if needed
            if (!_initialized || _topologyVersion != graph.TopologyVersion)
            {
                UpdateTopologyFromGraph(graph);
            }

            // Calculate geodesic distance using BFS
            int geodesicDistance = CalculateGeodesicDistance(graph, sourceNode, targetNode);
            if (geodesicDistance <= 0)
            {
                return 0; // Nodes not connected
            }

            // Initialize all walkers at source node
            InitializeWalkersAt(sourceNode);

            int detectionCount = (int)(_walkerCount * detectionThreshold);

            for (int t = 1; t <= maxSteps; t++)
            {
                Step();

                // Count walkers at target
                int[] positions = GetWalkerPositions();
                int atTarget = positions.Count(p => p == targetNode);

                if (atTarget >= detectionCount)
                {
                    return (double)geodesicDistance / t;
                }
            }

            return 0; // Signal didn't reach target in time
        }

        /// <summary>
        /// Calculates geodesic (shortest path) distance between two nodes using BFS.
        /// </summary>
        private static int CalculateGeodesicDistance(RQGraph graph, int source, int target)
        {
            if (source == target)
            {
                return 0;
            }

            graph.BuildSoAViews();
            int[] offsets = graph.CsrOffsets;
            int[] neighbors = graph.CsrIndices;

            int[] distance = new int[graph.N];
            Array.Fill(distance, -1);
            distance[source] = 0;

            Queue<int> queue = new();
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int start = offsets[current];
                int end = offsets[current + 1];

                for (int k = start; k < end; k++)
                {
                    int neighbor = neighbors[k];
                    if (distance[neighbor] < 0)
                    {
                        distance[neighbor] = distance[current] + 1;
                        if (neighbor == target)
                        {
                            return distance[neighbor];
                        }
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return -1; // Not connected
        }

        /// <summary>
        /// Calculates variance in signal velocity across different directions.
        /// 
        /// Low variance indicates isotropic speed of light (Lorentz invariance).
        /// High variance suggests preferred directions or anisotropic geometry.
        /// </summary>
        /// <param name="graph">The RQGraph to analyze</param>
        /// <param name="numSamples">Number of source-target pairs to test</param>
        /// <param name="minDistance">Minimum geodesic distance for valid pairs</param>
        /// <returns>Variance in measured velocities, and mean velocity</returns>
        public (double Variance, double Mean) CalculateIsotropyVariance(
            RQGraph graph,
            int numSamples = 10,
            int minDistance = 3)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (graph.N < minDistance * 2)
            {
                return (double.NaN, double.NaN);
            }

            List<double> velocities = [];
            Random rng = new();

            // Sample random source-target pairs
            int attempts = 0;
            int maxAttempts = numSamples * 10;

            while (velocities.Count < numSamples && attempts < maxAttempts)
            {
                attempts++;

                int source = rng.Next(graph.N);
                int target = rng.Next(graph.N);

                if (source == target)
                {
                    continue;
                }

                int distance = CalculateGeodesicDistance(graph, source, target);
                if (distance < minDistance)
                {
                    continue;
                }

                double velocity = MeasureSignalVelocity(graph, source, target, maxSteps: distance * 5);
                if (velocity > 0)
                {
                    velocities.Add(velocity);
                }
            }

            if (velocities.Count < 2)
            {
                return (double.NaN, double.NaN);
            }

            double mean = velocities.Average();
            double variance = velocities.Sum(v => (v - mean) * (v - mean)) / (velocities.Count - 1);

            return (variance, mean);
        }

        /// <summary>
        /// Gets the effective speed of light (mean signal velocity) in the graph.
        /// </summary>
        public double GetEffectiveSpeedOfLight(RQGraph graph, int numSamples = 10)
        {
            var (_, mean) = CalculateIsotropyVariance(graph, numSamples);

            return mean;
        }

        public void Dispose()
        {
            _walkerPositions?.Dispose();
            _startPositions?.Dispose();
            _returnCounts?.Dispose();
            _adjOffsets?.Dispose();
            _adjNeighbors?.Dispose();
            _cumulativeWeights?.Dispose();
        }
    }

    /// <summary>
    /// GPU shader for weighted random walk.
    /// Each thread manages one walker.
    /// 
    /// Uses PCG hash for pseudo-random number generation.
    /// Weighted neighbor selection via cumulative weights.
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct RandomWalkShader : IComputeShader
    {
        public readonly ReadWriteBuffer<int> walkerPositions;
        public readonly ReadOnlyBuffer<int> startPositions;
        public readonly ReadWriteBuffer<int> returnCounts;
        public readonly ReadOnlyBuffer<int> offsets;
        public readonly ReadOnlyBuffer<int> neighbors;
        public readonly ReadOnlyBuffer<float> cumulativeWeights;
        public readonly uint seed;
        public readonly int walkerCount;
        public readonly int nodeCount;

        public RandomWalkShader(
            ReadWriteBuffer<int> walkerPositions,
            ReadOnlyBuffer<int> startPositions,
            ReadWriteBuffer<int> returnCounts,
            ReadOnlyBuffer<int> offsets,
            ReadOnlyBuffer<int> neighbors,
            ReadOnlyBuffer<float> cumulativeWeights,
            uint seed,
            int walkerCount,
            int nodeCount)
        {
            this.walkerPositions = walkerPositions;
            this.startPositions = startPositions;
            this.returnCounts = returnCounts;
            this.offsets = offsets;
            this.neighbors = neighbors;
            this.cumulativeWeights = cumulativeWeights;
            this.seed = seed;
            this.walkerCount = walkerCount;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int walkerIdx = ThreadIds.X;
            if (walkerIdx >= walkerCount)
            {
                return;
            }

            int currentNode = walkerPositions[walkerIdx];

            // 1. Generate pseudo-random number using PCG hash
            // Use both walkerIdx and seed for better entropy
            uint state = (uint)(walkerIdx * 73856093) ^ seed ^ (uint)(currentNode * 19349663);
            state = state * 747796405u + 2891336453u;
            uint word = ((state >> ((int)(state >> 28) + 4)) ^ state) * 277803737u;
            uint result = (word >> 22) ^ word;
            float rnd = (float)result / 4294967295.0f; // Normalize to [0, 1]

            // 2. Get neighbor range for current node
            int start = offsets[currentNode];
            int end = offsets[currentNode + 1];

            // Track whether walker actually moved. Isolated nodes (degree=0) should not count as returns.
            bool didMove = false;
            int nextNode = currentNode;

            if (end > start)
            {
                // 3. Select neighbor using per-node cumulative weights.
                // cumulativeWeights contains per-node prefix sums, so we must
                // use a local base offset to derive local cumulative values.
                float totalWeight = cumulativeWeights[end - 1];
                float baseOffset = 0.0f;
                if (start > 0)
                {
                    baseOffset = cumulativeWeights[start - 1];
                    totalWeight -= baseOffset;
                }

                if (totalWeight > 0.0f)
                {
                    float targetWeight = rnd * totalWeight;

                    // Linear search for neighbor selection
                    for (int k = start; k < end; k++)
                    {
                        float localCum = cumulativeWeights[k] - baseOffset;
                        if (localCum >= targetWeight)
                        {
                            nextNode = neighbors[k];
                            didMove = true;
                            break;
                        }
                    }

                    // Fallback to last neighbor if loop didn't select (floating point edge case)
                    if (!didMove)
                    {
                        nextNode = neighbors[end - 1];
                        didMove = true;
                    }
                }
            }

            // 4. Update position
            walkerPositions[walkerIdx] = nextNode;

            // 5. Check if returned to start and atomically increment counter
            // Only count as return if walker actually moved this step to avoid
            // isolated nodes inflating return probability.
            if (didMove && nextNode == startPositions[walkerIdx])
            {
                Hlsl.InterlockedAdd(ref returnCounts[0], 1);
            }
        }
    }
}
