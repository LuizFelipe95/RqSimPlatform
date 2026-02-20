using System;
using System.Collections.Generic;
using System.Numerics;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

internal static class SphereMesh
{
    internal static (Dx12VertexPositionNormal[] Vertices, ushort[] Indices) Create(int latitudeSegments = 16, int longitudeSegments = 32)
    {
        latitudeSegments = Math.Clamp(latitudeSegments, 3, 128);
        longitudeSegments = Math.Clamp(longitudeSegments, 3, 256);

        List<Dx12VertexPositionNormal> vertices = new((latitudeSegments + 1) * (longitudeSegments + 1));
        List<ushort> indices = new(latitudeSegments * longitudeSegments * 6);

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = (float)lat / latitudeSegments;
            float phi = v * MathF.PI;

            float y = MathF.Cos(phi);
            float r = MathF.Sin(phi);

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = (float)lon / longitudeSegments;
                float theta = u * MathF.Tau;

                float x = r * MathF.Cos(theta);
                float z = r * MathF.Sin(theta);

                var pos = new Vector3(x, y, z);
                vertices.Add(new Dx12VertexPositionNormal(pos, Vector3.Normalize(pos)));
            }
        }

        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int i0 = (lat * stride) + lon;
                int i1 = i0 + 1;
                int i2 = i0 + stride;
                int i3 = i2 + 1;

                if (i3 > ushort.MaxValue)
                    throw new InvalidOperationException("Sphere mesh exceeded 16-bit index range.");

                // 2 triangles per quad
                indices.Add((ushort)i0);
                indices.Add((ushort)i2);
                indices.Add((ushort)i1);

                indices.Add((ushort)i1);
                indices.Add((ushort)i2);
                indices.Add((ushort)i3);
            }
        }

        return (vertices.ToArray(), indices.ToArray());
    }
}
