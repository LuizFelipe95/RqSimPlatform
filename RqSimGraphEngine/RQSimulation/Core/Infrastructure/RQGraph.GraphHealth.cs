using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// RQGraph partial class containing graph health monitoring and recovery methods.
    /// 
    /// RQ-HYPOTHESIS COMPLIANCE:
    /// - Detects graph fragmentation (d_S < 1.0) which violates 4D emergence
    /// - Detects giant clusters (>30% N) which indicate percolation, not particles
    /// - Provides decoherence mechanisms to break giant clusters
    /// - Provides recovery methods to restore healthy topology
    /// 
    /// ARCHITECTURE:
    /// - CheckGraphHealth(): Returns GraphHealthStatus with diagnostics
    /// - ApplyGiantClusterDecoherence(): Weakens edges in oversized clusters
    /// - AddRandomEdgesForConnectivity(): Restores graph connectivity when fragmented
    /// </summary>
    public partial class RQGraph
    {
        // Tracking for fragmentation detection
        private int _consecutiveFragmentedChecks = 0;
        private double _lastHealthySpectralDim = 0;

        /// <summary>
        /// Checks the health status of the graph topology.
        /// Returns a GraphHealthStatus struct with diagnostics.
        /// 
        /// Call this periodically (e.g., every 200 steps) to monitor graph health.
        /// </summary>
        /// <param name="spectralDimension">Pre-computed spectral dimension (expensive to compute)</param>
        /// <param name="largestClusterSize">Size of largest cluster in nodes</param>
        public GraphHealthStatus CheckGraphHealth(double spectralDimension, int largestClusterSize)
        {
            double clusterFraction = N > 0 ? (double)largestClusterSize / N : 0.0;
            double avgDegree = ComputeAverageDegree();

            var status = new GraphHealthStatus
            {
                SpectralDimension = spectralDimension,
                LargestClusterFraction = clusterFraction,
                AverageDegree = avgDegree
            };

            // Track consecutive fragmentation events
            if (status.IsFragmented)
            {
                _consecutiveFragmentedChecks++;
            }
            else
            {
                _consecutiveFragmentedChecks = 0;
                if (spectralDimension > PhysicsConstants.WarningSpectralDimension)
                {
                    _lastHealthySpectralDim = spectralDimension;
                }
            }

            return status;
        }

        /// <summary>
        /// Returns true if graph fragmentation is terminal (cannot recover).
        /// Throws GraphFragmentationException if terminal.
        /// </summary>
        /// <param name="step">Current simulation step</param>
        /// <param name="spectralDimension">Current spectral dimension</param>
        /// <returns>True if fragmentation is terminal and exception was not thrown</returns>
        public bool CheckFragmentationTerminal(int step, double spectralDimension)
        {
            if (spectralDimension < PhysicsConstants.CriticalSpectralDimension)
            {
                _consecutiveFragmentedChecks++;

                if (_consecutiveFragmentedChecks >= PhysicsConstants.FragmentationGracePeriodSteps)
                {
                    throw new GraphFragmentationException(
                        spectralDimension,
                        step,
                        _consecutiveFragmentedChecks);
                }
                return false;
            }

            _consecutiveFragmentedChecks = 0;
            return false;
        }

        /// <summary>
        /// Applies decoherence to edges within giant clusters to break them up.
        /// 
        /// RQ-HYPOTHESIS: Particles are SMALL stable topological structures.
        /// A giant cluster indicates failed structure formation (percolated state).
        /// Decoherence weakens internal edges to allow fragmentation into smaller clusters.
        /// 
        /// ALGORITHM:
        /// 1. Find all clusters above threshold
        /// 2. For each giant cluster, identify internal edges (both endpoints in cluster)
        /// 3. Weaken strongest internal edges by decoherence rate
        /// 4. Optionally add noise to break symmetry
        /// </summary>
        /// <param name="threshold">Weight threshold for cluster detection</param>
        /// <returns>Number of edges weakened</returns>
        public int ApplyGiantClusterDecoherence(double? threshold = null)
        {
            double effectiveThreshold = threshold ?? GetAdaptiveHeavyThreshold();
            var clusters = GetStrongCorrelationClusters(effectiveThreshold);

            int giantThreshold = (int)(N * PhysicsConstants.GiantClusterThreshold);
            int totalWeakened = 0;

            foreach (var cluster in clusters)
            {
                if (cluster.Count < giantThreshold)
                    continue;

                totalWeakened += WeakenClusterEdges(cluster);
            }

            return totalWeakened;
        }

        /// <summary>
        /// Weakens edges within a cluster to encourage fragmentation.
        /// Targets the strongest internal edges first.
        /// </summary>
        /// <param name="cluster">List of node indices in the cluster</param>
        /// <returns>Number of edges weakened</returns>
        private int WeakenClusterEdges(List<int> cluster)
        {
            // Convert to HashSet for O(1) membership check
            var clusterSet = new HashSet<int>(cluster);

            // Find all internal edges (both endpoints in cluster)
            var internalEdges = new List<(int i, int j, double weight)>();

            foreach (int i in cluster)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i && clusterSet.Contains(j))
                    {
                        internalEdges.Add((i, j, Weights[i, j]));
                    }
                }
            }

            if (internalEdges.Count == 0)
                return 0;

            // Sort by weight descending (weaken strongest first)
            internalEdges.Sort((a, b) => b.weight.CompareTo(a.weight));

            // Determine how many edges to weaken
            int maxToWeaken = Math.Max(1, (int)(internalEdges.Count * PhysicsConstants.MaxDecoherenceEdgesFraction));
            int edgesToWeaken = Math.Min(maxToWeaken, internalEdges.Count);

            // Apply decoherence with some randomness
            int weakened = 0;
            for (int k = 0; k < edgesToWeaken; k++)
            {
                var (i, j, w) = internalEdges[k];

                // Calculate weight reduction
                double reduction = Math.Max(
                    PhysicsConstants.MinDecoherenceWeightReduction,
                    w * PhysicsConstants.GiantClusterDecoherenceRate
                );

                // Add small random component to break symmetry
                if (_rng != null)
                {
                    reduction *= (0.8 + 0.4 * _rng.NextDouble());
                }

                double newWeight = Math.Max(PhysicsConstants.WeightLowerSoftWall, w - reduction);

                Weights[i, j] = newWeight;
                Weights[j, i] = newWeight;

                weakened++;
            }

            return weakened;
        }

        // Note: AddRandomEdgesForConnectivity is defined in RQGraph.CoreHelpers.cs
        // It returns void - we use the existing implementation

        /// <summary>
        /// Computes average degree of the graph.
        /// </summary>
        public double ComputeAverageDegree()
        {
            if (N == 0) return 0.0;

            int totalDegree = 0;
            for (int i = 0; i < N; i++)
            {
                totalDegree += _degree[i];
            }

            return (double)totalDegree / N;
        }

        /// <summary>
        /// Injects decoherence noise into a specific cluster to destabilize it.
        /// This is more aggressive than WeakenClusterEdges - it adds random perturbations.
        /// 
        /// Use when giant cluster decoherence is not effective enough.
        /// </summary>
        /// <param name="cluster">List of node indices in the cluster</param>
        /// <param name="noiseAmplitude">Amplitude of random noise to inject</param>
        public void InjectDecoherenceIntoCluster(List<int> cluster, double noiseAmplitude = 0.1)
        {
            if (_rng == null || cluster.Count < 2)
                return;

            var clusterSet = new HashSet<int>(cluster);

            foreach (int i in cluster)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i && clusterSet.Contains(j))
                    {
                        // Add random noise to weight
                        double noise = (2.0 * _rng.NextDouble() - 1.0) * noiseAmplitude;
                        double newWeight = Weights[i, j] + noise;

                        // Clamp to valid range
                        newWeight = Math.Clamp(newWeight,
                            PhysicsConstants.WeightLowerSoftWall,
                            PhysicsConstants.WeightUpperSoftWall);

                        Weights[i, j] = newWeight;
                        Weights[j, i] = newWeight;
                    }
                }

                // Also perturb node state to break coherence
                if (State[i] == NodeState.Excited && _rng.NextDouble() < noiseAmplitude)
                {
                    State[i] = NodeState.Rest;
                }
            }
        }

        /// <summary>
        /// Comprehensive graph recovery procedure.
        /// Call this when graph health is critical but not terminal.
        /// 
        /// STEPS:
        /// 1. Apply giant cluster decoherence
        /// 2. Add random edges for connectivity
        /// 3. Reduce stored energies to prevent runaway
        /// </summary>
        /// <returns>Recovery action description</returns>
        public string PerformGraphRecovery(GraphHealthStatus status)
        {
            var actions = new List<string>();

            // 1. Break giant clusters if present
            if (status.HasGiantCluster)
            {
                int weakened = ApplyGiantClusterDecoherence();
                actions.Add($"Weakened {weakened} giant cluster edges");

                // If emergency level, also inject noise
                if (status.HasEmergencyGiantCluster)
                {
                    var clusters = GetStrongCorrelationClusters(GetAdaptiveHeavyThreshold());
                    int giantThreshold = (int)(N * PhysicsConstants.EmergencyGiantClusterThreshold);

                    foreach (var cluster in clusters.Where(c => c.Count >= giantThreshold))
                    {
                        InjectDecoherenceIntoCluster(cluster, 0.15);
                    }
                    actions.Add("Injected decoherence noise into giant clusters");
                }
            }

            // 2. Add edges if fragmenting
            if (status.SpectralDimension < PhysicsConstants.WarningSpectralDimension)
            {
                int edgesToAdd = (int)(N * PhysicsConstants.FragmentationRecoveryEdgeFraction);
                AddRandomEdgesForConnectivity(edgesToAdd);
                actions.Add($"Added {edgesToAdd} random edges for connectivity");
            }

            // 3. Normalize stored energies to prevent runaway
            if (StoredEnergy != null)
            {
                double maxEnergy = StoredEnergy.Max();
                if (maxEnergy > 10.0) // Arbitrary high threshold
                {
                    double scale = 10.0 / maxEnergy;
                    for (int i = 0; i < N; i++)
                    {
                        StoredEnergy[i] *= scale;
                    }
                    actions.Add($"Normalized stored energies (max was {maxEnergy:F2})");
                }
            }

            return actions.Count > 0
                ? string.Join("; ", actions)
                : "No recovery actions needed";
        }

        /// <summary>
        /// Returns the number of consecutive fragmented checks.
        /// Used to determine if fragmentation is persistent.
        /// </summary>
        public int ConsecutiveFragmentedChecks => _consecutiveFragmentedChecks;

        /// <summary>
        /// Resets the fragmentation counter.
        /// Call this when external recovery actions are taken.
        /// </summary>
        public void ResetFragmentationCounter()
        {
            _consecutiveFragmentedChecks = 0;
        }

        // ============================================================
        // RQ-MODERNIZATION: Singularity Detection
        // ============================================================

        // Tracking for singularity detection
        private int _consecutiveSingularitySteps = 0;

        /// <summary>
        /// RQ-MODERNIZATION: Check for spacetime singularity formation.
        /// 
        /// A singularity is detected when:
        /// - Any edge weight becomes NaN or Infinity (numerical singularity)
        /// - Graph becomes disconnected (topological singularity)
        /// - Edge weights reach exactly zero (horizon formation)
        /// 
        /// SCIENTIFIC RATIONALE:
        /// In General Relativity, singularities are legitimate physical predictions:
        /// - Black hole interiors (curvature singularity)
        /// - Big Crunch (cosmological singularity)
        /// - Vacuum decay (false vacuum collapse)
        /// 
        /// Instead of throwing exceptions, we recognize these as valid simulation results.
        /// </summary>
        /// <returns>Singularity status indicating type of singularity if detected</returns>
        public SingularityStatus CheckSingularityState()
        {
            bool hasNaN = false;
            bool hasInfinity = false;
            bool hasZeroWeight = false;
            int zeroWeightCount = 0;
            int nanCount = 0;
            int infCount = 0;

            // Check all edge weights for singularity indicators
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue; // Process each edge once

                    double w = Weights[i, j];

                    if (double.IsNaN(w))
                    {
                        hasNaN = true;
                        nanCount++;
                    }
                    else if (double.IsInfinity(w))
                    {
                        hasInfinity = true;
                        infCount++;
                    }
                    else if (w <= 0.0)
                    {
                        hasZeroWeight = true;
                        zeroWeightCount++;
                    }
                }
            }

            // Check for disconnection (simplified - full check is expensive)
            bool isDisconnected = ComputeAverageDegree() < 1.5;

            // Determine singularity type
            SingularityType type = SingularityType.None;
            string description = "";

            if (hasNaN)
            {
                type = SingularityType.Numerical;
                description = $"Numerical singularity: {nanCount} edges with NaN weights";
            }
            else if (hasInfinity)
            {
                type = SingularityType.Curvature;
                description = $"Curvature singularity: {infCount} edges with infinite weights";
            }
            else if (isDisconnected)
            {
                type = SingularityType.Topological;
                description = "Topological singularity: spacetime fragmented (disconnected graph)";
            }
            else if (hasZeroWeight && zeroWeightCount > N * 0.1)
            {
                type = SingularityType.Horizon;
                description = $"Horizon formation: {zeroWeightCount} edges with zero weight";
            }

            // Track consecutive singularity steps
            if (type != SingularityType.None)
            {
                _consecutiveSingularitySteps++;
            }
            else
            {
                _consecutiveSingularitySteps = 0;
            }

            bool isTerminal = _consecutiveSingularitySteps >= PhysicsConstants.SingularityGracePeriodSteps;

            return new SingularityStatus
            {
                Type = type,
                Description = description,
                IsTerminal = isTerminal,
                ConsecutiveSteps = _consecutiveSingularitySteps,
                NaNEdgeCount = nanCount,
                InfinityEdgeCount = infCount,
                ZeroWeightEdgeCount = zeroWeightCount
            };
        }

        /// <summary>
        /// Resets the singularity tracking counter.
        /// </summary>
        public void ResetSingularityCounter()
        {
            _consecutiveSingularitySteps = 0;
        }

        /// <summary>
        /// RQ-MODERNIZATION: Export full graph state snapshot when singularity occurs.
        /// 
        /// This is critical for scientific analysis:
        /// - Allows post-mortem investigation of WHY the universe died
        /// - Provides data for statistical analysis of singularity formation
        /// - Enables debugging of numerical issues vs physical predictions
        /// 
        /// The export includes:
        /// 1. All edges with weights < criticalThreshold (near-singular edges)
        /// 2. Full curvature distribution statistics
        /// 3. Spectral dimension before crash
        /// 4. Node mass distribution
        /// 5. Timestamp and simulation step
        /// </summary>
        public void ExportSingularitySnapshot(
            string filePath,
            int step,
            SingularityStatus singularityStatus,
            double? spectralDimension = null)
        {
            var snapshot = new SingularitySnapshot
            {
                Timestamp = DateTime.UtcNow,
                SimulationStep = step,
                SingularityType = singularityStatus.Type.ToString(),
                SingularityDescription = singularityStatus.Description,
                ConsecutiveSingularitySteps = singularityStatus.ConsecutiveSteps,
                NodeCount = N,
                TotalEdgeCount = FlatEdgesFrom?.Length ?? 0,
                SpectralDimension = spectralDimension ?? EstimateSpectralDimensionFast(),
                AverageDegree = ComputeAverageDegree(),
                NaNEdgeCount = singularityStatus.NaNEdgeCount,
                InfinityEdgeCount = singularityStatus.InfinityEdgeCount,
                ZeroWeightEdgeCount = singularityStatus.ZeroWeightEdgeCount
            };

            // Collect critical edges and curvature stats
            const double criticalThreshold = 1e-6;
            var criticalEdges = new List<CriticalEdgeInfo>();
            var curvatures = new List<double>();

            if (FlatEdgesFrom != null && FlatEdgesTo != null)
            {
                for (int e = 0; e < FlatEdgesFrom.Length; e++)
                {
                    int i = FlatEdgesFrom[e];
                    int j = FlatEdgesTo[e];
                    double w = Weights[i, j];

                    double curvature = 0.0;
                    try
                    {
                        curvature = GPUOptimized.OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(this, i, j);
                    }
                    catch { curvature = double.NaN; }
                    
                    curvatures.Add(curvature);

                    if (w < criticalThreshold || double.IsNaN(w) || double.IsInfinity(w))
                    {
                        criticalEdges.Add(new CriticalEdgeInfo
                        {
                            NodeA = i, NodeB = j, Weight = w, Curvature = curvature,
                            Reason = w < criticalThreshold ? "NearZero" : double.IsNaN(w) ? "NaN" : "Infinity"
                        });
                    }
                }
            }

            snapshot.CriticalEdges = criticalEdges;
            
            var valid = curvatures.Where(c => !double.IsNaN(c) && !double.IsInfinity(c)).ToList();
            if (valid.Count > 0)
            {
                snapshot.MeanCurvature = valid.Average();
                snapshot.MinCurvature = valid.Min();
                snapshot.MaxCurvature = valid.Max();
            }

            if (NodeMasses != null)
            {
                var masses = NodeMasses.Select(m => m.TotalMass).ToList();
                snapshot.MeanNodeMass = masses.Average();
                snapshot.MaxNodeMass = masses.Max();
            }

            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(snapshot, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Singularity Export] Failed: {ex.Message}");
            }
        }

        private double EstimateSpectralDimensionFast()
        {
            double avgDegree = ComputeAverageDegree();
            if (avgDegree < 2) return 1.0;
            if (avgDegree < 4) return 2.0;
            if (avgDegree < 8) return 3.0;
            return 4.0;
        }
    }

    /// <summary>Snapshot data for singularity crash dump.</summary>
    public class SingularitySnapshot
    {
        public DateTime Timestamp { get; set; }
        public int SimulationStep { get; set; }
        public string SingularityType { get; set; } = "";
        public string SingularityDescription { get; set; } = "";
        public int ConsecutiveSingularitySteps { get; set; }
        public int NodeCount { get; set; }
        public int TotalEdgeCount { get; set; }
        public double SpectralDimension { get; set; }
        public double AverageDegree { get; set; }
        public int NaNEdgeCount { get; set; }
        public int InfinityEdgeCount { get; set; }
        public int ZeroWeightEdgeCount { get; set; }
        public double MeanCurvature { get; set; }
        public double MinCurvature { get; set; }
        public double MaxCurvature { get; set; }
        public double MeanNodeMass { get; set; }
        public double MaxNodeMass { get; set; }
        public List<CriticalEdgeInfo> CriticalEdges { get; set; } = new();
    }

    /// <summary>Information about a critical edge.</summary>
    public class CriticalEdgeInfo
    {
        public int NodeA { get; set; }
        public int NodeB { get; set; }
        public double Weight { get; set; }
        public double Curvature { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// RQ-MODERNIZATION: Types of spacetime singularities.
    /// </summary>
    public enum SingularityType
    {
        /// <summary>No singularity detected.</summary>
        None,

        /// <summary>Numerical breakdown (NaN weights) - requires investigation.</summary>
        Numerical,

        /// <summary>Curvature singularity (infinite weights) - black hole interior.</summary>
        Curvature,

        /// <summary>Topological singularity (disconnected graph) - spacetime fragmentation.</summary>
        Topological,

        /// <summary>Horizon formation (zero weight edges) - event horizon.</summary>
        Horizon
    }

    /// <summary>
    /// RQ-MODERNIZATION: Status of singularity detection.
    /// </summary>
    public readonly struct SingularityStatus
    {
        /// <summary>Type of singularity detected.</summary>
        public SingularityType Type { get; init; }

        /// <summary>Human-readable description of the singularity.</summary>
        public string Description { get; init; }

        /// <summary>True if singularity persists long enough to terminate simulation.</summary>
        public bool IsTerminal { get; init; }

        /// <summary>Number of consecutive steps with singularity indicators.</summary>
        public int ConsecutiveSteps { get; init; }

        /// <summary>Number of edges with NaN weights.</summary>
        public int NaNEdgeCount { get; init; }

        /// <summary>Number of edges with infinite weights.</summary>
        public int InfinityEdgeCount { get; init; }

        /// <summary>Number of edges with zero weights.</summary>
        public int ZeroWeightEdgeCount { get; init; }

        /// <summary>True if any singularity is present.</summary>
        public bool HasSingularity => Type != SingularityType.None;
    }
}
