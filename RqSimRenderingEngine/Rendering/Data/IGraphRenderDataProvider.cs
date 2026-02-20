using System.Numerics;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

namespace RqSimRenderingEngine.Rendering.Data;

/// <summary>
/// Interface for providing render data from different graph sources.
/// Abstracts the data source (RQGraph, CSR, ECS) from the renderer.
/// </summary>
public interface IGraphRenderDataProvider
{
    /// <summary>
    /// Number of nodes to render.
    /// </summary>
    int NodeCount { get; }

    /// <summary>
    /// Number of edge vertices to render (2 per edge).
    /// </summary>
    int EdgeVertexCount { get; }

    /// <summary>
    /// Whether data has been extracted and is ready for rendering.
    /// </summary>
    bool HasData { get; }

    /// <summary>
    /// Extract render data from the current graph state.
    /// </summary>
    /// <returns>True if extraction succeeded</returns>
    bool Extract();

    /// <summary>
    /// Get node instance data for DX12 rendering.
    /// </summary>
    /// <param name="instances">Output span to fill</param>
    /// <returns>Number of instances written</returns>
    int GetNodeInstances(Span<Dx12NodeInstance> instances);

    /// <summary>
    /// Get edge vertex data for DX12 rendering.
    /// </summary>
    /// <param name="vertices">Output span to fill</param>
    /// <returns>Number of vertices written</returns>
    int GetEdgeVertices(Span<Dx12LineVertex> vertices);

    /// <summary>
    /// Get the internal node instances array directly.
    /// </summary>
    Dx12NodeInstance[] NodeInstances { get; }

    /// <summary>
    /// Get the internal edge vertices array directly.
    /// </summary>
    Dx12LineVertex[] EdgeVertices { get; }
}

/// <summary>
/// Configuration for render data extraction.
/// </summary>
public sealed class RenderDataConfig
{
    /// <summary>
    /// Default node radius.
    /// </summary>
    public float NodeRadius { get; set; } = 0.05f;

    /// <summary>
    /// Color for Rest state nodes.
    /// </summary>
    public Vector4 RestColor { get; set; } = new(0.2f, 0.6f, 1.0f, 1.0f);

    /// <summary>
    /// Color for Excited state nodes.
    /// </summary>
    public Vector4 ExcitedColor { get; set; } = new(1.0f, 0.3f, 0.2f, 1.0f);

    /// <summary>
    /// Color for Refractory state nodes.
    /// </summary>
    public Vector4 RefractoryColor { get; set; } = new(0.3f, 0.9f, 0.3f, 1.0f);

    /// <summary>
    /// Default edge color.
    /// </summary>
    public Vector4 EdgeColor { get; set; } = new(0.4f, 0.4f, 0.4f, 0.6f);

    /// <summary>
    /// Whether to color nodes based on energy.
    /// </summary>
    public bool UseEnergyColoring { get; set; }

    /// <summary>
    /// Whether to vary node radius based on mass.
    /// </summary>
    public bool UseMassRadius { get; set; }

    /// <summary>
    /// Whether to use curvature-based coloring (CSR mode).
    /// </summary>
    public bool UseCurvatureColoring { get; set; }

    /// <summary>
    /// Minimum edge weight to render.
    /// </summary>
    public double EdgeWeightThreshold { get; set; }
}
