using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Topological Protection: Identifies and protects topologically non-trivial structures
    /// 
    /// In RQ-hypothesis, stable particles correspond to topologically protected graph configurations.
    /// This implements:
    /// - Berry phase / Chern number computation for cluster regions
    /// - Topological invariant detection
    /// - Protected cluster decay suppression
    /// </summary>
    public partial class RQGraph
    {
        // Topological invariants cache
        private Dictionary<int, int> _clusterChernNumbers = new();
        private Dictionary<int, double> _clusterBerryPhases = new();
        
        /// <summary>
        /// Compute Berry phase for a path around a cluster boundary
        /// Berry phase = ∮ A·dl where A is the gauge connection
        /// </summary>
        /// <param name="boundaryNodes">Ordered list of boundary nodes forming a closed path</param>
        /// <returns>Berry phase in [0, 2π)</returns>
        public double ComputeBerryPhase(List<int> boundaryNodes)
        {
            if (boundaryNodes == null || boundaryNodes.Count < 3)
                return 0;
            
            if (_edgePhaseU1 == null)
                return 0;
            
            double totalPhase = 0;
            
            // Sum phases around the boundary
            for (int k = 0; k < boundaryNodes.Count; k++)
            {
                int i = boundaryNodes[k];
                int j = boundaryNodes[(k + 1) % boundaryNodes.Count];
                
                if (Edges[i, j])
                {
                    totalPhase += _edgePhaseU1[i, j];
                }
                else
                {
                    // If not directly connected, use shortest path contribution
                    // (This handles non-contiguous boundaries)
                    totalPhase += 0; // Skip disconnected segments
                }
            }
            
            // Normalize to [0, 2π)
            totalPhase = totalPhase % (2 * Math.PI);
            if (totalPhase < 0) totalPhase += 2 * Math.PI;
            
            return totalPhase;
        }
        
        /// <summary>
        /// Compute Chern number (topological charge) for a cluster region
        /// Chern number = (1/2π) ∫ F where F is the field strength
        /// For discrete graph: C = (1/2π) Σ_triangles Wilson_loop
        /// </summary>
        /// <param name="clusterNodes">Nodes in the cluster</param>
        /// <returns>Integer Chern number</returns>
        public int ComputeChernNumber(List<int> clusterNodes)
        {
            if (clusterNodes == null || clusterNodes.Count < 3)
                return 0;
            
            if (_edgePhaseU1 == null)
                return 0;
            
            double totalFlux = 0;
            var nodeSet = new HashSet<int>(clusterNodes);
            
            // Sum Wilson loops over all triangles in the cluster
            foreach (int i in clusterNodes)
            {
                foreach (int j in Neighbors(i))
                {
                    if (!nodeSet.Contains(j) || j <= i) continue;
                    
                    foreach (int k in Neighbors(i))
                    {
                        if (!nodeSet.Contains(k) || k <= j || !Edges[j, k]) continue;
                        
                        // Triangle i-j-k is inside the cluster
                        double loop = ComputeWilsonLoop(i, j, k);
                        
                        // Bring loop to (-π, π] for proper flux counting
                        while (loop > Math.PI) loop -= 2 * Math.PI;
                        while (loop <= -Math.PI) loop += 2 * Math.PI;
                        
                        totalFlux += loop;
                    }
                }
            }
            
            // Chern number = total flux / 2π, rounded to nearest integer
            double chern = totalFlux / (2 * Math.PI);
            return (int)Math.Round(chern);
        }
        
        /// <summary>
        /// Find boundary nodes of a cluster
        /// Boundary = nodes in cluster that have neighbors outside cluster
        /// </summary>
        public List<int> FindClusterBoundary(List<int> clusterNodes)
        {
            if (clusterNodes == null || clusterNodes.Count == 0)
                return new List<int>();
            
            var nodeSet = new HashSet<int>(clusterNodes);
            var boundary = new List<int>();
            
            foreach (int node in clusterNodes)
            {
                bool isBoundary = false;
                foreach (int neighbor in Neighbors(node))
                {
                    if (!nodeSet.Contains(neighbor))
                    {
                        isBoundary = true;
                        break;
                    }
                }
                
                if (isBoundary)
                    boundary.Add(node);
            }
            
            return boundary;
        }
        
        /// <summary>
        /// Check if a cluster is topologically protected (non-trivial Chern number)
        /// </summary>
        public bool IsTopologicallyProtected(List<int> clusterNodes)
        {
            int chern = ComputeChernNumber(clusterNodes);
            return chern != 0;
        }
        
        /// <summary>
        /// Compute topological decay suppression factor
        /// Protected clusters decay exponentially slower
        /// </summary>
        /// <param name="clusterNodes">Cluster to evaluate</param>
        /// <returns>Decay rate multiplier in (0, 1], where smaller = more protected</returns>
        public double ComputeTopologicalProtectionFactor(List<int> clusterNodes)
        {
            int chern = ComputeChernNumber(clusterNodes);
            
            if (chern == 0)
                return 1.0; // No protection
            
            // Exponential suppression based on |Chern number|
            // Factor = exp(-|C|) ensures topological stability
            return Math.Exp(-Math.Abs(chern));
        }
        
        /// <summary>
        /// Update all cluster topological invariants
        /// </summary>
        public void UpdateTopologicalInvariants()
        {
            _clusterChernNumbers.Clear();
            _clusterBerryPhases.Clear();
            
            var clusters = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            
            for (int clusterId = 0; clusterId < clusters.Count; clusterId++)
            {
                var cluster = clusters[clusterId];
                
                if (cluster.Count >= HeavyClusterMinSize)
                {
                    int chern = ComputeChernNumber(cluster);
                    _clusterChernNumbers[clusterId] = chern;
                    
                    var boundary = FindClusterBoundary(cluster);
                    if (boundary.Count >= 3)
                    {
                        // Order boundary for consistent Berry phase
                        var orderedBoundary = OrderBoundaryNodes(boundary);
                        double berry = ComputeBerryPhase(orderedBoundary);
                        _clusterBerryPhases[clusterId] = berry;
                    }
                }
            }
        }
        
        /// <summary>
        /// Order boundary nodes into a consistent path for Berry phase computation
        /// Uses greedy nearest-neighbor ordering
        /// </summary>
        private List<int> OrderBoundaryNodes(List<int> boundary)
        {
            if (boundary.Count <= 1)
                return boundary;
            
            var ordered = new List<int>();
            var remaining = new HashSet<int>(boundary);
            
            // Start with first boundary node
            int current = boundary[0];
            ordered.Add(current);
            remaining.Remove(current);
            
            // Greedy nearest-neighbor
            while (remaining.Count > 0)
            {
                int nearest = -1;
                double minDist = double.MaxValue;
                
                foreach (int candidate in remaining)
                {
                    // Prefer direct neighbors
                    if (Edges[current, candidate])
                    {
                        nearest = candidate;
                        break;
                    }
                    
                    // Otherwise use graph distance
                    double dist = GraphDistance(current, candidate);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = candidate;
                    }
                }
                
                if (nearest >= 0)
                {
                    ordered.Add(nearest);
                    remaining.Remove(nearest);
                    current = nearest;
                }
                else
                {
                    break; // Disconnected boundary
                }
            }
            
            return ordered;
        }
        
        /// <summary>
        /// Get Chern number for a specific cluster (by ID)
        /// </summary>
        public int GetClusterChernNumber(int clusterId)
        {
            return _clusterChernNumbers.TryGetValue(clusterId, out int chern) ? chern : 0;
        }
        
        /// <summary>
        /// Get Berry phase for a specific cluster (by ID)
        /// </summary>
        public double GetClusterBerryPhase(int clusterId)
        {
            return _clusterBerryPhases.TryGetValue(clusterId, out double phase) ? phase : 0;
        }
        
        /// <summary>
        /// Apply topological protection to cluster dynamics
        /// Protected clusters have reduced decay probability
        /// </summary>
        public void ApplyTopologicalProtection()
        {
            UpdateTopologicalInvariants();
            
            var clusters = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            
            for (int clusterId = 0; clusterId < clusters.Count; clusterId++)
            {
                var cluster = clusters[clusterId];
                
                if (cluster.Count < HeavyClusterMinSize)
                    continue;
                
                // Get protection factor
                double protection = ComputeTopologicalProtectionFactor(cluster);
                
                if (protection < PhysicsConstants.TopologicalProtectionThreshold) // Significantly protected
                {
                    // Strengthen internal correlations
                    foreach (int i in cluster)
                    {
                        foreach (int j in cluster)
                        {
                            if (i < j && Edges[i, j])
                            {
                                // Small stabilization boost for protected clusters
                                Weights[i, j] = Math.Min(Weights[i, j] + PhysicsConstants.TopologicalProtectionStrength, 1.0);
                                Weights[j, i] = Weights[i, j];
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Get topological summary for all clusters
        /// </summary>
        public List<(int ClusterId, int Size, int ChernNumber, double BerryPhase, double Protection)> 
            GetTopologicalSummary()
        {
            UpdateTopologicalInvariants();
            
            var summary = new List<(int, int, int, double, double)>();
            var clusters = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            
            for (int clusterId = 0; clusterId < clusters.Count; clusterId++)
            {
                var cluster = clusters[clusterId];
                
                if (cluster.Count >= HeavyClusterMinSize)
                {
                    int chern = GetClusterChernNumber(clusterId);
                    double berry = GetClusterBerryPhase(clusterId);
                    double protection = ComputeTopologicalProtectionFactor(cluster);
                    
                    summary.Add((clusterId, cluster.Count, chern, berry, protection));
                }
            }
            
            return summary;
        }
        
        /// <summary>
        /// Calculate winding number for a cluster region.
        /// The winding number is a topological invariant that measures how many times
        /// the phase field wraps around 2π over a closed path.
        /// Non-zero winding number indicates a stable topological defect (particle).
        /// Implements RQ-hypothesis checklist item 5.1.
        /// </summary>
        /// <param name="clusterId">Cluster ID to analyze</param>
        /// <returns>Integer winding number</returns>
        public int CalculateWindingNumber(int clusterId)
        {
            var clusters = GetStrongCorrelationClusters(AdaptiveHeavyThreshold);
            if (clusterId < 0 || clusterId >= clusters.Count)
                return 0;
            
            return CalculateWindingNumber(clusters[clusterId]);
        }
        
        /// <summary>
        /// Calculate winding number for a list of nodes forming a cluster.
        /// </summary>
        /// <param name="clusterNodes">Nodes in the cluster</param>
        /// <returns>Integer winding number</returns>
        public int CalculateWindingNumber(List<int> clusterNodes)
        {
            if (clusterNodes == null || clusterNodes.Count < 3)
                return 0;
            
            if (_edgePhaseU1 == null)
                return 0;
            
            // Find the boundary of the cluster
            var boundary = FindClusterBoundary(clusterNodes);
            if (boundary.Count < 3)
                return 0;
            
            // Order boundary nodes into a consistent path
            var orderedBoundary = OrderBoundaryNodes(boundary);
            if (orderedBoundary.Count < 3)
                return 0;
            
            // Compute total phase winding around the boundary
            double totalPhase = 0.0;
            for (int k = 0; k < orderedBoundary.Count; k++)
            {
                int i = orderedBoundary[k];
                int j = orderedBoundary[(k + 1) % orderedBoundary.Count];
                
                if (Edges[i, j])
                {
                    // Accumulate phase difference, keeping track of windings
                    double phase = _edgePhaseU1[i, j];
                    
                    // Unwrap phase jumps greater than π to avoid discontinuities
                    while (phase > Math.PI) phase -= 2 * Math.PI;
                    while (phase < -Math.PI) phase += 2 * Math.PI;
                    
                    totalPhase += phase;
                }
            }
            
            // Winding number = total_phase / 2π, rounded to nearest integer
            int windingNumber = (int)Math.Round(totalPhase / (2 * Math.PI));
            return windingNumber;
        }
        
        /// <summary>
        /// Check if a cluster is a stable particle (topological defect).
        /// A stable particle has non-zero winding number that cannot be 
        /// removed by local operations.
        /// Implements RQ-hypothesis checklist item 5.1.
        /// </summary>
        /// <param name="clusterId">Cluster ID to check</param>
        /// <returns>True if cluster is topologically protected (stable particle)</returns>
        public bool IsStableParticle(int clusterId)
        {
            // Check topological invariant (e.g., winding number)
            int windingNumber = CalculateWindingNumber(clusterId);
            return windingNumber != 0;
        }
        
        /// <summary>
        /// Check if a cluster is topologically stable using the flux integral criterion.
        /// A cluster is stable if the sum of phases around its boundary is non-zero: ∮ φ ≠ 0.
        /// Implements checklist item 5.1: Flux-based topological stability.
        /// </summary>
        /// <param name="boundaryNodes">Ordered list of boundary nodes forming a closed path</param>
        /// <returns>True if the cluster is topologically stable (non-trivial flux)</returns>
        public bool IsTopologicallyStable(List<int> boundaryNodes)
        {
            if (boundaryNodes == null || boundaryNodes.Count < 3)
                return false;
            
            if (_edgePhaseU1 == null)
                return false;
            
            // Compute flux as product of link variables around boundary
            // flux = ∏ U_ij = exp(i * Σ φ_ij)
            // For U(1), this is equivalent to exp(i * ∮ φ)
            System.Numerics.Complex flux = System.Numerics.Complex.One;
            
            for (int k = 0; k < boundaryNodes.Count; k++)
            {
                int u = boundaryNodes[k];
                int v = boundaryNodes[(k + 1) % boundaryNodes.Count];
                
                // Get link variable U_uv = exp(i * φ_uv)
                flux *= GetLinkVariable(u, v);
            }
            
            // If flux phase differs from 0 (flux != 1), there's a charge/vortex inside
            // Use configurable threshold to account for numerical noise
            return Math.Abs(flux.Phase) > PhysicsConstants.FluxPhaseThreshold;
        }
        
        /// <summary>
        /// Check if a cluster is topologically stable by finding its boundary
        /// and computing the flux integral.
        /// </summary>
        /// <param name="clusterNodes">Nodes in the cluster</param>
        /// <returns>True if the cluster is topologically stable</returns>
        public bool IsTopologicallyStableCluster(List<int> clusterNodes)
        {
            if (clusterNodes == null || clusterNodes.Count < 3)
                return false;
            
            // Find the boundary of the cluster
            var boundary = FindClusterBoundary(clusterNodes);
            if (boundary.Count < 3)
                return false;
            
            // Order boundary nodes for consistent flux calculation
            var orderedBoundary = OrderBoundaryNodes(boundary);
            
            // Check flux-based stability
            return IsTopologicallyStable(orderedBoundary);
        }
        
        /// <summary>
        /// Compute Betti number b_1 for a cluster (number of independent cycles/holes).
        /// For a connected graph: b_1 = |E| - |V| + 1 (Euler characteristic).
        /// Non-zero b_1 indicates topological protection (cycles cannot be contracted).
        /// Implements checklist item 4.2: Topological protection via Betti numbers.
        /// </summary>
        /// <param name="clusterNodes">Nodes in the cluster</param>
        /// <returns>First Betti number (number of independent cycles)</returns>
        public int ComputeBettiNumber(List<int> clusterNodes)
        {
            if (clusterNodes == null || clusterNodes.Count < 2)
                return 0;
            
            var clusterSet = new HashSet<int>(clusterNodes);
            
            // Count vertices and edges within cluster
            int V = clusterNodes.Count;
            int E = 0;
            
            foreach (int i in clusterNodes)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i && clusterSet.Contains(j))
                    {
                        E++;
                    }
                }
            }
            
            // Count connected components within cluster using BFS
            int C = 0;
            var visited = new HashSet<int>();
            
            foreach (int start in clusterNodes)
            {
                if (visited.Contains(start))
                    continue;
                
                // BFS for this component
                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited.Add(start);
                
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    foreach (int neighbor in Neighbors(current))
                    {
                        if (clusterSet.Contains(neighbor) && !visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                
                C++;
            }
            
            // Euler characteristic: χ = V - E + F (for 2D)
            // For graph (1D simplicial complex): χ = V - E
            // First Betti number: b_1 = E - V + C (number of independent cycles)
            // Note: For connected graph (C=1), b_1 = E - V + 1
            int bettiNumber = E - V + C;
            
            return Math.Max(0, bettiNumber);
        }
        
        /// <summary>
        /// Check if a cluster is topologically stable (has non-trivial Betti number).
        /// A cluster with b_1 > 0 contains at least one cycle that cannot be
        /// contracted through local operations, making it topologically protected.
        /// Implements checklist item 4.2: Topological stability criterion.
        /// </summary>
        /// <param name="clusterNodes">Nodes in the cluster</param>
        /// <returns>True if cluster is topologically stable (has cycles)</returns>
        public bool IsStable(List<int> clusterNodes)
        {
            int bettiNumber = ComputeBettiNumber(clusterNodes);
            // Cluster is stable if it contains at least one cycle
            return bettiNumber > 0;
        }
    }
}
