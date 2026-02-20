using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RQSimulation;
using RQSimulation.GPUCompressedSparseRow.Data;

namespace RqSimRenderingEngine.Rendering.Data;

/// <summary>
/// Extracts render data from RQGraph or CSR topology for DX12 rendering.
/// Converts graph nodes and edges to Dx12NodeInstance[] and Dx12LineVertex[].
/// 
/// USAGE:
/// 1. Call EnsureCapacity() when graph size changes
/// 2. Call ExtractNodes() and ExtractEdges() each frame
/// 3. Pass arrays to Dx12RenderHost.SetNodeInstances/SetEdgeVertices
/// 
/// Supports two data sources:
/// - RQGraph (dense adjacency matrix, 2D coordinates)
/// - CsrTopology (sparse format, optional 3D positions)
/// </summary>
public sealed class RenderDataExtractor
{
    private Dx12NodeInstance[] _nodeInstances = [];
    private Dx12LineVertex[] _edgeVertices = [];

    private int _lastNodeCount;
    private int _lastEdgeVertexCount;

    // Cached 3D positions for CSR mode
    private Vector3[]? _nodePositions3D;

    /// <summary>
    /// Node instance array for DX12 rendering.
    /// </summary>
    public Dx12NodeInstance[] NodeInstances => _nodeInstances;

    /// <summary>
    /// Number of valid node instances.
    /// </summary>
    public int NodeCount => _lastNodeCount;

    /// <summary>
    /// Edge vertex array for DX12 rendering (2 vertices per edge).
    /// </summary>
    public Dx12LineVertex[] EdgeVertices => _edgeVertices;

    /// <summary>
    /// Number of valid edge vertices.
    /// </summary>
    public int EdgeVertexCount => _lastEdgeVertexCount;

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
    /// Whether to color nodes based on energy instead of state.
    /// </summary>
    public bool UseEnergyColoring { get; set; }

    /// <summary>
    /// Whether to vary node radius based on mass.
    /// </summary>
    public bool UseMassRadius { get; set; }

    /// <summary>
    /// Whether to use curvature-based coloring (for CSR mode).
    /// </summary>
    public bool UseCurvatureColoring { get; set; }

    /// <summary>
    /// Ensure arrays have sufficient capacity for the graph.
    /// </summary>
    public void EnsureCapacity(int nodeCount, int maxEdgeCount)
    {
        if (_nodeInstances.Length < nodeCount)
        {
            _nodeInstances = new Dx12NodeInstance[Math.Max(nodeCount, 64)];
        }

        int edgeVertexCount = maxEdgeCount * 2;
        if (_edgeVertices.Length < edgeVertexCount)
        {
            _edgeVertices = new Dx12LineVertex[Math.Max(edgeVertexCount, 128)];
        }
    }

    /// <summary>
    /// Set 3D node positions for CSR mode rendering.
    /// </summary>
    /// <param name="positions">Array of 3D positions, one per node</param>
    public void SetNodePositions3D(Vector3[]? positions)
    {
        _nodePositions3D = positions;
    }

    /// <summary>
    /// Extract node instances from RQGraph.
    /// </summary>
    /// <param name="graph">Source graph</param>
    /// <returns>Number of nodes extracted</returns>
    public int ExtractNodes(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        int nodeCount = graph.N;
        EnsureCapacity(nodeCount, nodeCount * 4); // Estimate edges

        var states = graph.State;

#pragma warning disable CS0618 // Coordinates is for rendering
        var coordinates = graph.Coordinates;
#pragma warning restore CS0618

        _lastNodeCount = 0;

        for (int i = 0; i < nodeCount; i++)
        {
            float x = 0f, y = 0f, z = 0f;
            
            // Prefer 3D positions if available (CSR mode)
            if (_nodePositions3D is not null && i < _nodePositions3D.Length)
            {
                var pos = _nodePositions3D[i];
                x = pos.X;
                y = pos.Y;
                z = pos.Z;
            }
            else if (coordinates is not null && i < coordinates.Length)
            {
                x = (float)coordinates[i].X;
                y = (float)coordinates[i].Y;
            }

            float radius = NodeRadius;
            if (UseMassRadius && graph.PhysicsProperties is not null && i < graph.PhysicsProperties.Length)
            {
                float mass = (float)graph.PhysicsProperties[i].Mass;
                radius = NodeRadius * (1f + mass * 0.1f);
            }

            Vector4 color = GetNodeColor(graph, i, states);

            _nodeInstances[_lastNodeCount++] = new Dx12NodeInstance(
                new Vector3(x, y, z),
                radius,
                color);
        }

        return _lastNodeCount;
    }

    /// <summary>
    /// Extract node instances from CSR topology with curvature data.
    /// </summary>
    /// <param name="topology">CSR topology for structure</param>
    /// <param name="curvatures">Optional curvature array for coloring</param>
    /// <param name="masses">Optional mass array for radius</param>
    /// <returns>Number of nodes extracted</returns>
    public int ExtractNodesFromCsr(CsrTopology topology, double[]? curvatures = null, double[]? masses = null)
    {
        ArgumentNullException.ThrowIfNull(topology);

        int nodeCount = topology.NodeCount;
        int edgeCount = topology.Nnz / 2; // Undirected: each edge stored twice
        EnsureCapacity(nodeCount, edgeCount);

        _lastNodeCount = 0;

        for (int i = 0; i < nodeCount; i++)
        {
            Vector3 position = Vector3.Zero;
            
            if (_nodePositions3D is not null && i < _nodePositions3D.Length)
            {
                position = _nodePositions3D[i];
            }

            float radius = NodeRadius;
            if (UseMassRadius && masses is not null && i < masses.Length)
            {
                float mass = (float)masses[i];
                radius = NodeRadius * (1f + MathF.Abs(mass) * 0.1f);
            }

            Vector4 color = GetNodeColorFromCurvature(i, curvatures);

            _nodeInstances[_lastNodeCount++] = new Dx12NodeInstance(position, radius, color);
        }

        return _lastNodeCount;
    }

    /// <summary>
    /// Extract edge vertices from CSR topology.
    /// </summary>
    /// <param name="topology">CSR topology</param>
    /// <param name="weightThreshold">Minimum edge weight to include (default 0)</param>
    /// <returns>Number of edge vertices extracted</returns>
    public int ExtractEdgesFromCsr(CsrTopology topology, double weightThreshold = 0.0)
    {
        ArgumentNullException.ThrowIfNull(topology);

        int nodeCount = topology.NodeCount;
        var rowOffsets = topology.RowOffsets;
        var colIndices = topology.ColIndices;
        var edgeWeights = topology.EdgeWeights;

        _lastEdgeVertexCount = 0;

        for (int from = 0; from < nodeCount; from++)
        {
            int start = rowOffsets[from];
            int end = from + 1 < nodeCount + 1 ? rowOffsets[from + 1] : topology.Nnz;

            for (int k = start; k < end; k++)
            {
                int to = colIndices[k];
                
                // Only process each edge once (from < to)
                if (to <= from)
                    continue;

                double weight = k < edgeWeights.Length ? edgeWeights[k] : 1.0;
                if (weight < weightThreshold)
                    continue;

                AddEdgeVerticesCsr(from, to, (float)weight);
            }
        }

        return _lastEdgeVertexCount;
    }

    /// <summary>
    /// Extract edge vertices from RQGraph.
    /// </summary>
    /// <param name="graph">Source graph</param>
    /// <returns>Number of edge vertices extracted</returns>
    public int ExtractEdges(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        int nodeCount = graph.N;

#pragma warning disable CS0618 // Coordinates is for rendering
        var coordinates = graph.Coordinates;
#pragma warning restore CS0618

        _lastEdgeVertexCount = 0;

        // Use Neighbors() if available, otherwise fall back to Edges matrix
        for (int from = 0; from < nodeCount; from++)
        {
            foreach (int to in graph.Neighbors(from))
            {
                if (to <= from) // Each edge once
                    continue;

                AddEdgeVertices(coordinates, from, to, graph);
            }
        }

        return _lastEdgeVertexCount;
    }

    /// <summary>
    /// Extract both nodes and edges in one pass.
    /// </summary>
    public (int nodeCount, int edgeVertexCount) Extract(RQGraph graph)
    {
        int nodes = ExtractNodes(graph);
        int edges = ExtractEdges(graph);
        return (nodes, edges);
    }

    /// <summary>
    /// Extract both nodes and edges from CSR topology.
    /// </summary>
    public (int nodeCount, int edgeVertexCount) ExtractFromCsr(
        CsrTopology topology, 
        double[]? curvatures = null, 
        double[]? masses = null,
        double weightThreshold = 0.0)
    {
        int nodes = ExtractNodesFromCsr(topology, curvatures, masses);
        int edges = ExtractEdgesFromCsr(topology, weightThreshold);
        return (nodes, edges);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector4 GetNodeColor(RQGraph graph, int nodeIndex, NodeState[] states)
    {
        if (UseEnergyColoring && graph.NodeEnergy is not null && nodeIndex < graph.NodeEnergy.Length)
        {
            float energy = (float)graph.NodeEnergy[nodeIndex];
            float normalized = MathF.Tanh(energy * 0.5f) * 0.5f + 0.5f;
            return new Vector4(normalized, 0.3f, 1f - normalized, 1f);
        }

        var state = nodeIndex < states.Length ? states[nodeIndex] : NodeState.Rest;
        return state switch
        {
            NodeState.Rest => RestColor,
            NodeState.Excited => ExcitedColor,
            NodeState.Refractory => RefractoryColor,
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector4 GetNodeColorFromCurvature(int nodeIndex, double[]? curvatures)
    {
        if (!UseCurvatureColoring || curvatures is null || nodeIndex >= curvatures.Length)
        {
            return RestColor;
        }

        float curvature = (float)curvatures[nodeIndex];
        
        // Color mapping: negative (blue) -> zero (white) -> positive (red)
        float normalized = MathF.Tanh(curvature * 2f);
        
        if (normalized < 0)
        {
            // Negative curvature: blue
            return new Vector4(0.3f + 0.7f * (1f + normalized), 0.3f + 0.7f * (1f + normalized), 1f, 1f);
        }
        else
        {
            // Positive curvature: red
            return new Vector4(1f, 0.3f + 0.7f * (1f - normalized), 0.3f + 0.7f * (1f - normalized), 1f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddEdgeVertices((double X, double Y)[]? coordinates, int from, int to, RQGraph graph)
    {
        float x1 = 0f, y1 = 0f, z1 = 0f;
        float x2 = 0f, y2 = 0f, z2 = 0f;

        // Prefer 3D positions if available
        if (_nodePositions3D is not null)
        {
            if (from < _nodePositions3D.Length)
            {
                var pos = _nodePositions3D[from];
                x1 = pos.X; y1 = pos.Y; z1 = pos.Z;
            }
            if (to < _nodePositions3D.Length)
            {
                var pos = _nodePositions3D[to];
                x2 = pos.X; y2 = pos.Y; z2 = pos.Z;
            }
        }
        else if (coordinates is not null)
        {
            if (from < coordinates.Length)
            {
                x1 = (float)coordinates[from].X;
                y1 = (float)coordinates[from].Y;
            }
            if (to < coordinates.Length)
            {
                x2 = (float)coordinates[to].X;
                y2 = (float)coordinates[to].Y;
            }
        }

        // Optionally color edges by weight
        Vector4 color = EdgeColor;
        if (graph.Weights is not null)
        {
            double weight = graph.Weights[from, to];
            if (weight > 0)
            {
                float intensity = (float)Math.Clamp(weight, 0, 1);
                color = new Vector4(
                    EdgeColor.X * (0.5f + intensity * 0.5f),
                    EdgeColor.Y * (0.5f + intensity * 0.5f),
                    EdgeColor.Z * (0.5f + intensity * 0.5f),
                    EdgeColor.W);
            }
        }

        EnsureEdgeCapacity();

        _edgeVertices[_lastEdgeVertexCount++] = new Dx12LineVertex(new Vector3(x1, y1, z1), color);
        _edgeVertices[_lastEdgeVertexCount++] = new Dx12LineVertex(new Vector3(x2, y2, z2), color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddEdgeVerticesCsr(int from, int to, float weight)
    {
        Vector3 pos1 = Vector3.Zero;
        Vector3 pos2 = Vector3.Zero;

        if (_nodePositions3D is not null)
        {
            if (from < _nodePositions3D.Length)
                pos1 = _nodePositions3D[from];
            if (to < _nodePositions3D.Length)
                pos2 = _nodePositions3D[to];
        }

        // Color edges by weight
        float intensity = MathF.Min(weight, 1f);
        Vector4 color = new(
            EdgeColor.X * (0.5f + intensity * 0.5f),
            EdgeColor.Y * (0.5f + intensity * 0.5f),
            EdgeColor.Z * (0.5f + intensity * 0.5f),
            EdgeColor.W);

        EnsureEdgeCapacity();

        _edgeVertices[_lastEdgeVertexCount++] = new Dx12LineVertex(pos1, color);
        _edgeVertices[_lastEdgeVertexCount++] = new Dx12LineVertex(pos2, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureEdgeCapacity()
    {
        if (_lastEdgeVertexCount + 2 > _edgeVertices.Length)
        {
            Array.Resize(ref _edgeVertices, _edgeVertices.Length * 2);
        }
    }
}
