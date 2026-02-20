using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation.GPUOptimized
{
    public static partial class SpectralDimensionValidator
    {
        /// <summary>
        /// Validate spinor compatibility and return status
        /// Also checks for suspicious d_S = 1.0 stability
        /// </summary>
        public static SpinorCompatibilityStatus ValidateSpinorCompatibility(RQGraph graph)
        {
            double spectralDim = graph.ComputeSpectralDimension();

            // Check for suspicious stability at d_S = 1.0
            var suspiciousStatus = CheckForSuspiciousStability(spectralDim);

            var status = new SpinorCompatibilityStatus
            {
                SpectralDimension = spectralDim,
                IsCompatible = ShouldEnableSpinorFields(spectralDim),
                DeviationPercent = Math.Abs(spectralDim - TargetDimension) / TargetDimension,
                Recommendation = GetRecommendation(spectralDim),
                IsSuspiciouslyStable = suspiciousStatus.IsSuspicious,
                SuspiciousDiagnosis = suspiciousStatus.Diagnosis
            };

            return status;
        }

        /// <summary>
        /// Extended validation with walker spread analysis
        /// Use this for detailed diagnostics
        /// </summary>
        public static ExtendedValidationResult ValidateWithWalkerAnalysis(
            RQGraph graph,
            int numWalkers = 100,
            int walkSteps = 50)
        {
            var result = new ExtendedValidationResult();

            // Run random walks and track spread
            int N = graph.N;
            var random = new Random();
            var visitedNodes = new HashSet<int>();
            var walkerFinalPositions = new int[numWalkers];
            var walkerDistances = new double[numWalkers];

            for (int w = 0; w < numWalkers; w++)
            {
                int startNode = random.Next(N);
                int currentNode = startNode;

                for (int step = 0; step < walkSteps; step++)
                {
                    var neighbors = graph.Neighbors(currentNode).ToList();
                    if (neighbors.Count == 0) break;

                    // Random walk step
                    currentNode = neighbors[random.Next(neighbors.Count)];
                    visitedNodes.Add(currentNode);
                }

                walkerFinalPositions[w] = currentNode;
                walkerDistances[w] = graph.ShortestPathDistance(startNode, currentNode);
            }

            // Analyze walker spread
            result.UniqueNodesVisited = visitedNodes.Count;
            result.UniqueNodesFraction = (double)visitedNodes.Count / N;
            result.AverageWalkerDistance = walkerDistances.Average();
            result.MaxWalkerDistance = walkerDistances.Max();

            // Check for trapped walkers (distance = 0 or very small)
            int trappedWalkers = walkerDistances.Count(d => d <= 2);
            result.TrappedWalkerFraction = (double)trappedWalkers / numWalkers;

            // Compute spectral dimension
            result.SpectralDimension = graph.ComputeSpectralDimension(numWalkers, walkSteps);

            // Determine if walkers are locked
            result.WalkersAreLocked = result.TrappedWalkerFraction > 0.5 ||
                                       result.UniqueNodesFraction < 0.1;

            if (result.WalkersAreLocked)
            {
                result.Diagnosis = $"[LOCKED WALKERS] {result.TrappedWalkerFraction * 100:F1}% of walkers " +
                                   $"traveled ?2 hops. Only {result.UniqueNodesFraction * 100:F1}% of nodes reached.\n" +
                                   "This invalidates spectral dimension calculation.\n" +
                                   "Graph may be disconnected or have bottleneck topology.";
            }
            else
            {
                result.Diagnosis = $"Walker spread OK: {result.UniqueNodesFraction * 100:F1}% nodes visited, " +
                                   $"avg distance {result.AverageWalkerDistance:F2}";
            }

            return result;
        }
    }
}
