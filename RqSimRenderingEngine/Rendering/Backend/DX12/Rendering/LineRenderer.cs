using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.D3DCompiler;
using Vortice.DXGI;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

internal sealed class LineRenderer : IDisposable
{
    private ID3D12Device? _device;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pso;

    private ID3D12Resource? _vertexBuffer;
    private int _maxVertices;

    private VertexBufferView _vbView;

    private ID3D12Resource? _cameraConstantBuffer;
    private IntPtr _cameraCbvPtr;

    private bool _disposed;

    internal void Initialize(ID3D12Device device, Format renderTargetFormat, Format depthFormat, SampleDescription sampleDescription)
    {
        ArgumentNullException.ThrowIfNull(device);

        Dispose();
        _disposed = false;

        _device = device;

        CreateRootSignature(device);
        CreatePipeline(device, renderTargetFormat, depthFormat, sampleDescription);
        CreateCameraConstantBuffer(device);
    }

    internal void EnsureVertexCapacity(ID3D12Device device, int maxVertices)
    {
        ArgumentNullException.ThrowIfNull(device);

        maxVertices = Math.Max(maxVertices, 2);
        if (_vertexBuffer is not null && maxVertices <= _maxVertices)
            return;

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;

        _maxVertices = maxVertices;

        ulong sizeInBytes = (ulong)(_maxVertices * Unsafe.SizeOf<Dx12LineVertex>());
        _vertexBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(sizeInBytes),
            ResourceStates.GenericRead);

        _vbView = new VertexBufferView
        {
            BufferLocation = _vertexBuffer.GPUVirtualAddress,
            SizeInBytes = (uint)sizeInBytes,
            StrideInBytes = (uint)Unsafe.SizeOf<Dx12LineVertex>()
        };
    }

    internal unsafe void UpdateVertices(ReadOnlySpan<Dx12LineVertex> vertices)
    {
        if (_vertexBuffer is null || vertices.Length == 0)
            return;

        void* mapped;
        _vertexBuffer.Map(0, null, &mapped);
        vertices.CopyTo(new Span<Dx12LineVertex>(mapped, vertices.Length));
        _vertexBuffer.Unmap(0);
    }

    internal void SetCameraMatrices(in Matrix4x4 view, in Matrix4x4 projection)
    {
        if (_cameraCbvPtr == IntPtr.Zero)
            return;

        // Diagnostic: log matrix values (first few frames only via static counter)
        if (_diagLogCount < 3)
        {
            _diagLogCount++;
            System.Diagnostics.Debug.WriteLine($"[LineRenderer] View M41-M44: {view.M41:F3}, {view.M42:F3}, {view.M43:F3}, {view.M44:F3}");
            System.Diagnostics.Debug.WriteLine($"[LineRenderer] Proj M33={projection.M33:F6}, M34={projection.M34:F3}, M43={projection.M43:F6}, M44={projection.M44:F3}");
        }

        CameraConstants data = new(view, projection);
        Marshal.StructureToPtr(data, _cameraCbvPtr, fDeleteOld: false);
    }

    private static int _diagLogCount = 0;

    internal void Draw(ID3D12GraphicsCommandList commandList, int vertexCount)
    {
        if (vertexCount < 2 || (vertexCount % 2) != 0 || _pso is null || _rootSignature is null || _cameraConstantBuffer is null || _vertexBuffer is null)
            return;

        // Diagnostic: log draw call
        if (_diagLogCount < 5)
        {
            System.Diagnostics.Debug.WriteLine($"[LineRenderer] Drawing {vertexCount} vertices ({vertexCount / 2} lines)");
        }

        commandList.SetPipelineState(_pso);
        commandList.SetGraphicsRootSignature(_rootSignature);

        commandList.IASetPrimitiveTopology(PrimitiveTopology.LineList);

        unsafe
        {
            VertexBufferView* view = stackalloc VertexBufferView[1];
            view[0] = _vbView;
            commandList.IASetVertexBuffers(0, 1, view);
        }

        commandList.SetGraphicsRootConstantBufferView(0, _cameraConstantBuffer.GPUVirtualAddress);

        commandList.DrawInstanced((uint)vertexCount, 1, 0, 0);
    }

    /// <summary>
    /// Draw lines using ExecuteIndirect with GPU-culled vertex buffer.
    /// Used when GPU culling is enabled for better performance with many edges.
    /// </summary>
    /// <param name="commandList">Command list to record draw commands.</param>
    /// <param name="culledVertexBuffer">Vertex buffer containing culled (visible) edges.</param>
    /// <param name="culledVbView">Vertex buffer view for the culled buffer.</param>
    /// <param name="indirectArgsBuffer">Buffer containing draw arguments.</param>
    /// <param name="commandSignature">Command signature for ExecuteIndirect.</param>
    internal void DrawIndirect(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource culledVertexBuffer,
        VertexBufferView culledVbView,
        ID3D12Resource indirectArgsBuffer,
        ID3D12CommandSignature commandSignature)
    {
        if (_pso is null || _rootSignature is null || _cameraConstantBuffer is null)
            return;

        commandList.SetPipelineState(_pso);
        commandList.SetGraphicsRootSignature(_rootSignature);

        commandList.IASetPrimitiveTopology(PrimitiveTopology.LineList);

        unsafe
        {
            VertexBufferView* view = stackalloc VertexBufferView[1];
            view[0] = culledVbView;
            commandList.IASetVertexBuffers(0, 1, view);
        }

        commandList.SetGraphicsRootConstantBufferView(0, _cameraConstantBuffer.GPUVirtualAddress);

        // Execute indirect draw with GPU-determined vertex count
        commandList.ExecuteIndirect(commandSignature, 1, indirectArgsBuffer, 0, null, 0);
    }

    private void CreateRootSignature(ID3D12Device device)
    {
        // Root signature embedded in shader - extract from compiled vertex shader
        Compiler.Compile(Dx12Shaders.LineVs, "main", null, "vs_5_0", out Blob vsBlob, out _);
        
        // Extract root signature from compiled shader (Vortice returns Blob directly)
        Blob rootSigBlob = Compiler.GetBlobPart(vsBlob.BufferPointer, vsBlob.BufferSize, ShaderBytecodePart.RootSignature, 0);
        _rootSignature = device.CreateRootSignature(0, rootSigBlob.BufferPointer, rootSigBlob.BufferSize);
        
        rootSigBlob.Dispose();
        vsBlob.Dispose();
    }

    private void CreatePipeline(ID3D12Device device, Format rtFormat, Format depthFormat, SampleDescription sampleDescription)
    {
        Compiler.Compile(Dx12Shaders.LineVs, "main", null, "vs_5_0", out Blob vsBlob, out _);
        Compiler.Compile(Dx12Shaders.LinePs, "main", null, "ps_5_0", out Blob psBlob, out _);

        // InputElementDescription: (semanticName, semanticIndex, format, offset, slot, slotClass, stepRate)
        InputElementDescription[] inputElements =
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 12, 0, InputClassification.PerVertexData, 0)
        ];

        // Explicit rasterizer state for lines
        var rasterizerState = new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthBias = 0,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = 0.0f,
            DepthClipEnable = true,
            MultisampleEnable = sampleDescription.Count > 1,
            AntialiasedLineEnable = true,
            ForcedSampleCount = 0,
            ConservativeRaster = ConservativeRasterizationMode.Off
        };

        // Create Reverse-Z depth stencil state
        var depthStencilState = new DepthStencilDescription
        {
            DepthEnable = true,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunction.Greater,  // Reverse-Z
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

        // Explicit blend state for alpha blending
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
            InputLayout = new InputLayoutDescription(inputElements),
            PrimitiveTopologyType = PrimitiveTopologyType.Line,
            RenderTargetFormats = [rtFormat],
            DepthStencilFormat = depthFormat,
            SampleDescription = sampleDescription,
            SampleMask = uint.MaxValue
        };

        _pso = device.CreateGraphicsPipelineState(psoDesc);

        vsBlob.Dispose();
        psBlob.Dispose();
    }

    private unsafe void CreateCameraConstantBuffer(ID3D12Device device)
    {
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

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _maxVertices = 0;

        _pso?.Dispose();
        _rootSignature?.Dispose();
        _pso = null;
        _rootSignature = null;

        _device = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CameraConstants
    {
        public readonly Matrix4x4 View;
        public readonly Matrix4x4 Projection;

        public CameraConstants(Matrix4x4 view, Matrix4x4 projection)
        {
            View = view;
            Projection = projection;
        }
    }
}
