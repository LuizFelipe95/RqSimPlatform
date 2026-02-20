using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Configuration for energy-based cluster stability
        private const double ClusterStabilizationTemperature = 0.1;
        private const int MetropolisTrialsPerCluster = 5;
        
        /// <summary>
        /// Stabilize clusters through energy minimization instead of manual strengthening
        /// This replaces StrengthenCompositeBag with physics-based approach
        /// </summary>
        public void StabilizeClustersEnergyBased()
        {
            double threshold = GetAdaptiveHeavyThreshold();
            var clusters = GetStrongCorrelationClusters(threshold);
            
            foreach (var cluster in clusters)
            {
                if (cluster.Count < HeavyClusterMinSize)
                    continue;
                
                // Try to minimize energy of cluster through local adjustments
                StabilizeClusterViaMetropolis(cluster);
                
                // Update physics properties
                if (PhysicsProperties != null && PhysicsProperties.Length == N)
                {
                    foreach (int node in cluster)
                    {
                        PhysicsProperties[node].Type = ParticleType.Composite;
                    }
                }
            }
        }
        
        /// <summary>
        /// Use Metropolis algorithm to find stable cluster configuration.
        /// Uses fast local energy computation to avoid O(N?) complexity per trial.
        /// </summary>
        private void StabilizeClusterViaMetropolis(List<int> cluster)
        {
            if (cluster.Count < 2)
                return;

            // Use cluster-local energy for fast computation
            double energyBefore = ComputeClusterLocalEnergy(cluster);
            
            // Try several edge weight adjustments
            for (int trial = 0; trial < MetropolisTrialsPerCluster; trial++)
            {
                // Pick a random internal edge
                int i = cluster[_rng.Next(cluster.Count)];
                int j = cluster[_rng.Next(cluster.Count)];
                
                if (i == j || !Edges[i, j])
                    continue;
                
                // Store old weight
                double oldWeight = Weights[i, j];
                
                // Try small increase (favor clustering)
                double newWeight = Math.Min(1.0, oldWeight + 0.05);
                Weights[i, j] = newWeight;
                Weights[j, i] = newWeight;
                
                // Compute new energy using fast local method
                double energyAfter = ComputeClusterLocalEnergy(cluster);
                
                // Metropolis acceptance
                bool accept = AcceptTopologyChange(energyBefore, energyAfter, ClusterStabilizationTemperature);
                
                if (accept)
                {
                    energyBefore = energyAfter;
                }
                else
                {
                    // Revert
                    Weights[i, j] = oldWeight;
                    Weights[j, i] = oldWeight;
                }
            }
            
            // Weaken external connections slightly to favor cluster cohesion
            WeakenExternalConnectionsViaDiffusion(cluster);
        }

        /// <summary>
        /// Fast local energy computation for a cluster.
        /// Avoids expensive global field computations (Yang-Mills, etc.)
        /// </summary>
        private double ComputeClusterLocalEnergy(List<int> cluster)
        {
            var clusterSet = new HashSet<int>(cluster);
            double energy = 0.0;
            
            // Sum energy over internal edges
            foreach (int i in cluster)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue; // Avoid double counting
                    
                    double w = Weights[i, j];
                    bool isInternal = clusterSet.Contains(j);
                    
                    // Internal edges have lower energy (favor clustering)
                    if (isInternal)
                    {
                        // Binding energy: stronger weight = lower energy
                        energy -= w * w;
                    }
                    else
                    {
                        // External edges contribute positive energy
                        energy += 0.5 * w * w;
                    }
                    
                    // Add curvature contribution (fast to compute)
                    double curvature = CalculateGraphCurvature(i, j);
                    energy += 0.05 * curvature * curvature;
                }
            }
            
            return energy;
        }
        
        /// <summary>
        /// Public wrapper for StabilizeClusterViaMetropolis
        /// RQ-compliant cluster stabilization through energy minimization
        /// </summary>
        public void StabilizeClusterViaMetropolisPublic(List<int> cluster)
        {
            StabilizeClusterViaMetropolis(cluster);
        }
        
        /// <summary>
        /// Gradually weaken external connections through diffusion process.
        /// Uses fast local energy computation instead of full unified energy
        /// to avoid O(N?) Yang-Mills computation per edge.
        /// </summary>
        private void WeakenExternalConnectionsViaDiffusion(List<int> cluster)
        {
            var clusterSet = new HashSet<int>(cluster);
            
            foreach (int i in cluster)
            {
                foreach (int k in Neighbors(i))
                {
                    if (clusterSet.Contains(k))
                        continue;
                    
                    // External edge - apply small decay
                    double oldWeight = Weights[i, k];
                    double newWeight = oldWeight * 0.98; // Small decay factor
                    
                    // Use fast local energy approximation instead of full unified energy
                    // This avoids the expensive O(N?) Yang-Mills field strength computation
                    double energyBefore = ComputeLocalEdgeEnergy(i, k, oldWeight);
                    Weights[i, k] = newWeight;
                    Weights[k, i] = newWeight;
                    double energyAfter = ComputeLocalEdgeEnergy(i, k, newWeight);
                    
                    if (energyAfter > energyBefore)
                    {
                        // Revert if energy increased
                        Weights[i, k] = oldWeight;
                        Weights[k, i] = oldWeight;
                    }
                }
            }
        }

        /// <summary>
        /// Fast local energy computation for a single edge.
        /// Used for Metropolis acceptance without expensive global field computations.
        /// </summary>
        private double ComputeLocalEdgeEnergy(int i, int j, double weight)
        {
            // Simple energy model: E = -w * ln(w) + (1-w) * ln(1-w) (entropy-like)
            // Plus gradient penalty from neighboring weights
            double epsilon = 1e-10;
            double w = Math.Clamp(weight, epsilon, 1.0 - epsilon);
            double entropyTerm = -w * Math.Log(w) - (1 - w) * Math.Log(1 - w);
            
            // Local curvature contribution (fast to compute)
            double curvature = CalculateGraphCurvature(i, j);
            double curvatureTerm = 0.1 * curvature * curvature;
            
            // Gradient penalty (difference from neighbor weights)
            double gradientPenalty = 0.0;
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                double diff = weight - Weights[i, k];
                gradientPenalty += diff * diff;
            }
            foreach (int k in Neighbors(j))
            {
                if (k == i) continue;
                double diff = weight - Weights[j, k];
                gradientPenalty += diff * diff;
            }
            
            return entropyTerm + curvatureTerm + 0.01 * gradientPenalty;
        }
        
        /// <summary>
        /// Break up over-correlated clusters through excitation (energy-based)
        /// This replaces BreakHeavyCluster with physics-based approach
        /// </summary>
        public void ExciteOvercorrelatedClusters(double excitationThreshold = 0.8)
        {
            double threshold = GetAdaptiveHeavyThreshold();
            var clusters = GetStrongCorrelationClusters(threshold);
            
            foreach (var cluster in clusters)
            {
                if (cluster.Count < HeavyClusterMinSize)
                    continue;
                
                // Compute average correlation in cluster
                double avgCorrelation = ComputeClusterAverageCorrelation(cluster);
                
                // If too highly correlated, excite nodes to break it up
                if (avgCorrelation > excitationThreshold)
                {
                    ExciteClusterNodes(cluster);
                }
            }
        }
        
        /// <summary>
        /// Compute average correlation (weight) within cluster
        /// </summary>
        private double ComputeClusterAverageCorrelation(List<int> cluster)
        {
            double totalWeight = 0;
            int edgeCount = 0;
            
            for (int a = 0; a < cluster.Count; a++)
            {
                for (int b = a + 1; b < cluster.Count; b++)
                {
                    int i = cluster[a];
                    int j = cluster[b];
                    
                    if (Edges[i, j])
                    {
                        totalWeight += Weights[i, j];
                        edgeCount++;
                    }
                }
            }
            
            return edgeCount > 0 ? totalWeight / edgeCount : 0;
        }
        
        /// <summary>
        /// Excite nodes in cluster to reduce correlation
        /// </summary>
        private void ExciteClusterNodes(List<int> cluster)
        {
            foreach (int i in cluster)
            {
                // Excite the node
                State[i] = NodeState.Excited;
                
                // Add energy to local potential
                if (LocalPotential != null && i < LocalPotential.Length)
                {
                    LocalPotential[i] += 0.2;
                }
                
                // Synchronize proper time if available
                if (ProperTime != null && i < ProperTime.Length)
                {
                    // Set proper time to maximum in cluster
                    foreach (int j in cluster)
                    {
                        if (j < ProperTime.Length)
                        {
                            ProperTime[i] = Math.Max(ProperTime[i], ProperTime[j]);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Replace old Step() method's manual manipulation with energy-based approach
        /// </summary>
        public void StepEnergyBased()
        {
            // Update adaptive threshold based on current weights
            UpdateAdaptiveHeavyThreshold();
            
            // Stabilize clusters through energy minimization
            StabilizeClustersEnergyBased();
            
            // Check for overcorrelated clusters and excite them
            ExciteOvercorrelatedClusters();
            
            // Update cluster states with momentum
            UpdateClusterStates();
            
            // Update condensed nodes set for visualization
            _condensedNodes.Clear();
            foreach (var cl in _clusters)
            {
                if (cl.NodeIds.Count >= HeavyClusterMinSize)
                {
                    foreach (int node in cl.NodeIds)
                    {
                        _condensedNodes.Add(node);
                    }
                }
            }
        }
    }
}
