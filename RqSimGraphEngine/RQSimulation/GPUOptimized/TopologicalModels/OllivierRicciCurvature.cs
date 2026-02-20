using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// Curvature computations for graph geometry: Ollivier-Ricci and Forman-Ricci.
    /// 
    /// CHECKLIST ITEM 4: Both Ollivier and Forman curvatures are provided.
    /// 
    /// Ollivier-Ricci (more accurate but slower):
    /// - Based on optimal transport between probability measures
    /// - Captures geodesic deviation like Einstein's gravity
    /// 
    /// Forman-Ricci (faster approximation):
    /// - Based on local combinatorics (degrees and triangles)
    /// - O(degree²) per edge vs O(N²) for full Wasserstein
    /// - Good approximation for Ricci flow on graphs
    /// </summary>
    public static class OllivierRicciCurvature
    {
        /// <summary>
        /// Compute Ollivier-Ricci curvature for edge (i,j) using Sinkhorn-Knopp
        /// optimal transport for the Wasserstein-1 distance.
        ///
        /// Algorithm:
        ///   1. Build lazy random walk distributions μ_i, μ_j on neighborhoods
        ///   2. Compute cost matrix C[a,b] = graph distance between support nodes
        ///   3. Gibbs kernel K[a,b] = exp(-C[a,b] / ε)
        ///   4. Sinkhorn iterations: u ← μ / (K·v),  v ← ν / (Kᵀ·u)
        ///   5. Transport plan T[a,b] = u[a]·K[a,b]·v[b]
        ///   6. W₁ ≈ ⟨T, C⟩ = Σ T[a,b]·C[a,b]
        ///   7. κ(i,j) = 1 - W₁ / d(i,j)
        ///
        /// This is mathematically correct (converges to true W₁ as ε→0)
        /// unlike the Jaccard approximation which is not a valid metric.
        /// </summary>
        /// <param name="graph">The graph</param>
        /// <param name="i">Source node</param>
        /// <param name="j">Target node</param>
        /// <param name="lazyWalkAlpha">Lazy walk parameter (probability of staying). Default 0.1</param>
        /// <param name="epsilon">Entropic regularization. Smaller = more accurate but slower convergence. Default 0.01</param>
        /// <param name="maxIterations">Maximum Sinkhorn iterations. Default 50</param>
        /// <param name="convergenceTol">Convergence tolerance for scaling vectors. Default 1e-6</param>
        public static double ComputeOllivierRicciSinkhorn(
            RQGraph graph, int i, int j,
            double lazyWalkAlpha = 0.1,
            double epsilon = 0.01,
            int maxIterations = 50,
            double convergenceTol = 1e-6)
        {
            if (!graph.Edges[i, j])
                return 0.0;

            double edgeWeight = graph.Weights[i, j];
            if (edgeWeight <= 0)
                return 0.0;

            // Step 1: Build probability distributions on neighborhoods (lazy random walk)
            var distI = GetWeightedNeighborhood(graph, i, lazyWalkAlpha);
            var distJ = GetWeightedNeighborhood(graph, j, lazyWalkAlpha);

            // Step 2: Collect support nodes and build index mapping
            var supportSet = new HashSet<int>(distI.Keys);
            supportSet.UnionWith(distJ.Keys);
            var supportNodes = new List<int>(supportSet);
            int M = supportNodes.Count;

            if (M == 0)
                return 0.0;

            var nodeIndex = new Dictionary<int, int>(M);
            for (int idx = 0; idx < M; idx++)
                nodeIndex[supportNodes[idx]] = idx;

            // Build μ and ν vectors (indexed by support)
            double[] mu = new double[M];
            double[] nu = new double[M];

            foreach (var kvp in distI)
                mu[nodeIndex[kvp.Key]] = kvp.Value;

            foreach (var kvp in distJ)
                nu[nodeIndex[kvp.Key]] = kvp.Value;

            // Step 3: Compute cost matrix C[a,b] = d(supportNodes[a], supportNodes[b])
            double[] cost = new double[M * M];
            for (int a = 0; a < M; a++)
            {
                for (int b = a; b < M; b++)
                {
                    double d = ComputeGraphDistance(graph, supportNodes[a], supportNodes[b]);
                    cost[a * M + b] = d;
                    cost[b * M + a] = d;
                }
            }

            // Step 4: Gibbs kernel K[a,b] = exp(-C[a,b] / ε)
            double invEps = 1.0 / epsilon;
            double[] K = new double[M * M];
            for (int a = 0; a < M; a++)
            {
                for (int b = 0; b < M; b++)
                {
                    K[a * M + b] = Math.Exp(-cost[a * M + b] * invEps);
                }
            }

            // Step 5: Sinkhorn-Knopp iterations
            double[] u = new double[M];
            double[] v = new double[M];
            Array.Fill(v, 1.0);

            double[] Kv = new double[M]; // K·v
            double[] Ktu = new double[M]; // Kᵀ·u

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // u = μ / (K·v)
                for (int a = 0; a < M; a++)
                {
                    double sum = 0.0;
                    for (int b = 0; b < M; b++)
                        sum += K[a * M + b] * v[b];
                    Kv[a] = sum;
                    u[a] = sum > 1e-300 ? mu[a] / sum : 0.0;
                }

                // v = ν / (Kᵀ·u)
                double maxChange = 0.0;
                for (int b = 0; b < M; b++)
                {
                    double sum = 0.0;
                    for (int a = 0; a < M; a++)
                        sum += K[a * M + b] * u[a];
                    Ktu[b] = sum;
                    double vNew = sum > 1e-300 ? nu[b] / sum : 0.0;
                    maxChange = Math.Max(maxChange, Math.Abs(vNew - v[b]));
                    v[b] = vNew;
                }

                if (maxChange < convergenceTol)
                    break;
            }

            // Step 6: Compute W₁ = ⟨T, C⟩ = Σ u[a]·K[a,b]·v[b]·C[a,b]
            double W1 = 0.0;
            for (int a = 0; a < M; a++)
            {
                for (int b = 0; b < M; b++)
                {
                    double transport = u[a] * K[a * M + b] * v[b];
                    W1 += transport * cost[a * M + b];
                }
            }

            // Step 7: Ollivier-Ricci curvature
            return 1.0 - (W1 / edgeWeight);
        }

        /// <summary>
        /// Get weighted probability distribution on node's neighborhood
        /// Returns dictionary: neighbor -> probability
        /// </summary>
        /// <param name="graph">The graph</param>
        /// <param name="node">Node to get neighborhood for</param>
        /// <param name="alpha">Lazy walk parameter - probability of staying at current node</param>
        private static Dictionary<int, double> GetWeightedNeighborhood(RQGraph graph, int node, double alpha = 0.1)
        {
            var distribution = new Dictionary<int, double>();
            double totalWeight = 0.0;

            // Self-loop with small weight (lazy random walk)
            // alpha is passed from SimulationParameters for dynamic configuration
            distribution[node] = alpha;
            totalWeight += alpha;

            // Neighbors with weights proportional to edge weights
            foreach (int neighbor in graph.Neighbors(node))
            {
                double weight = graph.Weights[node, neighbor];
                distribution[neighbor] = (1.0 - alpha) * weight;
                totalWeight += (1.0 - alpha) * weight;
            }

            // Normalize to probability distribution
            if (totalWeight > 0)
            {
                var normalized = new Dictionary<int, double>();
                foreach (var kvp in distribution)
                {
                    normalized[kvp.Key] = kvp.Value / totalWeight;
                }
                return normalized;
            }

            return distribution;
        }

        /// <summary>
        /// Compute graph distance between two nodes (shortest path)
        /// Uses Dijkstra's algorithm with edge weights as distances
        /// </summary>
        private static double ComputeGraphDistance(RQGraph graph, int source, int target)
        {
            if (source == target)
                return 0.0;

            int N = graph.N;
            double[] dist = new double[N];
            bool[] visited = new bool[N];

            for (int i = 0; i < N; i++)
                dist[i] = double.MaxValue;

            dist[source] = 0.0;

            // Dijkstra's algorithm
            for (int iter = 0; iter < N; iter++)
            {
                // Find unvisited node with minimum distance
                int u = -1;
                double minDist = double.MaxValue;

                for (int i = 0; i < N; i++)
                {
                    if (!visited[i] && dist[i] < minDist)
                    {
                        minDist = dist[i];
                        u = i;
                    }
                }

                if (u == -1 || u == target)
                    break;

                visited[u] = true;

                // Update neighbors
                foreach (int v in graph.Neighbors(u))
                {
                    if (!visited[v])
                    {
                        double alt = dist[u] + graph.Weights[u, v];
                        if (alt < dist[v])
                        {
                            dist[v] = alt;
                        }
                    }
                }
            }

            return dist[target];
        }
    }

    // ================================================================
    // CHECKLIST ITEM 4: FORMAN-RICCI CURVATURE
    // ================================================================

    /// <summary>
    /// Forman-Ricci curvature computation for graph geometry (Jost formula).
    /// 
    /// CHECKLIST ITEM 4: Fast local curvature approximation.
    /// 
    /// Unweighted Forman-Ricci curvature for an edge e = (x,y):
    ///   Ric_F(e) = 2 - deg(x) - deg(y)
    /// 
    /// Weighted Forman-Ricci curvature (Jost formula):
    ///   Ric_F(e) = 2 - w_e · [ Σ_{e'~x, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~y, e''≠e} √(1/(w_e·w_{e''})) ]
    /// 
    /// Positive curvature → sphere-like (clustering)
    /// Negative curvature → hyperbolic (tree-like, spreading)
    /// 
    /// Advantage over Ollivier-Ricci:
    /// - O(degree) per edge instead of O(N²) for Wasserstein
    /// - Local computation, no global optimization
    /// - Sufficient for Ricci flow gravity simulations
    /// 
    /// Reference: Jost, J. and Liu, S. "Ollivier's Ricci curvature, local clustering
    /// and curvature-dimension inequalities on graphs", 2014.
    /// </summary>
    public static class FormanRicciCurvature
    {
        /// <summary>
        /// Compute Forman-Ricci curvature for an unweighted edge (i,j).
        /// 
        /// Formula: Ric_F(e) = 2 - deg(i) - deg(j)
        /// </summary>
        public static double ComputeFormanRicci(RQGraph graph, int i, int j)
        {
            if (!graph.Edges[i, j])
                return 0.0;

            // Get degrees
            int degI = 0, degJ = 0;
            foreach (int _ in graph.Neighbors(i)) degI++;
            foreach (int _ in graph.Neighbors(j)) degJ++;

            // Strict Jost reduction: Ric(e) = 2 - deg(x) - deg(y)
            return 2.0 - degI - degJ;
        }
        
        /// <summary>
        /// Compute Forman-Ricci curvature with weighted edges (Jost formula).
        /// 
        /// Jost formula for weighted graphs:
        ///   Ric_F(e) = 2 - w_e · [ Σ_{e'~x, e'≠e} √(1/(w_e·w_{e'})) + Σ_{e''~y, e''≠e} √(1/(w_e·w_{e''})) ]
        /// 
        /// For edges with zero or negative weight, returns 0.
        /// </summary>
        public static double ComputeFormanRicciWeighted(RQGraph graph, int i, int j)
        {
            if (!graph.Edges[i, j])
                return 0.0;

            double w_e = graph.Weights[i, j];
            if (w_e <= 0)
                return 0.0;

            // Sum over edges incident to node i (excluding edge e itself)
            double sumI = 0.0;
            foreach (int k in graph.Neighbors(i))
            {
                if (k == j) continue;
                double w_ik = graph.Weights[i, k];
                if (w_ik > 0)
                    sumI += Math.Sqrt(1.0 / (w_e * w_ik));
            }

            // Sum over edges incident to node j (excluding edge e itself)
            double sumJ = 0.0;
            foreach (int k in graph.Neighbors(j))
            {
                if (k == i) continue;
                double w_jk = graph.Weights[j, k];
                if (w_jk > 0)
                    sumJ += Math.Sqrt(1.0 / (w_e * w_jk));
            }

            // Jost weighted Forman-Ricci curvature
            return 2.0 - w_e * (sumI + sumJ);
        }
        
        /// <summary>
        /// Compute Forman-Ricci curvature for all edges in the graph.
        /// Returns array indexed by flattened edge coordinates.
        /// </summary>
        public static double[,] ComputeAllFormanRicci(RQGraph graph)
        {
            int N = graph.N;
            var curvatures = new double[N, N];
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue; // Only compute once per edge
                    
                    double R = ComputeFormanRicciWeighted(graph, i, j);
                    curvatures[i, j] = R;
                    curvatures[j, i] = R; // Symmetric
                }
            }
            
            return curvatures;
        }
        
        /// <summary>
        /// Compute scalar curvature at a node (average of incident edge curvatures).
        /// 
        /// R(v) = (1/deg(v)) * Σ_e R_F(e) for edges e incident to v
        /// </summary>
        public static double ComputeScalarCurvature(RQGraph graph, int node)
        {
            double sum = 0.0;
            int count = 0;
            
            foreach (int neighbor in graph.Neighbors(node))
            {
                sum += ComputeFormanRicciWeighted(graph, node, neighbor);
                count++;
            }
            
            return count > 0 ? sum / count : 0.0;
        }
        
        /// <summary>
        /// Compute average scalar curvature over the entire graph.
        /// This is analogous to the Einstein-Hilbert action integrand.
        /// </summary>
        public static double ComputeAverageScalarCurvature(RQGraph graph)
        {
            double totalCurvature = 0.0;
            int edgeCount = 0;
            
            for (int i = 0; i < graph.N; i++)
            {
                foreach (int j in graph.Neighbors(i))
                {
                    if (j <= i) continue;
                    totalCurvature += ComputeFormanRicciWeighted(graph, i, j);
                    edgeCount++;
                }
            }
            
            return edgeCount > 0 ? totalCurvature / edgeCount : 0.0;
        }
    }
}
