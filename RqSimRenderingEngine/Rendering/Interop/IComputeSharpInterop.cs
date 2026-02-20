using System;
using Vortice.Direct3D12;

namespace RqSimUI.Rendering.Interop;

/// <summary>
/// Interface for accessing ComputeSharp device and resources from DX12 render pipeline.
/// Enables zero-copy interop between compute and graphics.
/// </summary>
public interface IComputeSharpInterop : IDisposable
{
    /// <summary>
    /// Whether the interop is initialized and ready.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Whether the device supports double precision (fp64).
    /// </summary>
    bool IsDoublePrecisionSupported { get; }

    /// <summary>
    /// Native pointer to the underlying ID3D12Device.
    /// </summary>
    nint NativeDevicePtr { get; }

    /// <summary>
    /// Get a Vortice wrapper around the shared device.
    /// Do NOT dispose this - the ComputeSharp owns the underlying device.
    /// </summary>
    ID3D12Device? GetSharedDevice();

    /// <summary>
    /// Wait for all pending GPU compute operations to complete.
    /// Call before accessing compute buffers from render queue.
    /// </summary>
    void WaitForCompute();
}

/// <summary>
/// Interface for GPU buffer that can be shared between ComputeSharp and DX12.
/// </summary>
/// <typeparam name="T">Element type (must be unmanaged)</typeparam>
public interface ISharedGpuBuffer<T> : IDisposable where T : unmanaged
{
    /// <summary>
    /// Number of elements in the buffer.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    int SizeInBytes { get; }

    /// <summary>
    /// GPU virtual address for DX12 binding.
    /// </summary>
    ulong GpuVirtualAddress { get; }

    /// <summary>
    /// Native pointer to the underlying ID3D12Resource.
    /// </summary>
    nint NativeResourcePtr { get; }

    /// <summary>
    /// Upload data from CPU to GPU.
    /// </summary>
    void CopyFrom(ReadOnlySpan<T> source);

    /// <summary>
    /// Download data from GPU to CPU.
    /// </summary>
    void CopyTo(Span<T> destination);
}

/// <summary>
/// Describes the intended usage of a shared buffer.
/// </summary>
[Flags]
public enum SharedBufferUsage
{
    /// <summary>
    /// Buffer will be read by compute shaders.
    /// </summary>
    ComputeRead = 1,

    /// <summary>
    /// Buffer will be written by compute shaders (UAV).
    /// </summary>
    ComputeWrite = 2,

    /// <summary>
    /// Buffer will be used as vertex buffer for rendering.
    /// </summary>
    VertexBuffer = 4,

    /// <summary>
    /// Buffer will be used as constant buffer.
    /// </summary>
    ConstantBuffer = 8,

    /// <summary>
    /// Default usage: compute read/write + vertex buffer.
    /// </summary>
    Default = ComputeRead | ComputeWrite | VertexBuffer
}

/// <summary>
/// Result of interop initialization attempt.
/// </summary>
public sealed record InteropInitResult(
    bool Success,
    InteropStrategy Strategy,
    string DiagnosticMessage);

/// <summary>
/// Strategy used for ComputeSharp <-> DX12 interop.
/// </summary>
public enum InteropStrategy
{
    /// <summary>
    /// No interop available; CPU fallback required.
    /// </summary>
    None = 0,

    /// <summary>
    /// Unified device: ComputeSharp and Vortice share the same ID3D12Device.
    /// Zero-copy, best performance.
    /// </summary>
    UnifiedDevice = 1,

    /// <summary>
    /// Shared resource handles between separate devices.
    /// Low overhead but requires explicit synchronization.
    /// </summary>
    SharedHandle = 2,

    /// <summary>
    /// GPU-to-GPU copy between devices.
    /// Moderate overhead, simpler synchronization.
    /// </summary>
    GpuCopy = 3,

    /// <summary>
    /// CPU staging buffer (upload heap).
    /// Highest overhead but most compatible.
    /// </summary>
    CpuStaging = 4
}
