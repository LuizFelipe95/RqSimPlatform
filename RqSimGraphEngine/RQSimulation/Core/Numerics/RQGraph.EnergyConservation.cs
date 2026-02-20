using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Configuration for energy conservation
        private const double C2_EnergyMassConversion = 1.0; // E = mc^2 constant
        private const double RadiationDistributionRadius = 3; // Hops for radiation distribution
        private const double EnergyConservationTolerance = 0.01; // Tolerance for energy checks
        
        // Energy tracking
        private double _lastTotalEnergy = 0.0;
        private List<string> _energyViolationLog = new();
        
        /// <summary>
        /// Handle cluster merger with energy conservation
        /// </summary>
        public void MergeClustersWithEnergyConservation(ClusterState cl1, ClusterState cl2)
        {
            // Compute masses before merge
            double M_before = ComputeClusterMass(cl1) + ComputeClusterMass(cl2);
            
            // Create merged cluster
            var mergedNodes = cl1.NodeIds.Union(cl2.NodeIds).ToList();
            var mergedCluster = new ClusterState
            {
                Id = cl1.Id,
                NodeIds = mergedNodes,
                CenterOfMass = (cl1.CenterOfMass + cl2.CenterOfMass) * 0.5f,
                Momentum = cl1.Momentum + cl2.Momentum,
                RestMass = ComputeRestMassOfCluster(mergedNodes)
            };
            
            // Compute mass after merge
            double M_after = ComputeClusterMass(mergedCluster);
            
            // If mass decreased, emit radiation
            if (M_before > M_after)
            {
                double deltaM = M_before - M_after;
                double E_radiation = deltaM * C2_EnergyMassConversion;
                
                DistributeRadiation(mergedCluster, E_radiation);
            }
            
            // Update cluster list
            _clusters.RemoveAll(c => c.Id == cl1.Id || c.Id == cl2.Id);
            _clusters.Add(mergedCluster);
        }
        
        /// <summary>
        /// Handle cluster decay with energy conservation
        /// </summary>
        public List<ClusterState> DecayClusterWithEnergyConservation(ClusterState cluster)
        {
            // Mass before decay
            double M_before = ComputeClusterMass(cluster);
            
            // Split cluster into fragments (simplified: split by connectivity)
            var fragments = SplitClusterIntoFragments(cluster);
            
            // Compute total mass of fragments
            double M_after = 0;
            foreach (var fragment in fragments)
            {
                M_after += ComputeClusterMass(fragment);
            }
            
            // Energy conservation: missing mass becomes field excitation
            if (M_before > M_after)
            {
                double deltaM = M_before - M_after;
                double E_excitation = deltaM * C2_EnergyMassConversion;
                
                // Distribute excitation energy among fragment nodes
                int totalNodes = fragments.Sum(f => f.NodeIds.Count);
                double energyPerNode = totalNodes > 0 ? E_excitation / totalNodes : 0;
                
                foreach (var fragment in fragments)
                {
                    foreach (int nodeId in fragment.NodeIds)
                    {
                        if (LocalPotential != null && nodeId < LocalPotential.Length)
                        {
                            LocalPotential[nodeId] += energyPerNode;
                        }
                    }
                }
            }
            
            return fragments;
        }
        
        /// <summary>
        /// Distribute radiation energy to nodes around cluster
        /// </summary>
        private void DistributeRadiation(ClusterState cluster, double totalEnergy)
        {
            if (totalEnergy <= 0 || LocalPotential == null)
                return;
            
            // Find nodes in neighborhood of cluster
            var neighborhood = new HashSet<int>();
            var frontier = new Queue<(int node, int depth)>();
            
            foreach (int nodeId in cluster.NodeIds)
            {
                frontier.Enqueue((nodeId, 0));
                neighborhood.Add(nodeId);
            }
            
            // BFS to find neighborhood
            while (frontier.Count > 0)
            {
                var (node, depth) = frontier.Dequeue();
                
                if (depth >= RadiationDistributionRadius)
                    continue;
                
                foreach (int neighbor in Neighbors(node))
                {
                    if (!neighborhood.Contains(neighbor))
                    {
                        neighborhood.Add(neighbor);
                        frontier.Enqueue((neighbor, depth + 1));
                    }
                }
            }
            
            // Distribute energy uniformly to neighborhood
            double energyPerNode = totalEnergy / Math.Max(1, neighborhood.Count);
            
            foreach (int nodeId in neighborhood)
            {
                if (nodeId >= 0 && nodeId < N)
                {
                    LocalPotential[nodeId] += energyPerNode;
                }
            }
        }
        
        /// <summary>
        /// Split cluster into connected fragments
        /// </summary>
        private List<ClusterState> SplitClusterIntoFragments(ClusterState cluster)
        {
            var fragments = new List<ClusterState>();
            var unvisited = new HashSet<int>(cluster.NodeIds);
            int fragmentId = 0;
            
            while (unvisited.Count > 0)
            {
                int seed = unvisited.First();
                var fragment = new List<int>();
                var queue = new Queue<int>();
                
                queue.Enqueue(seed);
                unvisited.Remove(seed);
                
                // BFS to find connected component within cluster
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    fragment.Add(node);
                    
                    foreach (int neighbor in Neighbors(node))
                    {
                        if (unvisited.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                            unvisited.Remove(neighbor);
                        }
                    }
                }
                
                if (fragment.Count >= HeavyClusterMinSize)
                {
                    var fragmentCluster = new ClusterState
                    {
                        Id = fragmentId++,
                        NodeIds = fragment,
                        CenterOfMass = ComputeClusterCenter(fragment),
                        Momentum = cluster.Momentum * (fragment.Count / (float)cluster.NodeIds.Count),
                        RestMass = ComputeRestMassOfCluster(fragment)
                    };
                    
                    fragments.Add(fragmentCluster);
                }
            }
            
            return fragments;
        }
        
        /// <summary>
        /// Check energy conservation and log violations
        /// </summary>
        public void CheckEnergyConservation(string eventDescription)
        {
            double currentEnergy = ComputeTotalEnergyUnified();
            
            if (_lastTotalEnergy > 0)
            {
                double deltaE = Math.Abs(currentEnergy - _lastTotalEnergy);
                double relativeChange = deltaE / Math.Max(1e-10, _lastTotalEnergy);
                
                if (relativeChange > EnergyConservationTolerance)
                {
                    string violation = $"Energy violation at {eventDescription}: " +
                                     $"ΔE = {deltaE:F6}, relative = {relativeChange:F6}";
                    _energyViolationLog.Add(violation);
                    
                    // Only log first 10 violations to avoid spam
                    if (_energyViolationLog.Count <= 10)
                    {
                        //Console.WriteLine($"[WARNING] {violation}");
                    }
                }
            }
            
            _lastTotalEnergy = currentEnergy;
        }
        
        /// <summary>
        /// Get energy violation log
        /// </summary>
        public IReadOnlyList<string> GetEnergyViolationLog() => _energyViolationLog;
    }
}
