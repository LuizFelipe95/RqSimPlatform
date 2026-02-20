using System;
using System.Diagnostics;
using ComputeSharp;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace RqSimUI.Rendering.Interop;

/// <summary>
/// Provides unified device context for ComputeSharp and DX12 interop.
/// 
/// Strategy: Both ComputeSharp and Vortice create devices on the same default adapter.
/// This enables staging-based data sharing between compute and graphics.
/// 
/// Note: True zero-copy unified device would require ComputeSharp interop APIs
/// to expose the underlying ID3D12Device pointer. Current approach uses
/// separate device instances on the same adapter with CPU staging.
/// </summary>
public sealed class UnifiedDeviceContext : IComputeSharpInterop
{
    private GraphicsDevice? _computeDevice;
    private ID3D12Device? _vorticeDevice;
    private IDXGIAdapter1? _adapter;
    private nint _nativeDevicePtr;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsInitialized => _computeDevice is not null && _vorticeDevice is not null;

    /// <inheritdoc/>
    public bool IsDoublePrecisionSupported { get; private set; }

    /// <inheritdoc/>
    public nint NativeDevicePtr => _nativeDevicePtr;

    /// <summary>
    /// The ComputeSharp graphics device.
    /// </summary>
    public GraphicsDevice? ComputeDevice => _computeDevice;

    /// <summary>
    /// The interop strategy being used.
    /// </summary>
    public InteropStrategy Strategy { get; private set; } = InteropStrategy.None;

    /// <summary>
    /// Initialize the unified device context.
    /// </summary>
    /// <returns>Result indicating success/failure and diagnostics</returns>
    public InteropInitResult Initialize()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnifiedDeviceContext));

        if (IsInitialized)
            return new InteropInitResult(true, Strategy, "Already initialized");

        try
        {
            return InitializeDevices();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UnifiedDeviceContext] Initialization failed: {ex.Message}");
            return new InteropInitResult(false, InteropStrategy.None, $"Failed: {ex.Message}");
        }
    }

    private InteropInitResult InitializeDevices()
    {
        // Step 1: Initialize ComputeSharp device
        _computeDevice = GraphicsDevice.GetDefault();

        if (_computeDevice is null)
        {
            return new InteropInitResult(false, InteropStrategy.None, "ComputeSharp: No GPU device available");
        }

        // Check double precision support
        IsDoublePrecisionSupported = _computeDevice.IsDoublePrecisionSupportAvailable();

        // Step 2: Create Vortice device on the default (first hardware) adapter
        // This should be the same adapter ComputeSharp is using
        var (device, adapter) = CreateDefaultDx12Device();

        if (device is null)
        {
            return new InteropInitResult(
                false,
                InteropStrategy.CpuStaging,
                "Cannot create DX12 device; falling back to CPU staging");
        }

        _vorticeDevice = device;
        _adapter = adapter;
        _nativeDevicePtr = device.NativePointer;

        // We're using separate device instances on same adapter
        // Data transfer will use CPU staging (CopyFrom/CopyTo)
        Strategy = InteropStrategy.CpuStaging;

        string adapterName = GetAdapterDescription();
        Debug.WriteLine($"[UnifiedDeviceContext] Initialized: {adapterName}, DoublePrecision={IsDoublePrecisionSupported}");

        return new InteropInitResult(
            true,
            InteropStrategy.CpuStaging,
            $"Same-adapter devices: {adapterName}");
    }

    /// <inheritdoc/>
    public ID3D12Device? GetSharedDevice()
    {
        if (!IsInitialized)
            return null;

        return _vorticeDevice;
    }

    /// <inheritdoc/>
    public void WaitForCompute()
    {
        // ComputeSharp CopyFrom/CopyTo operations are synchronous from CPU perspective
        // No additional wait needed for staging approach
    }

    /// <summary>
    /// Create a shared buffer that can be accessed by both ComputeSharp and DX12.
    /// </summary>
    /// <typeparam name="T">Element type</typeparam>
    /// <param name="length">Number of elements</param>
    /// <param name="usage">Intended buffer usage</param>
    /// <returns>Shared buffer wrapper</returns>
    public SharedGpuBuffer<T> CreateSharedBuffer<T>(int length, SharedBufferUsage usage = SharedBufferUsage.Default)
        where T : unmanaged
    {
        if (!IsInitialized || _computeDevice is null)
            throw new InvalidOperationException("UnifiedDeviceContext not initialized");

        return new SharedGpuBuffer<T>(_computeDevice, length, usage);
    }

    private string GetAdapterDescription()
    {
        try
        {
            if (_adapter is not null)
            {
                return _adapter.Description1.Description;
            }

            if (_computeDevice is not null)
            {
                return _computeDevice.ToString() ?? "Unknown GPU";
            }
        }
        catch
        {
            // Ignore
        }

        return "Unknown GPU";
    }

    /// <summary>
    /// Create a D3D12 device on the default hardware adapter.
    /// </summary>
    private static (ID3D12Device? Device, IDXGIAdapter1? Adapter) CreateDefaultDx12Device()
    {
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(debug: false);

        // Find the first hardware adapter
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            if (adapter is null)
                continue;

            var desc = adapter.Description1;
            bool isSoftware = (desc.Flags & AdapterFlags.Software) != 0;

            if (isSoftware)
            {
                adapter.Dispose();
                continue;
            }

            var createResult = D3D12.D3D12CreateDevice(
                adapter,
                FeatureLevel.Level_11_0,
                out ID3D12Device? device);

            if (createResult.Success && device is not null)
            {
                Debug.WriteLine($"[UnifiedDeviceContext] Created DX12 device on adapter: {desc.Description}");
                return (device, adapter);
            }

            adapter.Dispose();
        }

        Debug.WriteLine("[UnifiedDeviceContext] No suitable DX12 adapter found");
        return (null, null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vorticeDevice?.Dispose();
        _vorticeDevice = null;

        _adapter?.Dispose();
        _adapter = null;

        _nativeDevicePtr = 0;

        // ComputeSharp's GraphicsDevice is a singleton-like object
        // Don't dispose it - let ComputeSharp manage its lifecycle
        _computeDevice = null;

        Strategy = InteropStrategy.None;
    }
}
