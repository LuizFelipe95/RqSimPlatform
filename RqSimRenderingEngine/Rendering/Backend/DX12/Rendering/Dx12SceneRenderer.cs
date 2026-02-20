using System;
using System.Numerics;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// Edge rendering mode.
/// </summary>
public enum EdgeRenderMode
{
    /// <summary>Standard 1px lines (legacy).</summary>
    Lines,

    /// <summary>Billboarded quads with variable thickness (Vertex Pulling).</summary>
    Quads
}

/// <summary>
/// Manages 3D scene rendering with proper draw order for depth testing.
/// Implements Early-Z optimization by drawing opaque occluders (nodes) before edges.
/// Supports GPU-driven culling for large edge counts.
/// Supports Occlusion Culling via Depth Pre-Pass.
/// </summary>
internal sealed class Dx12SceneRenderer : IDisposable
{
    private readonly SphereRenderer _sphereRenderer = new();
    private readonly LineRenderer _lineRenderer = new();
    private readonly EdgeQuadRenderer _edgeQuadRenderer = new();
    private readonly GpuEdgeCuller _edgeCuller = new();
    private readonly OcclusionCullingPipeline _occlusionCuller = new();
    
    private ID3D12Device? _device;
    private bool _initialized;
    private bool _disposed;
    
    // Edge rendering settings
    private EdgeRenderMode _edgeRenderMode = EdgeRenderMode.Lines;
    
    // GPU culling settings
    private bool _gpuCullingEnabled;
    private bool _occlusionCullingEnabled;
    private float _minProjectedEdgeSize = 0.002f; // Subpixel culling threshold
    private float _occlusionDepthBias = 0.001f;   // Bias to avoid self-occlusion
    private Matrix4x4 _lastView;
    private Matrix4x4 _lastProjection;
    private Vector3 _cameraPosition;
    private Vector2 _screenSize;
    
    // Depth buffer reference for occlusion culling
    private ID3D12Resource? _depthBuffer;
    private Format _depthFormat;

    /// <summary>
    /// Edge rendering mode (Lines or Quads).
    /// </summary>
    public EdgeRenderMode EdgeRenderMode
    {
        get => _edgeRenderMode;
        set => _edgeRenderMode = value;
    }

    /// <summary>
    /// Whether GPU-driven edge culling is enabled.
    /// Recommended for scenes with >10,000 edges.
    /// </summary>
    public bool GpuCullingEnabled
    {
        get => _gpuCullingEnabled;
        set => _gpuCullingEnabled = value && _edgeCuller.IsAvailable;
    }

    /// <summary>
    /// Whether Occlusion Culling (Depth Pre-Pass) is enabled.
    /// Culls edges hidden behind spheres.
    /// </summary>
    public bool OcclusionCullingEnabled
    {
        get => _occlusionCullingEnabled;
        set => _occlusionCullingEnabled = value && _occlusionCuller.IsAvailable && _sphereRenderer.IsDepthPrePassAvailable;
    }

    /// <summary>
    /// Minimum projected edge size in NDC before culling (subpixel threshold).
    /// Default is 0.002 (roughly 1-2 pixels at 1080p).
    /// </summary>
    public float MinProjectedEdgeSize
    {
        get => _minProjectedEdgeSize;
        set => _minProjectedEdgeSize = Math.Max(0.0001f, value);
    }

    /// <summary>
    /// Depth bias for occlusion culling to avoid self-occlusion artifacts.
    /// </summary>
    public float OcclusionDepthBias
    {
        get => _occlusionDepthBias;
        set => _occlusionDepthBias = Math.Max(0f, value);
    }

    /// <summary>
    /// Base thickness for edge quads (only used when EdgeRenderMode is Quads).
    /// </summary>
    public float EdgeQuadThickness
    {
        get => _edgeQuadRenderer.BaseThickness;
        set => _edgeQuadRenderer.BaseThickness = value;
    }

    /// <summary>
    /// Whether EdgeQuadRenderer is available.
    /// </summary>
    public bool IsEdgeQuadAvailable => _edgeQuadRenderer.IsInitialized;

    /// <summary>
    /// Whether Occlusion Culling is available.
    /// </summary>
    public bool IsOcclusionCullingAvailable => _occlusionCuller.IsAvailable && _sphereRenderer.IsDepthPrePassAvailable;

    /// <summary>
    /// Initialize the scene renderer with the DX12 device.
    /// </summary>
    /// <param name="device">DX12 device.</param>
    /// <param name="renderTargetFormat">Render target format.</param>
    /// <param name="depthFormat">Depth buffer format.</param>
    /// <param name="sampleDescription">MSAA sample description.</param>
    public void Initialize(
        ID3D12Device device,
        Format renderTargetFormat,
        Format depthFormat,
        SampleDescription sampleDescription)
    {
        ArgumentNullException.ThrowIfNull(device);
        
        _device = device;
        _depthFormat = depthFormat;
        
        _sphereRenderer.Initialize(device, renderTargetFormat, depthFormat, sampleDescription);
        _lineRenderer.Initialize(device, renderTargetFormat, depthFormat, sampleDescription);
        
        // Initialize EdgeQuadRenderer (optional)
        InitializeEdgeQuadRendererOptional(device, renderTargetFormat, depthFormat, sampleDescription);
        
        // GPU culling is optional
        InitializeGpuCullingOptional(device);
        
        // Occlusion culling is optional
        InitializeOcclusionCullingOptional(device);
        
        _initialized = true;
    }

    /// <summary>
    /// Set depth buffer reference for occlusion culling.
    /// </summary>
    public void SetDepthBuffer(ID3D12Resource depthBuffer, Vector2 screenSize)
    {
        _depthBuffer = depthBuffer;
        _screenSize = screenSize;
    }

    /// <summary>
    /// Try to initialize EdgeQuadRenderer. This is optional.
    /// </summary>
    private void InitializeEdgeQuadRendererOptional(
        ID3D12Device device,
        Format renderTargetFormat,
        Format depthFormat,
        SampleDescription sampleDescription)
    {
        try
        {
            _edgeQuadRenderer.Initialize(device, renderTargetFormat, depthFormat, sampleDescription);
            System.Diagnostics.Debug.WriteLine("[SceneRenderer] EdgeQuadRenderer (Vertex Pulling) available");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SceneRenderer] EdgeQuadRenderer unavailable (will use lines): {ex.Message}");
            // If EdgeQuadRenderer fails, force Lines mode
            if (_edgeRenderMode == EdgeRenderMode.Quads)
                _edgeRenderMode = EdgeRenderMode.Lines;
        }
    }

    /// <summary>
    /// Try to initialize GPU culling. This is optional and won't fail the renderer.
    /// </summary>
    private void InitializeGpuCullingOptional(ID3D12Device device)
    {
        try
        {
            _edgeCuller.Initialize(device);
            System.Diagnostics.Debug.WriteLine("[SceneRenderer] GPU edge culling available");
        }
        catch (Exception ex)
        {
            // GPU culling is optional - just log and continue without it
            System.Diagnostics.Debug.WriteLine($"[SceneRenderer] GPU culling unavailable (will use direct draw): {ex.Message}");
            _gpuCullingEnabled = false;
        }
    }

    /// <summary>
    /// Try to initialize Occlusion Culling pipeline.
    /// </summary>
    private void InitializeOcclusionCullingOptional(ID3D12Device device)
    {
        try
        {
            _occlusionCuller.Initialize(device);
            System.Diagnostics.Debug.WriteLine("[SceneRenderer] Occlusion Culling available");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SceneRenderer] Occlusion Culling unavailable: {ex.Message}");
            _occlusionCullingEnabled = false;
        }
    }

    /// <summary>
    /// Ensure all buffers have capacity.
    /// </summary>
    public void EnsureCapacity(int maxNodes, int maxEdgeVertices)
    {
        if (_device is null || !_initialized)
            return;
            
        _sphereRenderer.EnsureInstanceCapacity(_device, maxNodes);
        _lineRenderer.EnsureVertexCapacity(_device, maxEdgeVertices);
        
        int maxEdges = maxEdgeVertices / 2;
        if (_edgeQuadRenderer.IsInitialized)
        {
            _edgeQuadRenderer.EnsureEdgeCapacity(_device, maxEdges);
            _edgeQuadRenderer.EnsureNodeCapacity(_device, maxNodes);
        }
        
        if (_gpuCullingEnabled && _edgeCuller.IsAvailable)
        {
            _edgeCuller.EnsureCapacity(_device, maxEdges);
        }
        
        if (_occlusionCullingEnabled && _occlusionCuller.IsAvailable)
        {
            _occlusionCuller.EnsureCapacity(_device, maxEdges, maxNodes);
        }
    }

    /// <summary>
    /// Update camera matrices for all renderers.
    /// </summary>
    public void SetCameraMatrices(in Matrix4x4 view, in Matrix4x4 projection)
    {
        _lastView = view;
        _lastProjection = projection;
        
        // Extract camera position from view matrix inverse
        if (Matrix4x4.Invert(view, out var invView))
        {
            _cameraPosition = new Vector3(invView.M41, invView.M42, invView.M43);
        }
        
        _sphereRenderer.SetCameraMatrices(view, projection);
        _lineRenderer.SetCameraMatrices(view, projection);
        
        if (_edgeQuadRenderer.IsInitialized)
        {
            _edgeQuadRenderer.SetCameraData(view, projection, _cameraPosition);
        }
    }

    /// <summary>
    /// Upload node instance data to GPU.
    /// </summary>
    public void UpdateNodeInstances(ReadOnlySpan<Dx12NodeInstance> instances)
    {
        _sphereRenderer.UpdateInstances(instances);
    }

    /// <summary>
    /// Upload edge vertex data to GPU (for LineRenderer).
    /// </summary>
    public void UpdateEdgeVertices(ReadOnlySpan<Dx12LineVertex> vertices)
    {
        _lineRenderer.UpdateVertices(vertices);
        
        // Also upload to GPU culler if enabled
        if (_gpuCullingEnabled && _edgeCuller.IsAvailable)
        {
            _edgeCuller.UploadEdges(vertices);
        }
    }

    /// <summary>
    /// Upload packed edge data to GPU (for EdgeQuadRenderer).
    /// </summary>
    public void UpdatePackedEdges(ReadOnlySpan<Dx12PackedEdgeData> edges)
    {
        if (_edgeQuadRenderer.IsInitialized)
        {
            _edgeQuadRenderer.UpdateEdges(edges);
        }
    }

    /// <summary>
    /// Upload packed node data to GPU (for EdgeQuadRenderer).
    /// </summary>
    public void UpdatePackedNodes(ReadOnlySpan<Dx12PackedNodeData> nodes)
    {
        if (_edgeQuadRenderer.IsInitialized)
        {
            _edgeQuadRenderer.UpdateNodes(nodes);
        }
    }

    /// <summary>
    /// Render the scene with proper draw order for Early-Z optimization.
    /// </summary>
    public void Render(
        ID3D12GraphicsCommandList commandList,
        int nodeCount,
        int edgeVertexCount)
    {
        if (!_initialized || _device is null)
        {
            System.Diagnostics.Debug.WriteLine($"[SceneRenderer] Render skipped: initialized={_initialized}, device={_device is not null}");
            return;
        }

        // CRITICAL: Draw order for Early-Z optimization
        // Step 1: Draw nodes (spheres) first - they fill the depth buffer
        if (nodeCount > 0)
        {
            _sphereRenderer.EnsureMeshUploaded(commandList);
            _sphereRenderer.Draw(commandList, nodeCount);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[SceneRenderer] No nodes to render");
        }

        // Step 2: Draw edges second
        if (edgeVertexCount >= 2)
        {
            RenderEdges(commandList, edgeVertexCount);
        }
    }

    /// <summary>
    /// Render the scene using packed data (for EdgeQuadRenderer).
    /// </summary>
    public void RenderWithPackedData(
        ID3D12GraphicsCommandList commandList,
        int nodeCount,
        int edgeCount)
    {
        if (!_initialized || _device is null)
            return;

        // Step 1: Draw nodes (spheres) first
        if (nodeCount > 0)
        {
            _sphereRenderer.EnsureMeshUploaded(commandList);
            _sphereRenderer.Draw(commandList, nodeCount);
        }

        // Step 2: Draw edges using EdgeQuadRenderer
        if (edgeCount > 0 && _edgeQuadRenderer.IsInitialized)
        {
            _edgeQuadRenderer.Draw(commandList, edgeCount);
        }
    }

    /// <summary>
    /// Render scene with Occlusion Culling (Depth Pre-Pass pipeline).
    /// </summary>
    /// <param name="commandList">Command list.</param>
    /// <param name="nodeCount">Number of nodes.</param>
    /// <param name="edgeCount">Number of edges (packed format).</param>
    /// <param name="depthBuffer">Depth buffer resource.</param>
    public void RenderWithOcclusionCulling(
        ID3D12GraphicsCommandList commandList,
        int nodeCount,
        int edgeCount,
        ID3D12Resource depthBuffer)
    {
        if (!_initialized || _device is null)
            return;

        // =========================================================
        // STEP 1: DEPTH PRE-PASS
        // =========================================================
        // Draw spheres to depth buffer only (no color output)
        if (nodeCount > 0)
        {
            _sphereRenderer.EnsureMeshUploaded(commandList);
            _sphereRenderer.DrawDepthOnly(commandList, nodeCount);
        }

        // Barrier: Depth Write ? SRV for compute shader
        commandList.ResourceBarrierTransition(depthBuffer, ResourceStates.DepthWrite, ResourceStates.NonPixelShaderResource);

        // =========================================================
        // STEP 2: COMPUTE OCCLUSION CULLING
        // =========================================================
        if (edgeCount > 0 && _occlusionCuller.IsAvailable)
        {
            // Register depth buffer SRV
            _occlusionCuller.SetDepthBufferSRV(_device, depthBuffer, _depthFormat);

            // Update culling constants
            var cullConstants = OcclusionCullingConstants.Create(
                _lastView,
                _lastProjection,
                _screenSize,
                _occlusionDepthBias,
                _minProjectedEdgeSize);
            cullConstants.TotalEdgeCount = (uint)edgeCount;
            _occlusionCuller.UpdateConstants(cullConstants);

            // Execute culling
            _occlusionCuller.ExecuteCulling(commandList, edgeCount);
        }

        // Barrier: Depth SRV ? Depth Read for rendering
        commandList.ResourceBarrierTransition(depthBuffer, ResourceStates.NonPixelShaderResource, ResourceStates.DepthRead);

        // =========================================================
        // STEP 3: DRAW VISIBLE EDGES (with ExecuteIndirect)
        // =========================================================
        if (edgeCount > 0 && _edgeQuadRenderer.IsInitialized)
        {
            var indirectArgs = _occlusionCuller.GetIndirectArgsBuffer();
            var cmdSignature = _occlusionCuller.GetDrawInstancedSignature();

            if (indirectArgs is not null && cmdSignature is not null)
            {
                _edgeQuadRenderer.DrawIndirect(commandList, indirectArgs, cmdSignature);
            }
        }

        // =========================================================
        // STEP 4: DRAW SPHERES WITH COLOR (regular pass)
        // =========================================================
        // Barrier: Depth Read ? Depth Write for color pass
        commandList.ResourceBarrierTransition(depthBuffer, ResourceStates.DepthRead, ResourceStates.DepthWrite);

        if (nodeCount > 0)
        {
            _sphereRenderer.Draw(commandList, nodeCount);
        }
    }

    private void RenderEdges(ID3D12GraphicsCommandList commandList, int edgeVertexCount)
    {
        int edgeCount = edgeVertexCount / 2;

        // Use EdgeQuadRenderer if mode is Quads and renderer is available
        if (_edgeRenderMode == EdgeRenderMode.Quads && _edgeQuadRenderer.IsInitialized)
        {
            _edgeQuadRenderer.Draw(commandList, edgeCount);
            return;
        }
        
        // Otherwise use LineRenderer
        // Use GPU culling for large edge counts
        if (_gpuCullingEnabled && _edgeCuller.IsAvailable && edgeCount >= 1000)
        {
            RenderEdgesWithGpuCulling(commandList, edgeCount);
        }
        else
        {
            // Direct draw without culling
            _lineRenderer.Draw(commandList, edgeVertexCount);
        }
    }

    private void RenderEdgesWithGpuCulling(ID3D12GraphicsCommandList commandList, int edgeCount)
    {
        // Update culling constants
        var cullConstants = CullingConstants.Create(
            _lastView, 
            _lastProjection, 
            _cameraPosition, 
            _minProjectedEdgeSize);
        cullConstants.TotalEdgeCount = (uint)edgeCount;
        _edgeCuller.UpdateCullingConstants(cullConstants);

        // Execute GPU culling
        if (!_edgeCuller.ExecuteCulling(commandList, edgeCount))
        {
            // Fallback to direct draw if culling fails
            _lineRenderer.Draw(commandList, edgeCount * 2);
            return;
        }

        // Get culled buffers
        var visibleBuffer = _edgeCuller.GetVisibleEdgesBuffer();
        var indirectArgs = _edgeCuller.GetIndirectArgsBuffer();
        var cmdSignature = _edgeCuller.GetDrawCommandSignature();

        if (visibleBuffer is null || indirectArgs is null || cmdSignature is null)
        {
            _lineRenderer.Draw(commandList, edgeCount * 2);
            return;
        }

        // Draw with ExecuteIndirect
        _lineRenderer.DrawIndirect(
            commandList,
            visibleBuffer,
            _edgeCuller.GetVisibleEdgesVBView(),
            indirectArgs,
            cmdSignature);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        _initialized = false;
        
        _occlusionCuller.Dispose();
        _edgeCuller.Dispose();
        _sphereRenderer.Dispose();
        _lineRenderer.Dispose();
        _edgeQuadRenderer.Dispose();
        
        _device = null;
    }
}
