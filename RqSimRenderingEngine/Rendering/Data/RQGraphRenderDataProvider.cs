using System;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RQSimulation;

namespace RqSimRenderingEngine.Rendering.Data;

/// <summary>
/// Provides render data from RQGraph (dense adjacency matrix).
/// </summary>
public sealed class RQGraphRenderDataProvider : IGraphRenderDataProvider
{
    private readonly RenderDataExtractor _extractor;
    private RQGraph? _graph;
    private bool _hasData;

    public RQGraphRenderDataProvider()
    {
        _extractor = new RenderDataExtractor();
    }

    public RQGraphRenderDataProvider(RenderDataConfig config) : this()
    {
        ApplyConfig(config);
    }

    /// <summary>
    /// Set the graph to extract data from.
    /// </summary>
    public void SetGraph(RQGraph? graph)
    {
        _graph = graph;
        _hasData = false;
    }

    /// <summary>
    /// Apply render configuration.
    /// </summary>
    public void ApplyConfig(RenderDataConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _extractor.NodeRadius = config.NodeRadius;
        _extractor.RestColor = config.RestColor;
        _extractor.ExcitedColor = config.ExcitedColor;
        _extractor.RefractoryColor = config.RefractoryColor;
        _extractor.EdgeColor = config.EdgeColor;
        _extractor.UseEnergyColoring = config.UseEnergyColoring;
        _extractor.UseMassRadius = config.UseMassRadius;
    }

    public int NodeCount => _extractor.NodeCount;

    public int EdgeVertexCount => _extractor.EdgeVertexCount;

    public bool HasData => _hasData;

    public Dx12NodeInstance[] NodeInstances => _extractor.NodeInstances;

    public Dx12LineVertex[] EdgeVertices => _extractor.EdgeVertices;

    public bool Extract()
    {
        if (_graph is null)
        {
            _hasData = false;
            return false;
        }

        try
        {
            _extractor.Extract(_graph);
            _hasData = _extractor.NodeCount > 0;
            return _hasData;
        }
        catch
        {
            _hasData = false;
            return false;
        }
    }

    public int GetNodeInstances(Span<Dx12NodeInstance> instances)
    {
        if (!_hasData)
            return 0;

        int count = Math.Min(_extractor.NodeCount, instances.Length);
        _extractor.NodeInstances.AsSpan(0, count).CopyTo(instances);
        return count;
    }

    public int GetEdgeVertices(Span<Dx12LineVertex> vertices)
    {
        if (!_hasData)
            return 0;

        int count = Math.Min(_extractor.EdgeVertexCount, vertices.Length);
        _extractor.EdgeVertices.AsSpan(0, count).CopyTo(vertices);
        return count;
    }
}
