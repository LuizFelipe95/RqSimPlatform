using System;
using System.Diagnostics;
using ComputeSharp;
using RqSimRenderingEngine.Rendering.Interop;
using RQSimulation.GPUOptimized.Rendering;

namespace RqSimUI.Rendering.Interop;

/// <summary>
/// High-level facade for ComputeSharp <-> DX12 interop.
/// 
/// Provides a simple API for:
/// - Initializing the interop layer
/// - Running physics-to-render mapping on GPU
/// - Uploading render data to DX12 vertex buffers
/// 
/// Usage:
/// 1. Create ComputeSharpDx12Bridge
/// 2. Call Initialize() once
/// 3. Each frame: MapPhysicsToRender() then UploadToVertexBuffer()
/// </summary>
public sealed class ComputeSharpDx12Bridge : IDisposable
{
    private UnifiedDeviceContext? _deviceContext;
    private GpuSyncManager? _syncManager;
    private RenderDataMapper? _renderMapper;
    private bool _disposed;

    /// <summary>
    /// Whether the bridge is initialized and ready.
    /// </summary>
    public bool IsInitialized => _deviceContext?.IsInitialized == true;

    /// <summary>
    /// The active interop strategy.
    /// </summary>
    public InteropStrategy Strategy => _deviceContext?.Strategy ?? InteropStrategy.None;

    /// <summary>
    /// Whether double precision is supported on the GPU.
    /// </summary>
    public bool IsDoublePrecisionSupported => _deviceContext?.IsDoublePrecisionSupported == true;

    /// <summary>
    /// The unified device context.
    /// </summary>
    public UnifiedDeviceContext? DeviceContext => _deviceContext;

    /// <summary>
    /// The GPU sync manager.
    /// </summary>
    public GpuSyncManager? SyncManager => _syncManager;

    /// <summary>
    /// Initialize the interop bridge.
    /// </summary>
    /// <param name="maxNodes">Maximum number of physics nodes to process</param>
    /// <returns>Initialization result</returns>
    public InteropInitResult Initialize(int maxNodes = 100_000)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ComputeSharpDx12Bridge));

        if (IsInitialized)
            return new InteropInitResult(true, Strategy, "Already initialized");

        try
        {
            // Run diagnostics first
            var diagnostics = InteropCapabilities.RunDiagnostics();
            Debug.WriteLine(diagnostics.GetSummary());

            if (!diagnostics.IsViable)
            {
                return new InteropInitResult(
                    false,
                    InteropStrategy.None,
                    $"Interop not viable: {diagnostics.StrategyReason}");
            }

            // Initialize unified device context
            _deviceContext = new UnifiedDeviceContext();
            var initResult = _deviceContext.Initialize();

            if (!initResult.Success)
            {
                _deviceContext.Dispose();
                _deviceContext = null;
                return initResult;
            }

            // Initialize sync manager
            _syncManager = new GpuSyncManager(_deviceContext);

            // Initialize render mapper (in RqSimGraphEngine)
            _renderMapper = new RenderDataMapper();
            _renderMapper.Initialize(maxNodes);

            Debug.WriteLine($"[ComputeSharpDx12Bridge] Initialized: strategy={Strategy}, fp64={IsDoublePrecisionSupported}");

            return new InteropInitResult(
                true,
                Strategy,
                $"Initialized with {Strategy} strategy");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ComputeSharpDx12Bridge] Initialization failed: {ex.Message}");
            Cleanup();
            return new InteropInitResult(false, InteropStrategy.None, $"Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Map physics data to render vertices.
    /// This runs the GPU compute shader for double->float conversion and coloring.
    /// </summary>
    /// <param name="physics">Source physics node states</param>
    /// <param name="vertices">Destination render vertices</param>
    /// <param name="colorMode">Color mode (0=Phase, 1=Energy, 2=Mass)</param>
    /// <param name="baseSize">Base vertex size</param>
    /// <param name="sizeScale">Size scaling for probability</param>
    public void MapPhysicsToRender(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<RenderNodeVertex> vertices,
        int colorMode = 0,
        float baseSize = 0.5f,
        float sizeScale = 2f)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ComputeSharpDx12Bridge));

        if (_renderMapper is null)
            throw new InvalidOperationException("Bridge not initialized");

        _renderMapper.Map(physics, vertices, colorMode, baseSize, sizeScale);
    }

    /// <summary>
    /// Run the render mapper on existing ComputeSharp buffers.
    /// For direct GPU-to-GPU data flow.
    /// </summary>
    /// <param name="physicsBuffer">ComputeSharp physics buffer</param>
    /// <param name="vertexBuffer">ComputeSharp vertex output buffer</param>
    /// <param name="count">Number of nodes to process</param>
    /// <param name="colorMode">Color mode</param>
    /// <param name="baseSize">Base vertex size</param>
    /// <param name="sizeScale">Size scaling</param>
    public void MapOnGpu(
        ReadOnlyBuffer<PhysicsNodeState> physicsBuffer,
        ReadWriteBuffer<RenderNodeVertex> vertexBuffer,
        int count,
        int colorMode = 0,
        float baseSize = 0.5f,
        float sizeScale = 2f)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ComputeSharpDx12Bridge));

        if (!IsDoublePrecisionSupported)
            throw new InvalidOperationException("GPU double precision not supported");

        GraphicsDevice.GetDefault().For(
            count,
            new RenderMapperShader(physicsBuffer, vertexBuffer, colorMode, baseSize, sizeScale));
    }

    /// <summary>
    /// Wait for compute operations to complete.
    /// Call before using compute results in rendering.
    /// </summary>
    public void WaitForCompute()
    {
        _syncManager?.WaitForComputeComplete();
    }

    /// <summary>
    /// Get the render mapper's capacity.
    /// </summary>
    public int MapperCapacity => _renderMapper?.Capacity ?? 0;

    private void Cleanup()
    {
        _renderMapper?.Dispose();
        _renderMapper = null;

        _syncManager?.Dispose();
        _syncManager = null;

        _deviceContext?.Dispose();
        _deviceContext = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Cleanup();
    }
}
