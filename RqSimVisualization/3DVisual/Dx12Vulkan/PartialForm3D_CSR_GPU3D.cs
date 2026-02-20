using System.Numerics;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RQSimulation;

namespace RqSimVisualization;

/// <summary>
/// GPU 3D rendering methods for the embedded CSR 3D visualization.
/// Handles conversion of graph data to GPU-compatible formats and 3D camera setup.
/// Matches Form_Rsim3DForm.GPU3D.cs implementation.
/// </summary>
public partial class RqSimVisualizationForm
{
    // === GPU Buffers (CSR) ===
    private Dx12NodeInstance[]? _csrNodeInstanceBuffer;
    private Dx12LineVertex[]? _csrEdgeVertexBuffer;
    
    // === 3D Camera Matrices (CSR) ===
    private Matrix4x4 _csrViewMatrix = Matrix4x4.Identity;
    private Matrix4x4 _csrProjMatrix = Matrix4x4.Identity;
    private Vector3 _csrCameraTarget = Vector3.Zero;
    
    // === Computed bounds for auto-centering (CSR) ===
    private Vector3 _csrGraphCenter = Vector3.Zero;
    private float _csrGraphRadius = 10f;

    // === Debug: Track last log time for throttled diagnostics ===
    private DateTime _csrGpu3DLastDebugLog = DateTime.MinValue;

    /// <summary>
    /// Renders the graph using GPU 3D pipeline with spheres and lines.
    /// Matches Form_Rsim3DForm.RenderSceneGpu3D().
    /// </summary>
    private void RenderCsrSceneGpu3D()
    {
        if (_csrDx12Host is null || _csrNodeCount == 0 || _csrNodeX is null)
        {
            return;
        }

        // Update camera matrices based on orbit parameters
        UpdateCsrCameraMatrices();

        // Convert graph data to GPU format (updates every frame)
        ConvertToCsrGpuNodeInstances();
        ConvertToCsrGpuEdgeVertices();

        // Set camera matrices on render host
        _csrDx12Host.SetCameraMatrices(_csrViewMatrix, _csrProjMatrix);

        // Upload data to GPU - pass actual count, not buffer length
        int actualNodeCount = Math.Min(_csrNodeCount, _csrNodeInstanceBuffer?.Length ?? 0);
        if (_csrNodeInstanceBuffer is not null && actualNodeCount > 0)
        {
            _csrDx12Host.SetNodeInstances(_csrNodeInstanceBuffer, actualNodeCount);
        }

        int actualEdgeVertexCount = _csrEdgeVertexBuffer?.Length ?? 0;
        if (_csrEdgeVertexBuffer is not null && actualEdgeVertexCount > 0)
        {
            _csrDx12Host.SetEdgeVertices(_csrEdgeVertexBuffer, actualEdgeVertexCount);
        }

        // Throttled debug output
        if ((DateTime.Now - _csrGpu3DLastDebugLog).TotalSeconds > 2)
        {
            string node0Info = "N/A";
            if (_csrNodeInstanceBuffer is not null && actualNodeCount > 0)
            {
                var n0 = _csrNodeInstanceBuffer[0];
                node0Info = $"Pos=({n0.Position.X:F2},{n0.Position.Y:F2},{n0.Position.Z:F2}) R={n0.Radius:F2} Col=({n0.Color.X:F2},{n0.Color.Y:F2},{n0.Color.Z:F2})";
            }
            
            System.Diagnostics.Debug.WriteLine($"[CSR GPU3D] RenderScene: Nodes={actualNodeCount}, EdgeVerts={actualEdgeVertexCount}, Node0: {node0Info}");
            _csrGpu3DLastDebugLog = DateTime.Now;
        }

        // Render the scene (nodes first, then edges for Early-Z)
        _csrDx12Host.RenderScene();
    }

    /// <summary>
    /// Updates the 3D camera view and projection matrices based on orbit camera parameters.
    /// </summary>
    private void UpdateCsrCameraMatrices()
    {
        if (_csrRenderPanel is null || _csrCamera is null) return;

        // Calculate graph bounds and center
        ComputeCsrGraphBounds();

        // Use orbit camera centered on graph
        _csrCameraTarget = _csrGraphCenter;
        
        // Camera distance scales with graph radius
        float baseDistance = _csrGraphRadius * 3f;
        float zoomFactor = _csrCamera.Distance / 50f;
        float cameraDistance = baseDistance * zoomFactor;
        
        // Clamp to reasonable range
        cameraDistance = MathF.Max(cameraDistance, _csrGraphRadius * 0.5f);

        _csrViewMatrix = CameraMatrixHelper.CreateOrbitCamera(
            _csrCameraTarget,
            cameraDistance,
            _csrCamera.Yaw,
            _csrCamera.Pitch);

        // Create perspective projection with Reverse-Z
        float aspectRatio = (float)_csrRenderPanel.Width / Math.Max(_csrRenderPanel.Height, 1);
        float fovY = MathF.PI / 4f; // 45 degrees
        float nearPlane = 0.01f;
        float farPlane = cameraDistance * 20f;

        _csrProjMatrix = CameraMatrixHelper.CreatePerspectiveReverseZ(fovY, aspectRatio, nearPlane, farPlane);

        // Debug: log camera parameters once every 2 seconds
        if ((DateTime.Now - _csrGpu3DLastDebugLog).TotalSeconds > 2)
        {
            System.Diagnostics.Debug.WriteLine($"[CSR GPU3D] Camera: Distance={cameraDistance:F1}, Target={_csrCameraTarget}, GraphRadius={_csrGraphRadius:F2}");
        }
    }

    /// <summary>
    /// Computes the bounding sphere of the graph for camera auto-centering.
    /// </summary>
    private void ComputeCsrGraphBounds()
    {
        if (_csrNodeCount == 0 || _csrNodeX is null || _csrNodeY is null || _csrNodeZ is null)
        {
            _csrGraphCenter = Vector3.Zero;
            _csrGraphRadius = 10f;
            return;
        }

        int count = Math.Min(_csrNodeCount, _csrNodeX.Length);
        
        float sumX = 0, sumY = 0, sumZ = 0;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            float x = _csrNodeX[i];
            float y = _csrNodeY[i];
            float z = _csrNodeZ[i];

            sumX += x;
            sumY += y;
            sumZ += z;

            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            minZ = MathF.Min(minZ, z);
            maxZ = MathF.Max(maxZ, z);
        }

        _csrGraphCenter = new Vector3(sumX / count, sumY / count, sumZ / count);

        // Compute radius as half of max extent
        float extentX = maxX - minX;
        float extentY = maxY - minY;
        float extentZ = maxZ - minZ;
        _csrGraphRadius = MathF.Max(extentX, MathF.Max(extentY, extentZ)) / 2f;
        _csrGraphRadius = MathF.Max(_csrGraphRadius, 1f);
    }

    /// <summary>
    /// Converts node data to GPU-Compatible Dx12NodeInstance format.
    /// </summary>
    private void ConvertToCsrGpuNodeInstances()
    {
        if (_csrNodeCount == 0 || _csrNodeX is null || _csrNodeY is null || _csrNodeZ is null)
            return;

        int count = Math.Min(_csrNodeCount, _csrNodeX.Length);

        // Resize buffer if needed
        if (_csrNodeInstanceBuffer is null || _csrNodeInstanceBuffer.Length < count)
        {
            _csrNodeInstanceBuffer = new Dx12NodeInstance[count];
        }

        // Scale radius relative to graph size
        float baseRadiusFraction = 0.02f;
        float worldRadius = _csrGraphRadius * baseRadiusFraction * _csrNodeRadius;
        
        // Clamp to reasonable range
        worldRadius = MathF.Max(worldRadius, 0.01f);
        worldRadius = MathF.Min(worldRadius, _csrGraphRadius * 0.3f);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = new(_csrNodeX[i], _csrNodeY[i], _csrNodeZ[i]);
            Vector4 color = GetCsrNodeColorForGpu(i);
            float radius = worldRadius;

            // Modulate radius by node state (Excited nodes are larger)
            if (_csrNodeStates is not null && i < _csrNodeStates.Length)
            {
                var state = _csrNodeStates[i];
                radius *= state switch
                {
                    NodeState.Excited => 1.2f,
                    NodeState.Refractory => 0.9f,
                    _ => 1.0f
                };
            }

            _csrNodeInstanceBuffer[i] = new Dx12NodeInstance(position, radius, color);
        }
    }

    /// <summary>
    /// Converts edge data to GPU-compatible Dx12LineVertex format.
    /// </summary>
    private void ConvertToCsrGpuEdgeVertices()
    {
        if (!_csrShowEdges || _csrEdgeList is null || _csrNodeX is null || _csrNodeY is null || _csrNodeZ is null)
        {
            _csrEdgeVertexBuffer = null;
            return;
        }

        int count = Math.Min(_csrNodeCount, _csrNodeX.Length);
        
        // Count visible edges that pass threshold
        int visibleEdgeCount = 0;
        foreach (var (u, v, w) in _csrEdgeList)
        {
            if (u < count && v < count && w >= _csrEdgeWeightThreshold)
            {
                visibleEdgeCount++;
            }
        }

        // Each edge needs 2 vertices
        int requiredVertices = visibleEdgeCount * 2;
        
        if (requiredVertices == 0)
        {
            _csrEdgeVertexBuffer = null;
            return;
        }

        // Resize buffer if needed
        if (_csrEdgeVertexBuffer is null || _csrEdgeVertexBuffer.Length < requiredVertices)
        {
            _csrEdgeVertexBuffer = new Dx12LineVertex[requiredVertices];
        }

        int vertexIndex = 0;
        foreach (var (u, v, w) in _csrEdgeList)
        {
            if (u < count && v < count && w >= _csrEdgeWeightThreshold)
            {
                Vector3 posA = new(_csrNodeX[u], _csrNodeY[u], _csrNodeZ[u]);
                Vector3 posB = new(_csrNodeX[v], _csrNodeY[v], _csrNodeZ[v]);

                var (edgeColor, _) = GetCsrEdgeStyle(u, v, w);

                _csrEdgeVertexBuffer[vertexIndex++] = new Dx12LineVertex(posA, edgeColor);
                _csrEdgeVertexBuffer[vertexIndex++] = new Dx12LineVertex(posB, edgeColor);
            }
        }
    }

    /// <summary>
    /// Gets node color for GPU rendering based on visualization mode.
    /// Delegates to the same coloring logic as ImGui 2D mode for consistency.
    /// </summary>
    private Vector4 GetCsrNodeColorForGpu(int nodeIndex)
    {
        // Use the same coloring logic as ImGui 2D mode for consistency
        return GetCsrNodeColor(nodeIndex);
    }

    /// <summary>
    /// Gets density color as Vector4 for GPU.
    /// </summary>
    private Vector4 GetDensityColor(int nodeIndex)
    {
        return GetCsrProbabilityDensityColor(nodeIndex);
    }

    /// <summary>
    /// Gets curvature color as Vector4 for GPU.
    /// </summary>
    private Vector4 GetCurvatureColor(int nodeIndex)
    {
        return GetCsrCurvatureColor(nodeIndex);
    }

    /// <summary>
    /// Gets gravity heatmap color as Vector4 for GPU.
    /// </summary>
    private Vector4 GetGravityColor(int nodeIndex)
    {
        return GetCsrGravityHeatmapColor(nodeIndex);
    }

    /// <summary>
    /// Gets topology color as Vector4 for GPU.
    /// </summary>
    private Vector4 GetTopologyColor(int nodeIndex)
    {
        return GetCsrNetworkTopologyColor(nodeIndex);
    }

    /// <summary>
    /// Gets cluster color as Vector4 for GPU.
    /// </summary>
    private Vector4 GetClusterColorVec4(int nodeIndex)
    {
        return GetCsrClustersColor(nodeIndex);
    }

    /// <summary>
    /// Converts HSV to RGB as Vector4.
    /// </summary>
    private static Vector4 HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1 - MathF.Abs((h * 6) % 2 - 1));
        float m = v - c;

        float r, g, b;
        if (h < 1f/6f) { r = c; g = x; b = 0; }
        else if (h < 2f/6f) { r = x; g = c; b = 0; }
        else if (h < 3f/6f) { r = 0; g = c; b = x; }
        else if (h < 4f/6f) { r = 0; g = x; b = c; }
        else if (h < 5f/6f) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new Vector4(r + m, g + m, b + m, 1.0f);
    }
}
