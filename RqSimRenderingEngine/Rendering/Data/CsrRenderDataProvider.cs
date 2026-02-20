using System;
using System.Numerics;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RqSimRenderingEngine.Rendering.Data;
using RQSimulation.GPUCompressedSparseRow.Data;
using RqSimUI.Rendering.Interop;
using RQSimulation.GPUOptimized.Rendering;

namespace RqSimUI.Rendering.Data;

/// <summary>
/// Provides render data from CSR topology (sparse format).
/// Optimized for large sparse graphs.
/// </summary>
public sealed class CsrRenderDataProvider : IGraphRenderDataProvider
{
    private readonly RenderDataExtractor _extractor;
    private CsrTopology? _topology;
    private double[]? _curvatures;
    private double[]? _masses;
    private Vector3[]? _positions;
    private double _weightThreshold;
    private bool _hasData;

    // Optional CSR unified engine instance to access GPU buffers
    private readonly RQSimulation.GPUCompressedSparseRow.Unified.CsrUnifiedEngine? _engine;

    public CsrRenderDataProvider()
    {
        _extractor = new RenderDataExtractor();
    }

    public CsrRenderDataProvider(RenderDataConfig config) : this()
    {
        ApplyConfig(config);
    }

    public CsrRenderDataProvider(RQSimulation.GPUCompressedSparseRow.Unified.CsrUnifiedEngine engine) : this()
    {
        _engine = engine;
    }

    /// <summary>
    /// Set the CSR topology to extract data from.
    /// </summary>
    public void SetTopology(CsrTopology? topology)
    {
        _topology = topology;
        _hasData = false;
    }

    /// <summary>
    /// Set curvature data for node coloring.
    /// </summary>
    public void SetCurvatures(double[]? curvatures)
    {
        _curvatures = curvatures;
    }

    /// <summary>
    /// Set mass data for node sizing.
    /// </summary>
    public void SetMasses(double[]? masses)
    {
        _masses = masses;
    }

    /// <summary>
    /// Set 3D node positions for rendering.
    /// </summary>
    public void SetPositions(Vector3[]? positions)
    {
        _positions = positions;
        _extractor.SetNodePositions3D(positions);
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
        _extractor.UseCurvatureColoring = config.UseCurvatureColoring;
        _weightThreshold = config.EdgeWeightThreshold;
    }

    public int NodeCount => _extractor.NodeCount;

    public int EdgeVertexCount => _extractor.EdgeVertexCount;

    public bool HasData => _hasData;

    public Dx12NodeInstance[] NodeInstances => _extractor.NodeInstances;

    public Dx12LineVertex[] EdgeVertices => _extractor.EdgeVertices;

    public bool Extract()
    {
        if (_topology is null)
        {
            _hasData = false;
            return false;
        }

        try
        {
            _extractor.ExtractFromCsr(_topology, _curvatures, _masses, _weightThreshold);
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

    /// <summary>
    /// If a CSR unified engine was provided, return a shared GPU buffer wrapper
    /// that exposes the ComputeSharp buffer as a DX12-friendly vertex buffer.
    /// Returns null if no engine or buffer is available.
    /// </summary>
    public SharedGpuBuffer<RenderNodeVertex>? GetVertexBuffer()
    {
        if (_engine is null)
            return null;

        var buf = _engine.GetRenderBufferInterop();
        if (buf is null)
            return null;

        // Wrap existing ComputeSharp buffer into SharedGpuBuffer for interop
        return new SharedGpuBuffer<RenderNodeVertex>(buf, SharedBufferUsage.Default);
    }
}
