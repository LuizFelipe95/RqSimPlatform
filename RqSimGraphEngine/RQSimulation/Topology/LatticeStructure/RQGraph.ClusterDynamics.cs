using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Cluster state tracking
        private List<ClusterState> _clusters = new();
        public IReadOnlyList<ClusterState> Clusters => _clusters;
        
        // Configuration constants for cluster dynamics (extracted from magic numbers)
        private const double ClusterMomentumScale = 0.01; // Scale for momentum accumulation
        private const double DefaultStrongThresholdAlpha = 1.5; // Standard deviations above mean for strong edges
        
        /// <summary>
        /// Compute dynamic mass of a cluster including field energy contribution
        /// </summary>
        public double ComputeClusterMass(ClusterState cl)
        {
            if (cl == null || cl.NodeIds.Count == 0) return 0.0;
            
            // Rest mass from topology (already computed)
            double restMass = cl.RestMass;
            
            // Add field energy contribution if available
            double fieldEnergy = 0.0;
            if (LocalPotential != null)
            {
                foreach (int nodeId in cl.NodeIds)
                {
                    if (nodeId >= 0 && nodeId < N)
                    {
                        fieldEnergy += LocalPotential[nodeId];
                    }
                }
                fieldEnergy /= Math.Max(1, cl.NodeIds.Count); // Average field energy
            }
            
            // Dynamic mass = rest mass + field contribution
            return restMass + fieldEnergy * 0.1; // Field contribution scaled
        }
        
        /// <summary>
        /// Update cluster states including momentum tracking
        /// </summary>
        public void UpdateClusterStates()
        {
            var adaptiveThreshold = GetAdaptiveHeavyThreshold();
            var strongClusters = GetStrongCorrelationClusters(adaptiveThreshold);
            
            // Match existing clusters with new detection
            var newClusters = new List<ClusterState>();
            
            for (int i = 0; i < strongClusters.Count; i++)
            {
                var nodeList = strongClusters[i];
                if (nodeList.Count < HeavyClusterMinSize) continue;
                
                // Compute center of mass in spectral coordinates
                Vector3 newCenter = ComputeClusterCenter(nodeList);
                
                // Find matching existing cluster
                ClusterState? existing = FindMatchingCluster(nodeList);
                
                if (existing != null)
                {
                    // Update momentum based on center movement
                    Vector3 centerDelta = newCenter - existing.CenterOfMass;
                    existing.Momentum += centerDelta * (float)ClusterMomentumScale;
                    existing.CenterOfMass = newCenter;
                    existing.NodeIds = nodeList;
                    
                    // Update rest mass
                    existing.RestMass = ComputeRestMassOfCluster(nodeList);
                    
                    newClusters.Add(existing);
                }
                else
                {
                    // New cluster
                    var cl = new ClusterState
                    {
                        Id = i,
                        NodeIds = nodeList,
                        CenterOfMass = newCenter,
                        Momentum = Vector3.Zero,
                        RestMass = ComputeRestMassOfCluster(nodeList)
                    };
                    
                    // Compute fuzzy membership
                    ComputeClusterMembership(cl);
                    
                    newClusters.Add(cl);
                }
            }
            
            _clusters = newClusters;
            
            // Apply momentum friction to prevent unbounded motion
            foreach (var cluster in _clusters)
            {
                cluster.Momentum *= 0.99f; // Damping factor
            }
        }
        
        /// <summary>
        /// Compute center of mass in spectral coordinates.
        /// 
        /// RQ-HYPOTHESIS FIX: Removed fallback to external Coordinates.
        /// If spectral coordinates are unavailable, the cluster has no well-defined center of mass
        /// in the relational sense (it is "delocalized" in graph space).
        /// 
        /// Physics: External coordinates violate background independence.
        /// Only spectral coordinates (Laplacian eigenvectors) are RQ-compliant.
        /// </summary>
        private Vector3 ComputeClusterCenter(List<int> nodeIds)
        {
            // RQ-FIX: If spectral coordinates unavailable, cluster center is undefined (delocalized)
            // Do NOT fall back to external Coordinates - this violates RQ-hypothesis
            if (_spectralX == null || _spectralY == null || _spectralZ == null)
            {
                // Cluster is delocalized without spectral embedding
                // Return zero vector to indicate undefined center of mass
                // This is physically correct: without emergent geometry, there is no "position"
                return Vector3.Zero;
            }
            
            // Use spectral coordinates (RQ-compliant: derived from graph Laplacian)
            double sx = 0, sy = 0, sz = 0;
            double totalMass = 0;
            
            foreach (int id in nodeIds)
            {
                if (id >= 0 && id < N)
                {
                    double mass = _correlationMass != null && id < _correlationMass.Length 
                        ? _correlationMass[id] : 1.0;
                    sx += _spectralX[id] * mass;
                    sy += _spectralY[id] * mass;
                    sz += _spectralZ[id] * mass;
                    totalMass += mass;
                }
            }
            
            if (totalMass > 0)
            {
                return new Vector3((float)(sx / totalMass), (float)(sy / totalMass), (float)(sz / totalMass));
            }
            return Vector3.Zero;
        }
        
        /// <summary>
        /// Find existing cluster that matches node list (by overlap)
        /// </summary>
        private ClusterState? FindMatchingCluster(List<int> nodeIds)
        {
            var nodeSet = new HashSet<int>(nodeIds);
            ClusterState? bestMatch = null;
            double bestOverlap = 0;
            
            foreach (var cl in _clusters)
            {
                var existingSet = new HashSet<int>(cl.NodeIds);
                int intersection = nodeSet.Intersect(existingSet).Count();
                int union = nodeSet.Union(existingSet).Count();
                double overlap = union > 0 ? (double)intersection / union : 0;
                
                if (overlap > bestOverlap && overlap > 0.5) // At least 50% overlap
                {
                    bestOverlap = overlap;
                    bestMatch = cl;
                }
            }
            
            return bestMatch;
        }
        
        /// <summary>
        /// Compute fuzzy membership values for cluster nodes
        /// </summary>
        private void ComputeClusterMembership(ClusterState cl)
        {
            cl.Membership.Clear();
            
            // Core nodes get full membership
            var coreNodes = new HashSet<int>();
            
            // Find nodes with strongest internal connections
            foreach (int i in cl.NodeIds)
            {
                double internalStrength = 0;
                int internalCount = 0;
                
                foreach (int j in cl.NodeIds)
                {
                    if (i != j && Edges[i, j])
                    {
                        internalStrength += Weights[i, j];
                        internalCount++;
                    }
                }
                
                double avgInternal = internalCount > 0 ? internalStrength / internalCount : 0;
                
                // If strongly connected internally, it's a core node
                if (avgInternal > GetAdaptiveHeavyThreshold())
                {
                    coreNodes.Add(i);
                    cl.Membership[i] = 1.0;
                }
            }
            
            // Boundary nodes get partial membership
            foreach (int i in cl.NodeIds)
            {
                if (!coreNodes.Contains(i))
                {
                    double internalStrength = 0;
                    double externalStrength = 0;
                    
                    foreach (int j in Neighbors(i))
                    {
                        if (cl.NodeIds.Contains(j))
                        {
                            internalStrength += Weights[i, j];
                        }
                        else
                        {
                            externalStrength += Weights[i, j];
                        }
                    }
                    
                    double total = internalStrength + externalStrength;
                    double membership = total > 0 ? internalStrength / total : 0.5;
                    cl.Membership[i] = Math.Clamp(membership, 0.0, 1.0);
                }
            }
        }
        
        // Note: UpdateAdaptiveHeavyThreshold() is defined in CoreHelpers.cs using mean + sigma formula
    }
}
