using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Spectral Relational Helpers: Physics operations using only graph structure
    /// NO external coordinates - all distances/positions from spectral embedding
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Compute center of mass using spectral coordinates (graph-based)
        /// Replaces external coordinate-based COM
        /// </summary>
        public (double X, double Y, double Z) ComputeSpectralCenterOfMass(List<int> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return (0, 0, 0);

            // Ensure spectral coordinates are computed
            if (_spectralX == null || _spectralX.Length != N)
            {
                UpdateSpectralCoordinates();
            }

            double cx = 0, cy = 0, cz = 0;
            int count = 0;

            foreach (int i in nodes)
            {
                if (i < 0 || i >= N) continue;

                cx += _spectralX![i];
                cy += _spectralY != null && i < _spectralY.Length ? _spectralY[i] : 0;
                cz += _spectralZ != null && i < _spectralZ.Length ? _spectralZ[i] : 0;
                count++;
            }

            if (count == 0) return (0, 0, 0);

            return (cx / count, cy / count, cz / count);
        }

        /// <summary>
        /// Compute mass-weighted center of mass using spectral coordinates (graph-based)
        /// This is the RQ-compliant replacement for external coordinate-based COM
        /// </summary>
        public (double X, double Y, double Z) ComputeSpectralCenterOfMassWeighted(List<int> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return (0, 0, 0);

            // Ensure spectral coordinates are computed
            if (_spectralX == null || _spectralX.Length != N)
            {
                UpdateSpectralCoordinates();
            }

            double cx = 0, cy = 0, cz = 0;
            double totalMass = 0;

            foreach (int i in nodes)
            {
                if (i < 0 || i >= N) continue;

                double mass = _correlationMass != null && i < _correlationMass.Length 
                    ? _correlationMass[i] : PhysicsConstants.DefaultNodeMass;
                
                cx += _spectralX![i] * mass;
                cy += (_spectralY != null && i < _spectralY.Length ? _spectralY[i] : 0) * mass;
                cz += (_spectralZ != null && i < _spectralZ.Length ? _spectralZ[i] : 0) * mass;
                totalMass += mass;
            }

            if (totalMass < 1e-10) return (0, 0, 0);

            return (cx / totalMass, cy / totalMass, cz / totalMass);
        }

        /// <summary>
        /// Compute spectral radius of a cluster
        /// Measures spread in spectral space
        /// </summary>
        public double ComputeSpectralRadius(List<int> nodes)
        {
            if (nodes == null || nodes.Count <= 1)
                return 0;

            var (cx, cy, cz) = ComputeSpectralCenterOfMass(nodes);

            double maxDist = 0;
            foreach (int i in nodes)
            {
                if (i < 0 || i >= N) continue;

                double dx = _spectralX![i] - cx;
                double dy = (_spectralY != null && i < _spectralY.Length ? _spectralY[i] : 0) - cy;
                double dz = (_spectralZ != null && i < _spectralZ.Length ? _spectralZ[i] : 0) - cz;

                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                maxDist = Math.Max(maxDist, dist);
            }

            return maxDist;
        }

        /// <summary>
        /// Compute graph-based distance (primary method for all distance calculations)
        /// Uses shortest path when available, otherwise -log(weight)
        /// </summary>
        public double GraphDistance(int i, int j)
        {
            if (i < 0 || i >= N || j < 0 || j >= N)
                return double.PositiveInfinity;

            if (i == j) return 0;

            // If directly connected, use edge length
            if (Edges[i, j])
            {
                return EdgeLength(i, j);
            }

            // Otherwise use shortest path
            return ShortestPathDistance(i, j);
        }

        /// <summary>
        /// Compute graph-based velocity between two clusters
        /// Based on change in graph distance over time
        /// </summary>
        public double ComputeRelativeVelocity(List<int> cluster1, List<int> cluster2, double dt)
        {
            if (dt <= 0) return 0;

            // Current distance (shortest path between closest nodes)
            double minDist = double.PositiveInfinity;
            foreach (int i in cluster1)
            {
                foreach (int j in cluster2)
                {
                    double dist = GraphDistance(i, j);
                    minDist = Math.Min(minDist, dist);
                }
            }

            // Velocity would require tracking distance over time
            // For now, return 0 (would need historical tracking)
            return 0;
        }

        /// <summary>
        /// Check if two clusters overlap in graph space
        /// </summary>
        public bool ClustersOverlap(List<int> cluster1, List<int> cluster2)
        {
            if (cluster1 == null || cluster2 == null) return false;

            var set1 = new HashSet<int>(cluster1);
            return cluster2.Any(node => set1.Contains(node));
        }

        /// <summary>
        /// Compute graph-based cluster separation
        /// Minimum graph distance between any two nodes in different clusters
        /// </summary>
        public double ClusterSeparation(List<int> cluster1, List<int> cluster2)
        {
            if (cluster1 == null || cluster2 == null || cluster1.Count == 0 || cluster2.Count == 0)
                return double.PositiveInfinity;

            double minDist = double.PositiveInfinity;

            // Sample random pairs if clusters are large
            int maxSamples = 100;
            if (cluster1.Count * cluster2.Count > maxSamples)
            {
                for (int k = 0; k < maxSamples; k++)
                {
                    int i = cluster1[_rng.Next(cluster1.Count)];
                    int j = cluster2[_rng.Next(cluster2.Count)];
                    double dist = GraphDistance(i, j);
                    minDist = Math.Min(minDist, dist);
                }
            }
            else
            {
                // Check all pairs for small clusters
                foreach (int i in cluster1)
                {
                    foreach (int j in cluster2)
                    {
                        double dist = GraphDistance(i, j);
                        minDist = Math.Min(minDist, dist);
                    }
                }
            }

            return minDist;
        }

        /// <summary>
        /// Find nearest neighbor to a node using graph distance
        /// </summary>
        public int FindNearestNeighbor(int node, IEnumerable<int> candidates)
        {
            int nearest = -1;
            double minDist = double.PositiveInfinity;

            foreach (int candidate in candidates)
            {
                if (candidate == node) continue;

                double dist = GraphDistance(node, candidate);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Compute spectral density at a point
        /// Number of nodes within spectral radius r
        /// </summary>
        public int ComputeSpectralDensity(int centerNode, double radius)
        {
            if (centerNode < 0 || centerNode >= N) return 0;
            if (_spectralX == null || _spectralX.Length != N) return 0;

            double cx = _spectralX[centerNode];
            double cy = _spectralY != null && centerNode < _spectralY.Length ? _spectralY[centerNode] : 0;
            double cz = _spectralZ != null && centerNode < _spectralZ.Length ? _spectralZ[centerNode] : 0;

            int count = 0;
            for (int i = 0; i < N; i++)
            {
                double dx = _spectralX[i] - cx;
                double dy = (_spectralY != null && i < _spectralY.Length ? _spectralY[i] : 0) - cy;
                double dz = (_spectralZ != null && i < _spectralZ.Length ? _spectralZ[i] : 0) - cz;

                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist <= radius) count++;
            }

            return count;
        }

        /// <summary>
        /// Get nodes within graph distance d of center node
        /// Returns the d-hop neighborhood
        /// </summary>
        public HashSet<int> GetGraphNeighborhood(int centerNode, int hops)
        {
            var neighborhood = new HashSet<int>();
            if (centerNode < 0 || centerNode >= N) return neighborhood;

            var queue = new Queue<(int node, int depth)>();
            queue.Enqueue((centerNode, 0));
            neighborhood.Add(centerNode);

            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (depth >= hops) continue;

                foreach (int neighbor in Neighbors(node))
                {
                    if (neighborhood.Add(neighbor))
                    {
                        queue.Enqueue((neighbor, depth + 1));
                    }
                }
            }

            return neighborhood;
        }

        /// <summary>
        /// Compute graph Laplacian distance between two nodes
        /// Uses the graph metric without external coordinates
        /// </summary>
        public double ComputeLaplacianDistance(int i, int j)
        {
            if (i < 0 || i >= N || j < 0 || j >= N) return double.PositiveInfinity;
            if (i == j) return 0;

            // Use spectral coordinates if available
            if (_spectralX != null && i < _spectralX.Length && j < _spectralX.Length)
            {
                double dx = _spectralX[i] - _spectralX[j];
                double dy = _spectralY != null && i < _spectralY.Length && j < _spectralY.Length
                    ? _spectralY[i] - _spectralY[j] : 0;
                double dz = _spectralZ != null && i < _spectralZ.Length && j < _spectralZ.Length
                    ? _spectralZ[i] - _spectralZ[j] : 0;

                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            // Fallback to graph distance
            return GraphDistance(i, j);
        }
    }
}
