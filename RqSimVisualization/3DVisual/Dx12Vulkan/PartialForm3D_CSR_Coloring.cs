using System.Numerics;
using RQSimulation;

namespace RqSimVisualization;

/// <summary>
/// CSR visualization mode coloring logic.
/// Provides color computation for nodes based on selected CsrVisualizationMode.
/// Mirrors GDI+ visualization modes from PartialForm3D.cs.
/// </summary>
public partial class RqSimVisualizationForm
{
    /// <summary>
    /// Updates cluster assignment for nodes.
    /// Called when Clusters visualization mode is active.
    /// If only 1 or 2 clusters are found, falls back to position-based clustering
    /// for more visually interesting results.
    /// </summary>
    private void UpdateCsrClusterIds(RQGraph graph, int nodeCount)
    {
        try
        {
            // Get clusters from graph
            var clusters = graph.GetStrongCorrelationClusters(graph.GetAdaptiveHeavyThreshold());

            // If we have very few clusters (1-2), use position-based fallback for variety
            // This matches the standalone window behavior when cluster data is unavailable
            if (clusters.Count < 3)
            {
                // Set to null to trigger position-based fallback in GetCsrNodeClusterId
                _csrClusterIds = null;
                return;
            }

            // Resize array if needed
            if (_csrClusterIds is null || _csrClusterIds.Length != nodeCount)
            {
                _csrClusterIds = new int[nodeCount];
            }

            // Initialize all to unassigned
            Array.Fill(_csrClusterIds, -1);

            for (int c = 0; c < clusters.Count; c++)
            {
                foreach (int node in clusters[c])
                {
                    if (node >= 0 && node < nodeCount)
                    {
                        _csrClusterIds[node] = c;
                    }
                }
            }
        }
        catch
        {
            // On error, set to null to use position-based fallback
            _csrClusterIds = null;
        }
    }

    /// <summary>
    /// Gets the color for a node based on the current CSR visualization mode.
    /// </summary>
    /// <param name="nodeIndex">Index of the node to color.</param>
    /// <returns>Color as Vector4 (RGBA normalized 0-1).</returns>
    private Vector4 GetCsrNodeColor(int nodeIndex)
    {
        return _csrVisMode switch
        {
            CsrVisualizationMode.QuantumPhase => GetCsrQuantumPhaseColor(nodeIndex),
            CsrVisualizationMode.ProbabilityDensity => GetCsrProbabilityDensityColor(nodeIndex),
            CsrVisualizationMode.Curvature => GetCsrCurvatureColor(nodeIndex),
            CsrVisualizationMode.GravityHeatmap => GetCsrGravityHeatmapColor(nodeIndex),
            CsrVisualizationMode.NetworkTopology => GetCsrNetworkTopologyColor(nodeIndex),
            CsrVisualizationMode.Clusters => GetCsrClustersColor(nodeIndex),
            _ => new Vector4(1f, 1f, 1f, 1f)
        };
    }

    /// <summary>
    /// Quantum Phase mode: Color based on node's quantum probability amplitude.
    /// Blue (low) -> Cyan -> Green -> Yellow -> Red (high probability).
    /// Falls back to node state coloring if quantum probability is unavailable.
    /// </summary>
    private Vector4 GetCsrQuantumPhaseColor(int nodeIndex)
    {
        // Try quantum probability first
        if (_quantumProbability3D is not null && nodeIndex >= 0 && nodeIndex < _quantumProbability3D.Length)
        {
            double prob = _quantumProbability3D[nodeIndex];
            double normalizedProb = Math.Clamp(prob * _quantumProbability3D.Length, 0, 1);
            return GetQuantumGradientColor(normalizedProb);
        }

        // Fallback: use node state as proxy for quantum probability (matching standalone)
        if (_csrNodeStates is not null && nodeIndex >= 0 && nodeIndex < _csrNodeStates.Length)
        {
            double normalizedProb = _csrNodeStates[nodeIndex] switch
            {
                NodeState.Excited => 0.9,
                NodeState.Refractory => 0.5,
                NodeState.Rest => 0.2,
                _ => 0.1
            };
            return GetQuantumGradientColor(normalizedProb);
        }

        return new Vector4(0.2f, 0.2f, 0.2f, 1f);
    }

    /// <summary>
    /// Computes quantum gradient color from normalized probability.
    /// Blue (0) -> Cyan (0.25) -> Green (0.5) -> Yellow (0.75) -> Red (1)
    /// </summary>
    private static Vector4 GetQuantumGradientColor(double normalizedProb)
    {
        float r, g, b;
        if (normalizedProb < 0.25)
        {
            float t =  (float)(normalizedProb / 0.25);
            r = 0f;
            g = t;
            b = 1f;
        }
        else if (normalizedProb < 0.5)
        {
            float t =  (float)((normalizedProb - 0.25) / 0.25);
            r = 0f;
            g = 1f;
            b = 1f - t;
        }
        else if (normalizedProb < 0.75)
        {
            float t =  (float)((normalizedProb - 0.5) / 0.25);
            r = t;
            g = 1f;
            b = 0f;
        }
        else
        {
            float t =  (float)((normalizedProb - 0.75) / 0.25);
            r = 1f;
            g = 1f - t;
            b = 0f;
        }

        return new Vector4(r, g, b, 1f);
    }

    /// <summary>
    /// Probability Density mode: Color by node state (Excited/Rest/Refractory).
    /// Similar to "State" mode in GDI+.
    /// </summary>
    private Vector4 GetCsrProbabilityDensityColor(int nodeIndex)
    {
        if (_csrNodeStates is null || nodeIndex < 0 || nodeIndex >= _csrNodeStates.Length)
        {
            return new Vector4(0.5f, 0.5f, 0.5f, 1f);
        }

        return _csrNodeStates[nodeIndex] switch
        {
            NodeState.Excited => new Vector4(1f, 0.2f, 0.2f, 1f),    // Bright red
            NodeState.Refractory => new Vector4(0.8f, 0.4f, 1f, 1f), // Purple
            NodeState.Rest => new Vector4(0.2f, 1f, 0.2f, 1f),       // Green
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1f)
        };
    }

    /// <summary>
    /// Curvature mode: Color based on local connectivity as proxy for Ricci curvature.
    /// High connectivity (positive curvature) = red, flat = green, low = blue.
    /// </summary>
    private Vector4 GetCsrCurvatureColor(int nodeIndex)
    {
        if (_csrEdgeList is null || _csrNodeCount == 0)
        {
            return new Vector4(0.5f, 0.5f, 0.5f, 1f);
        }

        // Count edges connected to this node
        int edgeCount = 0;
        float totalWeight = 0f;
        foreach (var (u, v, w) in _csrEdgeList)
        {
            if (u == nodeIndex || v == nodeIndex)
            {
                edgeCount++;
                totalWeight += w;
            }
        }

        // Normalize by expected average degree
        float avgDegree = (float)_csrEdgeCount * 2f / Math.Max(_csrNodeCount, 1);
        float curvatureProxy = edgeCount / Math.Max(avgDegree, 1f);
        float normalized = Math.Clamp(curvatureProxy, 0f, 2f) / 2f;

        // Blue (low curvature) -> Green (flat) -> Red (high curvature)
        float r = normalized;
        float g = 1f - Math.Abs(normalized - 0.5f) * 2f;
        float b = 1f - normalized;

        return new Vector4(r, g, b, 1f);
    }

    /// <summary>
    /// Gravity Heatmap mode: Color based on edge weight sum (gravitational potential proxy).
    /// Dark blue (low gravity) -> Light blue -> White -> Yellow -> Red (high gravity).
    /// </summary>
    private Vector4 GetCsrGravityHeatmapColor(int nodeIndex)
    {
        if (_csrEdgeList is null || _csrNodeCount == 0)
        {
            return new Vector4(0.1f, 0.1f, 0.3f, 1f);
        }

        // Sum of edge weights = gravitational potential proxy
        float totalWeight = 0f;
        foreach (var (u, v, w) in _csrEdgeList)
        {
            if (u == nodeIndex || v == nodeIndex)
            {
                totalWeight += w;
            }
        }

        // Normalize (assume max weight sum around 5-10)
        float normalized = Math.Clamp(totalWeight / 5f, 0f, 1f);

        // Heatmap: Dark blue -> Cyan -> White -> Yellow -> Red
        float r, g, b;
        if (normalized < 0.25f)
        {
            float t = normalized / 0.25f;
            r = t * 0.2f;
            g = t * 0.5f;
            b = 0.3f + t * 0.5f;
        }
        else if (normalized < 0.5f)
        {
            float t = (normalized - 0.25f) / 0.25f;
            r = 0.2f + t * 0.8f;
            g = 0.5f + t * 0.5f;
            b = 0.8f + t * 0.2f;
        }
        else if (normalized < 0.75f)
        {
            float t = (normalized - 0.5f) / 0.25f;
            r = 1f;
            g = 1f;
            b = 1f - t;
        }
        else
        {
            float t = (normalized - 0.75f) / 0.25f;
            r = 1f;
            g = 1f - t * 0.7f;
            b = 0f;
        }

        return new Vector4(r, g, b, 1f);
    }

    /// <summary>
    /// Network Topology mode: Color by depth (distance) in spectral coordinates.
    /// Similar to "Depth" mode in GDI+.
    /// </summary>
    private Vector4 GetCsrNetworkTopologyColor(int nodeIndex)
    {
        if (_csrNodeX is null || _csrNodeY is null || _csrNodeZ is null ||
            nodeIndex < 0 || nodeIndex >= _csrNodeCount)
        {
            return new Vector4(0.4f, 0.8f, 1f, 1f);
        }

        // Calculate distance from center
        float x = _csrNodeX[nodeIndex];
        float y = _csrNodeY![nodeIndex];
        float z = _csrNodeZ![nodeIndex];
        float dist = MathF.Sqrt(x * x + y * y + z * z);

        // Normalize distance
        float maxDist = 20f;
        float normalized = Math.Clamp(dist / maxDist, 0f, 1f);

        // Center: bright cyan -> Edge: dim blue-purple
        float r = normalized * 0.5f;
        float g = 0.5f + (1f - normalized) * 0.5f;
        float b = 1f - normalized * 0.3f;

        return new Vector4(r, g, b, 1f);
    }

    /// <summary>
    /// Clusters mode: Color by cluster assignment.
    /// Uses a palette of distinct colors for different clusters.
    /// Falls back to position-based clustering if cluster data unavailable.
    /// </summary>
    private Vector4 GetCsrClustersColor(int nodeIndex)
    {
        int clusterId;
        
        // Try to use actual cluster IDs first
        if (_csrClusterIds is not null && nodeIndex >= 0 && nodeIndex < _csrClusterIds.Length && _csrClusterIds[nodeIndex] >= 0)
        {
            clusterId = _csrClusterIds[nodeIndex];
        }
        else
        {
            // Fallback: position-based clustering (matching standalone behavior)
            float x = _csrNodeX?[nodeIndex] ?? 0;
            float y = _csrNodeY?[nodeIndex] ?? 0;
            float z = _csrNodeZ?[nodeIndex] ?? 0;
            clusterId = Math.Abs((int)(x * 2 + y * 3 + z * 5)) % 8;
        }

        return GetCsrClusterPaletteColor(clusterId);
    }

    /// <summary>
    /// Gets color from cluster palette by ID.
    /// </summary>
    private static Vector4 GetCsrClusterPaletteColor(int clusterId)
    {
        // Color palette for clusters
        Vector4[] palette =
        [
            new Vector4(0.0f, 1.0f, 0.0f, 1f),  // Lime
            new Vector4(1.0f, 1.0f, 0.0f, 1f),  // Yellow
            new Vector4(1.0f, 0.0f, 1.0f, 1f),  // Magenta
            new Vector4(1.0f, 0.5f, 0.0f, 1f),  // Orange
            new Vector4(0.0f, 1.0f, 1.0f, 1f),  // Cyan
            new Vector4(1.0f, 0.75f, 0.8f, 1f), // Pink
            new Vector4(0.5f, 0.0f, 1.0f, 1f),  // Violet
            new Vector4(0.0f, 0.5f, 1.0f, 1f),  // Sky blue
        ];

        return palette[clusterId % palette.Length];
    }
}
