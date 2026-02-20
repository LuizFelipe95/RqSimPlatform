using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.D3DCompiler;
using Vortice.DXGI;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// GPU data structure for packed edge data (16 bytes).
/// Must match HLSL PackedEdgeData struct.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Dx12PackedEdgeData
{
    public int NodeIndexA;
    public int NodeIndexB;
    public float Weight;
    public float Tension;

    public const int SizeInBytes = 16;
}

/// <summary>
/// GPU data structure for packed node data (32 bytes).
/// Must match HLSL PackedNodeData struct.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Dx12PackedNodeData
{
    public Vector3 Position;
    public float Scale;
    public uint ColorEncoded;
    public float Energy;
    public uint Flags;
    public float Padding;

    public const int SizeInBytes = 32;

    /// <summary>
    /// Encode RGBA color (0-1) to uint.
    /// </summary>
    public static uint EncodeColor(float r, float g, float b, float a = 1f)
    {
        uint rByte = (uint)Math.Clamp((int)(r * 255f), 0, 255);
        uint gByte = (uint)Math.Clamp((int)(g * 255f), 0, 255);
        uint bByte = (uint)Math.Clamp((int)(b * 255f), 0, 255);
        uint aByte = (uint)Math.Clamp((int)(a * 255f), 0, 255);
        return rByte | (gByte << 8) | (bByte << 16) | (aByte << 24);
    }

    /// <summary>
    /// Encode RGBA color from Vector4.
    /// </summary>
    public static uint EncodeColor(Vector4 color)
        => EncodeColor(color.X, color.Y, color.Z, color.W);
}

/// <summary>
/// Renders edges as billboarded quads using Vertex Pulling technique.
/// No vertex buffer needed - geometry is generated procedurally in the vertex shader.
/// </summary>
internal sealed class EdgeQuadRenderer : IDisposable
{
    private ID3D12Device? _device;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pso;
    private ID3D12PipelineState? _psoDebug;

    // Structured buffers for Vertex Pulling
    private ID3D12Resource? _edgeBuffer;
    private ID3D12Resource? _nodeBuffer;
    private int _maxEdges;
    private int _maxNodes;

    // Camera constants
    private ID3D12Resource? _cameraConstantBuffer;
    private IntPtr _cameraCbvPtr;

    private bool _disposed;
    private bool _useDebugShader;

    /// <summary>
    /// Gets or sets whether to use the debug shader (solid colors).
    /// </summary>
    public bool UseDebugShader
    {
        get => _useDebugShader;
        set => _useDebugShader = value;
    }

    /// <summary>
    /// Gets whether the renderer is initialized.
    /// </summary>
    public bool IsInitialized => _device is not null && _pso is not null;

    /// <summary>
    /// Current base thickness for edge quads.
    /// </summary>
    public float BaseThickness { get; set; } = 0.02f;

    internal void Initialize(
        ID3D12Device device,
        Format renderTargetFormat,
        Format depthFormat,
        SampleDescription sampleDescription)
    {
        ArgumentNullException.ThrowIfNull(device);

        Dispose();
        _disposed = false;

        _device = device;

        CreateRootSignature(device);
        CreatePipeline(device, renderTargetFormat, depthFormat, sampleDescription);
        CreateCameraConstantBuffer(device);
    }

    /// <summary>
    /// Ensure edge buffer has enough capacity.
    /// </summary>
    internal void EnsureEdgeCapacity(ID3D12Device device, int maxEdges)
    {
        ArgumentNullException.ThrowIfNull(device);

        maxEdges = Math.Max(maxEdges, 1);
        if (_edgeBuffer is not null && maxEdges <= _maxEdges)
            return;

        _edgeBuffer?.Dispose();
        _edgeBuffer = null;

        _maxEdges = maxEdges;

        ulong sizeInBytes = (ulong)(_maxEdges * Dx12PackedEdgeData.SizeInBytes);
        _edgeBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(sizeInBytes),
            ResourceStates.GenericRead);
    }

    /// <summary>
    /// Ensure node buffer has enough capacity.
    /// </summary>
    internal void EnsureNodeCapacity(ID3D12Device device, int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(device);

        maxNodes = Math.Max(maxNodes, 1);
        if (_nodeBuffer is not null && maxNodes <= _maxNodes)
            return;

        _nodeBuffer?.Dispose();
        _nodeBuffer = null;

        _maxNodes = maxNodes;

        ulong sizeInBytes = (ulong)(_maxNodes * Dx12PackedNodeData.SizeInBytes);
        _nodeBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(sizeInBytes),
            ResourceStates.GenericRead);
    }

    /// <summary>
    /// Upload edge data to GPU.
    /// </summary>
    internal unsafe void UpdateEdges(ReadOnlySpan<Dx12PackedEdgeData> edges)
    {
        if (_edgeBuffer is null || edges.Length == 0)
            return;

        void* mapped;
        _edgeBuffer.Map(0, null, &mapped);
        edges.CopyTo(new Span<Dx12PackedEdgeData>(mapped, edges.Length));
        _edgeBuffer.Unmap(0);
    }

    /// <summary>
    /// Upload node data to GPU.
    /// </summary>
    internal unsafe void UpdateNodes(ReadOnlySpan<Dx12PackedNodeData> nodes)
    {
        if (_nodeBuffer is null || nodes.Length == 0)
            return;

        void* mapped;
        _nodeBuffer.Map(0, null, &mapped);
        nodes.CopyTo(new Span<Dx12PackedNodeData>(mapped, nodes.Length));
        _nodeBuffer.Unmap(0);
    }

    /// <summary>
    /// Set camera matrices and position.
    /// </summary>
    internal unsafe void SetCameraData(in Matrix4x4 view, in Matrix4x4 projection, Vector3 cameraPosition)
    {
        if (_cameraCbvPtr == IntPtr.Zero)
            return;

        var data = new EdgeQuadCameraConstants(view, projection, cameraPosition, BaseThickness);
        Unsafe.Copy((void*)_cameraCbvPtr, ref data);
    }

    /// <summary>
    /// Draw edges as quads using Vertex Pulling.
    /// </summary>
    /// <param name="commandList">Command list to record draw commands.</param>
    /// <param name="edgeCount">Number of edges to draw.</param>
    internal void Draw(ID3D12GraphicsCommandList commandList, int edgeCount)
    {
        if (edgeCount <= 0)
            return;

        if (_rootSignature is null || _cameraConstantBuffer is null || _edgeBuffer is null || _nodeBuffer is null)
            return;

        var pso = _useDebugShader ? _psoDebug : _pso;
        if (pso is null)
            return;

        commandList.SetPipelineState(pso);
        commandList.SetGraphicsRootSignature(_rootSignature);

        // Use TriangleList topology - we generate 6 vertices per edge (2 triangles)
        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        // No vertex buffer needed - Vertex Pulling reads from StructuredBuffers

        // Bind resources:
        // Slot 0: Camera constants (CBV)
        // Slot 1: Edges (SRV)
        // Slot 2: Nodes (SRV)
        commandList.SetGraphicsRootConstantBufferView(0, _cameraConstantBuffer.GPUVirtualAddress);
        commandList.SetGraphicsRootShaderResourceView(1, _edgeBuffer.GPUVirtualAddress);
        commandList.SetGraphicsRootShaderResourceView(2, _nodeBuffer.GPUVirtualAddress);

        // Draw 6 vertices per edge instance
        commandList.DrawInstanced(6, (uint)edgeCount, 0, 0);
    }

    /// <summary>
    /// Draw edges using ExecuteIndirect with GPU-determined edge count.
    /// </summary>
    internal void DrawIndirect(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource indirectArgsBuffer,
        ID3D12CommandSignature commandSignature)
    {
        if (_rootSignature is null || _cameraConstantBuffer is null || _edgeBuffer is null || _nodeBuffer is null)
            return;

        var pso = _useDebugShader ? _psoDebug : _pso;
        if (pso is null)
            return;

        commandList.SetPipelineState(pso);
        commandList.SetGraphicsRootSignature(_rootSignature);
        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        commandList.SetGraphicsRootConstantBufferView(0, _cameraConstantBuffer.GPUVirtualAddress);
        commandList.SetGraphicsRootShaderResourceView(1, _edgeBuffer.GPUVirtualAddress);
        commandList.SetGraphicsRootShaderResourceView(2, _nodeBuffer.GPUVirtualAddress);

        commandList.ExecuteIndirect(commandSignature, 1, indirectArgsBuffer, 0, null, 0);
    }

    private void CreateRootSignature(ID3D12Device device)
    {
        // Extract root signature from compiled vertex shader
        Compiler.Compile(Dx12Shaders.EdgeQuadVs, "main", null, "vs_5_0", out Blob vsBlob, out Blob? errorBlob);
        
        if (vsBlob.BufferSize == 0)
        {
            string error = errorBlob?.AsString() ?? "Unknown shader compilation error";
            errorBlob?.Dispose();
            throw new InvalidOperationException($"Failed to compile EdgeQuadVs: {error}");
        }

        Blob rootSigBlob = Compiler.GetBlobPart(vsBlob.BufferPointer, vsBlob.BufferSize, ShaderBytecodePart.RootSignature, 0);
        _rootSignature = device.CreateRootSignature(0, rootSigBlob.BufferPointer, rootSigBlob.BufferSize);

        rootSigBlob.Dispose();
        vsBlob.Dispose();
        errorBlob?.Dispose();
    }

    private void CreatePipeline(
        ID3D12Device device,
        Format rtFormat,
        Format depthFormat,
        SampleDescription sampleDescription)
    {
        Compiler.Compile(Dx12Shaders.EdgeQuadVs, "main", null, "vs_5_0", out Blob vsBlob, out _);
        Compiler.Compile(Dx12Shaders.EdgeQuadPs, "main", null, "ps_5_0", out Blob psBlob, out _);
        Compiler.Compile(Dx12Shaders.EdgeQuadPsDebug, "main", null, "ps_5_0", out Blob psDebugBlob, out _);

        // No input elements - Vertex Pulling reads from StructuredBuffers
        var inputLayout = new InputLayoutDescription([]);

        var rasterizerState = new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None, // Draw both sides
            FrontCounterClockwise = false,
            DepthBias = 0,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = 0.0f,
            DepthClipEnable = true,
            MultisampleEnable = sampleDescription.Count > 1,
            AntialiasedLineEnable = false,
            ForcedSampleCount = 0,
            ConservativeRaster = ConservativeRasterizationMode.Off
        };

        // Reverse-Z depth stencil
        var depthStencilState = new DepthStencilDescription
        {
            DepthEnable = true,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunction.Greater, // Reverse-Z
            StencilEnable = false,
            StencilReadMask = 0,
            StencilWriteMask = 0,
            FrontFace = new DepthStencilOperationDescription
            {
                StencilFailOp = StencilOperation.Keep,
                StencilDepthFailOp = StencilOperation.Keep,
                StencilPassOp = StencilOperation.Keep,
                StencilFunc = ComparisonFunction.Never
            },
            BackFace = new DepthStencilOperationDescription
            {
                StencilFailOp = StencilOperation.Keep,
                StencilDepthFailOp = StencilOperation.Keep,
                StencilPassOp = StencilOperation.Keep,
                StencilFunc = ComparisonFunction.Never
            }
        };

        // Alpha blending for smooth edges
        var blendState = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        blendState.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            LogicOpEnable = false,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            LogicOp = LogicOp.Noop,
            RenderTargetWriteMask = ColorWriteEnable.All
        };

        var psoDesc = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vsBlob.AsMemory(),
            PixelShader = psBlob.AsMemory(),
            BlendState = blendState,
            RasterizerState = rasterizerState,
            DepthStencilState = depthStencilState,
            InputLayout = inputLayout,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle, // TriangleList
            RenderTargetFormats = [rtFormat],
            DepthStencilFormat = depthFormat,
            SampleDescription = sampleDescription,
            SampleMask = uint.MaxValue
        };

        _pso = device.CreateGraphicsPipelineState(psoDesc);

        // Create debug PSO with same settings but debug pixel shader
        psoDesc.PixelShader = psDebugBlob.AsMemory();
        _psoDebug = device.CreateGraphicsPipelineState(psoDesc);

        vsBlob.Dispose();
        psBlob.Dispose();
        psDebugBlob.Dispose();
    }

    private unsafe void CreateCameraConstantBuffer(ID3D12Device device)
    {
        // Constant buffer must be 256-byte aligned
        _cameraConstantBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(256),
            ResourceStates.GenericRead);

        void* mapped;
        _cameraConstantBuffer.Map(0, null, &mapped);
        _cameraCbvPtr = (IntPtr)mapped;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_cameraCbvPtr != IntPtr.Zero)
        {
            _cameraConstantBuffer?.Unmap(0);
            _cameraCbvPtr = IntPtr.Zero;
        }

        _cameraConstantBuffer?.Dispose();
        _cameraConstantBuffer = null;

        _edgeBuffer?.Dispose();
        _edgeBuffer = null;
        _maxEdges = 0;

        _nodeBuffer?.Dispose();
        _nodeBuffer = null;
        _maxNodes = 0;

        _pso?.Dispose();
        _psoDebug?.Dispose();
        _rootSignature?.Dispose();
        _pso = null;
        _psoDebug = null;
        _rootSignature = null;

        _device = null;
    }

    /// <summary>
    /// Camera constants for EdgeQuad shader.
    /// Must match HLSL CameraData cbuffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EdgeQuadCameraConstants
    {
        public readonly Matrix4x4 View;
        public readonly Matrix4x4 Projection;
        public readonly Vector3 CameraPos;
        public readonly float BaseThickness;

        public EdgeQuadCameraConstants(
            Matrix4x4 view,
            Matrix4x4 projection,
            Vector3 cameraPos,
            float baseThickness)
        {
            View = view;
            Projection = projection;
            CameraPos = cameraPos;
            BaseThickness = baseThickness;
        }
    }
}
