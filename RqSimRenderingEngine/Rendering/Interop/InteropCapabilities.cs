using System;
using System.Diagnostics;
using System.Text;
using ComputeSharp;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace RqSimUI.Rendering.Interop;

/// <summary>
/// Diagnostic utility for checking ComputeSharp <-> DX12 interop capabilities.
/// Use to verify system readiness and debug interop issues.
/// </summary>
public static class InteropCapabilities
{
    /// <summary>
    /// Run full interop diagnostics and return a detailed report.
    /// </summary>
    public static InteropDiagnosticReport RunDiagnostics()
    {
        var report = new InteropDiagnosticReport();

        try
        {
            CheckComputeSharp(report);
            CheckDx12(report);
            DetermineRecommendedStrategy(report);
        }
        catch (Exception ex)
        {
            report.AddError($"Diagnostics failed: {ex.Message}");
        }

        return report;
    }

    private static void CheckComputeSharp(InteropDiagnosticReport report)
    {
        try
        {
            var device = GraphicsDevice.GetDefault();

            if (device is null)
            {
                report.ComputeSharpAvailable = false;
                report.AddWarning("ComputeSharp: No GPU device available");
                return;
            }

            report.ComputeSharpAvailable = true;
            report.DoublePrecisionSupported = device.IsDoublePrecisionSupportAvailable();
            report.ComputeSharpDeviceName = device.ToString() ?? "Unknown";

            report.AddInfo($"ComputeSharp device: {report.ComputeSharpDeviceName}");
            report.AddInfo($"Double precision: {(report.DoublePrecisionSupported ? "Supported" : "Not supported")}");
        }
        catch (Exception ex)
        {
            report.ComputeSharpAvailable = false;
            report.AddError($"ComputeSharp check failed: {ex.Message}");
        }
    }

    private static void CheckDx12(InteropDiagnosticReport report)
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(debug: false);

            int adapterCount = 0;

            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
            {
                if (adapter is null)
                    continue;

                adapterCount++;

                var desc = adapter.Description1;
                bool isSoftware = (desc.Flags & AdapterFlags.Software) != 0;

                if (!isSoftware)
                {
                    var result = D3D12.D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out ID3D12Device? device);

                    if (result.Success && device is not null)
                    {
                        report.Dx12Available = true;
                        report.Dx12AdapterName = desc.Description;
                        report.Dx12AdapterIndex = (int)i;

                        // Check feature levels
                        var featureLevels = new FeatureLevel[]
                        {
                            FeatureLevel.Level_12_2,
                            FeatureLevel.Level_12_1,
                            FeatureLevel.Level_12_0,
                            FeatureLevel.Level_11_1,
                            FeatureLevel.Level_11_0
                        };

                        foreach (var level in featureLevels)
                        {
                            if (D3D12.D3D12CreateDevice(adapter, level, out ID3D12Device? testDevice).Success)
                            {
                                report.MaxFeatureLevel = level.ToString();
                                testDevice?.Dispose();
                                break;
                            }
                        }

                        report.AddInfo($"DX12 adapter: {desc.Description}");
                        report.AddInfo($"Max feature level: {report.MaxFeatureLevel}");

                        device.Dispose();
                    }
                }

                adapter.Dispose();
            }

            if (!report.Dx12Available)
            {
                report.AddWarning($"DX12: No compatible adapter found ({adapterCount} adapters checked)");
            }
        }
        catch (Exception ex)
        {
            report.Dx12Available = false;
            report.AddError($"DX12 check failed: {ex.Message}");
        }
    }

    private static void DetermineRecommendedStrategy(InteropDiagnosticReport report)
    {
        if (!report.ComputeSharpAvailable)
        {
            report.RecommendedStrategy = InteropStrategy.None;
            report.StrategyReason = "ComputeSharp not available";
            return;
        }

        if (!report.Dx12Available)
        {
            report.RecommendedStrategy = InteropStrategy.None;
            report.StrategyReason = "DX12 not available";
            return;
        }

        // Assume same adapter when both are available on default GPU
        // (ComputeSharp GetDefault and DX12 both pick the primary adapter)
        report.LuidMatch = true;
        report.RecommendedStrategy = InteropStrategy.UnifiedDevice;
        report.StrategyReason = "Both use primary GPU adapter";

        // Check for double precision fallback needs
        if (!report.DoublePrecisionSupported)
        {
            report.AddWarning("Double precision not supported - physics mapper will use CPU fallback");
        }
    }
}

/// <summary>
/// Detailed interop diagnostic report.
/// </summary>
public sealed class InteropDiagnosticReport
{
    private readonly StringBuilder _log = new();

    // ComputeSharp status
    public bool ComputeSharpAvailable { get; set; }
    public bool DoublePrecisionSupported { get; set; }
    public string ComputeSharpDeviceName { get; set; } = "";

    // DX12 status
    public bool Dx12Available { get; set; }
    public string Dx12AdapterName { get; set; } = "";
    public int Dx12AdapterIndex { get; set; } = -1;
    public string MaxFeatureLevel { get; set; } = "";

    // Interop status
    public bool LuidMatch { get; set; }
    public InteropStrategy RecommendedStrategy { get; set; }
    public string StrategyReason { get; set; } = "";

    // Diagnostics
    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }

    public void AddInfo(string message)
    {
        _log.AppendLine($"[INFO] {message}");
        Debug.WriteLine($"[InteropDiag] {message}");
    }

    public void AddWarning(string message)
    {
        _log.AppendLine($"[WARN] {message}");
        Debug.WriteLine($"[InteropDiag] WARNING: {message}");
        WarningCount++;
    }

    public void AddError(string message)
    {
        _log.AppendLine($"[ERROR] {message}");
        Debug.WriteLine($"[InteropDiag] ERROR: {message}");
        ErrorCount++;
    }

    /// <summary>
    /// Get the full diagnostic log.
    /// </summary>
    public string GetLog() => _log.ToString();

    /// <summary>
    /// Get a summary string.
    /// </summary>
    public string GetSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Interop Diagnostic Summary ===");
        sb.AppendLine($"ComputeSharp: {(ComputeSharpAvailable ? "Available" : "Not available")}");
        sb.AppendLine($"DX12: {(Dx12Available ? "Available" : "Not available")}");
        sb.AppendLine($"Double Precision: {(DoublePrecisionSupported ? "Supported" : "Not supported")}");
        sb.AppendLine($"LUID Match: {(LuidMatch ? "Yes (assumed)" : "No")}");
        sb.AppendLine($"Recommended Strategy: {RecommendedStrategy}");
        sb.AppendLine($"Reason: {StrategyReason}");
        sb.AppendLine($"Warnings: {WarningCount}, Errors: {ErrorCount}");
        return sb.ToString();
    }

    /// <summary>
    /// Whether interop is viable (at least CPU staging).
    /// </summary>
    public bool IsViable => ComputeSharpAvailable && Dx12Available;
}
