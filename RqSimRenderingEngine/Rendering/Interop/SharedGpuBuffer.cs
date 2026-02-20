using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ComputeSharp;

namespace RqSimUI.Rendering.Interop;

/// <summary>
/// GPU buffer that can be shared between ComputeSharp compute shaders and DX12 rendering.
/// 
/// Uses ComputeSharp's ReadWriteBuffer internally, and extracts the native D3D12 resource
/// pointer for DX12 vertex buffer binding.
/// 
/// IMPORTANT: 
/// - The buffer is owned by ComputeSharp; do NOT dispose the native resource pointer
/// - Resource state transitions must be handled carefully for correct synchronization
/// </summary>
/// <typeparam name="T">Element type (must be unmanaged)</typeparam>
public sealed class SharedGpuBuffer<T> : ISharedGpuBuffer<T> where T : unmanaged
{
    private ReadWriteBuffer<T>? _computeBuffer;
    private nint _nativeResourcePtr;
    private ulong _gpuVirtualAddress;
    private readonly SharedBufferUsage _usage;
    private bool _disposed;

    /// <inheritdoc/>
    public int Length { get; }

    /// <inheritdoc/>
    public int SizeInBytes => Length * Unsafe.SizeOf<T>();

    /// <inheritdoc/>
    public ulong GpuVirtualAddress => _gpuVirtualAddress;

    /// <inheritdoc/>
    public nint NativeResourcePtr => _nativeResourcePtr;

    /// <summary>
    /// The underlying ComputeSharp buffer.
    /// </summary>
    public ReadWriteBuffer<T>? ComputeBuffer => _computeBuffer;

    /// <summary>
    /// Whether the native resource pointer was successfully extracted.
    /// </summary>
    public bool HasNativeAccess => _nativeResourcePtr != 0;

    /// <summary>
    /// Creates a new shared GPU buffer.
    /// </summary>
    /// <param name="device">ComputeSharp graphics device</param>
    /// <param name="length">Number of elements</param>
    /// <param name="usage">Intended usage flags</param>
    internal SharedGpuBuffer(GraphicsDevice device, int length, SharedBufferUsage usage)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);

        Length = length;
        _usage = usage;

        // Allocate the buffer through ComputeSharp
        _computeBuffer = device.AllocateReadWriteBuffer<T>(length);

        // Try to extract the native resource pointer for DX12 interop
        ExtractNativePointers();
    }

    /// <summary>
    /// Creates a shared buffer wrapping an existing ComputeSharp buffer.
    /// </summary>
    /// <param name="existingBuffer">Existing buffer to wrap</param>
    /// <param name="usage">Intended usage flags</param>
    public SharedGpuBuffer(ReadWriteBuffer<T> existingBuffer, SharedBufferUsage usage)
    {
        ArgumentNullException.ThrowIfNull(existingBuffer);

        _computeBuffer = existingBuffer;
        Length = existingBuffer.Length;
        _usage = usage;

        ExtractNativePointers();
    }

    private void ExtractNativePointers()
    {
        if (_computeBuffer is null)
            return;

        try
        {
            // ComputeSharp 3.x: The buffer internally holds an ID3D12Resource
            // We need to extract it for DX12 vertex buffer binding
            // 
            // Option 1: Use ComputeSharp.Interop APIs (if available)
            // Option 2: Use reflection to access internal allocation
            // Option 3: Use the GraphicsDevice to get resource info

            _nativeResourcePtr = ExtractResourcePointer(_computeBuffer);

            if (_nativeResourcePtr != 0)
            {
                // Get GPU virtual address from the resource
                _gpuVirtualAddress = GetGpuVirtualAddress(_nativeResourcePtr);
                Debug.WriteLine($"[SharedGpuBuffer] Extracted native ptr: 0x{_nativeResourcePtr:X}, GPU VA: 0x{_gpuVirtualAddress:X}");
            }
            else
            {
                Debug.WriteLine("[SharedGpuBuffer] Failed to extract native pointer; CPU staging will be used");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SharedGpuBuffer] ExtractNativePointers failed: {ex.Message}");
            _nativeResourcePtr = 0;
            _gpuVirtualAddress = 0;
        }
    }

    /// <summary>
    /// Extract the ID3D12Resource pointer from a ComputeSharp buffer.
    /// </summary>
    private static nint ExtractResourcePointer(ReadWriteBuffer<T> buffer)
    {
        // ComputeSharp 3.x buffer internals:
        // - The buffer is backed by D3D12MA::Allocation
        // - The allocation contains the ID3D12Resource
        // 
        // Without public API access, we cannot get the raw pointer directly.
        // This would require:
        // 1. ComputeSharp.Interop package with InteropServices.GetD3D12Resource()
        // 2. Reflection into internal fields
        // 3. Modification of ComputeSharp source
        //
        // For now, return 0 to indicate native interop is not available.
        // The system will fall back to CPU staging.

        // Future: When ComputeSharp exposes interop APIs, implement here
        // Example (hypothetical):
        // return ComputeSharp.Interop.InteropServices.GetD3D12Resource(buffer);

        return 0;
    }

    /// <summary>
    /// Get the GPU virtual address from an ID3D12Resource pointer.
    /// </summary>
    private static ulong GetGpuVirtualAddress(nint resourcePtr)
    {
        if (resourcePtr == 0)
            return 0;

        try
        {
            // Wrap the pointer in a Vortice resource to get the GPU VA
            // Note: We don't dispose this wrapper - it doesn't own the resource
            using var resource = new Vortice.Direct3D12.ID3D12Resource(resourcePtr);
            return resource.GPUVirtualAddress;
        }
        catch
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    public void CopyFrom(ReadOnlySpan<T> source)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SharedGpuBuffer<T>));

        if (_computeBuffer is null)
            throw new InvalidOperationException("Buffer not initialized");

        int count = Math.Min(source.Length, Length);
        _computeBuffer.CopyFrom(source[..count]);
    }

    /// <inheritdoc/>
    public void CopyTo(Span<T> destination)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SharedGpuBuffer<T>));

        if (_computeBuffer is null)
            throw new InvalidOperationException("Buffer not initialized");

        int count = Math.Min(destination.Length, Length);
        _computeBuffer.CopyTo(destination[..count]);
    }

    /// <summary>
    /// Get a DX12 vertex buffer view for this buffer.
    /// </summary>
    /// <returns>Vertex buffer view, or default if native access unavailable</returns>
    public Vortice.Direct3D12.VertexBufferView GetVertexBufferView()
    {
        if (!HasNativeAccess)
            return default;

        return new Vortice.Direct3D12.VertexBufferView
        {
            BufferLocation = _gpuVirtualAddress,
            SizeInBytes = (uint)SizeInBytes,
            StrideInBytes = (uint)Unsafe.SizeOf<T>()
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose the ComputeSharp buffer (this releases the GPU resource)
        _computeBuffer?.Dispose();
        _computeBuffer = null;

        _nativeResourcePtr = 0;
        _gpuVirtualAddress = 0;
    }
}
