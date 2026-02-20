using Vortice.Direct3D12;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// Provides optimized depth-stencil configurations for DX12 rendering.
/// Uses Reverse-Z buffering for improved depth precision at large scales.
/// </summary>
/// <remarks>
/// Reverse-Z maps far plane to 0.0 and near plane to 1.0, which provides
/// better floating-point precision distribution across the depth range.
/// This is critical for simulations with large scale differences (e.g., cosmological to Planck scales).
/// </remarks>
internal static class Dx12DepthStencilConfigs
{
    // Default stencil operation - keep everything, compare always
    private static readonly DepthStencilOperationDescription DefaultStencilOp = new()
    {
        StencilFailOp = StencilOperation.Keep,
        StencilDepthFailOp = StencilOperation.Keep,
        StencilPassOp = StencilOperation.Keep,
        StencilFunc = ComparisonFunction.Always
    };

    /// <summary>
    /// Reverse-Z depth stencil for opaque geometry.
    /// Uses Greater comparison (far=0, near=1).
    /// </summary>
    public static DepthStencilDescription ReverseZOpaque { get; } = new()
    {
        DepthEnable = true,
        DepthWriteMask = DepthWriteMask.All,
        DepthFunc = ComparisonFunction.Greater,  // Reverse-Z: Greater passes if closer
        StencilEnable = false,
        StencilReadMask = 0xFF,
        StencilWriteMask = 0xFF,
        FrontFace = DefaultStencilOp,
        BackFace = DefaultStencilOp
    };

    /// <summary>
    /// Reverse-Z depth stencil for transparent/line geometry.
    /// Uses Greater comparison with depth read but no write (for proper blending).
    /// </summary>
    public static DepthStencilDescription ReverseZReadOnly { get; } = new()
    {
        DepthEnable = true,
        DepthWriteMask = DepthWriteMask.Zero,  // Read only, don't write
        DepthFunc = ComparisonFunction.Greater,
        StencilEnable = false,
        StencilReadMask = 0xFF,
        StencilWriteMask = 0xFF,
        FrontFace = DefaultStencilOp,
        BackFace = DefaultStencilOp
    };

    /// <summary>
    /// Standard depth stencil (Less comparison) for compatibility.
    /// Use when Reverse-Z is not desired.
    /// </summary>
    public static DepthStencilDescription StandardOpaque => DepthStencilDescription.Default;
}
