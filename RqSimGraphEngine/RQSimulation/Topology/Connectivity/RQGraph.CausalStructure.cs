using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Causality configuration
        private const double SpeedOfLight = 1.0; // Network speed of light (edges per time unit)
        private const int MaxCausalDistance = 5; // Maximum hops for causality checks
        
        // Cache for graph distances (optional optimization)
        private Dictionary<(int, int), double>? _graphDistanceCache;
        private const int CacheMaxSize = 10000;
        
        /// <summary>
        /// Compute edge length from weight (stronger edges = shorter "distance")
        /// </summary>
        public double EdgeLength(int i, int j)
        {
            if (!Edges[i, j])
                return double.PositiveInfinity;
            
            double weight = Weights[i, j];
            
            // Edge length inversely proportional to weight
            // Length = -log(weight) gives proper metric properties
            if (weight > 1e-10)
            {
                return -Math.Log(weight);
            }
            else
            {
                return 10.0; // Large but finite for very weak edges
            }
        }
        
        /// <summary>
        /// Compute shortest path distance between two nodes using weighted edges
        /// </summary>
        public double ShortestPathDistance(int i, int j)
        {
            if (i == j)
                return 0.0;
            
            if (i < 0 || i >= N || j < 0 || j >= N)
                return double.PositiveInfinity;
            
            // Check cache first
            if (_graphDistanceCache != null && _graphDistanceCache.TryGetValue((i, j), out double cached))
            {
                return cached;
            }
            
            // Dijkstra's algorithm with distance limit
            var distances = new double[N];
            var visited = new bool[N];
            var pq = new SortedSet<(double dist, int node)>();
            
            for (int k = 0; k < N; k++)
            {
                distances[k] = double.PositiveInfinity;
            }
            
            distances[i] = 0.0;
            pq.Add((0.0, i));
            
            while (pq.Count > 0)
            {
                var (dist, current) = pq.Min;
                pq.Remove(pq.Min);
                
                if (visited[current])
                    continue;
                
                visited[current] = true;
                
                // Stop if we reached target
                if (current == j)
                {
                    CacheDistance(i, j, dist);
                    return dist;
                }
                
                // Limit search depth for efficiency
                if (dist > MaxCausalDistance * 2.0)
                    break;
                
                // Update neighbors
                foreach (int neighbor in Neighbors(current))
                {
                    if (visited[neighbor])
                        continue;
                    
                    double edgeLen = EdgeLength(current, neighbor);
                    double newDist = dist + edgeLen;
                    
                    if (newDist < distances[neighbor])
                    {
                        pq.Remove((distances[neighbor], neighbor));
                        distances[neighbor] = newDist;
                        pq.Add((newDist, neighbor));
                    }
                }
            }
            
            // If not reachable within limit
            double result = distances[j];
            CacheDistance(i, j, result);
            return result;
        }
        
        /// <summary>
        /// Cache computed distance (with size limit)
        /// </summary>
        private void CacheDistance(int i, int j, double distance)
        {
            if (_graphDistanceCache == null)
            {
                _graphDistanceCache = new Dictionary<(int, int), double>();
            }
            
            if (_graphDistanceCache.Count < CacheMaxSize)
            {
                _graphDistanceCache[(i, j)] = distance;
                _graphDistanceCache[(j, i)] = distance; // Symmetric
            }
        }
        
        /// <summary>
        /// Check if two nodes are causally connected within time dt
        /// </summary>
        public bool IsCausallyConnected(int i, int j, double dt)
        {
            if (i == j)
                return true;
            
            double distance = ShortestPathDistance(i, j);
            
            // Can signal reach from i to j within time dt?
            // Signal travels at speed c, so max distance = c * dt
            return distance <= SpeedOfLight * dt;
        }
        
        /// <summary>
        /// Get all nodes in the causal future of node i within time dt
        /// </summary>
        public List<int> GetCausalFuture(int i, double dt)
        {
            var causalFuture = new List<int>();
            double maxDistance = SpeedOfLight * dt;
            
            // BFS with distance tracking
            var queue = new Queue<(int node, double dist)>();
            var visited = new HashSet<int>();
            
            queue.Enqueue((i, 0.0));
            visited.Add(i);
            
            while (queue.Count > 0)
            {
                var (current, dist) = queue.Dequeue();
                
                if (current != i)
                {
                    causalFuture.Add(current);
                }
                
                // Explore neighbors within causal range
                foreach (int neighbor in Neighbors(current))
                {
                    if (visited.Contains(neighbor))
                        continue;
                    
                    double edgeLen = EdgeLength(current, neighbor);
                    double newDist = dist + edgeLen;
                    
                    if (newDist <= maxDistance)
                    {
                        visited.Add(neighbor);
                        queue.Enqueue((neighbor, newDist));
                    }
                }
            }
            
            return causalFuture;
        }
        
        /// <summary>
        /// Get all nodes in the causal past of node i within time dt
        /// (same as future due to symmetry in undirected graph)
        /// </summary>
        public List<int> GetCausalPast(int i, double dt)
        {
            return GetCausalFuture(i, dt);
        }
        
        /// <summary>
        /// Clear the graph distance cache
        /// </summary>
        public void ClearGraphDistanceCache()
        {
            _graphDistanceCache?.Clear();
        }
        
        /// <summary>
        /// Update causal structure (to be called periodically)
        /// </summary>
        public void UpdateCausalStructureRelational()
        {
            // Recalculate edge delays based on current weights
            if (EdgeDelay == null || EdgeDelay.GetLength(0) != N)
            {
                EdgeDelay = new double[N, N];
            }
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    // Edge delay is the edge length
                    EdgeDelay[i, j] = EdgeLength(i, j);
                }
            }
            
            // Clear cache to reflect updated structure
            ClearGraphDistanceCache();
        }
        
        /// <summary>
        /// Get relational distance between two nodes.
        /// Distance = -ln(connection_strength) computed via shortest path.
        /// This implements RQ-hypothesis: distances emerge from graph correlations.
        /// </summary>
        /// <param name="nodeA">First node index</param>
        /// <param name="nodeB">Second node index</param>
        /// <returns>Relational distance based on weighted shortest path</returns>
        public double GetRelationalDistance(int nodeA, int nodeB)
        {
            // Distance = -ln(connectivity)
            // Uses Dijkstra's algorithm for shortest paths
            // w_ij - edge weight (entanglement)
            return ShortestPathDistance(nodeA, nodeB);
        }
    }
}
