using System.Numerics;

namespace RqSimVisualization;

/// <summary>
/// CSR visualization edge styling logic.
/// Provides edge color and styling based on selected CsrVisualizationMode.
/// </summary>
public partial class RqSimVisualizationForm
{
    /// <summary>
    /// Gets the color and thickness for an edge based on the current CSR visualization mode.
    /// </summary>
    /// <param name="u">Source node index.</param>
    /// <param name="v">Target node index.</param>
    /// <param name="weight">Edge weight.</param>
    /// <returns>Tuple of (color as Vector4, thickness).</returns>
    private (Vector4 Color, float Thickness) GetCsrEdgeStyle(int u, int v, float weight)
    {
        return _csrVisMode switch
        {
            CsrVisualizationMode.QuantumPhase => GetCsrQuantumPhaseEdgeStyle(weight),
            CsrVisualizationMode.ProbabilityDensity => GetCsrStateEdgeStyle(u, v, weight),
            CsrVisualizationMode.Curvature => GetCsrCurvatureEdgeStyle(weight),
            CsrVisualizationMode.GravityHeatmap => GetCsrGravityEdgeStyle(weight),
            CsrVisualizationMode.NetworkTopology => GetCsrTopologyEdgeStyle(weight),
            CsrVisualizationMode.Clusters => GetCsrClustersEdgeStyle(u, v, weight),
            _ => (new Vector4(0.3f, 0.5f, 0.8f, 0.4f), 1f)
        };
    }

    /// <summary>
    /// Quantum Phase mode edges: Color by weight intensity.
    /// Higher weight = brighter, more saturated.
    /// </summary>
    private (Vector4 Color, float Thickness) GetCsrQuantumPhaseEdgeStyle(float weight)
    {
        float normalized = Math.Clamp(weight, 0.1f, 1f);
        float alpha = 0.3f + normalized * 0.4f;

        // Purple-ish quantum style
        var color = new Vector4(0.6f * normalized, 0.3f, 0.8f * normalized, alpha);
        float thickness = 0.5f + normalized * 1.5f;

        return (color, thickness);
    }

    /// <summary>
    /// State/Probability Density mode edges: Color based on endpoint states.
    /// </summary>
    private (Vector4 Color, float Thickness) GetCsrStateEdgeStyle(int u, int v, float weight)
    {
        if (_csrNodeStates is null || u >= _csrNodeStates.Length || v >= _csrNodeStates.Length)
        {
            return (new Vector4(0.3f, 0.3f, 0.3f, 0.3f), 1f);
        }

        var stateU = _csrNodeStates[u];
        var stateV = _csrNodeStates[v];

        // Both excited = red edge, both rest = green, mixed = gray
        Vector4 color;
        if (stateU == RQSimulation.NodeState.Excited && stateV == RQSimulation.NodeState.Excited)
        {
            color = new Vector4(1f, 0.3f, 0.3f, 0.5f);
        }
        else if (stateU == RQSimulation.NodeState.Rest && stateV == RQSimulation.NodeState.Rest)
        {
            color = new Vector4(0.3f, 0.8f, 0.3f, 0.4f);
        }
        else
        {
            color = new Vector4(0.5f, 0.5f, 0.5f, 0.3f);
        }

        float thickness = 0.5f + weight;
        return (color, thickness);
    }

    /// <summary>
    /// Curvature mode edges: Color by weight indicating local curvature contribution.
    /// </summary>
    private (Vector4 Color, float Thickness) GetCsrCurvatureEdgeStyle(float weight)
    {
        float normalized = Math.Clamp(weight, 0f, 1f);

        // Blue (weak) -> Green -> Red (strong curvature connection)
        float r = normalized;
        float g = 1f - Math.Abs(normalized - 0.5f) * 2f;
        float b = 1f - normalized;
        float alpha = 0.2f + normalized * 0.5f;

        return (new Vector4(r, g, b, alpha), 0.5f + normalized * 1.5f);
    }

    /// <summary>
    /// Gravity Heatmap mode edges: Show gravitational interaction strength.
    /// </summary>
    private (Vector4 Color, float Thickness) GetCsrGravityEdgeStyle(float weight)
    {
        float normalized = Math.Clamp(weight, 0f, 1f);

        // Dark blue -> Bright yellow for strong gravity
        float r = normalized;
        float g = normalized * 0.8f;
        float b = 0.3f + (1f - normalized) * 0.5f;
        float alpha = 0.2f + normalized * 0.6f;

        return (new Vector4(r, g, b, alpha), 0.5f + normalized * 2f);
    }

    /// <summary>
    /// Network Topology mode edges: Standard visualization by weight.
    /// </summary>
    private (Vector4 Color, float Thickness) GetCsrTopologyEdgeStyle(float weight)
    {
        float normalized = Math.Clamp(weight, 0.1f, 1f);
        float alpha = 0.2f + normalized * 0.3f;

        // Cyan-ish network style
        var color = new Vector4(0.2f, 0.6f * normalized, 0.8f, alpha);
        return (color, 0.5f + normalized);
    }

    /// <summary>
    /// Clusters mode edges: Highlight intra-cluster connections.
    /// </summary>
    private (Vector4 Color, float Thickness) GetCsrClustersEdgeStyle(int u, int v, float weight)
    {
        int clusterU = GetCsrNodeClusterId(u);
        int clusterV = GetCsrNodeClusterId(v);

        if (clusterU >= 0 && clusterU == clusterV)
        {
            // Same cluster - use cluster color, thicker edge
            var clusterColor = GetCsrClusterPaletteColor(clusterU);
            return (clusterColor with { W = 0.6f }, 1.5f + weight);
        }
        else
        {
            // Cross-cluster or unassigned - dim gray
            return (new Vector4(0.3f, 0.3f, 0.3f, 0.15f), 0.5f);
        }
    }

    /// <summary>
    /// Gets the cluster ID for a node, with position-based fallback.
    /// </summary>
    private int GetCsrNodeClusterId(int nodeIndex)
    {
        // Use actual cluster IDs if available and valid
        if (_csrClusterIds is not null && nodeIndex >= 0 && nodeIndex < _csrClusterIds.Length && _csrClusterIds[nodeIndex] >= 0)
        {
            return _csrClusterIds[nodeIndex];
        }

        // Fallback: position-based clustering (matching standalone behavior)
        float x = _csrNodeX?[nodeIndex] ?? 0;
        float y = _csrNodeY?[nodeIndex] ?? 0;
        float z = _csrNodeZ?[nodeIndex] ?? 0;
        return Math.Abs((int)(x * 2 + y * 3 + z * 5)) % 8;
    }
}
