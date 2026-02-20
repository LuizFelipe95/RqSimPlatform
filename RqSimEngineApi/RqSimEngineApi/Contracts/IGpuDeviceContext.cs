using System.Runtime.InteropServices;

namespace RqSimEngineApi.Contracts;

/// <summary>
/// Interface for GPU device context injection into plugins.
/// Plugins should NOT create their own GPU devices - they receive
/// a shared context via this interface.
/// 
/// This abstraction allows plugins to work with the shared GPU
/// device without depending on specific rendering engine implementations.
/// </summary>
public interface IGpuDeviceContext
{
    /// <summary>
    /// Whether the context is initialized and ready for use.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Whether the device supports double precision (fp64).
    /// </summary>
    bool IsDoublePrecisionSupported { get; }

    /// <summary>
    /// Native pointer to the underlying graphics device.
    /// For DX12: ID3D12Device pointer.
    /// </summary>
    nint NativeDevicePtr { get; }

    /// <summary>
    /// Wait for all pending GPU compute operations to complete.
    /// </summary>
    void WaitForCompute();
}

/// <summary>
/// Interface for GPU buffer that can be shared between compute and render.
/// </summary>
/// <typeparam name="T">Element type (must be unmanaged)</typeparam>
public interface IGpuBuffer<T> : IDisposable where T : unmanaged
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
    /// Upload data from CPU to GPU.
    /// </summary>
    void CopyFrom(ReadOnlySpan<T> source);

    /// <summary>
    /// Download data from GPU to CPU.
    /// </summary>
    void CopyTo(Span<T> destination);
}

/// <summary>
/// Describes intended usage of a shared buffer.
/// </summary>
[Flags]
public enum GpuBufferUsage
{
    /// <summary>
    /// Default usage (compute read/write).
    /// </summary>
    Default = 0,

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
    ConstantBuffer = 8
}

/// <summary>
/// Factory for creating shared GPU buffers.
/// </summary>
public interface IGpuBufferFactory
{
    /// <summary>
    /// Creates a shared GPU buffer.
    /// </summary>
    /// <typeparam name="T">Element type</typeparam>
    /// <param name="length">Number of elements</param>
    /// <param name="usage">Intended usage</param>
    IGpuBuffer<T> CreateBuffer<T>(int length, GpuBufferUsage usage = GpuBufferUsage.Default) where T : unmanaged;
}
