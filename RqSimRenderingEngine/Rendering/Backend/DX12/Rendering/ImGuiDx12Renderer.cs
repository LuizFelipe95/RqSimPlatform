using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// Available pixel shader variants for debugging ImGui rendering.
/// </summary>
public enum ImGuiShaderMode
{
    /// <summary>Normal production rendering - texture * vertex color</summary>
    Production,
    /// <summary>Solid magenta - verifies geometry pipeline</summary>
    SolidMagenta,
    /// <summary>Vertex color only - no texture sampling</summary>
    VertexColorOnly,
    /// <summary>UV coordinates as color - Red=U, Green=V</summary>
    UvDebug,
    /// <summary>Texture alpha visualization - Red=zero alpha, White=has alpha</summary>
    AlphaDebug,
    /// <summary>NDC position as color gradient</summary>
    PositionDebug,
    /// <summary>Diagnostic mode - outputs raw vertex color bytes</summary>
    DiagnosticColorBytes
}

/// <summary>
/// ImGui renderer backend for DirectX 12.
/// Implements the rendering portion of imgui_impl_dx12.
/// </summary>
public sealed class ImGuiDx12Renderer : IDisposable
{
    private ID3D12Device? _device;

    // Pipeline states for each shader variant
    private ID3D12RootSignature? _rootSignature;
    private readonly Dictionary<ImGuiShaderMode, ID3D12PipelineState> _pipelineStates = [];
    private ImGuiShaderMode _currentShaderMode = ImGuiShaderMode.Production;
    private bool _pipelinesCreated;

    // Font texture and SRV
    private ID3D12Resource? _fontTexture;
    private ID3D12DescriptorHeap? _srvHeap;
    private CpuDescriptorHandle _fontSrvCpu;
    private GpuDescriptorHandle _fontSrvGpu;

    // Dynamic buffers (upload heap)
    private ID3D12Resource? _vertexBuffer;
    private ID3D12Resource? _indexBuffer;
    private int _vertexBufferSize;
    private int _indexBufferSize;

    // Projection constant buffer
    private ID3D12Resource? _constantBuffer;
    private IntPtr _constantBufferPtr;

    // ImGui context
    private IntPtr _imguiContext;

    // State
    private bool _initialized;
    private bool _disposed;
    private int _width;
    private int _height;
    private int _matrixLogCount;

    private Format _renderTargetFormat;
    private SampleDescription _sampleDescription;

    /// <summary>
    /// Whether the renderer is initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// The ImGui context handle.
    /// </summary>
    public IntPtr Context => _imguiContext;

    /// <summary>
    /// Current shader mode for debugging.
    /// </summary>
    public ImGuiShaderMode ShaderMode
    {
        get => _currentShaderMode;
        set => _currentShaderMode = value;
    }

    /// <summary>
    /// Available shader modes for UI binding.
    /// </summary>
    public static IReadOnlyList<ImGuiShaderMode> AvailableShaderModes { get; } =
        Enum.GetValues<ImGuiShaderMode>();

    /// <summary>
    /// Initialize the ImGui DX12 renderer.
    /// </summary>
    /// <param name="device">The D3D12 device</param>
    /// <param name="renderTargetFormat">Format of the render target</param>
    /// <param name="sampleDescription">MSAA sample description</param>
    /// <param name="width">Initial viewport width</param>
    /// <param name="height">Initial viewport height</param>
    public void Initialize(ID3D12Device device, Format renderTargetFormat, SampleDescription sampleDescription, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);

        Dispose();
        _disposed = false;

        _device = device;
        _renderTargetFormat = renderTargetFormat;
        _sampleDescription = sampleDescription;
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        // Create ImGui context
        _imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imguiContext);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.DisplaySize = new Vector2(_width, _height);
        io.DisplayFramebufferScale = Vector2.One;

        // Set default font
        io.Fonts.AddFontDefault();
        
        // Apply dark theme style for better visibility
        ImGui.StyleColorsDark();

        CreateDeviceObjects();

        _initialized = true;
    }

    /// <summary>
    /// Resize the viewport.
    /// </summary>
    public void Resize(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        if (_imguiContext != IntPtr.Zero)
        {
            ImGui.SetCurrentContext(_imguiContext);
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_width, _height);
        }
    }

    /// <summary>
    /// Begin a new ImGui frame. Call before ImGui commands.
    /// </summary>
    public void NewFrame(float deltaTime)
    {
        if (!_initialized || _imguiContext == IntPtr.Zero)
            return;

        ImGui.SetCurrentContext(_imguiContext);
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_width, _height);
        io.DeltaTime = deltaTime > 0 ? deltaTime : 1f / 60f;

        ImGui.NewFrame();
    }

    /// <summary>
    /// Render ImGui draw data. Call after ImGui.Render().
    /// </summary>
    public void Render(ID3D12GraphicsCommandList commandList)
    {
        if (!_initialized || _imguiContext == IntPtr.Zero)
            return;

        ImGui.SetCurrentContext(_imguiContext);
        ImGui.Render();

        var drawData = ImGui.GetDrawData();
        RenderDrawDataInternal(drawData, commandList);
    }

    /// <summary>
    /// Render ImGui draw data (public API).
    /// /// </summary>
    /// <param name="drawData">The ImGui draw data</param>
    /// <param name="commandList">The command list to record into</param>
    public unsafe void RenderDrawData(ImDrawDataPtr drawData, ID3D12GraphicsCommandList commandList)
    {
        RenderDrawDataInternal(drawData, commandList);
    }

    private unsafe void RenderDrawDataInternal(ImDrawDataPtr drawData, ID3D12GraphicsCommandList commandList)
    {
        if (!_initialized || drawData.CmdListsCount == 0)
            return;

        // Avoid rendering when minimized
        if (drawData.DisplaySize.X <= 0 || drawData.DisplaySize.Y <= 0)
            return;

        ArgumentNullException.ThrowIfNull(commandList);

        // Validate device is still valid
        if (_device is null || _pipelineStates.Count == 0 || _rootSignature is null)
        {
            System.Diagnostics.Debug.WriteLine("[DX12 ImGui] ERROR: Device objects not valid");
            return;
        }

        // Validate SRV heap
        if (_srvHeap is null)
        {
            System.Diagnostics.Debug.WriteLine("[DX12 ImGui] ERROR: SRV heap is null");
            return;
        }

        // CRITICAL: Ensure font texture is uploaded to GPU before first render
        EnsureFontUploaded(commandList);

        // Ensure buffers are large enough
        EnsureBufferCapacity(drawData.TotalVtxCount, drawData.TotalIdxCount);

        // Validate buffers were created successfully
        if (_vertexBuffer is null || _indexBuffer is null)
        {
            System.Diagnostics.Debug.WriteLine("[DX12 ImGui] ERROR: Vertex/Index buffers are null");
            return;
        }

        // Upload vertex/index data
        UploadBuffers(drawData);

        // Setup orthographic projection
        SetupRenderState(drawData, commandList);

        // Render command lists
        RenderCommandLists(drawData, commandList);
    }

    private static string GetPixelShaderSource(ImGuiShaderMode mode) => mode switch
    {
        ImGuiShaderMode.Production => Dx12Shaders.ImGuiPs,
        ImGuiShaderMode.SolidMagenta => Dx12Shaders.ImGuiPsDebug,
        ImGuiShaderMode.VertexColorOnly => Dx12Shaders.ImGuiPsVertexColorOnly,
        ImGuiShaderMode.UvDebug => Dx12Shaders.ImGuiPsUvDebug,
        ImGuiShaderMode.AlphaDebug => Dx12Shaders.ImGuiPsAlphaDebug,
        ImGuiShaderMode.PositionDebug => Dx12Shaders.ImGuiPsPosDebug,
        ImGuiShaderMode.DiagnosticColorBytes => Dx12Shaders.ImGuiPsColorBytesDebug,
        _ => Dx12Shaders.ImGuiPs
    };

    private void CreateDeviceObjects()
    {
        if (_device is null)
            return;

        CreateRootSignatureAndPipelines();
        CreateFontTexture();
        CreateConstantBuffer();
    }

    private void CreateRootSignatureAndPipelines()
    {
        if (_device is null)
            return;

        System.Diagnostics.Debug.WriteLine("[DX12 ImGui] Creating pipelines for all shader modes...");

        // Compile vertex shader (shared by all pipelines)
        var vsResult = Compiler.Compile(Dx12Shaders.ImGuiVs, "main", null, "vs_5_0", out Blob? vsBlob, out Blob? vsError);
        if (vsResult.Failure || vsBlob is null)
        {
            string errorMsg = vsError is not null
                ? System.Text.Encoding.UTF8.GetString(vsError.AsSpan())
                : "Unknown error";
            vsError?.Dispose();
            throw new InvalidOperationException($"Failed to compile ImGui vertex shader: {errorMsg}");
        }

        // Extract root signature from vertex shader
        Blob? rootSigBlob = Compiler.GetBlobPart(vsBlob.BufferPointer, vsBlob.BufferSize, ShaderBytecodePart.RootSignature, 0);
        if (rootSigBlob is null)
        {
            vsBlob.Dispose();
            throw new InvalidOperationException("Failed to extract root signature from vertex shader.");
        }

        _rootSignature = _device.CreateRootSignature(0, rootSigBlob.BufferPointer, rootSigBlob.BufferSize);
        rootSigBlob.Dispose();

        if (_rootSignature is null)
        {
            vsBlob.Dispose();
            throw new InvalidOperationException("Failed to create root signature.");
        }

        // Create pipeline state for each shader mode
        foreach (ImGuiShaderMode mode in Enum.GetValues<ImGuiShaderMode>())
        {
            string psSource = GetPixelShaderSource(mode);
            var psResult = Compiler.Compile(psSource, "main", null, "ps_5_0", out Blob? psBlob, out Blob? psError);
            if (psResult.Failure || psBlob is null)
            {
                string errorMsg = psError is not null
                    ? System.Text.Encoding.UTF8.GetString(psError.AsSpan())
                    : "Unknown error";
                psError?.Dispose();
                vsBlob.Dispose();
                throw new InvalidOperationException($"Failed to compile ImGui pixel shader ({mode}): {errorMsg}");
            }

            var pso = CreatePipelineState(vsBlob, psBlob);
            _pipelineStates[mode] = pso;

            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Created pipeline for {mode}");
            psBlob.Dispose();
        }

        vsBlob.Dispose();
        _pipelinesCreated = true;
        System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] All {_pipelineStates.Count} pipelines created successfully");
    }

    private ID3D12PipelineState CreatePipelineState(Blob vsBlob, Blob? psBlob)
    {
        // Input layout matches ImDrawVert structure
        // ImDrawVert layout (from ImGui.NET):
        //   float2 pos  @ offset 0  (8 bytes)
        //   float2 uv   @ offset 8  (8 bytes)
        //   uint   col  @ offset 16 (4 bytes) - packed RGBA as 0xAABBGGRR
        // Total: 20 bytes
        //
        // ImGui stores colors as 0xAABBGGRR (ABGR when read as uint32)
        // In memory (little-endian x86): bytes are [R, G, B, A] at offsets 16,17,18,19
        // Format.R8G8B8A8_UNorm reads bytes as [R, G, B, A] - matches perfectly
        
        // Vortice InputElementDescription constructor:
        // (string semanticName, int semanticIndex, Format format, int offset, int slot, InputClassification slotClass, int stepRate)
        // Note: offset is the 4th parameter, slot is the 5th parameter!
        InputElementDescription[] inputElements =
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0, InputClassification.PerVertexData, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0, InputClassification.PerVertexData, 0),
            new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 16, 0, InputClassification.PerVertexData, 0)
        ];

        var blendDesc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
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

        var rasterizerDesc = new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthBias = 0,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = 0.0f,
            DepthClipEnable = true,
            MultisampleEnable = false,
            AntialiasedLineEnable = false,
            ForcedSampleCount = 0,
            ConservativeRaster = ConservativeRasterizationMode.Off
        };

        var depthStencilDesc = new DepthStencilDescription
        {
            DepthEnable = false,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunction.Always,
            StencilEnable = false
        };

        var psoDesc = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vsBlob.AsMemory(),
            PixelShader = psBlob?.AsMemory() ?? ReadOnlyMemory<byte>.Empty,
            BlendState = blendDesc,
            RasterizerState = rasterizerDesc,
            DepthStencilState = depthStencilDesc,
            InputLayout = new InputLayoutDescription(inputElements),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [_renderTargetFormat],
            DepthStencilFormat = Format.Unknown, // No depth testing for ImGui
            SampleDescription = new SampleDescription(1, 0), // ImGui renders to non-MSAA target after resolve
            SampleMask = uint.MaxValue
        };

        return _device.CreateGraphicsPipelineState(psoDesc);
    }

    private unsafe void CreateFontTexture()
    {
        if (_device is null)
            return;

        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        // Debug: Log texture info and sample pixels
        System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Font atlas: {width}x{height}, {bytesPerPixel} bytes/pixel, ptr=0x{pixels:X}");
        
        byte* pixelData = (byte*)pixels;
        if (pixelData != null && width > 0 && height > 0)
        {
            // Sample corner pixels
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Pixel[0,0]: R={pixelData[0]}, G={pixelData[1]}, B={pixelData[2]}, A={pixelData[3]}");
            
            // Sample from middle of texture (likely contains glyph data)
            int midX = width / 2;
            int midY = height / 2;
            int midOffset = (midY * width + midX) * bytesPerPixel;
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Pixel[{midX},{midY}]: R={pixelData[midOffset]}, G={pixelData[midOffset+1]}, B={pixelData[midOffset+2]}, A={pixelData[midOffset+3]}");

            // Find first pixel with non-zero alpha and log its position
            int firstNonZeroAlphaIndex = -1;
            int nonZeroAlphaCount = 0;
            int totalPixels = width * height;
            for (int i = 0; i < totalPixels; i++)
            {
                if (pixelData[i * 4 + 3] > 0)
                {
                    if (firstNonZeroAlphaIndex < 0)
                        firstNonZeroAlphaIndex = i;
                    nonZeroAlphaCount++;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Total pixels: {totalPixels}, Non-zero alpha: {nonZeroAlphaCount}");
            
            if (firstNonZeroAlphaIndex >= 0)
            {
                int fx = firstNonZeroAlphaIndex % width;
                int fy = firstNonZeroAlphaIndex / width;
                int fOffset = firstNonZeroAlphaIndex * bytesPerPixel;
                System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] First glyph pixel at [{fx},{fy}]: R={pixelData[fOffset]}, G={pixelData[fOffset+1]}, B={pixelData[fOffset+2]}, A={pixelData[fOffset+3]}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] WARNING: No pixels with non-zero alpha found! Font atlas may be empty.");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] ERROR: Font pixel data is null or dimensions invalid!");
            return;
        }

        // Create SRV descriptor heap (shader visible for ImGui texture binding)
        _srvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            1,
            DescriptorHeapFlags.ShaderVisible));

        _fontSrvCpu = _srvHeap.GetCPUDescriptorHandleForHeapStart();
        _fontSrvGpu = _srvHeap.GetGPUDescriptorHandleForHeapStart();

        // Create texture resource
        var textureDesc = ResourceDescription.Texture2D(
            Format.R8G8B8A8_UNorm,
            (uint)width,
            (uint)height,
            1,
            1);

        _fontTexture = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            textureDesc,
            ResourceStates.CopyDest);

        // Create SRV
        var srvDesc = new ShaderResourceViewDescription
        {
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Format = Format.R8G8B8A8_UNorm,
            ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
            Texture2D = new Texture2DShaderResourceView { MipLevels = 1 }
        };
        _device.CreateShaderResourceView(_fontTexture, srvDesc, _fontSrvCpu);

        // Upload heap for staging
        // Align row pitch to D3D12_TEXTURE_DATA_PITCH_ALIGNMENT (256 bytes)
        ulong alignedRowPitch = (ulong)((width * bytesPerPixel + 255) & ~255);
        ulong uploadBufferSize = alignedRowPitch * (ulong)height;

        // DO NOT use 'using' here - we need to keep the buffer alive for later GPU upload
        var uploadBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(uploadBufferSize),
            ResourceStates.GenericRead);

        // Map and copy data row by row (with alignment)
        void* mappedData;
        uploadBuffer.Map(0, null, &mappedData);

        byte* srcPtr = (byte*)pixels;
        byte* dstPtr = (byte*)mappedData;
        int srcPitch = width * bytesPerPixel;

        for (int y = 0; y < height; y++)
        {
            Buffer.MemoryCopy(srcPtr + y * srcPitch, dstPtr + y * (int)alignedRowPitch, srcPitch, srcPitch);
        }

        // Verify data was copied to upload buffer
        System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Upload buffer filled: First 4 bytes = {dstPtr[0]}, {dstPtr[1]}, {dstPtr[2]}, {dstPtr[3]}");

        uploadBuffer.Unmap(0);

        // Store upload buffer for later command list upload
        // Will be disposed in EnsureFontUploaded after copy is complete
        _fontUploadBuffer = uploadBuffer;
        _fontWidth = width;
        _fontHeight = height;
        _fontRowPitch = (int)alignedRowPitch;
        _fontUploaded = false;

        // Set texture ID for ImGui
        io.Fonts.SetTexID((IntPtr)_fontSrvGpu.Ptr);
        io.Fonts.ClearTexData();
    }

    // Font upload state
    private ID3D12Resource? _fontUploadBuffer;
    private int _fontWidth;
    private int _fontHeight;
    private int _fontRowPitch;
    private bool _fontUploaded;
    private bool _fontUploadPending; // True when copy command recorded but not yet executed

    /// <summary>
    /// Ensure font texture is uploaded. Call in command list before rendering.
    /// </summary>
    public void EnsureFontUploaded(ID3D12GraphicsCommandList commandList)
    {
        if (_fontUploaded || _fontUploadBuffer is null || _fontTexture is null || _device is null)
            return;

        System.Diagnostics.Debug.WriteLine("[DX12 ImGui] Uploading font texture...");

        // Create footprint that matches our upload buffer layout
        // We created the upload buffer with our own aligned row pitch (256-byte aligned)
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint
            {
                Format = Format.R8G8B8A8_UNorm,
                Width = (uint)_fontWidth,
                Height = (uint)_fontHeight,
                Depth = 1,
                RowPitch = (uint)_fontRowPitch // Use our stored row pitch (256-byte aligned)
            }
        };

        System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Font footprint: Width={footprint.Footprint.Width}, Height={footprint.Footprint.Height}, RowPitch={footprint.Footprint.RowPitch}");

        // Copy from upload buffer to texture
        commandList.CopyTextureRegion(
            new TextureCopyLocation(_fontTexture, 0),
            0, 0, 0,
            new TextureCopyLocation(_fontUploadBuffer, footprint));

        // Transition to shader resource state
        // The copy operation is guaranteed to complete before the barrier transition
        // because barriers create execution dependencies within the same command list
        commandList.ResourceBarrierTransition(_fontTexture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

        _fontUploaded = true;
        System.Diagnostics.Debug.WriteLine("[DX12 ImGui] Font texture upload commands recorded");

        // NOTE: We cannot dispose upload buffer here because the copy hasn't executed yet!
        // The buffer will be disposed in the next frame or on Dispose()
        _fontUploadPending = true;
    }

    /// <summary>
    /// Called after GPU has finished executing commands that include font upload.
    /// </summary>
    public void OnFrameComplete()
    {
        if (_fontUploadPending && _fontUploadBuffer is not null)
        {
            try
            {
                _fontUploadBuffer.Dispose();
                System.Diagnostics.Debug.WriteLine("[DX12 ImGui] Font upload buffer disposed");
            }
            catch
            {
                // Ignore disposal errors
            }
            _fontUploadBuffer = null;
            _fontUploadPending = false;
        }
    }

    private unsafe void CreateConstantBuffer()
    {
        if (_device is null)
            return;

        // 256-byte aligned for constant buffer
        ulong cbSize = (ulong)((Unsafe.SizeOf<Matrix4x4>() + 255) & ~255);

        _constantBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(cbSize),
            ResourceStates.GenericRead);

        void* mapped;
        _constantBuffer.Map(0, null, &mapped);
        _constantBufferPtr = (IntPtr)mapped;
    }

    private unsafe void EnsureBufferCapacity(int vertexCount, int indexCount)
    {
        if (_device is null)
            return;

        int requiredVbSize = vertexCount * Unsafe.SizeOf<ImDrawVert>();
        int requiredIbSize = indexCount * sizeof(ushort);

        // Grow with some headroom
        if (_vertexBuffer is null || requiredVbSize > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(requiredVbSize * 1.5f);
            _vertexBufferSize = Math.Max(_vertexBufferSize, 5000 * Unsafe.SizeOf<ImDrawVert>());

            _vertexBuffer = _device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer((ulong)_vertexBufferSize),
                ResourceStates.GenericRead);
        }

        if (_indexBuffer is null || requiredIbSize > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(requiredIbSize * 1.5f);
            _indexBufferSize = Math.Max(_indexBufferSize, 10000 * sizeof(ushort));

            _indexBuffer = _device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer((ulong)_indexBufferSize),
                ResourceStates.GenericRead);
        }
    }

    private unsafe void UploadBuffers(ImDrawDataPtr drawData)
    {
        if (_vertexBuffer is null || _indexBuffer is null)
            return;

        // Check if draw data is valid
        if (drawData.CmdListsCount == 0 || drawData.TotalVtxCount == 0)
            return;

        void* vtxDst = null;
        void* idxDst = null;

        try
        {
            _vertexBuffer.Map(0, null, &vtxDst);
            _indexBuffer.Map(0, null, &idxDst);

            // Validate mapped pointers
            if (vtxDst == null || idxDst == null)
            {
                System.Diagnostics.Debug.WriteLine("[DX12 ImGui] ERROR: Failed to map buffers!");
                if (vtxDst != null) _vertexBuffer.Unmap(0);
                if (idxDst != null) _indexBuffer.Unmap(0);
                return;
            }

            // Log first vertex only on first few frames
            bool logFirstVert = _frameLogCount < 5;
            byte* vtxDstStart = (byte*)vtxDst; // Save start for byte dump
            
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];

                // Validate source data
                if (cmdList.VtxBuffer.Data == IntPtr.Zero || cmdList.IdxBuffer.Data == IntPtr.Zero)
                    continue;

                int vtxSize = cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
                int idxSize = cmdList.IdxBuffer.Size * sizeof(ushort);

                // Log first vertex for debugging (only first few frames)
                if (logFirstVert && cmdList.VtxBuffer.Size > 0)
                {
                    var firstVert = *(ImDrawVert*)cmdList.VtxBuffer.Data;
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] FirstVertex: Pos=({firstVert.pos.X:F1},{firstVert.pos.Y:F1}), UV=({firstVert.uv.X:F2},{firstVert.uv.Y:F2}), Col=0x{firstVert.col:X8}");
                    
                    // Log the raw bytes of the first vertex to verify layout
                    byte* srcBytes = (byte*)cmdList.VtxBuffer.Data;
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] FirstVertex raw bytes:");
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui]   Pos (bytes 0-7): {srcBytes[0]:X2} {srcBytes[1]:X2} {srcBytes[2]:X2} {srcBytes[3]:X2} {srcBytes[4]:X2} {srcBytes[5]:X2} {srcBytes[6]:X2} {srcBytes[7]:X2}");
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui]   UV  (bytes 8-15): {srcBytes[8]:X2} {srcBytes[9]:X2} {srcBytes[10]:X2} {srcBytes[11]:X2} {srcBytes[12]:X2} {srcBytes[13]:X2} {srcBytes[14]:X2} {srcBytes[15]:X2}");
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui]   Col (bytes 16-19): {srcBytes[16]:X2} {srcBytes[17]:X2} {srcBytes[18]:X2} {srcBytes[19]:X2}");
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui]   ImDrawVert size: {Unsafe.SizeOf<ImDrawVert>()} bytes");
                    
                    logFirstVert = false;
                    _frameLogCount++;
                }

                if (vtxSize > 0)
                {
                    Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vtxDst, vtxSize, vtxSize);
                    vtxDst = (byte*)vtxDst + vtxSize;
                }

                if (idxSize > 0)
                {
                    Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, idxDst, idxSize, idxSize);
                    idxDst = (byte*)idxDst + idxSize;
                }
            }

            // Verify copied data in upload buffer
            if (_frameLogCount <= 2)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] VB after copy - Col (bytes 16-19): {vtxDstStart[16]:X2} {vtxDstStart[17]:X2} {vtxDstStart[18]:X2} {vtxDstStart[19]:X2}");
            }

            _vertexBuffer.Unmap(0);
            _indexBuffer.Unmap(0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] UploadBuffers exception: {ex.Message}");
            try { _vertexBuffer?.Unmap(0); } catch { }
            try { _indexBuffer?.Unmap(0); } catch { }
        }
    }
    
    private int _frameLogCount; // Track frames logged to reduce spam
    private int _drawLogCount; // Track draw call logging

    private void SetupRenderState(ImDrawDataPtr drawData, ID3D12GraphicsCommandList commandList)
    {
        if (_pipelineStates.Count == 0 || _rootSignature is null || _srvHeap is null || _constantBuffer is null)
        {
            System.Diagnostics.Debug.WriteLine("[DX12 ImGui] SetupRenderState: Missing objects!");
            return;
        }

        // Get pipeline for current shader mode
        if (!_pipelineStates.TryGetValue(_currentShaderMode, out var pso))
        {
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] SetupRenderState: Pipeline not found for {_currentShaderMode}, using Production");
            pso = _pipelineStates[ImGuiShaderMode.Production];
        }

        // Setup render state
        commandList.SetPipelineState(pso);
        commandList.SetGraphicsRootSignature(_rootSignature);
        commandList.SetDescriptorHeaps(_srvHeap);
        commandList.SetGraphicsRootConstantBufferView(0, _constantBuffer.GPUVirtualAddress);
        commandList.SetGraphicsRootDescriptorTable(1, _fontSrvGpu);

        // Setup viewport
        commandList.RSSetViewport(new Viewport(
            0, 0,
            drawData.DisplaySize.X,
            drawData.DisplaySize.Y,
            0.0f, 1.0f));

        // Setup orthographic projection matrix
        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        // Log matrix parameters for first few frames
        if (_matrixLogCount < 3)
        {
            _matrixLogCount++;
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] Projection: L={L}, R={R}, T={T}, B={B}");
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] DisplayPos={drawData.DisplayPos}, DisplaySize={drawData.DisplaySize}");
        }

        // Orthographic projection matrix matching imgui_impl_dx12.cpp
        // Original C++ code (stored column-major in memory):
        // float mvp[4][4] = {
        //     { 2/(R-L),     0,          0,     0 },  // Column 0
        //     { 0,           2/(T-B),    0,     0 },  // Column 1
        //     { 0,           0,          0.5,   0 },  // Column 2
        //     { (R+L)/(L-R), (T+B)/(B-T), 0.5,  1 },  // Column 3
        // };
        //
        // C# Matrix4x4 is stored row-major. To get same memory layout, we create the TRANSPOSED matrix:
        float scaleX = 2.0f / (R - L);
        float scaleY = 2.0f / (T - B);
        float translateX = (R + L) / (L - R);
        float translateY = (T + B) / (B - T);
        
        // Row-major in C# that becomes column-major when written to memory for HLSL:
        // We build the matrix as it should appear in HLSL (column-major conceptually),
        // then write it directly (C# will write row-by-row, which gives us column-major layout)
        var mvp = new Matrix4x4(
            scaleX,       0.0f,        0.0f,        0.0f,        // Will become Column 0 in HLSL
            0.0f,         scaleY,      0.0f,        0.0f,        // Will become Column 1 in HLSL
            0.0f,         0.0f,        0.5f,        0.0f,        // Will become Column 2 in HLSL
            translateX,   translateY,  0.5f,        1.0f         // Will become Column 3 in HLSL
        );

        // Log the actual matrix for first few frames
        if (_matrixLogCount < 3)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] MVP (row-major in C#, will be column-major in HLSL):");
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] MVP[Row0]: ({mvp.M11:F4}, {mvp.M12:F4}, {mvp.M13:F4}, {mvp.M14:F4})");
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] MVP[Row1]: ({mvp.M21:F4}, {mvp.M22:F4}, {mvp.M23:F4}, {mvp.M24:F4})");
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] MVP[Row2]: ({mvp.M31:F4}, {mvp.M32:F4}, {mvp.M33:F4}, {mvp.M34:F4})");
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] MVP[Row3]: ({mvp.M41:F4}, {mvp.M42:F4}, {mvp.M43:F4}, {mvp.M44:F4})");
        }

        // Copy to GPU buffer
        Marshal.StructureToPtr(mvp, _constantBufferPtr, fDeleteOld: false);

        // CRITICAL: Setup scissor rect - without this, all geometry is clipped!
        // Use RawRect for D3D12_RECT format: (left, top, right, bottom)
        commandList.RSSetScissorRect(new Vortice.RawRect(
            0, 0,
            (int)drawData.DisplaySize.X,
            (int)drawData.DisplaySize.Y));

        // Setup blend factor
        commandList.OMSetBlendFactor(new Color4(0, 0, 0, 0));

        // Setup vertex/index buffers
        int stride = Unsafe.SizeOf<ImDrawVert>();
        commandList.IASetVertexBuffers(0, new VertexBufferView
        {
            BufferLocation = _vertexBuffer!.GPUVirtualAddress,
            SizeInBytes = (uint)_vertexBufferSize,
            StrideInBytes = (uint)stride
        });

        commandList.IASetIndexBuffer(new IndexBufferView
        {
            BufferLocation = _indexBuffer!.GPUVirtualAddress,
            SizeInBytes = (uint)_indexBufferSize,
            Format = Format.R16_UInt
        });

        commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    }

    private void RenderCommandLists(ImDrawDataPtr drawData, ID3D12GraphicsCommandList commandList)
    {
        int globalVtxOffset = 0;
        int globalIdxOffset = 0;
        int drawCallCount = 0;

        Vector2 clipOff = drawData.DisplayPos;
        bool shouldLog = _drawLogCount < 5;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                var pcmd = cmdList.CmdBuffer[cmdI];

                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    continue;
                }

                // Apply scissor rect
                int left = (int)(pcmd.ClipRect.X - clipOff.X);
                int top = (int)(pcmd.ClipRect.Y - clipOff.Y);
                int right = (int)(pcmd.ClipRect.Z - clipOff.X);
                int bottom = (int)(pcmd.ClipRect.W - clipOff.Y);

                // Clamp to positive values
                if (left < 0) left = 0;
                if (top < 0) top = 0;

                if (right <= left || bottom <= top)
                    continue;

                // RawRect directly matches D3D12_RECT (left, top, right, bottom)
                var scissorRect = new Vortice.RawRect(left, top, right, bottom);
                commandList.RSSetScissorRect(scissorRect);

                // Log first draw call for debugging (only first few frames)
                if (drawCallCount == 0 && shouldLog)
                {
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] DrawCall: ElemCount={pcmd.ElemCount}, IdxOff={pcmd.IdxOffset + globalIdxOffset}, VtxOff={pcmd.VtxOffset + globalVtxOffset}");
                    System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] ScissorRect: left={left}, top={top}, right={right}, bottom={bottom}");
                }

                // Draw
                commandList.DrawIndexedInstanced(
                    pcmd.ElemCount,
                    1,
                    (uint)(pcmd.IdxOffset + globalIdxOffset),
                    (int)(pcmd.VtxOffset + globalVtxOffset),
                    0);

                drawCallCount++;
            }

            globalVtxOffset += cmdList.VtxBuffer.Size;
            globalIdxOffset += cmdList.IdxBuffer.Size;
        }

        // Log draw calls count (only first few frames)
        if (shouldLog && drawCallCount > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12 ImGui] RenderCommandLists: {drawCallCount} draw calls executed");
            _drawLogCount++;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Destroy ImGui context
        if (_imguiContext != IntPtr.Zero)
        {
            ImGui.DestroyContext(_imguiContext);
            _imguiContext = IntPtr.Zero;
        }

        // Use Dispose instead of Release
        _fontUploadBuffer?.Dispose();
        _fontUploadBuffer = null;

        // Dispose all pipeline states
        foreach (var pso in _pipelineStates.Values)
        {
            pso?.Dispose();
        }
        _pipelineStates.Clear();

        _rootSignature?.Dispose();
        _rootSignature = null;

        _fontTexture?.Dispose();
        _fontTexture = null;

        _srvHeap?.Dispose();
        _srvHeap = null;

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;

        _indexBuffer?.Dispose();
        _indexBuffer = null;

        if (_constantBuffer is not null)
        {
            _constantBuffer.Unmap(0);
            _constantBuffer.Dispose();
            _constantBuffer = null;
        }
        _constantBufferPtr = IntPtr.Zero;

        _device = null;
        _initialized = false;
    }
}
