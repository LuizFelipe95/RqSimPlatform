using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // NOTE: _stringEnergy is declared in RQGraph.ApiCompat.cs

        /// <summary>
        /// Gets the number of connected components in the graph.
        /// Used for connectivity protection in gravity evolution.
        /// Returns 1 if graph is fully connected.
        /// </summary>
        public int GetConnectedComponentCount()
        {
            var (_, comps) = GetComponentLabels();
            return comps;
        }

        /// <summary>
        /// Checks if the graph is fully connected (single component).
        /// RQ-FIX: Used to prevent fragmentation during gravity evolution.
        /// </summary>
        public bool IsGraphConnected()
        {
            return GetConnectedComponentCount() == 1;
        }

        /// <summary>
        /// Add random edges to restore connectivity when graph is fragmenting.
        /// Called when d_S drops below threshold, indicating linear/fragmented structure.
        /// RQ-FIX: Emergency connectivity restoration.
        /// </summary>
        /// <param name="count">Number of edges to add</param>
        public void AddRandomEdgesForConnectivity(int count)
        {
            for (int attempt = 0; attempt < count * 3 && count > 0; attempt++)
            {
                int i = _rng.Next(N);
                int j = _rng.Next(N);

                if (i == j) continue;
                if (Edges[i, j]) continue; // Edge already exists

                // Prefer connecting nodes from different components
                var (labels, components) = GetComponentLabels();
                if (components > 1 && labels[i] == labels[j])
                {
                    // Try to find j in a different component
                    for (int k = 0; k < N; k++)
                    {
                        if (labels[k] != labels[i])
                        {
                            j = k;
                            break;
                        }
                    }
                }

                // Add the edge
                AddEdge(i, j);
                Weights[i, j] = 0.5; // Neutral initial weight
                Weights[j, i] = 0.5;
                count--;
            }
        }

        public List<int> GetComponentMembers(int label)
        {
            var (labels, comps) = GetComponentLabels();
            var members = new List<int>();
            for (int i = 0; i < N; i++) if (labels[i] == label) members.Add(i);
            return members;
        }

        public (int[] Labels, int Components) GetComponentLabels()
        {
            var labels = new int[N];
            for (int i = 0; i < N; i++) labels[i] = -1;
            int comp = 0;
            for (int i = 0; i < N; i++)
            {
                if (labels[i] != -1) continue;
                var q = new Queue<int>();
                q.Enqueue(i);
                labels[i] = comp;
                while (q.Count > 0)
                {
                    int v = q.Dequeue();
                    foreach (int u in Neighbors(v))
                    {
                        if (labels[u] != -1) continue;
                        labels[u] = comp;
                        q.Enqueue(u);
                    }
                }
                comp++;
            }
            return (labels, comp);
        }

        // Minimal placeholders to satisfy references across partials
        // Note: _gaugeSU2 is now defined in RQGraph.GaugeSU.cs as SU2Matrix[,]
        private double[,] _gaugeU1;    // U(1) hypercharge phase placeholder

        public int Degree(int i) => _degree != null && i >= 0 && i < _degree.Length ? _degree[i] : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEdge(int i, int j) => Edges[i, j];

        // Struct enumerator (mutable) for zero-allocation neighbor iteration
        public struct NeighborEnumerator
        {
            private readonly bool[,] _edges;
            private readonly int _n;
            private readonly int _i;
            private int _cursor;
            public int Current { get; private set; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public NeighborEnumerator(bool[,] edges, int n, int i)
            {
                _edges = edges; _n = n; _i = i; _cursor = -1; Current = -1;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                int c = _cursor + 1;
                while (c < _n)
                {
                    if (_edges[_i, c]) { Current = c; _cursor = c; return true; }
                    c++;
                }
                _cursor = _n; return false;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public NeighborEnumerator GetEnumerator() => this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NeighborEnumerator FastNeighbors(int i) => new NeighborEnumerator(Edges, N, i);

        public IEnumerable<int> Neighbors(int i)
        {
            for (int j = 0; j < N; j++)
            {
                if (Edges[i, j]) yield return j;
            }
        }

        public int CountOnNodes()
        {
            int count = 0;
            for (int i = 0; i < N; i++) if (State[i] == NodeState.Excited) count++;
            return count;
        }

        public int LastNodesFlipped { get; private set; } = 0;

        public void FlipNodeWithNeighbors(int node)
        {
            if (node < 0 || node >= N) return;
            LastNodesFlipped = 0;
            // Flip target
            State[node] = State[node] == NodeState.Excited ? NodeState.Rest : NodeState.Excited;
            LastNodesFlipped++;
            // Flip neighbors
            foreach (int j in Neighbors(node))
            {
                State[j] = State[j] == NodeState.Excited ? NodeState.Rest : NodeState.Excited;
                LastNodesFlipped++;
            }
        }

        private double ComputeClusterEnergy(List<int> cluster)
        {
            double e = 0.0;
            for (int a = 0; a < cluster.Count; a++)
            {
                int i = cluster[a];
                for (int b = a + 1; b < cluster.Count; b++)
                {
                    int j = cluster[b];
                    if (Edges[i, j]) e += Weights[i, j];
                }
            }
            return e;
        }

        private HashSet<int> _condensedNodes = new HashSet<int>();
        public bool IsInCondensedCluster(int i) => _condensedNodes.Contains(i);

        public void Step()
        {
            double thr = GetAdaptiveHeavyThreshold();
            var clusters = GetStrongCorrelationClusters(thr);
            _condensedNodes.Clear();
            foreach (var cluster in clusters)
            {
                if (cluster.Count >= HeavyClusterMinSize)
                {
                    // RQ-compliant: Use energy-based stabilization instead of manual strengthening
                    StabilizeClusterViaMetropolisPublic(cluster);
                    ReinforceCluster(cluster); // age-based reinforcement (checklist 2)
                    if (PhysicsProperties != null && PhysicsProperties.Length == N)
                        foreach (int node in cluster) PhysicsProperties[node].Type = ParticleType.Composite;
                    double deltaM = ComputeClusterEnergy(cluster);
                    ApplyHeavyMassDelta(deltaM, cluster);
                    ProcessClusterBoundaries(cluster); // boundary weakening/removal
                    // correlation string around stable core (checklist 5)
                    // RQ-FIX: Replaced StructuralMass with weight strengthening (mass from correlations)
                    foreach (int n in cluster)
                    {
                        foreach (int nb in Neighbors(n))
                        {
                            // Strengthen connection to halo/string
                            Weights[n, nb] = Math.Min(1.0, Weights[n, nb] + 0.003);
                            Weights[nb, n] = Weights[n, nb];
                        }
                    }
                }
                if (LocalPotential != null && cluster.Count >= 5)
                {
                    double avgPot = 0.0;
                    foreach (int v in cluster) avgPot += LocalPotential[v];
                    avgPot /= cluster.Count;
                    if (avgPot > 0.5)
                    {
                        foreach (int x in cluster)
                        {
                            _condensedNodes.Add(x);
                            foreach (int y in cluster)
                            {
                                if (x < y && Edges[x, y])
                                {
                                    double nw = Math.Min(1.0, Weights[x, y] + 0.05);
                                    Weights[x, y] = nw; Weights[y, x] = nw;
                                }
                            }
                        }
                    }
                }
            }
            // particle feedback bending structure (checklist 6)
            // RQ-FIX: Use correlation mass instead of StructuralMass
            if (_correlationMass != null)
            {
                for (int i = 0; i < N; i++)
                {
                    // Threshold adjusted for correlation mass (typically > 1.0 for heavy nodes)
                    if (_correlationMass[i] > 1.0)
                    {
                        foreach (int j in Neighbors(i))
                        {
                            if (Edges[i, j])
                            {
                                if (_rng.NextDouble() < 0.1)
                                {
                                    Weights[i, j] = Math.Min(1.0, Weights[i, j] + 0.05);
                                    Weights[j, i] = Weights[i, j];
                                }
                            }
                        }
                    }
                }
            }
            EnforcePlanckCorrelationLimit();
            UpdateEdgeDelaysFromDistances();
        }

        public void RebuildComponents()
        {
            // simple connectivity rebuild using existing GetComponentLabels helper
            var (labels, comps) = GetComponentLabels();
            _componentsCount = comps; // store if needed by other partials
        }
        private int _componentsCount;

        private void ProcessClusterBoundaries(List<int> cluster)
        {
            if (cluster == null) return;
            if (cluster.Count < HeavyClusterMinSize) return;
            double thr = HeavyClusterThreshold;
            var clusterSet = new HashSet<int>(cluster);
            foreach (int i in cluster)
            {
                foreach (int j in Neighbors(i).ToList())
                {
                    if (!clusterSet.Contains(j))
                    {
                        if (Weights[i, j] < thr)
                        {
                            RemoveEdge(i, j);
                        }
                        else
                        {
                            Weights[i, j] *= 0.5;
                            Weights[j, i] = Weights[i, j];
                        }
                    }
                }
            }
            _hasHeavyClusters = true;
            RebuildComponents();
        }
        /// <summary>
        /// Updates the adaptive heavy threshold based on current edge weight statistics.
        /// Formula: threshold = mean(weights) + AdaptiveThresholdSigma * stddev(weights)
        /// This is a statistical measure (~7% of edges are "heavy" at 1.5?).
        /// Implements RQ-Hypothesis Checklist adaptive threshold.
        /// </summary>
        public void UpdateAdaptiveHeavyThreshold()
        {
            // Collect all edge weights
            var weights = new List<double>();
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (Edges[i, j])
                    {
                        weights.Add(Weights[i, j]);
                    }
                }
            }

            if (weights.Count == 0)
            {
                _adaptiveHeavyThreshold = PhysicsConstants.DefaultHeavyClusterThreshold;
                return;
            }

            // Compute mean and standard deviation
            double mean = 0.0;
            foreach (var w in weights) mean += w;
            mean /= weights.Count;

            double variance = 0.0;
            foreach (var w in weights)
            {
                double diff = w - mean;
                variance += diff * diff;
            }
            variance /= weights.Count;
            double stddev = Math.Sqrt(variance);

            // Adaptive threshold: mean + sigma * stddev
            double sigma = PhysicsConstants.AdaptiveThresholdSigma;
            double computed = mean + sigma * stddev;

            // Clamp to reasonable range [0.1, 0.95]
            _adaptiveHeavyThreshold = Math.Clamp(computed, 0.1, 0.95);
        }

        /// <summary>
        /// Returns the current adaptive heavy threshold.
        /// Call UpdateAdaptiveHeavyThreshold() periodically to refresh this value.
        /// </summary>
        public double GetAdaptiveHeavyThreshold() => _adaptiveHeavyThreshold > 0 ? _adaptiveHeavyThreshold : PhysicsConstants.DefaultHeavyClusterThreshold;

        // If strong cluster finder not present in this partial, implement lightweight version (breadth-first with threshold)
        public List<List<int>> GetStrongCorrelationClusters(double threshold)
        {
            var visited = new bool[N];
            var result = new List<List<int>>();
            for (int i = 0; i < N; i++)
            {
                if (visited[i]) continue;
                bool seedOk = false;
                for (int j = 0; j < N; j++)
                {
                    if (Edges[i, j] && Weights[i, j] >= threshold) { seedOk = true; break; }
                }
                if (!seedOk) continue;
                var q = new Queue<int>();
                var cluster = new List<int>();
                visited[i] = true; q.Enqueue(i);
                while (q.Count > 0)
                {
                    int v = q.Dequeue(); cluster.Add(v);
                    for (int u = 0; u < N; u++)
                    {
                        if (!Edges[v, u] || visited[u]) continue;
                        if (Weights[v, u] < threshold) continue;
                        visited[u] = true; q.Enqueue(u);
                    }
                }
                result.Add(cluster);
            }
            return result;
        }


        // Average pair correlation (entanglement proxy)
        public double ComputeAvgPairCorrelation()
        {
            if (_pairCorrelation == null || _pairCorrelation.GetLength(0) != N) return 0.0;
            double s = 0; int c = 0;
            for (int i = 0; i < N; i++) for (int j = i + 1; j < N; j++) { s += _pairCorrelation[i, j]; c++; }
            return c > 0 ? s / c : 0.0;
        }

        // Restore AddEdge (single authoritative implementation needed by other partials)
        /// <summary>
        /// Add an edge between two nodes.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: Edge creation requires vacuum energy.
        /// This overload does NOT check energy cost (used by internal operations).
        /// For energy-conserving edge creation, use AddEdgeWithEnergyCost().
        /// </summary>
        public void AddEdge(int i, int j)
        {
            if (i == j || i < 0 || j < 0 || i >= N || j >= N) return;
            if (!Edges[i, j])
            {
                Edges[i, j] = true; Edges[j, i] = true;
                _degree[i]++; _degree[j]++;
                TopologyVersion++; // Track topology changes for parallel coloring
                if (Weights[i, j] == 0.0 && Weights[j, i] == 0.0)
                {
                    // === RQ-HYPOTHESIS CHECKLIST: WEIGHT QUANTIZATION ===
                    // Initial weight must be a quantum multiple
                    double initialWeight = PhysicsConstants.EdgeWeightQuantum * 2; // 0.1 as 2 quanta
                    Weights[i, j] = initialWeight;
                    Weights[j, i] = initialWeight;
                }
                if (EdgeDelay != null && EdgeDirection != null)
                {
                    // metric update
                    double d = GetPhysicalDistance(i, j);
                    double avgDeg = 0.0; for (int k = 0; k < N; k++) avgDeg += _degree[k];
                    avgDeg = N > 0 ? avgDeg / N : 1.0;
                    double maxSignalSpeed = 1.0 / Math.Max(1.0, avgDeg);
                    double delay = d / maxSignalSpeed;
                    EdgeDelay[i, j] = delay; EdgeDelay[j, i] = delay;
                    EdgeDirection[i, j] = 1; EdgeDirection[j, i] = -1;
                }
            }
        }

        /// <summary>
        /// Add an edge with energy cost check from vacuum pool.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: Creating new correlations (edges) requires vacuum energy.
        /// This method checks if sufficient vacuum energy is available and deducts the cost.
        /// Returns false if not enough energy is available.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <returns>True if edge was created successfully, false if not enough energy</returns>
        public bool AddEdgeWithEnergyCost(int i, int j)
        {
            if (i == j || i < 0 || j < 0 || i >= N || j >= N) return false;
            if (Edges[i, j]) return true; // Edge already exists

            // Check and spend vacuum energy for edge creation
            var ledger = GetEnergyLedger();
            if (!ledger.TrySpendVacuumEnergy(PhysicsConstants.EdgeCreationCost))
            {
                return false; // Not enough vacuum energy to create edge
            }

            // Create the edge (energy already deducted)
            AddEdge(i, j);
            return true;
        }

        /// <summary>
        /// Internal edge removal helper (avoids ambiguity with ApiCompat.RemoveEdge)
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: Returns edge creation energy to vacuum pool.
        /// </summary>
        private void RemoveEdgeInternal(int i, int j)
        {
            if (i == j || i < 0 || j < 0 || i >= N || j >= N) return;
            if (Edges[i, j])
            {
                // === RQ-HYPOTHESIS CHECKLIST: RETURN EDGE ENERGY TO VACUUM ===
                var ledger = GetEnergyLedger();
                ledger.RegisterRadiation(PhysicsConstants.EdgeCreationCost);

                Edges[i, j] = false; Edges[j, i] = false;
                _degree[i]--; _degree[j]--;
                Weights[i, j] = 0.0; Weights[j, i] = 0.0;
                if (EdgeDelay != null)
                {
                    EdgeDelay[i, j] = 0.0; EdgeDelay[j, i] = 0.0;
                }
                if (EdgeDirection != null)
                {
                    EdgeDirection[i, j] = 0; EdgeDirection[j, i] = 0;
                }
            }
        }

        public int[] ClusterAge; // checklist item 2: multi-step cluster fixation

        public void InitClusterAge()
        {
            ClusterAge = new int[N];
            for (int i = 0; i < N; i++) ClusterAge[i] = 0;
        }

        public void IncrementClusterAge(List<int> cluster)
        {
            if (ClusterAge == null) InitClusterAge();
            foreach (int n in cluster) ClusterAge[n]++;
        }

        private void ReinforceCluster(List<int> cluster)
        {
            if (cluster == null) return;
            foreach (int i in cluster)
                foreach (int j in cluster)
                    if (i < j && Edges[i, j])
                    {
                        // checklist: age-based reinforcement
                        double add = 0.02;
                        if (ClusterAge != null && ClusterAge[i] > 10 && ClusterAge[j] > 10) add = 0.03; // item 2
                        Weights[i, j] = Weights[j, i] = Math.Min(Weights[i, j] + add, 1.0);
                    }
        }

        public void UpdateHeavyNodes()
        {
            // simple wrapper to age clusters each step
            double thr = GetAdaptiveHeavyThreshold();
            var clusters = GetStrongCorrelationClusters(thr);
            foreach (var c in clusters) IncrementClusterAge(c);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<int> GetNeighborSpan(int i, ref int[] scratch)
        {
            // scratch length >= Degree(i); caller can allocate once per outer loop.
            int deg = 0;
            for (int j = 0; j < N; j++)
            {
                if (Edges[i, j]) scratch[deg++] = j;
            }
            return scratch.AsSpan(0, deg);
        }

        /// <summary>
        /// Topology Tunneling: Emergency breakup of a giant cluster that persists despite decoherence.
        /// 
        /// RQ-Hypothesis Compliant: Uses quantum tunneling metaphor - weak edges within
        /// the giant cluster are removed, fragmenting it into smaller components.
        /// Strong edges (true physical correlations) are preserved.
        /// 
        /// Called when cluster exceeds 70% of graph for multiple consecutive auto-tune cycles.
        /// </summary>
        /// <param name="removalFraction">Fraction of weak edges to remove (default 30%)</param>
        /// <returns>Number of edges removed</returns>
        public int PerformTopologyTunneling(double removalFraction = 0.30)
        {
            double threshold = GetAdaptiveHeavyThreshold();
            var clusters = GetStrongCorrelationClusters(threshold);

            // Find the giant cluster (largest)
            var giantCluster = clusters.OrderByDescending(c => c.Count).FirstOrDefault();
            if (giantCluster == null || giantCluster.Count < N * 0.5)
                return 0; // No giant cluster or not dominant

            // Collect all edges within giant cluster, sorted by weight (weakest first)
            var clusterEdges = new List<(int i, int j, double w)>();
            var clusterSet = new HashSet<int>(giantCluster);

            foreach (int i in giantCluster)
            {
                foreach (int j in Neighbors(i))
                {
                    if (i < j && clusterSet.Contains(j))
                    {
                        clusterEdges.Add((i, j, Weights[i, j]));
                    }
                }
            }

            // Sort by weight ascending (remove weakest edges first)
            clusterEdges.Sort((a, b) => a.w.CompareTo(b.w));

            // Remove bottom removalFraction of edges
            int toRemove = (int)(clusterEdges.Count * removalFraction);
            int removed = 0;

            for (int k = 0; k < toRemove && k < clusterEdges.Count; k++)
            {
                var (i, j, w) = clusterEdges[k];

                // Only remove if it won't disconnect the graph completely
                // (keep at least one path for each node)
                if (_degree[i] > 1 && _degree[j] > 1)
                {
                    RemoveEdgeInternal(i, j);
                    removed++;
                }
            }

            // Also weaken remaining edges in the cluster
            foreach (int i in giantCluster)
            {
                foreach (int j in Neighbors(i).ToList())
                {
                    if (clusterSet.Contains(j))
                    {
                        Weights[i, j] *= 0.7;
                        Weights[j, i] = Weights[i, j];
                    }
                }
            }

            // Rebuild components and update delays
            RebuildComponents();
            UpdateEdgeDelaysFromDistances();

            return removed;
        }

        // ================================================================
        // CHECKLIST ITEM 5: GAUGE FLUX PROTECTION FOR TOPOLOGY CHANGES
        // ================================================================

        /// <summary>
        /// Check if an edge can be safely removed without violating gauge invariance.
        /// 
        /// CHECKLIST ITEM 5: Prevent edge removal when gauge flux is non-trivial.
        /// 
        /// An edge carries gauge field (U(1) phase, SU(2)/SU(3) matrix). Removing it
        /// when flux is non-zero would violate charge/color conservation by creating
        /// a "hole" in the gauge field configuration.
        /// 
        /// Flux is non-trivial if:
        /// - U(1) phase is far from 0 or 2?
        /// - SU(3) link matrix is far from identity
        /// 
        /// Returns true if edge can be safely removed.
        /// </summary>
        /// <param name="i">First node</param>
        /// <param name="j">Second node</param>
        /// <param name="fluxThreshold">Maximum allowed flux magnitude (default from PhysicsConstants)</param>
        /// <returns>True if edge removal is gauge-safe</returns>
        public bool CanRemoveEdgeGaugeSafe(int i, int j, double fluxThreshold = -1)
        {
            if (!Edges[i, j])
                return true; // Edge doesn't exist

            if (fluxThreshold < 0)
                fluxThreshold = PhysicsConstants.TopologicalCensorshipFluxThreshold;

            double totalFlux = 0.0;

            // Check U(1) gauge phase
            if (_edgePhaseU1 != null)
            {
                double phase = _edgePhaseU1[i, j];
                // Flux is significant if phase is not near 0 or 2?
                double normalizedPhase = Math.Abs(phase) % (2 * Math.PI);
                if (normalizedPhase > Math.PI) normalizedPhase = 2 * Math.PI - normalizedPhase;
                totalFlux += normalizedPhase / Math.PI; // Normalize to [0,1]
            }

            // Check SU(3) gauge matrix
            if (_gaugeSU3 != null && GaugeDimension == 3)
            {
                var U = _gaugeSU3[i, j];
                // Distance from identity: ||U - I||_F (Frobenius norm)
                double dist = U.DistanceFromIdentity();
                totalFlux += dist / 2.0; // Normalize roughly to [0,1]
            }

            return totalFlux < fluxThreshold;
        }

        /// <summary>
        /// Remove an edge with gauge flux protection.
        /// 
        /// CHECKLIST ITEM 5: Gauge-safe topology changes.
        /// 
        /// Before removing an edge, checks if the gauge flux on that edge is trivial.
        /// If non-trivial flux exists, the edge removal is blocked (unless forced)
        /// to prevent gauge invariance violation.
        /// </summary>
        /// <param name="i">First node</param>
        /// <param name="j">Second node</param>
        /// <param name="forceRemove">If true, transfer flux and remove anyway</param>
        /// <returns>True if edge was removed</returns>
        public bool RemoveEdgeGaugeSafe(int i, int j, bool forceRemove = false)
        {
            if (!Edges[i, j])
                return false;

            if (CanRemoveEdgeGaugeSafe(i, j))
            {
                // Safe to remove - no significant gauge flux
                RemoveEdgeInternal(i, j);
                return true;
            }

            if (!forceRemove)
            {
                // Not safe and not forced - refuse removal
                return false;
            }

            // Force remove: transfer flux to neighboring edges first
            TransferGaugeFluxBeforeRemoval(i, j);

            // Now remove the edge
            RemoveEdgeInternal(i, j);

            // Trigger Gauss law enforcement to restore consistency
            // CHECKLIST ITEM 5: Synchronize topology changes with gauge constraints
            TriggerGaussLawEnforcement();

            return true;
        }

        /// <summary>
        /// Transfer gauge flux from edge (i,j) to neighboring edges before removal.
        /// 
        /// For each gauge field type, distribute the flux to common neighbors:
        /// - U(1) phase: distribute equally to triangles
        /// - SU(3) matrix: multiply into staple contributions
        /// </summary>
        private void TransferGaugeFluxBeforeRemoval(int i, int j)
        {
            // Find common neighbors (triangles containing edge i-j)
            var commonNeighbors = new List<int>();
            foreach (int k in Neighbors(i))
            {
                if (k != j && Edges[k, j])
                {
                    commonNeighbors.Add(k);
                }
            }

            if (commonNeighbors.Count == 0)
            {
                // No triangles - flux will be lost
                // At least zero out the gauge fields on this edge
                ClearGaugeFieldsOnEdge(i, j);
                return;
            }

            // Transfer U(1) phase to triangle edges
            if (_edgePhaseU1 != null)
            {
                double phase = _edgePhaseU1[i, j];
                double phasePerTriangle = phase / commonNeighbors.Count;

                foreach (int k in commonNeighbors)
                {
                    // Distribute to edges i-k and k-j
                    _edgePhaseU1[i, k] += phasePerTriangle * 0.5;
                    _edgePhaseU1[k, i] += phasePerTriangle * 0.5;
                    _edgePhaseU1[k, j] += phasePerTriangle * 0.5;
                    _edgePhaseU1[j, k] += phasePerTriangle * 0.5;
                }

                // Clear original edge
                _edgePhaseU1[i, j] = 0.0;
                _edgePhaseU1[j, i] = 0.0;
            }

            // Transfer SU(3) matrix contribution (simplified: distribute trace)
            if (_gaugeSU3 != null && GaugeDimension == 3)
            {
                // For SU(3), we can't simply distribute matrix elements
                // Instead, reset to identity (flux is absorbed into Wilson line on remaining triangle)
                _gaugeSU3[i, j] = Gauge.SU3Matrix.Identity;
                _gaugeSU3[j, i] = Gauge.SU3Matrix.Identity;
            }

            // Clear Yang-Mills component fields
            ClearGaugeFieldsOnEdge(i, j);
        }

        /// <summary>
        /// Clear all gauge fields on an edge (reset to trivial configuration).
        /// </summary>
        private void ClearGaugeFieldsOnEdge(int i, int j)
        {
            if (_edgePhaseU1 != null)
            {
                _edgePhaseU1[i, j] = 0.0;
                _edgePhaseU1[j, i] = 0.0;
            }

            if (_gaugeSU3 != null && GaugeDimension == 3)
            {
                _gaugeSU3[i, j] = Gauge.SU3Matrix.Identity;
                _gaugeSU3[j, i] = Gauge.SU3Matrix.Identity;
            }

            // Clear Yang-Mills gluon field if present
            if (_gluonField != null)
            {
                for (int a = 0; a < 8; a++)
                {
                    _gluonField[i, j, a] = 0.0;
                    _gluonField[j, i, a] = 0.0;
                }
            }
        }

        /// <summary>
        /// Trigger Gauss law enforcement after gauge-unsafe topology change.
        /// This re-projects gauge fields to satisfy div E = ?.
        /// </summary>
        private void TriggerGaussLawEnforcement()
        {
            // This will be called by the main physics loop if implemented
            // For now, mark that enforcement is needed
            _gaussLawEnforcementNeeded = true;
        }

        private bool _gaussLawEnforcementNeeded = false;

        /// <summary>
        /// Check if Gauss law enforcement is pending.
        /// </summary>
        public bool IsGaussLawEnforcementNeeded => _gaussLawEnforcementNeeded;

        /// <summary>
        /// Clear Gauss law enforcement flag after enforcement.
        /// </summary>
        public void ClearGaussLawEnforcementFlag() => _gaussLawEnforcementNeeded = false;
    }
}
