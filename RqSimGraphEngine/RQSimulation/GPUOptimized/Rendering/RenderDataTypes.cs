using System.Runtime.InteropServices;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.Rendering;

/// <summary>
/// GPU-side physics state ready for render mapper.
/// Physics data uses double precision.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsNodeState
{
    public double X;
    public double Y;
    public double Z;
    public double PsiReal;
    public double PsiImag;
    public double Potential;
    public double Mass;
}

/// <summary>
/// GPU-side render vertex output (float precision).
/// Layout matches PointVertex used by Veldrid/DX12 renderers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderNodeVertex
{
    public float X;
    public float Y;
    public float Z;
    public float R;
    public float G;
    public float B;
    public float A;
    public float Size;
}

#region Packed Data Structures (Optimized for GPU)

/// <summary>
/// Optimized node data structure for GPU rendering (32 bytes).
/// Uses float precision for positions with 16-byte alignment for maximum GPU read speed.
/// </summary>
/// <remarks>
/// Memory layout:
/// - Bytes 0-15:  Position (12) + Scale (4) - aligned as float4
/// - Bytes 16-31: ColorEncoded (4) + Energy (4) + Flags (4) + Padding (4)
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct PackedNodeData
{
    /// <summary>Position in world space (xyz).</summary>
    public Float3 Position;

    /// <summary>Node scale/radius.</summary>
    public float Scale;

    /// <summary>RGBA8 encoded color (use ColorEncoding helpers).</summary>
    public uint ColorEncoded;

    /// <summary>Glow/energy intensity for visual effects.</summary>
    public float Energy;

    /// <summary>Bitmask flags (see NodeFlags enum).</summary>
    public uint Flags;

    /// <summary>Padding for 16-byte alignment.</summary>
    public float Padding;

    /// <summary>Size in bytes of this structure.</summary>
    public const int SizeInBytes = 32;
}

/// <summary>
/// Optimized edge data structure for GPU rendering (16 bytes).
/// Uses 16-byte alignment for efficient GPU access.
/// </summary>
/// <remarks>
/// Memory layout:
/// - Bytes 0-7:   NodeIndexA (4) + NodeIndexB (4)
/// - Bytes 8-15:  Weight (4) + Tension (4)
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct PackedEdgeData
{
    /// <summary>Index of the first node.</summary>
    public int NodeIndexA;

    /// <summary>Index of the second node.</summary>
    public int NodeIndexB;

    /// <summary>Metric weight/distance of the edge.</summary>
    public float Weight;

    /// <summary>Tension factor (affects visual thickness/color).</summary>
    public float Tension;

    /// <summary>Size in bytes of this structure.</summary>
    public const int SizeInBytes = 16;
}

/// <summary>
/// High-precision node data structure for GPU rendering (48 bytes).
/// Uses double precision for positions, suitable for large-scale simulations.
/// </summary>
/// <remarks>
/// Use this when float precision is insufficient for world coordinates.
/// Memory layout:
/// - Bytes 0-31:  Position (24) + Scale (8) 
/// - Bytes 32-47: ColorEncoded (4) + Flags (4) + Energy (8)
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct PackedNodeDataDouble
{
    /// <summary>Position in world space (xyz) with double precision.</summary>
    public Double3 Position;

    /// <summary>Node scale/radius with double precision.</summary>
    public double Scale;

    /// <summary>RGBA8 encoded color.</summary>
    public uint ColorEncoded;

    /// <summary>Bitmask flags (see NodeFlags enum).</summary>
    public uint Flags;

    /// <summary>Glow/energy intensity.</summary>
    public double Energy;

    /// <summary>Size in bytes of this structure.</summary>
    public const int SizeInBytes = 48;
}

/// <summary>
/// Node state flags for packed data structures.
/// </summary>
[Flags]
public enum NodeFlags : uint
{
    /// <summary>No special flags.</summary>
    None = 0,

    /// <summary>Node is on event horizon.</summary>
    Horizon = 1 << 0,

    /// <summary>Node is a singularity.</summary>
    Singularity = 1 << 1,

    /// <summary>Node is selected by user.</summary>
    Selected = 1 << 2,

    /// <summary>Node is highlighted.</summary>
    Highlighted = 1 << 3,

    /// <summary>Node is part of a cluster.</summary>
    InCluster = 1 << 4,

    /// <summary>Node has high energy state.</summary>
    HighEnergy = 1 << 5,

    /// <summary>Node is frozen (no dynamics).</summary>
    Frozen = 1 << 6,

    /// <summary>Node is a boundary node.</summary>
    Boundary = 1 << 7,
}

#endregion

#region Color Encoding Utilities

/// <summary>
/// Helper methods for RGBA8 color encoding/decoding.
/// </summary>
public static class ColorEncoding
{
    /// <summary>
    /// Encodes RGBA float values (0-1) into a single uint (RGBA8 format).
    /// </summary>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <param name="a">Alpha component (0-1).</param>
    /// <returns>Packed RGBA8 color.</returns>
    public static uint EncodeRgba8(float r, float g, float b, float a = 1f)
    {
        uint rByte = (uint)Math.Clamp((int)(r * 255f), 0, 255);
        uint gByte = (uint)Math.Clamp((int)(g * 255f), 0, 255);
        uint bByte = (uint)Math.Clamp((int)(b * 255f), 0, 255);
        uint aByte = (uint)Math.Clamp((int)(a * 255f), 0, 255);

        return rByte | (gByte << 8) | (bByte << 16) | (aByte << 24);
    }

    /// <summary>
    /// Encodes RGBA byte values into a single uint.
    /// </summary>
    public static uint EncodeRgba8(byte r, byte g, byte b, byte a = 255)
    {
        return (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
    }

    /// <summary>
    /// Decodes a packed RGBA8 color into float components.
    /// </summary>
    /// <param name="encoded">Packed RGBA8 color.</param>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <param name="a">Alpha component (0-1).</param>
    public static void DecodeRgba8(uint encoded, out float r, out float g, out float b, out float a)
    {
        r = (encoded & 0xFF) / 255f;
        g = ((encoded >> 8) & 0xFF) / 255f;
        b = ((encoded >> 16) & 0xFF) / 255f;
        a = ((encoded >> 24) & 0xFF) / 255f;
    }

    /// <summary>
    /// Decodes a packed RGBA8 color into byte components.
    /// </summary>
    public static void DecodeRgba8(uint encoded, out byte r, out byte g, out byte b, out byte a)
    {
        r = (byte)(encoded & 0xFF);
        g = (byte)((encoded >> 8) & 0xFF);
        b = (byte)((encoded >> 16) & 0xFF);
        a = (byte)((encoded >> 24) & 0xFF);
    }

    /// <summary>
    /// Encodes HSV color to RGBA8.
    /// </summary>
    /// <param name="h">Hue (0-1).</param>
    /// <param name="s">Saturation (0-1).</param>
    /// <param name="v">Value/brightness (0-1).</param>
    /// <param name="a">Alpha (0-1).</param>
    /// <returns>Packed RGBA8 color.</returns>
    public static uint EncodeHsv(float h, float s, float v, float a = 1f)
    {
        HsvToRgb(h, s, v, out float r, out float g, out float b);
        return EncodeRgba8(r, g, b, a);
    }

    /// <summary>
    /// Converts HSV to RGB.
    /// </summary>
    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs(h * 6f % 2f - 1f));
        float m = v - c;

        float hue6 = h * 6f;

        if (hue6 < 1f) { r = c; g = x; b = 0f; }
        else if (hue6 < 2f) { r = x; g = c; b = 0f; }
        else if (hue6 < 3f) { r = 0f; g = c; b = x; }
        else if (hue6 < 4f) { r = 0f; g = x; b = c; }
        else if (hue6 < 5f) { r = x; g = 0f; b = c; }
        else { r = c; g = 0f; b = x; }

        r += m;
        g += m;
        b += m;
    }
}

#endregion

#region Render Precision Mode

/// <summary>
/// Specifies the precision mode for rendering data.
/// </summary>
public enum RenderPrecisionMode
{
    /// <summary>Standard mode: float precision for positions (32 bytes per node).</summary>
    Float,

    /// <summary>High precision mode: double precision for positions (48 bytes per node).</summary>
    Double
}

#endregion
