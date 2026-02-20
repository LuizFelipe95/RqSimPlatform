using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ComputeSharp;
using RqSimRenderingEngine.Rendering.Interop;
using RQSimulation.GPUOptimized.Rendering;
using Vortice.Direct3D12;

namespace RqSimUI.Rendering.Interop;

/// <summary>
/// Adapts ComputeSharp render data to DX12 vertex buffers.
/// 
/// This class bridges the gap between:
/// - ComputeSharp's RenderMapperShader output (ReadWriteBuffer&lt;RenderNodeVertex&gt;)
/// - DX12's vertex buffer binding (VertexBufferView)
/// 
/// When native interop is available: Zero-copy binding
/// When not available: CPU staging buffer with upload
/// </summary>
public sealed class Dx12VertexBufferAdapter : IDisposable
{
    private readonly UnifiedDeviceContext _deviceContext;
    private readonly GpuSyncManager _syncManager;

    // ComputeSharp side
    private ReadWriteBuffer<RenderNodeVertex>? _computeBuffer;

    // DX12 staging (fallback when native interop unavailable)
    private ID3D12Resource? _stagingBuffer;
    private ID3D12Resource? _gpuBuffer;
    private nint _stagingMappedPtr;

    // Cached view
    private VertexBufferView _vertexBufferView;

    private int _capacity;
    private bool _useNativeInterop;
    private bool _disposed;

    /// <summary>
    /// Current buffer capacity (max vertices).
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Whether native zero-copy interop is being used.
    /// </summary>
    public bool IsNativeInterop => _useNativeInterop;

    /// <summary>
    /// The current vertex buffer view for DX12 rendering.
    /// </summary>
    public VertexBufferView VertexBufferView => _vertexBufferView;

    /// <summary>
    /// The underlying ComputeSharp buffer for compute shader access.
    /// </summary>
    public ReadWriteBuffer<RenderNodeVertex>? ComputeBuffer => _computeBuffer;

    /// <summary>
    /// Creates a new vertex buffer adapter.
    /// </summary>
    /// <param name="deviceContext">Unified device context</param>
    /// <param name="syncManager">GPU sync manager</param>
    public Dx12VertexBufferAdapter(UnifiedDeviceContext deviceContext, GpuSyncManager syncManager)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        ArgumentNullException.ThrowIfNull(syncManager);

        _deviceContext = deviceContext;
        _syncManager = syncManager;
    }

    /// <summary>
    /// Initialize or resize the buffer.
    /// </summary>
    /// <param name="capacity">Maximum number of vertices</param>
    public void Initialize(int capacity)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Dx12VertexBufferAdapter));

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        if (_capacity == capacity && _computeBuffer is not null)
            return;

        Cleanup();

        _capacity = capacity;
        int sizeInBytes = capacity * Unsafe.SizeOf<RenderNodeVertex>();

        // Step 1: Allocate ComputeSharp buffer
        var computeDevice = _deviceContext.ComputeDevice;
        if (computeDevice is not null)
        {
            try
            {
                _computeBuffer = computeDevice.AllocateReadWriteBuffer<RenderNodeVertex>(capacity);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dx12VertexBufferAdapter] ComputeSharp buffer allocation failed: {ex.Message}");
            }
        }

        // Step 2: Try native interop first
        _useNativeInterop = TrySetupNativeInterop(sizeInBytes);

        // Step 3: Fall back to staging if native unavailable
        if (!_useNativeInterop)
        {
            SetupStagingBuffers(sizeInBytes);
        }

        Debug.WriteLine($"[Dx12VertexBufferAdapter] Initialized: capacity={capacity}, native={_useNativeInterop}");
    }

    private bool TrySetupNativeInterop(int sizeInBytes)
    {
        if (_computeBuffer is null)
            return false;

        // Try to extract the native resource pointer from ComputeSharp buffer
        // This requires ComputeSharp.Interop or reflection

        // For now, native interop is not available - would need ComputeSharp changes
        // Return false to use staging path
        return false;
    }

    private void SetupStagingBuffers(int sizeInBytes)
    {
        var device = _deviceContext.GetSharedDevice();
        if (device is null)
        {
            Debug.WriteLine("[Dx12VertexBufferAdapter] No DX12 device available");
            return;
        }

        try
        {
            // Create upload heap buffer (CPU-writable)
            var uploadDesc = ResourceDescription.Buffer((ulong)sizeInBytes);
            _stagingBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                uploadDesc,
                ResourceStates.GenericRead);

            // Create GPU buffer (for vertex binding)
            var gpuDesc = ResourceDescription.Buffer((ulong)sizeInBytes);
            _gpuBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                gpuDesc,
                ResourceStates.VertexAndConstantBuffer);

            // Map staging buffer for CPU writes
            unsafe
            {
                void* mappedData;
                _stagingBuffer.Map(0, null, &mappedData);
                _stagingMappedPtr = (nint)mappedData;
            }

            // Create vertex buffer view pointing to GPU buffer
            _vertexBufferView = new VertexBufferView
            {
                BufferLocation = _gpuBuffer.GPUVirtualAddress,
                SizeInBytes = (uint)sizeInBytes,
                StrideInBytes = (uint)Unsafe.SizeOf<RenderNodeVertex>()
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Dx12VertexBufferAdapter] Staging buffer setup failed: {ex.Message}");
            CleanupStagingBuffers();
        }
    }

    /// <summary>
    /// Upload vertex data to the GPU.
    /// Call after ComputeSharp has finished writing to the compute buffer.
    /// </summary>
    /// <param name="commandList">DX12 command list for copy commands</param>
    /// <param name="vertexCount">Number of vertices to upload</param>
    public void Upload(ID3D12GraphicsCommandList commandList, int vertexCount)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Dx12VertexBufferAdapter));

        ArgumentNullException.ThrowIfNull(commandList);

        if (_useNativeInterop)
        {
            // Native interop: No upload needed, just sync
            _syncManager.WaitForComputeComplete();
            return;
        }

        // Staging path: Copy from ComputeSharp to DX12
        UploadViaStagingBuffer(commandList, vertexCount);
    }

    private void UploadViaStagingBuffer(ID3D12GraphicsCommandList commandList, int vertexCount)
    {
        if (_computeBuffer is null || _stagingBuffer is null || _gpuBuffer is null)
            return;

        int count = Math.Min(vertexCount, _capacity);
        int sizeInBytes = count * Unsafe.SizeOf<RenderNodeVertex>();

        // Wait for compute to complete
        _syncManager.WaitForComputeComplete();

        // Step 1: Copy from ComputeSharp buffer to CPU staging
        // This requires a GPU->CPU readback, which is expensive
        unsafe
        {
            var stagingSpan = new Span<RenderNodeVertex>((void*)_stagingMappedPtr, count);
            _computeBuffer.CopyTo(stagingSpan);
        }

        // Step 2: Copy from staging buffer to GPU buffer via command list
        TransitionResource(commandList, _gpuBuffer, ResourceStates.VertexAndConstantBuffer, ResourceStates.CopyDest);
        commandList.CopyBufferRegion(_gpuBuffer, 0, _stagingBuffer, 0, (ulong)sizeInBytes);
        TransitionResource(commandList, _gpuBuffer, ResourceStates.CopyDest, ResourceStates.VertexAndConstantBuffer);
    }

    /// <summary>
    /// Upload data directly from CPU span (bypasses ComputeSharp).
    /// Use when compute buffer is not available or for direct CPU updates.
    /// </summary>
    /// <param name="commandList">DX12 command list</param>
    /// <param name="vertices">Vertex data to upload</param>
    public void UploadDirect(ID3D12GraphicsCommandList commandList, ReadOnlySpan<RenderNodeVertex> vertices)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Dx12VertexBufferAdapter));

        if (_stagingBuffer is null || _gpuBuffer is null || _stagingMappedPtr == 0)
        {
            Debug.WriteLine("[Dx12VertexBufferAdapter] Cannot upload: staging not initialized");
            return;
        }

        int count = Math.Min(vertices.Length, _capacity);
        int sizeInBytes = count * Unsafe.SizeOf<RenderNodeVertex>();

        // Copy to staging buffer
        unsafe
        {
            var dest = new Span<RenderNodeVertex>((void*)_stagingMappedPtr, count);
            vertices[..count].CopyTo(dest);
        }

        // Copy to GPU buffer
        TransitionResource(commandList, _gpuBuffer, ResourceStates.VertexAndConstantBuffer, ResourceStates.CopyDest);
        commandList.CopyBufferRegion(_gpuBuffer, 0, _stagingBuffer, 0, (ulong)sizeInBytes);
        TransitionResource(commandList, _gpuBuffer, ResourceStates.CopyDest, ResourceStates.VertexAndConstantBuffer);
    }

    private static void TransitionResource(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource resource,
        ResourceStates before,
        ResourceStates after)
    {
        if (before == after)
            return;

        var barrier = new ResourceBarrier(new ResourceTransitionBarrier(resource, before, after));
        commandList.ResourceBarrier(barrier);
    }

    private void Cleanup()
    {
        _computeBuffer?.Dispose();
        _computeBuffer = null;

        CleanupStagingBuffers();

        _vertexBufferView = default;
        _useNativeInterop = false;
    }

    private void CleanupStagingBuffers()
    {
        if (_stagingBuffer is not null && _stagingMappedPtr != 0)
        {
            _stagingBuffer.Unmap(0);
            _stagingMappedPtr = 0;
        }

        _stagingBuffer?.Dispose();
        _stagingBuffer = null;

        _gpuBuffer?.Dispose();
        _gpuBuffer = null;
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
