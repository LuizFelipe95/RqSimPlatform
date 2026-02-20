using System.Numerics;
using System.Runtime.InteropServices;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct Dx12VertexPositionNormal
{
    public Vector3 Position;
    public Vector3 Normal;

    public Dx12VertexPositionNormal(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
    }

    public static uint SizeInBytes => (uint)(6 * sizeof(float));
}

[StructLayout(LayoutKind.Sequential)]
public struct Dx12NodeInstance
{
    public Vector3 Position;
    public float Radius;
    public Vector4 Color;

    public Dx12NodeInstance(Vector3 position, float radius, Vector4 color)
    {
        Position = position;
        Radius = radius;
        Color = color;
    }

    public static uint SizeInBytes => (uint)(3 * sizeof(float) + sizeof(float) + 4 * sizeof(float));
}

[StructLayout(LayoutKind.Sequential)]
public struct Dx12LineVertex
{
    public Vector3 Position;
    public Vector4 Color;

    public Dx12LineVertex(Vector3 position, Vector4 color)
    {
        Position = position;
        Color = color;
    }

    public static uint SizeInBytes => (uint)(3 * sizeof(float) + 4 * sizeof(float));
}
