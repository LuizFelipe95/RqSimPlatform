using System;
using System.Diagnostics;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Spectral Dimension Stabilization for RQ-Hypothesis Compliance.
    /// 
    /// PHYSICS PRINCIPLE: Volume Constraint (CDT-style)
    /// =================================================
    /// In Causal Dynamical Triangulations (CDT), a constraint on the total
    /// spacetime volume is imposed to prevent the universe from either
    /// collapsing to a point or expanding to infinity.
    /// 
    /// On a graph, "volume" can be represented by:
    /// - Total number of edges N_E
    /// - Total edge weight W_total = ? w_ij
    /// - Effective volume V_eff = ? w_ij^d where d is a dimension parameter
    /// 
    /// STABILIZATION MECHANISM:
    /// S_vol = ? ? (V - V_target)?
    /// 
    /// This soft constraint:
    /// - Prevents graph evaporation (too few edges ? d_S ? 0)
    /// - Prevents percolation (too many edges ? giant cluster)
    /// - Allows fluctuations around target volume
    /// 
    /// TARGET: Achieve d_S ? 4 (4D spacetime emergence)
    /// </summary>
    public partial class RQGraph
    {
        // Volume stabilization parameters
        private double _volumeLambda = 0.01; // Soft constraint strength
        private double _targetEdgeCount = 0.0; // Set from initial graph
        private double _targetTotalWeight = 0.0;
        private bool _volumeConstraintInitialized = false;
        
        /// <summary>
        /// Volume constraint coupling constant ?.
        /// Higher values make the constraint stronger (less fluctuations).
        /// </summary>
        public double VolumeConstraintLambda
        {
            get => _volumeLambda;
            set => _volumeLambda = Math.Max(0, value);
        }
        
        /// <summary>
        /// Target number of edges for volume stabilization.
        /// </summary>
        public double TargetEdgeCount
        {
            get => _targetEdgeCount;
            set => _targetEdgeCount = Math.Max(0, value);
        }
        
        /// <summary>
        /// Target total weight for volume stabilization.
        /// </summary>
        public double TargetTotalWeight
        {
            get => _targetTotalWeight;
            set => _targetTotalWeight = Math.Max(0, value);
        }
        
        /// <summary>
        /// Initialize volume constraint with current graph state as target.
        /// Call this after initial graph setup but before simulation starts.
        /// 
        /// Implements RQ-Hypothesis Checklist: Volume Stabilization (Step 4).
        /// </summary>
        public void InitializeVolumeConstraint()
        {
            // Count current edges
            int edgeCount = 0;
            double totalWeight = 0.0;
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i) // Count each edge once
                    {
                        edgeCount++;
                        totalWeight += Weights[i, j];
                    }
                }
            }
            
            _targetEdgeCount = edgeCount;
            _targetTotalWeight = totalWeight;
            _volumeConstraintInitialized = true;
            
            //Console.WriteLine($"[VOLUME] Initialized: target edges = {edgeCount}, target weight = {totalWeight:F2}");
        }
        
        /// <summary>
        /// Initialize volume constraint with explicit targets.
        /// </summary>
        /// <param name="targetEdges">Target number of edges</param>
        /// <param name="targetWeight">Target total weight</param>
        /// <param name="lambda">Constraint strength</param>
        public void InitializeVolumeConstraint(double targetEdges, double targetWeight, double lambda = 0.01)
        {
            _targetEdgeCount = targetEdges;
            _targetTotalWeight = targetWeight;
            _volumeLambda = lambda;
            _volumeConstraintInitialized = true;
        }
        
        /// <summary>
        /// Compute the volume penalty contribution to the action.
        /// 
        /// S_vol = ? ? [(N_E - N_E^target)? + (W - W^target)?]
        /// 
        /// This acts as a soft constraint that:
        /// - Penalizes graphs with too few edges (evaporation)
        /// - Penalizes graphs with too many edges (percolation)
        /// </summary>
        /// <returns>Volume penalty contribution to action</returns>
        public double ComputeVolumePenalty()
        {
            if (!_volumeConstraintInitialized || _volumeLambda <= 0)
                return 0.0;
            
            // Count current edges and total weight
            int currentEdges = 0;
            double currentWeight = 0.0;
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i)
                    {
                        currentEdges++;
                        currentWeight += Weights[i, j];
                    }
                }
            }
            
            // Volume penalty: S_vol = ? ? (deviation)?
            double edgeDeviation = currentEdges - _targetEdgeCount;
            double weightDeviation = currentWeight - _targetTotalWeight;
            
            // Normalize deviations by target values to make them comparable
            double normalizedEdgeDev = _targetEdgeCount > 0 ? edgeDeviation / _targetEdgeCount : edgeDeviation;
            double normalizedWeightDev = _targetTotalWeight > 0 ? weightDeviation / _targetTotalWeight : weightDeviation;
            
            double penalty = _volumeLambda * (normalizedEdgeDev * normalizedEdgeDev 
                                            + normalizedWeightDev * normalizedWeightDev);
            
            return penalty;
        }
        
        /// <summary>
        /// Compute the change in volume penalty from a proposed edge modification.
        /// Used in Metropolis step for efficient local computation.
        /// 
        /// For edge creation:   ?S_vol = ? ? [2(N_E - N_target) + 1] (approximately)
        /// For edge deletion:   ?S_vol = ? ? [-2(N_E - N_target) + 1]
        /// For weight change:   ?S_vol = ? ? [2(W - W_target) ? ?w + ?w?]
        /// </summary>
        /// <param name="edgeCreated">1 if creating edge, -1 if deleting, 0 if modifying</param>
        /// <param name="deltaWeight">Change in edge weight</param>
        /// <returns>Change in volume penalty ?S_vol</returns>
        public double ComputeVolumePenaltyChange(int edgeCreated, double deltaWeight)
        {
            if (!_volumeConstraintInitialized || _volumeLambda <= 0)
                return 0.0;
            
            // Get current state
            int currentEdges = CountEdges();
            double currentWeight = TotalEdgeWeight();
            
            // Edge count contribution
            double edgeDevBefore = currentEdges - _targetEdgeCount;
            double edgeDevAfter = edgeDevBefore + edgeCreated;
            double deltaEdgePenalty = (edgeDevAfter * edgeDevAfter - edgeDevBefore * edgeDevBefore) 
                                      / (_targetEdgeCount * _targetEdgeCount + 1);
            
            // Weight contribution
            double weightDevBefore = currentWeight - _targetTotalWeight;
            double weightDevAfter = weightDevBefore + deltaWeight;
            double deltaWeightPenalty = (weightDevAfter * weightDevAfter - weightDevBefore * weightDevBefore)
                                        / (_targetTotalWeight * _targetTotalWeight + 1);
            
            return _volumeLambda * (deltaEdgePenalty + deltaWeightPenalty);
        }
        
        /// <summary>
        /// Count total number of edges in the graph.
        /// </summary>
        private int CountEdges()
        {
            int count = 0;
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i) count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// Compute total edge weight in the graph.
        /// </summary>
        private double TotalEdgeWeight()
        {
            double total = 0.0;
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i) total += Weights[i, j];
                }
            }
            return total;
        }
        
        /// <summary>
        /// Check if spectral dimension is healthy (2 ? d_S ? 5).
        /// If unhealthy, apply corrective measures.
        /// 
        /// Implements RQ-Hypothesis Checklist: Spectral Dimension Monitoring.
        /// </summary>
        /// <returns>True if correction was applied</returns>
        public bool CheckAndCorrectSpectralDimension()
        {
            double d_S = SmoothedSpectralDimension;
            
            // Check for fragmentation (d_S < critical)
            if (d_S < PhysicsConstants.CriticalSpectralDimension)
            {
                //Console.WriteLine($"[SPECTRAL] CRITICAL: d_S = {d_S:F2} < {PhysicsConstants.CriticalSpectralDimension}. Applying recovery.");
                ApplyFragmentationRecovery();
                return true;
            }
            
            // Check for warning (d_S < warning)
            if (d_S < PhysicsConstants.WarningSpectralDimension)
            {
                //Console.WriteLine($"[SPECTRAL] WARNING: d_S = {d_S:F2} < {PhysicsConstants.WarningSpectralDimension}. Softening gravity.");
                // Reduce effective gravity to allow graph to reconnect
                return false; // Signal to caller to reduce G
            }
            
            // Check for giant cluster
            double largestClusterFraction = GetLargestClusterFractionForStabilization();
            if (largestClusterFraction > PhysicsConstants.GiantClusterThreshold)
            {
                //Console.WriteLine($"[SPECTRAL] Giant cluster detected: {largestClusterFraction:P0}. Applying decoherence.");
                // Use existing method from RQGraph.GraphHealth.cs
                ApplyGiantClusterDecoherence();
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Apply fragmentation recovery by adding random edges.
        /// </summary>
        private void ApplyFragmentationRecovery()
        {
            int edgesToAdd = (int)(N * PhysicsConstants.FragmentationRecoveryEdgeFraction);
            int added = 0;
            
            for (int attempt = 0; attempt < edgesToAdd * 10 && added < edgesToAdd; attempt++)
            {
                int i = _rng.Next(N);
                int j = _rng.Next(N);
                if (i == j) continue;
                if (Edges[i, j]) continue;
                
                // Add edge
                Edges[i, j] = true;
                Edges[j, i] = true;
                Weights[i, j] = 0.3 + 0.4 * _rng.NextDouble();
                Weights[j, i] = Weights[i, j];
                _degree[i]++;
                _degree[j]++;
                added++;
            }
            
            InvalidateTopologyCache();
            //Console.WriteLine($"[SPECTRAL] Recovery: added {added} edges");
        }
        
        /// <summary>
        /// Get fraction of nodes in largest connected component.
        /// </summary>
        private double GetLargestClusterFractionForStabilization()
        {
            var clusters = GetStrongCorrelationClusters(0.3); // Use moderate threshold
            if (clusters.Count == 0) return 0.0;
            
            int largestSize = clusters.Max(c => c.Count);
            return (double)largestSize / N;
        }
        
        /// <summary>
        /// Compute average degree of nodes.
        /// </summary>
        private double GetAverageDegreeForStabilization()
        {
            if (N == 0) return 0.0;
            
            int totalDegree = 0;
            for (int i = 0; i < N; i++)
            {
                totalDegree += Neighbors(i).Count();
            }
            return (double)totalDegree / N;
        }
    }
}
