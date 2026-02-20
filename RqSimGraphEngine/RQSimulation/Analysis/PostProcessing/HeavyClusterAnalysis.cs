using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Статистика тяжёлых кластеров
    /// </summary>
    public record HeavyClusterStats(
        int ClusterCount,
        double TotalMass,
        int MaxSize,
        double TotalBinding);

    /// <summary>
    /// Анализатор тяжёлых кластеров
    /// </summary>
    public static class HeavyClusterAnalyzer
    {
        public static HeavyClusterStats ComputeHeavyClusters(
            RQGraph graph,
            double heavyEdgeThreshold)
        {
            ArgumentNullException.ThrowIfNull(graph);

            int n = graph.N;
            var visited = new bool[n];
            var clusters = new List<List<int>>();

            for (int v = 0; v < n; v++)
            {
                if (visited[v]) continue;

                bool hasHeavy = false;
                foreach (int neighbor in graph.Neighbors(v))
                {
                    if (graph.Weights[v, neighbor] >= heavyEdgeThreshold)
                    {
                        hasHeavy = true;
                        break;
                    }
                }
                if (!hasHeavy) continue;

                var q = new Queue<int>();
                var cluster = new List<int>();

                q.Enqueue(v);
                visited[v] = true;

                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    cluster.Add(u);

                    foreach (int w in graph.Neighbors(u))
                    {
                        double weight = graph.Weights[u, w];
                        if (weight < heavyEdgeThreshold)
                            continue;

                        if (visited[w]) continue;

                        visited[w] = true;
                        q.Enqueue(w);
                    }
                }

                clusters.Add(cluster);
            }

            int clusterCount = clusters.Count;
            int maxSize = clusterCount == 0 ? 0 : clusters.Max(c => c.Count);

            double totalMass = 0.0;
            double totalBinding = 0.0;

            foreach (var cluster in clusters)
            {
                var processedEdges = new HashSet<(int, int)>();

                foreach (int v in cluster)
                {
                    foreach (int u in cluster)
                    {
                        if (u <= v) continue;

                        if (!graph.Edges[v, u]) continue;

                        double weight = graph.Weights[v, u];
                        if (weight < heavyEdgeThreshold) continue;

                        int a = Math.Min(v, u);
                        int b = Math.Max(v, u);

                        if (processedEdges.Add((a, b)))
                        {
                            totalMass += (weight - heavyEdgeThreshold);
                            totalBinding += weight;
                        }
                    }
                }
            }

            return new HeavyClusterStats(clusterCount, totalMass, maxSize, totalBinding);
        }

        public static double ComputeAdaptiveThreshold(IReadOnlyList<double> weights, double quantile = 0.8)
        {
            if (weights == null || weights.Count == 0)
                return 0.0;

            var ordered = weights.OrderBy(w => w).ToArray();

            int idx = (int)Math.Clamp(
                quantile * (ordered.Length - 1),
                0,
                ordered.Length - 1);

            return ordered[idx];
        }
    }
}
