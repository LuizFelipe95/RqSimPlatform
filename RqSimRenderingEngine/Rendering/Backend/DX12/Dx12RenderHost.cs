using System;
using System.Numerics;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using ImGuiNET;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;
using System.Threading;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

namespace RqSimRenderingEngine.Rendering.Backend.DX12;

public unsafe class Dx12RenderHost : IRenderHost
{
    private IntPtr _hwnd;
    private int _width;
    private int _height;

    // --- Configuration DX12 ---
    private const int FrameCount = 2;
    private int _msaaSampleCount = 4;  // Changed to instance field for runtime detection

    // --- Core DX12 objects ---
    private ID3D12Device2? _device;
    private ID3D12CommandQueue? _commandQueue;
    private IDXGISwapChain3? _swapChain;
    private ID3D12DescriptorHeap? _rtvHeap;
    private int _rtvDescriptorSize;

    // --- Render targets ---
    private ID3D12Resource[]? _backBuffers;
    private ID3D12Resource? _msaaRenderTarget;
    private ID3D12Resource? _msaaDepthBuffer;  // MSAA depth buffer for Reverse-Z

    // --- Descriptor heaps ---
    private ID3D12DescriptorHeap? _dsvHeap;
    private int _dsvDescriptorSize;

    private ID3D12GraphicsCommandList? _commandList;
    private ID3D12CommandAllocator? _commandAllocator;

    // --- Synchronization ---
    private ID3D12Fence? _fence;
    private ulong _fenceValue;
    private IntPtr _fenceEvent;

    // --- ImGui ---
    private ImGuiDx12Renderer? _imGuiRenderer;

    // --- Scene Rendering ---
    private Dx12SceneRenderer? _sceneRenderer;

    // --- State ---
    private bool _initialized;
    private bool _deviceLost; // Set when DEVICE_REMOVED is detected
    private float _deltaTime = 1.0f / 60.0f;
    private int _currentBackBufferIndex;
    private int _frameCount; // For diagnostic logging of first few frames
    
    // --- Resource state tracking ---
    private ResourceStates _msaaRtState = ResourceStates.Common;
    private ResourceStates[] _backBufferStates = new ResourceStates[FrameCount];

    // --- Scene Data ---
    private Matrix4x4 _viewMatrix = Matrix4x4.Identity;
    private Matrix4x4 _projMatrix = Matrix4x4.Identity;
    private bool _imGuiEnabled = true;

    // --- Node/Edge buffers for scene rendering ---
    private Dx12NodeInstance[]? _nodeInstances;
    private int _nodeCount;
    private Dx12LineVertex[]? _edgeVertices;
    private int _edgeVertexCount;

    /// <summary>
    /// Whether the renderer has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized && !_deviceLost;

    /// <summary>
    /// Whether the DX12 device has been lost (DEVICE_REMOVED).
    /// When true, no rendering operations should be attempted.
    /// </summary>
    public bool IsDeviceLost => _deviceLost;

    /// <summary>
    /// Whether ImGui rendering is enabled.
    /// </summary>
    public bool ImGuiEnabled
    {
        get => _imGuiEnabled;
        set => _imGuiEnabled = value;
    }

    /// <summary>
    /// Current ImGui shader mode for debugging.
    /// </summary>
    public ImGuiShaderMode ImGuiShaderMode
    {
        get => _imGuiRenderer?.ShaderMode ?? ImGuiShaderMode.Production;
        set
        {
            if (_imGuiRenderer is not null)
                _imGuiRenderer.ShaderMode = value;
        }
    }

    /// <summary>
    /// Available ImGui shader modes for UI binding.
    /// </summary>
    public static IReadOnlyList<ImGuiShaderMode> AvailableShaderModes => ImGuiDx12Renderer.AvailableShaderModes;

    /// <summary>
    /// Whether GPU-driven edge culling is enabled.
    /// Recommended for scenes with >10,000 edges. Provides frustum culling and subpixel culling.
    /// </summary>
    public bool GpuEdgeCullingEnabled
    {
        get => _sceneRenderer?.GpuCullingEnabled ?? false;
        set
        {
            if (_sceneRenderer is not null)
                _sceneRenderer.GpuCullingEnabled = value;
        }
    }

    /// <summary>
    /// Minimum projected edge size (in NDC, 0-2 range) before subpixel culling.
    /// Default is 0.002 (~1-2 pixels at 1080p). Increase for more aggressive culling.
    /// </summary>
    public float MinProjectedEdgeSize
    {
        get => _sceneRenderer?.MinProjectedEdgeSize ?? 0.002f;
        set
        {
            if (_sceneRenderer is not null)
                _sceneRenderer.MinProjectedEdgeSize = value;
        }
    }

    /// <summary>
    /// Edge rendering mode (Lines or Quads with Vertex Pulling).
    /// </summary>
    public EdgeRenderMode EdgeRenderMode
    {
        get => _sceneRenderer?.EdgeRenderMode ?? EdgeRenderMode.Lines;
        set
        {
            if (_sceneRenderer is not null)
                _sceneRenderer.EdgeRenderMode = value;
        }
    }

    /// <summary>
    /// Base thickness for edge quads (only used when EdgeRenderMode is Quads).
    /// </summary>
    public float EdgeQuadThickness
    {
        get => _sceneRenderer?.EdgeQuadThickness ?? 0.02f;
        set
        {
            if (_sceneRenderer is not null)
                _sceneRenderer.EdgeQuadThickness = value;
        }
    }

    /// <summary>
    /// Whether Occlusion Culling (Depth Pre-Pass) is enabled.
    /// Culls edges hidden behind spheres.
    /// </summary>
    public bool OcclusionCullingEnabled
    {
        get => _sceneRenderer?.OcclusionCullingEnabled ?? false;
        set
        {
            if (_sceneRenderer is not null)
                _sceneRenderer.OcclusionCullingEnabled = value;
        }
    }

    /// <summary>
    /// Whether EdgeQuadRenderer is available.
    /// </summary>
    public bool IsEdgeQuadAvailable => _sceneRenderer?.IsEdgeQuadAvailable ?? false;

    /// <summary>
    /// Whether Occlusion Culling is available.
    /// </summary>
    public bool IsOcclusionCullingAvailable => _sceneRenderer?.IsOcclusionCullingAvailable ?? false;

    /// <summary>
    /// Whether ImGui wants to capture mouse input.
    /// </summary>
    public bool WantCaptureMouse => _initialized && ImGui.GetIO().WantCaptureMouse;

    /// <summary>
    /// Whether ImGui wants to capture keyboard input.
    /// </summary>
    public bool WantCaptureKeyboard => _initialized && ImGui.GetIO().WantCaptureKeyboard;

    /// <summary>
    /// Parameterless constructor for factory pattern.
    /// Call Initialize() before using.
    /// </summary>
    public Dx12RenderHost()
    {
        // Initialize back buffer states
        for (int i = 0; i < FrameCount; i++)
            _backBufferStates[i] = ResourceStates.Present;
    }

    /// <summary>
    /// Legacy constructor with immediate initialization.
    /// </summary>
    [Obsolete("Use parameterless constructor and Initialize() method instead")]
    public Dx12RenderHost(IntPtr hwnd, int width, int height)
    {
        for (int i = 0; i < FrameCount; i++)
            _backBufferStates[i] = ResourceStates.Present;
        Initialize(new RenderHostInitOptions(hwnd, width, height));
    }

    /// <summary>
    /// Initialize the DX12 render host.
    /// </summary>
    public void Initialize(RenderHostInitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_initialized)
            throw new InvalidOperationException("Render host is already initialized.");

        if (options.WindowHandle == IntPtr.Zero)
            throw new ArgumentException("Window handle cannot be zero.", nameof(options));

        if (options.Width <= 0 || options.Height <= 0)
            throw new ArgumentException("Width and height must be positive.", nameof(options));

        _hwnd = options.WindowHandle;
        _width = options.Width;
        _height = options.Height;

        System.Diagnostics.Debug.WriteLine($"[DX12] Initialize: HWND=0x{_hwnd:X}, Size={_width}x{_height}");

        try
        {
            InitializeDx12();
            System.Diagnostics.Debug.WriteLine($"[DX12] DX12 initialized successfully, SwapChain created for HWND=0x{_hwnd:X}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12] DX12 initialization FAILED: {ex.Message}\n{ex.StackTrace}");
            throw new InvalidOperationException($"DX12 initialization failed: {ex.Message}", ex);
        }

        try
        {
            InitializeImGui();
            System.Diagnostics.Debug.WriteLine($"[DX12] ImGui initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12] ImGui initialization FAILED: {ex.Message}\n{ex.StackTrace}");
            throw new InvalidOperationException($"ImGui initialization failed: {ex.Message}", ex);
        }

        try
        {
            InitializeSceneRenderer();
            System.Diagnostics.Debug.WriteLine($"[DX12] Scene renderer initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12] Scene renderer initialization FAILED: {ex.Message}\n{ex.StackTrace}");
            throw new InvalidOperationException($"Scene renderer initialization failed: {ex.Message}", ex);
        }

        _initialized = true;
        System.Diagnostics.Debug.WriteLine($"[DX12] Render host fully initialized");
    }

    /// <summary>
    /// Determine the maximum supported MSAA sample count for given formats.
    /// </summary>
    private static int DetermineMsaaSampleCount(ID3D12Device device, Format rtFormat, Format depthFormat)
    {
        // Try 4x, 2x, then 1x (no MSAA)
        uint[] sampleCounts = [4, 2, 1];
        
        foreach (uint count in sampleCounts)
        {
            // Check render target format support
            var rtFeature = new FeatureDataMultisampleQualityLevels
            {
                Format = rtFormat,
                SampleCount = count
            };
            device.CheckFeatureSupport(Vortice.Direct3D12.Feature.MultisampleQualityLevels, ref rtFeature);
            
            if (rtFeature.NumQualityLevels == 0)
                continue;
            
            // Check depth format support
            var depthFeature = new FeatureDataMultisampleQualityLevels
            {
                Format = depthFormat,
                SampleCount = count
            };
            device.CheckFeatureSupport(Vortice.Direct3D12.Feature.MultisampleQualityLevels, ref depthFeature);
            
            if (depthFeature.NumQualityLevels == 0)
                continue;
            
            // Both formats support this sample count
            return (int)count;
        }
        
        // Fallback to no MSAA
        return 1;
    }

    private void InitializeDx12()
    {
        // NOTE: To avoid DEVICE_REMOVED conflicts with ComputeSharp (which creates its own
        // DX12 device on the hardware GPU for compute shaders), we now use WARP (software
        // renderer) for visualization. This prevents two DX12 devices from fighting over
        // the same hardware adapter.
        //
        // WARP is slower but sufficient for UI rendering and small-scale 3D visualization.
        // For production use, consider implementing shared device with ComputeSharp.
        
        // Stop forcing WARP (software) device by default. Use hardware by default and only fall back to WARP.
        // Keep an opt-in env var to force WARP for troubleshooting and keep existing debug-layer behavior.
        bool useWarp = false;
        bool debugLayerEnabled = false;

#if DEBUG
        string? forceDebug = Environment.GetEnvironmentVariable("DX12_FORCE_DEBUG_LAYER");
        if (forceDebug == "1")
        {
            try
            {
                if (D3D12GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12Debug? debug).Success)
                {
                    debug?.EnableDebugLayer();
                    debugLayerEnabled = true;
                    System.Diagnostics.Debug.WriteLine("[DX12] Debug layer enabled (forced via DX12_FORCE_DEBUG_LAYER=1)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] Debug layer not available: {ex.Message}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[DX12] Debug layer disabled");
        }

        // Force WARP only when explicitly requested.
        string? forceWarp = Environment.GetEnvironmentVariable("DX12_FORCE_WARP");
        if (forceWarp == "1")
        {
            useWarp = true;
            System.Diagnostics.Debug.WriteLine("[DX12] Forcing WARP adapter (DX12_FORCE_WARP=1)");
        }
#endif

        using var factory = CreateDXGIFactory2<IDXGIFactory4>(debugLayerEnabled);

        IDXGIAdapter1? adapter = null;
        string? adapterName = null;

        // Try WARP only if explicitly forced
        if (useWarp)
        {
            System.Diagnostics.Debug.WriteLine("[DX12] Trying WARP adapter...");
            try
            {
                if (factory.EnumWarpAdapter(out IDXGIAdapter1? warpAdapter).Success && warpAdapter is not null)
                {
                    var result = D3D12CreateDevice(warpAdapter, FeatureLevel.Level_11_0, out ID3D12Device? warpDevice);
                    if (result.Success && warpDevice is not null)
                    {
                        warpDevice.Dispose();
                        adapter = warpAdapter;
                        adapterName = "WARP (Software)";
                        System.Diagnostics.Debug.WriteLine("[DX12] WARP adapter OK");
                    }
                    else
                    {
                        warpAdapter.Dispose();
                        System.Diagnostics.Debug.WriteLine($"[DX12] WARP failed: 0x{result.Code:X8}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DX12] WARP not available");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] WARP error: {ex.Message}");
            }
        }

        // Prefer hardware adapter; fall back to WARP only if no hardware is available
        if (adapter is null)
        {
            System.Diagnostics.Debug.WriteLine("[DX12] Trying hardware adapters...");
            for (uint i = 0; factory.EnumAdapters1(i, out var tempAdapter).Success; i++)
            {
                var desc = tempAdapter.Description1;
                System.Diagnostics.Debug.WriteLine($"[DX12] Adapter {i}: {desc.Description}");

                if ((desc.Flags & AdapterFlags.Software) != AdapterFlags.None)
                {
                    System.Diagnostics.Debug.WriteLine($"[DX12] Skipping software adapter: {desc.Description}");
                    tempAdapter.Dispose();
                    continue;
                }

                var result = D3D12CreateDevice(tempAdapter, FeatureLevel.Level_11_0, out ID3D12Device? tempDevice);
                if (result.Success && tempDevice is not null)
                {
                    tempDevice.Dispose();
                    adapter = tempAdapter;
                    adapterName = desc.Description;
                    System.Diagnostics.Debug.WriteLine($"[DX12] Selected: {adapterName}");
                    break;
                }

                tempAdapter.Dispose();
            }
        }

        // Hardware failed: fall back to WARP as a last resort
        if (adapter is null)
        {
            System.Diagnostics.Debug.WriteLine("[DX12] Hardware adapter not available, falling back to WARP...");
            if (factory.EnumWarpAdapter(out IDXGIAdapter1? warpAdapter).Success && warpAdapter is not null)
            {
                var result = D3D12CreateDevice(warpAdapter, FeatureLevel.Level_11_0, out ID3D12Device? warpDevice);
                if (result.Success && warpDevice is not null)
                {
                    warpDevice.Dispose();
                    adapter = warpAdapter;
                    adapterName = "WARP (Software)";
                    System.Diagnostics.Debug.WriteLine("[DX12] WARP adapter OK (fallback)");
                }
                else
                {
                    warpAdapter.Dispose();
                    System.Diagnostics.Debug.WriteLine($"[DX12] WARP failed: 0x{result.Code:X8}");
                }
            }
        }

        if (adapter is null)
            throw new InvalidOperationException("No compatible DirectX 12 adapter found (hardware or WARP).");

        try
        {
            var createResult = D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out _device);
            if (createResult.Failure || _device is null)
                throw new InvalidOperationException($"D3D12CreateDevice failed: 0x{createResult.Code:X8}");
            
            System.Diagnostics.Debug.WriteLine($"[DX12] Device created: {adapterName}");
        }
        finally
        {
            adapter.Dispose();
        }

        // Check MSAA support for render target and depth formats
        _msaaSampleCount = DetermineMsaaSampleCount(_device, Format.R8G8B8A8_UNorm, Format.D32_Float);
        System.Diagnostics.Debug.WriteLine($"[DX12] Using MSAA sample count: {_msaaSampleCount}");

        _commandQueue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        if (_commandQueue is null)
            throw new InvalidOperationException("Failed to create command queue");
        System.Diagnostics.Debug.WriteLine("[DX12] Command queue created");

        var swapChainDesc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.R8G8B8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = FrameCount,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Scaling = Scaling.Stretch,
            Flags = SwapChainFlags.None
        };

        System.Diagnostics.Debug.WriteLine($"[DX12] Creating SwapChain for HWND=0x{_hwnd:X}");
        using var swapChainTemp = factory.CreateSwapChainForHwnd(_commandQueue, _hwnd, swapChainDesc);
        _swapChain = swapChainTemp.QueryInterface<IDXGISwapChain3>();
        if (_swapChain is null)
            throw new InvalidOperationException("Failed to create swap chain");
        System.Diagnostics.Debug.WriteLine("[DX12] SwapChain created");

        // Disable Alt+Enter fullscreen
        factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);

        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount + 1));
        if (_rtvHeap is null)
            throw new InvalidOperationException("Failed to create RTV heap");

        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        System.Diagnostics.Debug.WriteLine($"[DX12] RTV heap created, descriptor size={_rtvDescriptorSize}");

        // Create DSV heap for depth buffer
        _dsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));
        if (_dsvHeap is null)
            throw new InvalidOperationException("Failed to create DSV heap");

        _dsvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        System.Diagnostics.Debug.WriteLine($"[DX12] DSV heap created, descriptor size={_dsvDescriptorSize}");

        CreateRenderTargets();
        _commandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
        if (_commandAllocator is null)
            throw new InvalidOperationException("Failed to create command allocator");

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(0u, CommandListType.Direct, _commandAllocator, null);
        if (_commandList is null)
            throw new InvalidOperationException("Failed to create command list");
        _commandList.Close();
        System.Diagnostics.Debug.WriteLine("[DX12] Command list created");

        _fence = _device.CreateFence(0ul);
        if (_fence is null)
            throw new InvalidOperationException("Failed to create fence");

        _fenceEvent = CreateEvent(IntPtr.Zero, false, false, null);
        if (_fenceEvent == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create fence event");
        System.Diagnostics.Debug.WriteLine("[DX12] Fence created");
    }

    private void CreateRenderTargets()
    {
        if (_device is null || _swapChain is null || _rtvHeap is null || _dsvHeap is null)
            return;

        _backBuffers = new ID3D12Resource[FrameCount];
        var rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();

        // A. BackBuffers
        for (int i = 0; i < FrameCount; i++)
        {
            _backBuffers[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _device.CreateRenderTargetView(_backBuffers[i], null, rtvHandle);
            _backBufferStates[i] = ResourceStates.Present;
            rtvHandle += _rtvDescriptorSize;
            System.Diagnostics.Debug.WriteLine($"[DX12] BackBuffer[{i}] created");
        }

        // B. MSAA Color Target
        var msaaDesc = ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)_width, (uint)_height, 1, 1, (uint)_msaaSampleCount, 0, ResourceFlags.AllowRenderTarget);
        var clearVal = new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0.1f, 0.1f, 0.12f, 1.0f));

        _msaaRenderTarget = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            msaaDesc,
            ResourceStates.RenderTarget,
            clearVal);
        _msaaRtState = ResourceStates.RenderTarget;

        _device.CreateRenderTargetView(_msaaRenderTarget, null, rtvHandle);
        System.Diagnostics.Debug.WriteLine("[DX12] MSAA render target created");

        // C. MSAA Depth Buffer (Reverse-Z: clear to 0.0 = far plane)
        var depthDesc = ResourceDescription.Texture2D(
            Format.D32_Float, 
            (uint)_width, 
            (uint)_height, 
            1, 1, 
            (uint)_msaaSampleCount, 
            0, 
            ResourceFlags.AllowDepthStencil);
        
        // Reverse-Z: far plane = 0.0, near plane = 1.0
        var clearDepth = new ClearValue(Format.D32_Float, new DepthStencilValue(CameraMatrixHelper.ReverseZClearDepth, 0));

        _msaaDepthBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            depthDesc,
            ResourceStates.DepthWrite,
            clearDepth);

        _device.CreateDepthStencilView(_msaaDepthBuffer, null, _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        System.Diagnostics.Debug.WriteLine("[DX12] MSAA depth buffer created (Reverse-Z)");
    }

    private void InitializeImGui()
    {
        if (_device is null)
            return;

        _imGuiRenderer = new ImGuiDx12Renderer();
        _imGuiRenderer.Initialize(
            _device,
            Format.R8G8B8A8_UNorm,
            new SampleDescription(1, 0), // ImGui renders to resolved (non-MSAA) buffer
            _width,
            _height);
    }

    private void InitializeSceneRenderer()
    {
        if (_device is null)
            return;

        _sceneRenderer = new Dx12SceneRenderer();
        _sceneRenderer.Initialize(
            _device,
            Format.R8G8B8A8_UNorm,
            Format.D32_Float,
            new SampleDescription((uint)_msaaSampleCount, 0));
    }

    /// <summary>
    /// Update input state for the current frame.
    /// </summary>
    public void UpdateInput(InputSnapshot snapshot)
    {
        if (!_initialized || _imGuiRenderer is null)
            return;

        _deltaTime = snapshot.DeltaTime > 0 ? snapshot.DeltaTime : 1.0f / 60.0f;

        var io = ImGui.GetIO();
        io.MousePos = snapshot.MousePosition;
        io.MouseDown[0] = snapshot.MouseButtons.HasFlag(MouseButtonState.Left);
        io.MouseDown[1] = snapshot.MouseButtons.HasFlag(MouseButtonState.Right);
        io.MouseDown[2] = snapshot.MouseButtons.HasFlag(MouseButtonState.Middle);
        io.MouseWheel = snapshot.WheelDelta;
        io.KeyCtrl = snapshot.Modifiers.HasFlag(KeyModifiers.Control);
        io.KeyShift = snapshot.Modifiers.HasFlag(KeyModifiers.Shift);
        io.KeyAlt = snapshot.Modifiers.HasFlag(KeyModifiers.Alt);

        foreach (var c in snapshot.TextInput)
            io.AddInputCharacter(c);

        foreach (var key in snapshot.KeysPressed)
        {
            var imguiKey = MapToImGuiKey(key);
            if (imguiKey != ImGuiKey.None)
                io.AddKeyEvent(imguiKey, true);
        }

        foreach (var key in snapshot.KeysReleased)
        {
            var imguiKey = MapToImGuiKey(key);
            if (imguiKey != ImGuiKey.None)
                io.AddKeyEvent(imguiKey, false);
        }
    }

    /// <summary>
    /// Map KeyCode to ImGuiKey.
    /// </summary>
    private static ImGuiKey MapToImGuiKey(KeyCode key)
    {
        return key switch
        {
            KeyCode.Tab => ImGuiKey.Tab,
            KeyCode.Left => ImGuiKey.LeftArrow,
            KeyCode.Right => ImGuiKey.RightArrow,
            KeyCode.Up => ImGuiKey.UpArrow,
            KeyCode.Down => ImGuiKey.DownArrow,
            KeyCode.PageUp => ImGuiKey.PageUp,
            KeyCode.PageDown => ImGuiKey.PageDown,
            KeyCode.Home => ImGuiKey.Home,
            KeyCode.End => ImGuiKey.End,
            KeyCode.Insert => ImGuiKey.Insert,
            KeyCode.Delete => ImGuiKey.Delete,
            KeyCode.Back => ImGuiKey.Backspace,
            KeyCode.Space => ImGuiKey.Space,
            KeyCode.Enter => ImGuiKey.Enter,
            KeyCode.Escape => ImGuiKey.Escape,
            KeyCode.A => ImGuiKey.A,
            KeyCode.C => ImGuiKey.C,
            KeyCode.V => ImGuiKey.V,
            KeyCode.X => ImGuiKey.X,
            KeyCode.Y => ImGuiKey.Y,
            KeyCode.Z => ImGuiKey.Z,
            _ => ImGuiKey.None
        };
    }

    /// <summary>
    /// Transition a resource to a new state, tracking current state.
    /// </summary>
    private void TransitionMsaaRt(ResourceStates newState)
    {
        if (_commandList is null || _msaaRenderTarget is null || _msaaRtState == newState)
            return;
        
        _commandList.ResourceBarrierTransition(_msaaRenderTarget, _msaaRtState, newState);
        _msaaRtState = newState;
    }

    private void TransitionBackBuffer(int index, ResourceStates newState)
    {
        if (_commandList is null || _backBuffers is null || _backBufferStates[index] == newState)
            return;
        
        _commandList.ResourceBarrierTransition(_backBuffers[index], _backBufferStates[index], newState);
        _backBufferStates[index] = newState;
    }

    /// <summary>
    /// Begin a new frame. Call before drawing scene and ImGui.
    /// </summary>
    public void BeginFrame()
    {
        // Early exit if device was lost - no operations
        if (_deviceLost)
            return;

        if (!_initialized || _commandAllocator is null || _commandList is null ||
            _swapChain is null || _rtvHeap is null || _msaaRenderTarget is null || _device is null)
            return;

        // Check for device removed (but don't check every frame - expensive)
        if (_frameCount % 60 == 0)
        {
            var deviceRemovedReason = _device.DeviceRemovedReason;
            if (deviceRemovedReason.Failure)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] DEVICE REMOVED: 0x{deviceRemovedReason.Code:X8}");
                _initialized = false;
                _deviceLost = true;
                return;
            }
        }

        try
        {
            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator, null);
        }
        catch (SharpGenException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12] Command list reset failed: 0x{ex.HResult:X8}");
            _deviceLost = true;
            return;
        }

        _currentBackBufferIndex = (int)_swapChain.CurrentBackBufferIndex;
        var rtvHandleStart = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        var msaaRtv = rtvHandleStart + (FrameCount * _rtvDescriptorSize);

        // Transition MSAA to RenderTarget
        TransitionMsaaRt(ResourceStates.RenderTarget);

        // Get DSV handle for depth buffer
        var dsvHandle = _dsvHeap?.GetCPUDescriptorHandleForHeapStart();

        // Set render target with depth buffer and clear both
        if (dsvHandle.HasValue)
        {
            _commandList.OMSetRenderTargets(msaaRtv, dsvHandle.Value);
            // Clear depth buffer with Reverse-Z value (0.0 = far plane)
            _commandList.ClearDepthStencilView(dsvHandle.Value, ClearFlags.Depth, CameraMatrixHelper.ReverseZClearDepth, 0);
        }
        else
        {
            _commandList.OMSetRenderTargets(msaaRtv, null);
        }
        
        _commandList.ClearRenderTargetView(msaaRtv, new Color4(0.15f, 0.15f, 0.18f, 1.0f));

        // Set viewport and scissor
        // Use RawRect for D3D12_RECT format: (left, top, right, bottom)
        _commandList.RSSetViewport(new Viewport(0, 0, _width, _height));
        _commandList.RSSetScissorRect(new Vortice.RawRect(0, 0, _width, _height));

        // Start ImGui frame if enabled
        if (_imGuiEnabled)
        {
            // Use ImGuiDx12Renderer to start a frame (sets context + delta)
            _imGuiRenderer?.NewFrame(_deltaTime);
        }

        _frameCount++;
        if (_frameCount <= 3)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12] BeginFrame {_frameCount}: BackBuffer={_currentBackBufferIndex}, Size={_width}x{_height}");
        }
    }

    /// <summary>
    /// End frame and present. Call after all drawing is complete.
    /// </summary>
    public void EndFrame()
    {
        // Early exit if device was lost - no operations possible
        if (_deviceLost)
            return;

        if (!_initialized || _commandList is null || _swapChain is null ||
            _rtvHeap is null || _msaaRenderTarget is null ||
            _backBuffers is null || _commandQueue is null)
            return;

        var rtvHandleStart = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        var backBufferRtv = rtvHandleStart + (_currentBackBufferIndex * _rtvDescriptorSize);

        // Resolve MSAA -> BackBuffer
        TransitionMsaaRt(ResourceStates.ResolveSource);
        TransitionBackBuffer(_currentBackBufferIndex, ResourceStates.ResolveDest);

        _commandList.ResolveSubresource(_backBuffers[_currentBackBufferIndex], 0u, _msaaRenderTarget, 0u, Format.R8G8B8A8_UNorm);

        // Render ImGui onto non-MSAA backbuffer
        if (_imGuiEnabled && _imGuiRenderer is not null)
        {
            TransitionBackBuffer(_currentBackBufferIndex, ResourceStates.RenderTarget);

            _commandList.OMSetRenderTargets(backBufferRtv, null);
            _commandList.RSSetViewport(new Viewport(0, 0, _width, _height));
            _commandList.RSSetScissorRect(new Vortice.RawRect(0, 0, _width, _height));

            if (_frameCount <= 3)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] ImGui RT set: BackBuffer[{_currentBackBufferIndex}], RTV=0x{backBufferRtv.Ptr:X}");
            }

            ImGui.SetCurrentContext(_imGuiRenderer.Context);

            // Note: Debug rectangles and DX12 Status window removed after successful testing
            // The rendering pipeline is now verified and working correctly

            ImGui.Render();

            var drawData = ImGui.GetDrawData();
            
            if (_frameCount <= 5)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] ImGui: CmdLists={drawData.CmdListsCount}, Vtx={drawData.TotalVtxCount}, DisplaySize={drawData.DisplaySize}");
            }
            
            if (drawData.CmdListsCount > 0)
            {
                _imGuiRenderer.RenderDrawData(drawData, _commandList);
            }
        }

        // Transition to Present
        TransitionBackBuffer(_currentBackBufferIndex, ResourceStates.Present);

        try
        {
            _commandList.Close();
            _commandQueue.ExecuteCommandList(_commandList);

            var presentResult = _swapChain.Present(1, 0);
            if (presentResult.Failure)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] Present FAILED: 0x{presentResult.Code:X8}");
                
                // Check if device was lost during present
                if (presentResult.Code == unchecked((int)0x887A0005) || // DXGI_ERROR_DEVICE_REMOVED
                    presentResult.Code == unchecked((int)0x887A0007))   // DXGI_ERROR_DEVICE_RESET
                {
                    _deviceLost = true;
                    return;
                }
            }
            else if (_frameCount <= 3)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] EndFrame {_frameCount}: Present OK");
            }

            WaitForGpu();
            
            // Notify ImGui renderer that GPU has finished - safe to dispose upload buffers
            _imGuiRenderer?.OnFrameComplete();
        }
        catch (SharpGenException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DX12] EndFrame exception: 0x{ex.HResult:X8} - {ex.Message}");
            _deviceLost = true;
        }
    }

    /// <summary>
    /// Legacy render method for backward compatibility.
    /// </summary>
    [Obsolete("Use BeginFrame() and EndFrame() instead")]
    public void Render(float deltaTime = 1.0f / 60.0f)
    {
        _deltaTime = deltaTime;
        BeginFrame();
        ImGui.Begin("Debug");
        ImGui.Text("Hello DX12");
        ImGui.End();
        EndFrame();
    }

    private void WaitForGpu()
    {
        if (_commandQueue is null || _fence is null)
            return;

        _fenceValue++;
        _commandQueue.Signal(_fence, _fenceValue);

        if (_fence.CompletedValue < _fenceValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            uint waitResult = WaitForSingleObject(_fenceEvent, 2000);
            if (waitResult != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DX12] WaitForGpu timeout: {waitResult}");
            }
        }
    }

    /// <summary>
    /// Legacy input method for backward compatibility.
    /// </summary>
    [Obsolete("Use UpdateInput(InputSnapshot) instead")]
    public void UpdateImGuiInput(char? keyChar, int? keyCode, bool mouseDown, Vector2 mousePos, int wheelDelta)
    {
    }

    /// <summary>
    /// Resize the render targets.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (!_initialized || _backBuffers is null || _msaaRenderTarget is null || _swapChain is null)
            return;

        WaitForGpu();

        for (int i = 0; i < FrameCount; i++)
        {
            _backBuffers[i]?.Dispose();
            _backBufferStates[i] = ResourceStates.Present;
        }

        _msaaRenderTarget.Dispose();
        _msaaDepthBuffer?.Dispose();

        _swapChain.ResizeBuffers(FrameCount, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
        _width = width;
        _height = height;

        CreateRenderTargets();
        _imGuiRenderer?.Resize(width, height);
        
        System.Diagnostics.Debug.WriteLine($"[DX12] Resized to {width}x{height}");
    }

    /// <summary>
    /// Clear the render target with specified color.
    /// Should be called between BeginFrame and EndFrame.
    /// </summary>
    public void Clear(Color4 color)
    {
        if (!_initialized || _commandList is null || _rtvHeap is null)
            return;

        var msaaRtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart() + (FrameCount * _rtvDescriptorSize);
        _commandList.ClearRenderTargetView(msaaRtv, color);
    }

    /// <summary>
    /// Set camera matrices for scene rendering.
    /// </summary>
    public void SetCameraMatrices(Matrix4x4 viewMatrix, Matrix4x4 projMatrix)
    {
        _viewMatrix = viewMatrix;
        _projMatrix = projMatrix;
    }

    /// <summary>
    /// Set node instance data for sphere rendering.
    /// </summary>
    public void SetNodeInstances(Dx12NodeInstance[] instances, int count)
    {
        _nodeInstances = instances;
        _nodeCount = count;
    }

    /// <summary>
    /// Set edge vertex data for line rendering.
    /// </summary>
    public void SetEdgeVertices(Dx12LineVertex[] vertices, int count)
    {
        _edgeVertices = vertices;
        _edgeVertexCount = count;
    }

    /// <summary>
    /// Render the 3D scene with proper draw order for Early-Z optimization.
    /// Call between BeginFrame() and EndFrame().
    /// </summary>
    /// <remarks>
    /// Draw order is critical for performance:
    /// 1. Nodes (spheres) are drawn first - they fill the Z-buffer as large occluders
    /// 2. Edges (lines) are drawn second - GPU rejects fragments behind nodes via Early-Z
    /// 
    /// This can eliminate millions of edge pixel shader invocations when nodes occlude
    /// significant portions of the scene.
    /// 
    /// For Reverse-Z to work correctly, use CreatePerspectiveReverseZ from CameraMatrixHelper.
    /// </remarks>
    public void RenderScene()
    {
        if (!_initialized || _deviceLost || _sceneRenderer is null || _commandList is null || _device is null)
            return;

        // Update camera matrices in scene renderer
        _sceneRenderer.SetCameraMatrices(_viewMatrix, _projMatrix);

        // Ensure buffers have capacity
        int requiredNodes = _nodeCount > 0 ? _nodeCount : 1;
        int requiredEdgeVerts = _edgeVertexCount > 0 ? _edgeVertexCount : 2;
        _sceneRenderer.EnsureCapacity(requiredNodes, requiredEdgeVerts);

        // Upload data to GPU
        if (_nodeInstances is not null && _nodeCount > 0)
        {
            _sceneRenderer.UpdateNodeInstances(_nodeInstances.AsSpan(0, _nodeCount));
        }

        if (_edgeVertices is not null && _edgeVertexCount >= 2)
        {
            _sceneRenderer.UpdateEdgeVertices(_edgeVertices.AsSpan(0, _edgeVertexCount));
        }

        // Render with proper draw order (nodes first, then edges for Early-Z)
        _sceneRenderer.Render(_commandList, _nodeCount, _edgeVertexCount);
    }

    /// <summary>
    /// Start a new ImGui frame.
    /// Call before ImGui commands.
    /// </summary>
    public void ImGuiNewFrame(float deltaTime)
    {
        if (!_initialized || !_imGuiEnabled || _imGuiRenderer is null)
            return;

        _deltaTime = deltaTime > 0 ? deltaTime : 1.0f / 60.0f;
        _imGuiRenderer.NewFrame(_deltaTime);
    }

    public void Dispose()
    {
        System.Diagnostics.Debug.WriteLine("[DX12] Disposing...");
        
        if (_initialized)
            WaitForGpu();

        _sceneRenderer?.Dispose();
        _imGuiRenderer?.Dispose();

        if (_backBuffers is not null)
        {
            foreach (var buf in _backBuffers)
                buf?.Dispose();
        }

        _msaaRenderTarget?.Dispose();
        _msaaDepthBuffer?.Dispose();
        _rtvHeap?.Dispose();
        _dsvHeap?.Dispose();
        _swapChain?.Dispose();
        _commandQueue?.Dispose();
        _commandAllocator?.Dispose();
        _commandList?.Dispose();
        _fence?.Dispose();
        _device?.Dispose();

        if (_fenceEvent != IntPtr.Zero)
            CloseHandle(_fenceEvent);

        _initialized = false;
        System.Diagnostics.Debug.WriteLine("[DX12] Disposed");
    }

    [System.Runtime.InteropServices.DllImport("kernel32")]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [System.Runtime.InteropServices.DllImport("kernel32")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [System.Runtime.InteropServices.DllImport("kernel32")]
    private static extern bool CloseHandle(IntPtr hObject);
}