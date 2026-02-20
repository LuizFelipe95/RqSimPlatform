using System;
using System.Diagnostics;
using System.Threading;
using ComputeSharp;
using RqSimUI.Rendering.Interop;
using RQSimulation.Core.Plugins;
using Vortice.Direct3D12;

namespace RqSimRenderingEngine.Rendering.Interop;

/// <summary>
/// Tracks the current resource state for barrier optimization.
/// </summary>
public enum ResourceAccessState
{
    /// <summary>
    /// Resource state is unknown or uninitialized.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Resource is in compute/write state (UAV - Unordered Access View).
    /// Compute shaders can read/write.
    /// </summary>
    Compute = 1,

    /// <summary>
    /// Resource is in render/read state (SRV - Shader Resource View).
    /// Graphics pipeline can read, no write access.
    /// </summary>
    Render = 2
}

/// <summary>
/// Manages GPU synchronization between ComputeSharp compute operations and DX12 rendering.
/// Implements IGpuSyncManager for integration with PhysicsPipeline.
/// 
/// Synchronization strategies:
/// 1. CPU Blocking (default): Simple but loses ~1-2ms per frame
/// 2. GPU Fence (optimal): Requires fence sharing between queues
/// 
/// Resource barrier management:
/// - TransitionToCompute: Prepares buffers for compute shader write access
/// - TransitionToRender: Prepares buffers for graphics pipeline read access
/// - State tracking prevents redundant barriers (idempotent operations)
/// 
/// Since ComputeSharp doesn't expose its command queue or fences publicly,
/// we use CPU blocking as the safe fallback.
/// </summary>
public sealed class GpuSyncManager : IGpuSyncManager, IDisposable
{
    private readonly UnifiedDeviceContext? _deviceContext;
    private ID3D12Fence? _renderFence;
    private ulong _renderFenceValue;
    private nint _renderFenceEvent;
    private bool _disposed;
    
    /// <summary>
    /// Current resource access state for barrier optimization.
    /// </summary>
    private ResourceAccessState _currentState = ResourceAccessState.Unknown;

    /// <summary>
    /// Synchronization strategy in use.
    /// </summary>
    public GpuSyncStrategy Strategy { get; private set; } = GpuSyncStrategy.CpuBlocking;

    /// <summary>
    /// Whether GPU fence synchronization is available.
    /// </summary>
    public bool HasGpuFence => _renderFence is not null;

    /// <summary>
    /// Current resource access state.
    /// </summary>
    public ResourceAccessState CurrentState => _currentState;

    /// <summary>
    /// Creates a new GPU sync manager.
    /// </summary>
    /// <param name="deviceContext">Unified device context</param>
    public GpuSyncManager(UnifiedDeviceContext? deviceContext)
    {
        _deviceContext = deviceContext;

        if (_deviceContext?.IsInitialized == true)
        {
            InitializeFences();
        }
    }

    private void InitializeFences()
    {
        var device = _deviceContext?.GetSharedDevice();
        if (device is null)
            return;

        try
        {
            // Create a fence for render queue synchronization
            _renderFence = device.CreateFence(0);
            _renderFenceValue = 0;
            _renderFenceEvent = CreateEventHandle();

            if (_renderFence is not null && _renderFenceEvent != 0)
            {
                Strategy = GpuSyncStrategy.GpuFence;
                Debug.WriteLine("[GpuSyncManager] GPU fence synchronization enabled");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuSyncManager] Failed to create fence: {ex.Message}");
            Strategy = GpuSyncStrategy.CpuBlocking;
        }
    }

    /// <summary>
    /// Transition shared buffers from render/read state to compute/write state.
    /// Call before GPU physics modules execute.
    /// 
    /// This is idempotent - if already in Compute state, this is a no-op.
    /// </summary>
    public void TransitionToCompute()
    {
        if (_disposed)
            return;

        // Skip if already in compute state (idempotent)
        if (_currentState == ResourceAccessState.Compute)
        {
            Debug.WriteLine("[GpuSyncManager] TransitionToCompute: Already in compute state, skipping");
            return;
        }

        // Wait for any pending render operations to complete before allowing compute writes
        if (_currentState == ResourceAccessState.Render)
        {
            WaitForRenderComplete();
        }

        // Note: ComputeSharp manages its own resource states internally.
        // This transition is primarily for coordination with DX12 rendering.
        // The actual D3D12 resource barriers would be placed here if we had
        // direct access to the command list and resources.
        //
        // For ComputeSharp interop:
        // - ReadWriteBuffer<T> is always in UAV state for compute
        // - When binding to DX12 vertex/index buffers, we need SRV state
        // - The barrier is implicit when using staging-based data transfer

        _currentState = ResourceAccessState.Compute;
        Debug.WriteLine("[GpuSyncManager] Transitioned to Compute state (UAV)");
    }

    /// <summary>
    /// Transition shared buffers from compute/write state to render/read state.
    /// Call after GPU physics modules complete.
    /// 
    /// This is idempotent - if already in Render state, this is a no-op.
    /// </summary>
    public void TransitionToRender()
    {
        if (_disposed)
            return;

        // Skip if already in render state (idempotent)
        if (_currentState == ResourceAccessState.Render)
        {
            Debug.WriteLine("[GpuSyncManager] TransitionToRender: Already in render state, skipping");
            return;
        }

        // Ensure compute operations are complete before render reads
        if (_currentState == ResourceAccessState.Compute)
        {
            // For ComputeSharp, we need to ensure the dispatch is complete
            // This is done via CPU blocking or fence synchronization
            WaitForComputeComplete();
        }

        _currentState = ResourceAccessState.Render;
        Debug.WriteLine("[GpuSyncManager] Transitioned to Render state (SRV)");
    }

    /// <summary>
    /// Wait for ComputeSharp compute operations to complete before rendering.
    /// Call this after dispatching compute shaders and before using their output.
    /// </summary>
    public void WaitForComputeComplete()
    {
        if (_disposed)
            return;

        switch (Strategy)
        {
            case GpuSyncStrategy.GpuFence:
                WaitWithFence();
                break;

            case GpuSyncStrategy.CpuBlocking:
            default:
                WaitWithCpuBlocking();
                break;
        }
    }

    /// <summary>
    /// Signal that rendering has started using compute output.
    /// Call this before binding compute buffers to the render pipeline.
    /// </summary>
    /// <param name="commandQueue">DX12 command queue for signaling</param>
    public void SignalRenderStart(ID3D12CommandQueue? commandQueue)
    {
        if (_disposed || commandQueue is null || _renderFence is null)
            return;

        try
        {
            _renderFenceValue++;
            commandQueue.Signal(_renderFence, _renderFenceValue);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuSyncManager] Signal failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Wait for render queue to complete before next compute dispatch.
    /// Call this before ComputeSharp writes to buffers that rendering reads.
    /// </summary>
    public void WaitForRenderComplete()
    {
        if (_disposed || _renderFence is null || _renderFenceEvent == 0)
            return;

        try
        {
            if (_renderFence.CompletedValue < _renderFenceValue)
            {
                _renderFence.SetEventOnCompletion(_renderFenceValue, _renderFenceEvent);
                WaitForEvent(_renderFenceEvent);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuSyncManager] WaitForRenderComplete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset resource state tracking. Call when resources are recreated.
    /// </summary>
    public void ResetStateTracking()
    {
        _currentState = ResourceAccessState.Unknown;
    }

    private void WaitWithFence()
    {
        // With proper fence interop, we would:
        // 1. Have ComputeSharp signal a fence after compute dispatch
        // 2. Have DX12 wait on that fence before rendering
        // 
        // Since ComputeSharp doesn't expose its fences, we fall back to CPU blocking
        WaitWithCpuBlocking();
    }

    private void WaitWithCpuBlocking()
    {
        // ComputeSharp operations are generally synchronous from CPU perspective
        // when using CopyFrom/CopyTo, but For() dispatch may be async.
        // 
        // Force synchronization by accessing the device
        _deviceContext?.WaitForCompute();

        // Additional safety: small yield to ensure GPU catches up
        // This is a workaround for lack of proper fence access
        Thread.SpinWait(100);
    }

    private static nint CreateEventHandle()
    {
        return Rendering.Backend.DX12.NativeMethods.CreateEventW(0, bManualReset: false, bInitialState: false, lpName: null);
    }

    private static void WaitForEvent(nint eventHandle)
    {
        if (eventHandle == 0)
            return;

        var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset)
        {
            SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(eventHandle, ownsHandle: false)
        };
        waitHandle.WaitOne();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _renderFence?.Dispose();
        _renderFence = null;

        if (_renderFenceEvent != 0)
        {
            RqSimRenderingEngine.Rendering.Backend.DX12.NativeMethods.CloseHandle(_renderFenceEvent);
            _renderFenceEvent = 0;
        }

        Strategy = GpuSyncStrategy.CpuBlocking;
        _currentState = ResourceAccessState.Unknown;
    }
}

/// <summary>
/// GPU synchronization strategy.
/// </summary>
public enum GpuSyncStrategy
{
    /// <summary>
    /// CPU waits for GPU operations to complete.
    /// Simple but adds latency.
    /// </summary>
    CpuBlocking = 0,

    /// <summary>
    /// GPU fence synchronization between queues.
    /// Optimal performance, requires fence sharing.
    /// </summary>
    GpuFence = 1
}
