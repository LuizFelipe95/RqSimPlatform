
using System.Numerics;
using RQSimulation;

namespace RqSim3DForm;

public partial class Form_Rsim3DForm
{
    private int[]? _clusterIds;

    private Vector4 GetNodeColor(int index)
    {
        return _visMode switch
        {
            VisualizationMode.QuantumPhase => GetQuantumPhaseColor(index),
            VisualizationMode.ProbabilityDensity => GetProbabilityDensityColor(index),
            VisualizationMode.Curvature => GetCurvatureColor(index),
            VisualizationMode.GravityHeatmap => GetGravityHeatmapColor(index),
            VisualizationMode.NetworkTopology => GetNetworkTopologyColor(index),
            VisualizationMode.Clusters => GetClustersColor(index),
            _ => new Vector4(1f, 1f, 1f, 1f)
        };
    }

    private Vector4 GetQuantumPhaseColor(int index)
    {
        if (_nodeStates is null || index >= _nodeStates.Length)
            return new Vector4(0.2f, 0.2f, 0.2f, 1f);
        double normalizedProb = _nodeStates[index] switch { NodeState.Excited => 0.9, NodeState.Refractory => 0.5, NodeState.Rest => 0.2, _ => 0.1 };
        float r, g, b;
        if (normalizedProb < 0.25) { float t = (float)(normalizedProb / 0.25); r = 0f; g = t; b = 1f; }
        else if (normalizedProb < 0.5) { float t = (float)((normalizedProb - 0.25) / 0.25); r = 0f; g = 1f; b = 1f - t; }
        else if (normalizedProb < 0.75) { float t = (float)((normalizedProb - 0.5) / 0.25); r = t; g = 1f; b = 0f; }
        else { float t = (float)((normalizedProb - 0.75) / 0.25); r = 1f; g = 1f - t; b = 0f; }
        return new Vector4(r, g, b, 1f);
    }

    private Vector4 GetProbabilityDensityColor(int index)
    {
        if (_nodeStates is null || index >= _nodeStates.Length) return new Vector4(0.5f, 0.5f, 0.5f, 1f);
        return _nodeStates[index] switch
        {
            NodeState.Excited => new Vector4(1f, 0.2f, 0.2f, 1f),
            NodeState.Refractory => new Vector4(0.8f, 0.4f, 1f, 1f),
            NodeState.Rest => new Vector4(0.2f, 1f, 0.2f, 1f),
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1f)
        };
    }

    private Vector4 GetCurvatureColor(int index)
    {
        if (_edges is null || _nodeCount == 0) return new Vector4(0.5f, 0.5f, 0.5f, 1f);
        int edgeCount = 0;
        foreach (var (u, v, w) in _edges) { if (u == index || v == index) edgeCount++; }
        float avgDegree = _edges.Count * 2f / Math.Max(_nodeCount, 1);
        float normalized = Math.Clamp(edgeCount / Math.Max(avgDegree, 1f), 0f, 2f) / 2f;
        return new Vector4(normalized, 1f - Math.Abs(normalized - 0.5f) * 2f, 1f - normalized, 1f);
    }

    private Vector4 GetGravityHeatmapColor(int index)
    {
        if (_edges is null || _nodeCount == 0) return new Vector4(0.1f, 0.1f, 0.3f, 1f);
        float totalWeight = 0f;
        foreach (var (u, v, w) in _edges) { if (u == index || v == index) totalWeight += w; }
        float n = Math.Clamp(totalWeight / 5f, 0f, 1f);
        float r, g, b;
        if (n < 0.25f) { r = n * 0.8f; g = n * 2f; b = 0.3f + n * 2f; }
        else if (n < 0.5f) { float t = (n - 0.25f) / 0.25f; r = 0.2f + t * 0.8f; g = 0.5f + t * 0.5f; b = 0.8f + t * 0.2f; }
        else if (n < 0.75f) { float t = (n - 0.5f) / 0.25f; r = 1f; g = 1f; b = 1f - t; }
        else { float t = (n - 0.75f) / 0.25f; r = 1f; g = 1f - t * 0.7f; b = 0f; }
        return new Vector4(r, g, b, 1f);
    }

    private Vector4 GetNetworkTopologyColor(int index)
    {
        if (_nodeX is null || _nodeY is null || _nodeZ is null || index >= _nodeCount) return new Vector4(0.4f, 0.8f, 1f, 1f);
        float dist = MathF.Sqrt(_nodeX[index] * _nodeX[index] + _nodeY[index] * _nodeY[index] + _nodeZ[index] * _nodeZ[index]);
        float n = Math.Clamp(dist / 20f, 0f, 1f);
        return new Vector4(n * 0.5f, 0.5f + (1f - n) * 0.5f, 1f - n * 0.3f, 1f);
    }

    private Vector4 GetClustersColor(int index)
    {
        int clusterId = (_clusterIds is not null && index < _clusterIds.Length && _clusterIds[index] >= 0)
            ? _clusterIds[index]
            : Math.Abs((int)((_nodeX?[index] ?? 0) * 2 + (_nodeY?[index] ?? 0) * 3 + (_nodeZ?[index] ?? 0) * 5)) % 8;
        return GetClusterPaletteColor(clusterId);
    }

    private Vector4 GetClusterPaletteColor(int id)
    {
        Vector4[] palette = [new(0f, 1f, 0f, 1f), new(1f, 1f, 0f, 1f), new(1f, 0f, 1f, 1f), new(1f, 0.5f, 0f, 1f), new(0f, 1f, 1f, 1f), new(1f, 0.75f, 0.8f, 1f), new(0.5f, 0f, 1f, 1f), new(0f, 0.5f, 1f, 1f)];
        return palette[id % 8];
    }

    private (Vector4 Color, float Thickness) GetEdgeStyle(int u, int v, float weight)
    {
        float w = Math.Clamp(weight, 0.1f, 1f);
        return _visMode switch
        {
            VisualizationMode.QuantumPhase => (new Vector4(0.6f * w, 0.3f, 0.8f * w, 0.3f + w * 0.4f), 0.5f + w * 1.5f),
            VisualizationMode.ProbabilityDensity => GetStateEdgeStyle(u, v, w),
            VisualizationMode.Curvature => (new Vector4(w, 1f - Math.Abs(w - 0.5f) * 2f, 1f - w, 0.2f + w * 0.5f), 0.5f + w * 1.5f),
            VisualizationMode.GravityHeatmap => (new Vector4(w, w * 0.8f, 0.3f + (1f - w) * 0.5f, 0.2f + w * 0.6f), 0.5f + w * 2f),
            VisualizationMode.NetworkTopology => (new Vector4(0.2f, 0.6f * w, 0.8f, 0.2f + w * 0.3f), 0.5f + w),
            VisualizationMode.Clusters => GetClustersEdgeStyle(u, v, w),
            _ => (new Vector4(0.3f, 0.5f, 0.8f, 0.4f), 1f)
        };
    }

    private (Vector4 Color, float Thickness) GetStateEdgeStyle(int u, int v, float weight)
    {
        if (_nodeStates is null || u >= _nodeStates.Length || v >= _nodeStates.Length) return (new Vector4(0.3f, 0.3f, 0.3f, 0.3f), 1f);
        var sU = _nodeStates[u]; var sV = _nodeStates[v];
        if (sU == NodeState.Excited && sV == NodeState.Excited) return (new Vector4(1f, 0.3f, 0.3f, 0.5f), 0.5f + weight);
        if (sU == NodeState.Rest && sV == NodeState.Rest) return (new Vector4(0.3f, 0.8f, 0.3f, 0.4f), 0.5f + weight);
        return (new Vector4(0.5f, 0.5f, 0.5f, 0.3f), 0.5f + weight);
    }

    private (Vector4 Color, float Thickness) GetClustersEdgeStyle(int u, int v, float weight)
    {
        int cU = GetNodeClusterId(u), cV = GetNodeClusterId(v);
        if (cU >= 0 && cU == cV) { var c = GetClusterPaletteColor(cU); return (c with { W = 0.6f }, 1.5f + weight); }
        return (new Vector4(0.3f, 0.3f, 0.3f, 0.15f), 0.5f);
    }

    private int GetNodeClusterId(int i) => (_clusterIds is not null && i < _clusterIds.Length) ? _clusterIds[i] : Math.Abs((int)((_nodeX?[i] ?? 0) * 2 + (_nodeY?[i] ?? 0) * 3 + (_nodeZ?[i] ?? 0) * 5)) % 8;
}