using System.Diagnostics;
using RqSimRenderingEngine.Abstractions;
using RqSimRenderingEngine.Rendering.Backend.DX12;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace RqSimUI.Rendering.Plugins;

/// <summary>
/// Factory for creating render host instances.
/// 
/// Note: Veldrid backend support was removed. Only native DX12 is supported.
/// </summary>
public static class RenderHostFactory
{
    private static bool? _dx12AvailableCache;

    /// <summary>
    /// Result of render host creation attempt.
    /// </summary>
    public sealed record RenderHostResult(
        IRenderHost? Host,
        RenderBackendKind ActualBackend,
        string DiagnosticMessage);

    /// <summary>
    /// Create a render host for the specified backend preference.
    /// </summary>
    /// <param name="preferred">Preferred backend (Auto will try DX12 first)</param>
    /// <returns>Result containing the host and actual backend used</returns>
    public static RenderHostResult Create(RenderBackendKind preferred)
    {
        // Ignore Veldrid preference: legacy backend removed.
        return TryCreateDx12();
    }

    /// <summary>
    /// Check if DX12 backend is available.
    /// Performs actual hardware and API availability check.
    /// </summary>
    public static bool IsDx12Available()
    {
        // Use cached result to avoid repeated expensive checks
        if (_dx12AvailableCache.HasValue)
            return _dx12AvailableCache.Value;

        try
        {
            // Check if D3D12 API is supported at minimum feature level 11_0
            // This validates both OS support and GPU capability
            _dx12AvailableCache = D3D12.IsSupported(FeatureLevel.Level_11_0);
            
            Log($"DX12 availability check: {(_dx12AvailableCache.Value ? "supported" : "not supported")}");
            return _dx12AvailableCache.Value;
        }
        catch (DllNotFoundException ex)
        {
            Log($"DX12 not available: D3D12 DLL not found - {ex.Message}");
            _dx12AvailableCache = false;
            return false;
        }
        catch (TypeLoadException ex)
        {
            Log($"DX12 not available: Type load error - {ex.Message}");
            _dx12AvailableCache = false;
            return false;
        }
        catch (Exception ex)
        {
            Log($"DX12 availability check failed: {ex.GetType().Name} - {ex.Message}");
            _dx12AvailableCache = false;
            return false;
        }
    }

    /// <summary>
    /// Get available render backends.
    /// </summary>
    public static IReadOnlyList<RenderBackendKind> GetAvailableBackends()
    {
        return IsDx12Available()
            ? [RenderBackendKind.Dx12]
            : Array.Empty<RenderBackendKind>();
    }

    /// <summary>
    /// Unload Veldrid plugin context.
    /// Call when shutting down to release resources.
    /// </summary>
    public static void UnloadPlugins()
    {
        // No plugins.
    }

    /// <summary>
    /// Create DX12 render host (direct reference - no plugin isolation needed).
    /// </summary>
    private static RenderHostResult TryCreateDx12()
    {
        try
        {
            Log("Attempting to create DX12 backend...");

            var host = new Dx12RenderHost();
            Log("DX12 backend instance created successfully (direct reference)");

            return new RenderHostResult(host, RenderBackendKind.Dx12, "DX12 host created (not yet initialized)");
        }
        catch (ArgumentException ex)
        {
            Log($"DX12 backend failed (ArgumentException): {ex.Message}");
            return new RenderHostResult(null, RenderBackendKind.Auto, 
                $"DX12 initialization failed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Log($"DX12 backend failed (InvalidOperation): {ex.Message}");
            return new RenderHostResult(null, RenderBackendKind.Auto, 
                $"DX12 initialization failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"DX12 backend failed: {ex.GetType().Name}: {ex.Message}");
            return new RenderHostResult(null, RenderBackendKind.Auto, 
                $"DX12 initialization failed: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [RenderHostFactory] {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);

        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "render_backend.log");
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
            // Ignore file write errors
        }
    }
}
