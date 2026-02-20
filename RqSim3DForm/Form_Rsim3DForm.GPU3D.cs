using System.Numerics;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;
using RQSimulation;

namespace RqSim3DForm;

/// <summary>
/// GPU 3D rendering methods for the standalone 3D visualization form.
/// Handles conversion of graph data to GPU-compatible formats and 3D camera setup.
/// </summary>
public partial class Form_Rsim3DForm
{
    // === GPU Buffers ===
    private Dx12NodeInstance[]? _nodeInstanceBuffer;
    private Dx12LineVertex[]? _edgeVertexBuffer;
    
    // === 3D Camera Matrices ===
    private Matrix4x4 _viewMatrix = Matrix4x4.Identity;
    private Matrix4x4 _projMatrix = Matrix4x4.Identity;
    private Vector3 _cameraTarget = Vector3.Zero;
    
    // === Computed bounds for auto-centering ===
    private Vector3 _graphCenter = Vector3.Zero;
    private float _graphRadius = 10f;

    // === Debug: Track last log time for throttled diagnostics ===
    private DateTime _gpu3DLastDebugLog = DateTime.MinValue;

    /// <summary>
    /// Renders the graph using GPU 3D pipeline with spheres and lines.
    /// </summary>
    private void RenderSceneGpu3D()
    {
        if (_dx12Host is null || _nodeCount == 0 || _nodeX is null)
        {
            return;
        }

        // Update camera matrices based on orbit parameters
        UpdateCameraMatrices();

        // Convert graph data to GPU format (updates every frame)
        ConvertToGpuNodeInstances();
        ConvertToGpuEdgeVertices();

        // Set camera matrices on render host
        _dx12Host.SetCameraMatrices(_viewMatrix, _projMatrix);

        // Upload data to GPU - pass actual count, not buffer length
        int actualNodeCount = Math.Min(_nodeCount, _nodeInstanceBuffer?.Length ?? 0);
        if (_nodeInstanceBuffer is not null && actualNodeCount > 0)
        {
            _dx12Host.SetNodeInstances(_nodeInstanceBuffer, actualNodeCount);
        }

        int actualEdgeVertexCount = _edgeVertexBuffer?.Length ?? 0;
        if (_edgeVertexBuffer is not null && actualEdgeVertexCount > 0)
        {
            _dx12Host.SetEdgeVertices(_edgeVertexBuffer, actualEdgeVertexCount);
        }

        // Throttled debug output
        if ((DateTime.Now - _gpu3DLastDebugLog).TotalSeconds > 2)
        {
            // Sample first node position to verify data is changing
            string node0Info = "N/A";
            if (_nodeInstanceBuffer is not null && actualNodeCount > 0)
            {
                var n0 = _nodeInstanceBuffer[0];
                node0Info = $"Pos=({n0.Position.X:F2},{n0.Position.Y:F2},{n0.Position.Z:F2}) R={n0.Radius:F2} Col=({n0.Color.X:F2},{n0.Color.Y:F2},{n0.Color.Z:F2})";
            }
            
            System.Diagnostics.Debug.WriteLine($"[GPU3D] RenderScene: Nodes={actualNodeCount}, EdgeVerts={actualEdgeVertexCount}, Node0: {node0Info}");
            _gpu3DLastDebugLog = DateTime.Now;
        }

        // Render the scene (nodes first, then edges for Early-Z)
        _dx12Host.RenderScene();
    }

    /// <summary>
    /// Updates the 3D camera view and projection matrices based on orbit camera parameters.
    /// </summary>
    private void UpdateCameraMatrices()
    {
        if (_renderPanel is null) return;

        // Calculate graph bounds and center
        ComputeGraphBounds();

        // Use orbit camera centered on graph
        _cameraTarget = _graphCenter;
        
        // Camera distance scales with graph radius
        // _cameraDistance is the "zoom factor" controlled by mouse wheel (default 50)
        // We want camera at ~3x graph radius when zoom is at default
        float baseDistance = _graphRadius * 3f;
        float zoomFactor = _cameraDistance / 50f; // Normalize to default zoom
        float cameraDistance = baseDistance * zoomFactor;
        
        // Clamp to reasonable range
        cameraDistance = MathF.Max(cameraDistance, _graphRadius * 0.5f);

        _viewMatrix = CameraMatrixHelper.CreateOrbitCamera(
            _cameraTarget,
            cameraDistance,
            _cameraYaw,
            _cameraPitch);

        // Create perspective projection with Reverse-Z for better depth precision
        float aspectRatio = (float)_renderPanel.Width / Math.Max(_renderPanel.Height, 1);
        float fovY = MathF.PI / 4f; // 45 degrees
        float nearPlane = 0.01f; // Smaller near plane for close-up viewing
        float farPlane = cameraDistance * 20f; // Dynamic far plane

        _projMatrix = CameraMatrixHelper.CreatePerspectiveReverseZ(fovY, aspectRatio, nearPlane, farPlane);

        // Debug: log camera parameters once every 2 seconds
        if ((DateTime.Now - _gpu3DLastDebugLog).TotalSeconds > 2)
        {
            System.Diagnostics.Debug.WriteLine($"[GPU3D] Camera: Distance={cameraDistance:F1}, Target={_cameraTarget}, GraphRadius={_graphRadius:F2}, Zoom={zoomFactor:F2}");
        }
    }

    /// <summary>
    /// Computes the bounding sphere of the graph for camera auto-centering.
    /// </summary>
    private void ComputeGraphBounds()
    {
        if (_nodeCount == 0 || _nodeX is null || _nodeY is null || _nodeZ is null)
        {
            _graphCenter = Vector3.Zero;
            _graphRadius = 10f;
            return;
        }

        int count = Math.Min(_nodeCount, _nodeX.Length);
        
        float sumX = 0, sumY = 0, sumZ = 0;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            float x = _nodeX[i];
            float y = _nodeY[i];
            float z = _nodeZ[i];

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

        _graphCenter = new Vector3(sumX / count, sumY / count, sumZ / count);

        // Compute radius as half of max extent
        float extentX = maxX - minX;
        float extentY = maxY - minY;
        float extentZ = maxZ - minZ;
        _graphRadius = MathF.Max(extentX, MathF.Max(extentY, extentZ)) / 2f;
        _graphRadius = MathF.Max(_graphRadius, 1f); // Minimum radius

        // Debug: log bounds once every 2 seconds
        if ((DateTime.Now - _gpu3DLastDebugLog).TotalSeconds > 2)
        {
            System.Diagnostics.Debug.WriteLine($"[GPU3D] Bounds: X=[{minX:F1},{maxX:F1}], Y=[{minY:F1},{maxY:F1}], Z=[{minZ:F1},{maxZ:F1}], Center={_graphCenter}, Radius={_graphRadius:F2}");
        }
    }

    /// <summary>
    /// Converts node data to GPU-Compatible Dx12NodeInstance format.
    /// </summary>
    private void ConvertToGpuNodeInstances()
    {
        if (_nodeCount == 0 || _nodeX is null || _nodeY is null || _nodeZ is null)
            return;

        int count = Math.Min(_nodeCount, _nodeX.Length);

        // Resize buffer if needed
        if (_nodeInstanceBuffer is null || _nodeInstanceBuffer.Length < count)
        {
            _nodeInstanceBuffer = new Dx12NodeInstance[count];
        }

        // Track state changes for debug
        int excitedCount = 0;
        int refractoryCount = 0;

        // Scale radius relative to graph size
        // _nodeRadius slider goes from 0.1 to 5.0
        // For a graph with radius ~2.0, we want spheres to be about 1-10% of the graph size
        // Base radius as a fraction of graph radius (0.02 = 2% at slider minimum)
        float baseRadiusFraction = 0.02f;
        float worldRadius = _graphRadius * baseRadiusFraction * _nodeRadius;
        
        // Clamp to reasonable range
        worldRadius = MathF.Max(worldRadius, 0.01f);
        worldRadius = MathF.Min(worldRadius, _graphRadius * 0.3f);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = new(_nodeX[i], _nodeY[i], _nodeZ[i]);
            Vector4 color = GetNodeColor(i);
            float radius = worldRadius;

            // Modulate radius by node state (Excited nodes are larger)
            if (_nodeStates is not null && i < _nodeStates.Length)
            {
                var state = _nodeStates[i];
                radius *= state switch
                {
                    NodeState.Excited => 1.2f,
                    NodeState.Refractory => 0.9f,
                    _ => 1.0f
                };

                if (state == NodeState.Excited) excitedCount++;
                else if (state == NodeState.Refractory) refractoryCount++;
            }

            _nodeInstanceBuffer[i] = new Dx12NodeInstance(position, radius, color);
        }

        // Throttled debug: report state distribution
        if ((DateTime.Now - _gpu3DLastDebugLog).TotalSeconds > 2)
        {
            System.Diagnostics.Debug.WriteLine($"[GPU3D] Nodes: {count}, Excited={excitedCount}, Refractory={refractoryCount}, Rest={count - excitedCount - refractoryCount}");
        }
    }

    /// <summary>
    /// Converts edge data to GPU-compatible Dx12LineVertex format.
    /// Each edge requires 2 vertices for line rendering.
    /// </summary>
    private void ConvertToGpuEdgeVertices()
    {
        if (!_showEdges || _edges is null || _nodeX is null || _nodeY is null || _nodeZ is null)
        {
            _edgeVertexBuffer = null;
            return;
        }

        int count = Math.Min(_nodeCount, _nodeX.Length);
        
        // Count visible edges that pass threshold
        int visibleEdgeCount = 0;
        foreach (var (u, v, w) in _edges)
        {
            if (u < count && v < count && w >= _edgeWeightThreshold)
            {
                visibleEdgeCount++;
            }
        }

        // Each edge needs 2 vertices
        int requiredVertices = visibleEdgeCount * 2;
        
        if (requiredVertices == 0)
        {
            _edgeVertexBuffer = null;
            return;
        }

        // Resize buffer if needed
        if (_edgeVertexBuffer is null || _edgeVertexBuffer.Length < requiredVertices)
        {
            _edgeVertexBuffer = new Dx12LineVertex[requiredVertices];
        }

        int vertexIndex = 0;
        foreach (var (u, v, w) in _edges)
        {
            if (u < count && v < count && w >= _edgeWeightThreshold)
            {
                Vector3 posA = new(_nodeX[u], _nodeY[u], _nodeZ[u]);
                Vector3 posB = new(_nodeX[v], _nodeY[v], _nodeZ[v]);

                var (edgeColorVec, _) = GetEdgeStyle(u, v, w);

                _edgeVertexBuffer[vertexIndex++] = new Dx12LineVertex(posA, edgeColorVec);
                _edgeVertexBuffer[vertexIndex++] = new Dx12LineVertex(posB, edgeColorVec);
            }
        }
    }
}
