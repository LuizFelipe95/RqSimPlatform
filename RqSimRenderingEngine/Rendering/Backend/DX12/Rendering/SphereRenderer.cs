using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.D3DCompiler;
using Vortice.DXGI;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

internal sealed class SphereRenderer : IDisposable
{
    private ID3D12Device? _device;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pso;
    private ID3D12PipelineState? _depthOnlyPso;  // Depth Pre-Pass PSO

    private ID3D12Resource? _vertexBuffer;
    private ID3D12Resource? _indexBuffer;
    private ID3D12Resource? _instanceBuffer;
    private int _maxInstances;

    private VertexBufferView _vbView;
    private IndexBufferView _ibView;
    private VertexBufferView _instanceView;

    private ID3D12Resource? _cameraConstantBuffer;
    private IntPtr _cameraCbvPtr;

    private uint _indexCount;

    private bool _disposed;

    private Dx12VertexPositionNormal[]? _uploadedVertices;
    private ushort[]? _uploadedIndices;
    private bool _meshUploaded;
    private ID3D12Resource? _meshUploadVertex;
    private ID3D12Resource? _meshUploadIndex;

    // Store formats for depth-only PSO creation
    private Format _depthFormat;
    private SampleDescription _sampleDescription;

    /// <summary>
    /// Whether Depth Pre-Pass is available.
    /// </summary>
    public bool IsDepthPrePassAvailable => _depthOnlyPso is not null;

    internal void Initialize(ID3D12Device device, Format renderTargetFormat, Format depthFormat, SampleDescription sampleDescription)
    {
        ArgumentNullException.ThrowIfNull(device);

        Dispose();
        _disposed = false;

        _device = device;
        _depthFormat = depthFormat;
        _sampleDescription = sampleDescription;

        CreateRootSignature(device);
        CreatePipeline(device, renderTargetFormat, depthFormat, sampleDescription);
        CreateDepthOnlyPipeline(device, depthFormat, sampleDescription);
        CreateMeshBuffers(device);
        CreateCameraConstantBuffer(device);
    }

    internal void EnsureInstanceCapacity(ID3D12Device device, int maxInstances)
    {
        ArgumentNullException.ThrowIfNull(device);

        maxInstances = Math.Max(maxInstances, 1);
        if (_instanceBuffer is not null && maxInstances <= _maxInstances)
            return;

        _instanceBuffer?.Dispose();
        _instanceBuffer = null;

        _maxInstances = maxInstances;

        ulong sizeInBytes = (ulong)(_maxInstances * Unsafe.SizeOf<Dx12NodeInstance>());
        _instanceBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(sizeInBytes),
            ResourceStates.GenericRead);

        _instanceView = new VertexBufferView
        {
            BufferLocation = _instanceBuffer.GPUVirtualAddress,
            SizeInBytes = (uint)sizeInBytes,
            StrideInBytes = (uint)Unsafe.SizeOf<Dx12NodeInstance>()
        };
    }

    internal unsafe void UpdateInstances(ReadOnlySpan<Dx12NodeInstance> instances)
    {
        if (_instanceBuffer is null || instances.Length == 0)
            return;

        void* mapped;
        _instanceBuffer.Map(0, null, &mapped);
        instances.CopyTo(new Span<Dx12NodeInstance>(mapped, instances.Length));
        _instanceBuffer.Unmap(0);
    }

    internal void SetCameraMatrices(in Matrix4x4 view, in Matrix4x4 projection)
    {
        if (_cameraCbvPtr == IntPtr.Zero)
            return;

        // Diagnostic: log matrix values (first few frames only via static counter)
        if (_diagLogCount < 3)
        {
            _diagLogCount++;
            System.Diagnostics.Debug.WriteLine($"[SphereRenderer] View M41-M44: {view.M41:F3}, {view.M42:F3}, {view.M43:F3}, {view.M44:F3}");
            System.Diagnostics.Debug.WriteLine($"[SphereRenderer] Proj M33={projection.M33:F6}, M34={projection.M34:F3}, M43={projection.M43:F6}, M44={projection.M44:F3}");
        }

        CameraConstants data = new(view, projection);
        Marshal.StructureToPtr(data, _cameraCbvPtr, fDeleteOld: false);
    }

    private static int _diagLogCount = 0;

    internal void EnsureMeshUploaded(ID3D12GraphicsCommandList commandList)
    {
        if (_meshUploaded || _device is null || _vertexBuffer is null || _indexBuffer is null)
            return;

        if (_uploadedVertices is null || _uploadedIndices is null)
            throw new InvalidOperationException("Mesh data not prepared.");

        Dx12UploadHelper.UploadBuffer(_device, commandList, _uploadedVertices, _vertexBuffer, out _meshUploadVertex);
        Dx12UploadHelper.UploadBuffer(_device, commandList, _uploadedIndices, _indexBuffer, out _meshUploadIndex);

        commandList.ResourceBarrierTransition(_vertexBuffer, ResourceStates.CopyDest, ResourceStates.VertexAndConstantBuffer);
        commandList.ResourceBarrierTransition(_indexBuffer, ResourceStates.CopyDest, ResourceStates.IndexBuffer);

        _meshUploaded = true;
    }

    internal void Draw(ID3D12GraphicsCommandList commandList, int instanceCount)
    {
        if (instanceCount <= 0 || _pso is null || _rootSignature is null || _cameraConstantBuffer is null)
        {
            System.Diagnostics.Debug.WriteLine($"[SphereRenderer] Draw skipped: instanceCount={instanceCount}, PSO={_pso is not null}, RootSig={_rootSignature is not null}, CameraCB={_cameraConstantBuffer is not null}");
            return;
        }

        if (_instanceBuffer is null)
        {
            System.Diagnostics.Debug.WriteLine("[SphereRenderer] Draw skipped: _instanceBuffer is null");
            return;
        }

        if (!_meshUploaded)
        {
            System.Diagnostics.Debug.WriteLine("[SphereRenderer] Draw called but mesh not uploaded yet!");
            return; // Don't draw if mesh isn't ready
        }

        // Diagnostic: log draw call details (throttled)
        System.Diagnostics.Debug.WriteLine($"[SphereRenderer] Drawing {instanceCount} spheres, indexCount={_indexCount}");

        commandList.SetPipelineState(_pso);
        commandList.SetGraphicsRootSignature(_rootSignature);

        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        unsafe
        {
            VertexBufferView* views = stackalloc VertexBufferView[2];
            views[0] = _vbView;
            views[1] = _instanceView;
            commandList.IASetVertexBuffers(0, 2, views);
        }

        commandList.IASetIndexBuffer(_ibView);
        commandList.SetGraphicsRootConstantBufferView(0, _cameraConstantBuffer.GPUVirtualAddress);

        commandList.DrawIndexedInstanced(_indexCount, (uint)instanceCount, 0, 0, 0);
    }

    /// <summary>
    /// Render spheres to depth buffer only (Depth Pre-Pass).
    /// Used for occlusion culling - fills Z-buffer without color writes.
    /// </summary>
    /// <param name="commandList">Command list to record draw commands.</param>
    /// <param name="instanceCount">Number of sphere instances to draw.</param>
    internal void DrawDepthOnly(ID3D12GraphicsCommandList commandList, int instanceCount)
    {
        if (instanceCount <= 0 || _depthOnlyPso is null || _rootSignature is null || _cameraConstantBuffer is null)
            return;

        commandList.SetPipelineState(_depthOnlyPso);
        commandList.SetGraphicsRootSignature(_rootSignature);

        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        unsafe
        {
            VertexBufferView* views = stackalloc VertexBufferView[2];
            views[0] = _vbView;
            views[1] = _instanceView;
            commandList.IASetVertexBuffers(0, 2, views);
        }

        commandList.IASetIndexBuffer(_ibView);
        commandList.SetGraphicsRootConstantBufferView(0, _cameraConstantBuffer.GPUVirtualAddress);

        commandList.DrawIndexedInstanced(_indexCount, (uint)instanceCount, 0, 0, 0);
    }

    private void CreateRootSignature(ID3D12Device device)
    {
        // Root signature embedded in shader - extract from compiled vertex shader
        Compiler.Compile(Dx12Shaders.NodeVs, "main", null, "vs_5_0", out Blob vsBlob, out _);
        
        // Extract root signature from compiled shader (Vortice returns Blob directly)
        Blob rootSigBlob = Compiler.GetBlobPart(vsBlob.BufferPointer, vsBlob.BufferSize, ShaderBytecodePart.RootSignature, 0);
        _rootSignature = device.CreateRootSignature(0, rootSigBlob.BufferPointer, rootSigBlob.BufferSize);
        
        rootSigBlob.Dispose();
        vsBlob.Dispose();
    }

    private void CreatePipeline(ID3D12Device device, Format rtFormat, Format depthFormat, SampleDescription sampleDescription)
    {
        // Compile shaders using D3DCompiler (fxc-style, sm 5.0)
        Compiler.Compile(Dx12Shaders.NodeVs, "main", null, "vs_5_0", out Blob vsBlob, out _);
        Compiler.Compile(Dx12Shaders.NodePs, "main", null, "ps_5_0", out Blob psBlob, out _);

        InputElementDescription[] inputElements =
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0, InputClassification.PerVertexData, 0),

            new InputElementDescription("INSTANCEPOS", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INSTANCERADIUS", 0, Format.R32_Float, 12, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INSTANCECOLOR", 0, Format.R32G32B32A32_Float, 16, 1, InputClassification.PerInstanceData, 1)
        ];

        // Explicit rasterizer state
        var rasterizerState = new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.Back,
            FrontCounterClockwise = true,
            DepthBias = 0,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = 0.0f,
            DepthClipEnable = true,
            MultisampleEnable = sampleDescription.Count > 1,
            AntialiasedLineEnable = false,
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

        // Explicit blend state
        var blendState = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        blendState.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = false,
            LogicOpEnable = false,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.Zero,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.Zero,
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
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [rtFormat],
            DepthStencilFormat = depthFormat,
            SampleDescription = sampleDescription,
            SampleMask = uint.MaxValue
        };

        _pso = device.CreateGraphicsPipelineState(psoDesc);

        vsBlob.Dispose();
        psBlob.Dispose();
    }

    /// <summary>
    /// Create Depth-Only PSO for Depth Pre-Pass.
    /// ColorWriteEnable = None, only writes to depth buffer.
    /// </summary>
    private void CreateDepthOnlyPipeline(ID3D12Device device, Format depthFormat, SampleDescription sampleDescription)
    {
        // Use same vertex shader, but a minimal pixel shader (or null)
        Compiler.Compile(Dx12Shaders.NodeVs, "main", null, "vs_5_0", out Blob vsBlob, out _);
        
        // Minimal pixel shader that just returns - we only care about depth
        Compiler.Compile(Dx12Shaders.DepthOnlyPs, "main", null, "ps_5_0", out Blob psBlob, out _);

        InputElementDescription[] inputElements =
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0, InputClassification.PerVertexData, 0),

            new InputElementDescription("INSTANCEPOS", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INSTANCERADIUS", 0, Format.R32_Float, 12, 1, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INSTANCECOLOR", 0, Format.R32G32B32A32_Float, 16, 1, InputClassification.PerInstanceData, 1)
        ];

        var rasterizerState = new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.Back,
            FrontCounterClockwise = true,
            DepthBias = 0,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = 0.0f,
            DepthClipEnable = true,
            MultisampleEnable = sampleDescription.Count > 1,
            AntialiasedLineEnable = false,
            ForcedSampleCount = 0,
            ConservativeRaster = ConservativeRasterizationMode.Off
        };

        // Reverse-Z depth stencil state
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

        // Blend state with ColorWriteEnable = None (no color output)
        var blendState = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        blendState.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = false,
            LogicOpEnable = false,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.Zero,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.Zero,
            BlendOperationAlpha = BlendOperation.Add,
            LogicOp = LogicOp.Noop,
            RenderTargetWriteMask = ColorWriteEnable.None  // KEY: No color writes
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
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [], // No render targets for depth-only pass
            DepthStencilFormat = depthFormat,
            SampleDescription = sampleDescription,
            SampleMask = uint.MaxValue
        };

        _depthOnlyPso = device.CreateGraphicsPipelineState(psoDesc);

        vsBlob.Dispose();
        psBlob.Dispose();
    }

    private void CreateMeshBuffers(ID3D12Device device)
    {
        var (vertices, indices) = SphereMesh.Create();
        _indexCount = (uint)indices.Length;

        ulong vbSize = (ulong)(vertices.Length * Unsafe.SizeOf<Dx12VertexPositionNormal>());
        ulong ibSize = (ulong)(indices.Length * sizeof(ushort));

        _vertexBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(vbSize),
            ResourceStates.CopyDest);

        _indexBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(ibSize),
            ResourceStates.CopyDest);

        _vbView = new VertexBufferView
        {
            BufferLocation = _vertexBuffer.GPUVirtualAddress,
            SizeInBytes = (uint)vbSize,
            StrideInBytes = (uint)Unsafe.SizeOf<Dx12VertexPositionNormal>()
        };

        _ibView = new IndexBufferView
        {
            BufferLocation = _indexBuffer.GPUVirtualAddress,
            SizeInBytes = (uint)ibSize,
            Format = Format.R16_UInt
        };

        _uploadedVertices = vertices;
        _uploadedIndices = indices;

        _meshUploaded = false;
        _meshUploadVertex = null;
        _meshUploadIndex = null;
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

        _meshUploadVertex?.Dispose();
        _meshUploadIndex?.Dispose();
        _meshUploadVertex = null;
        _meshUploadIndex = null;

        _instanceBuffer?.Dispose();
        _instanceBuffer = null;

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer = null;
        _indexBuffer = null;

        _pso?.Dispose();
        _depthOnlyPso?.Dispose();  // Dispose depth-only PSO
        _rootSignature?.Dispose();
        _pso = null;
        _depthOnlyPso = null;
        _rootSignature = null;

        _device = null;

        _uploadedVertices = null;
        _uploadedIndices = null;
        _meshUploaded = false;

        _maxInstances = 0;
        _indexCount = 0;
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
