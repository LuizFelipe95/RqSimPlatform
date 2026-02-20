using ComputeSharp;

namespace RQSimulation.GPUOptimized.Rendering;

/// <summary>
/// GPU compute shader for converting double-precision physics state to float-precision render vertices.
/// Runs on ComputeSharp device; output can be shared or copied to DX12 render buffers.
/// 
/// Features:
/// - Position: double -> float (truncation)
/// - Color: computed from quantum phase (HSV rainbow) or mode selector
/// - Size: based on |Psi|^2 (probability density)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct RenderMapperShader : IComputeShader
{
    private readonly ReadOnlyBuffer<PhysicsNodeState> _physics;
    private readonly ReadWriteBuffer<RenderNodeVertex> _vertices;
    private readonly int _colorMode;
    private readonly float _baseSize;
    private readonly float _sizeScale;

    public RenderMapperShader(
        ReadOnlyBuffer<PhysicsNodeState> physics,
        ReadWriteBuffer<RenderNodeVertex> vertices,
        int colorMode,
        float baseSize,
        float sizeScale)
    {
        _physics = physics;
        _vertices = vertices;
        _colorMode = colorMode;
        _baseSize = baseSize;
        _sizeScale = sizeScale;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= _physics.Length)
            return;

        PhysicsNodeState p = _physics[i];

        // Position: double -> float
        float x = (float)p.X;
        float y = (float)p.Y;
        float z = (float)p.Z;

        // Compute |Psi|^2 (probability density)
        float probDensity = (float)(p.PsiReal * p.PsiReal + p.PsiImag * p.PsiImag);

        // Compute phase angle in [-PI, PI]
        float phase = Hlsl.Atan2((float)p.PsiImag, (float)p.PsiReal);

        // HSV -> RGB for phase coloring
        float hue = (phase + 3.14159265f) / (2f * 3.14159265f); // [0,1]
        float4 color = HsvToRgb(hue, 1f, 1f);

        // Apply color mode adjustments
        if (_colorMode == 1)
        {
            // Energy mode: brightness from potential
            float brightness = Hlsl.Saturate((float)p.Potential * 0.5f + 0.5f);
            color = new float4(brightness, brightness, brightness, 1f);
        }
        else if (_colorMode == 2)
        {
            // Mass mode: red = high mass
            float massFactor = Hlsl.Saturate((float)p.Mass / 10f);
            color = new float4(massFactor, 0.2f, 1f - massFactor, 1f);
        }

        // Size: base + probability scaling
        float size = _baseSize + probDensity * _sizeScale;

        RenderNodeVertex v;
        v.X = x;
        v.Y = y;
        v.Z = z;
        v.R = color.X;
        v.G = color.Y;
        v.B = color.Z;
        v.A = color.W;
        v.Size = size;
        _vertices[i] = v;
    }

    private static float4 HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - Hlsl.Abs(Hlsl.Fmod(h * 6f, 2f) - 1f));
        float m = v - c;

        float3 rgb;
        float hue6 = h * 6f;

        if (hue6 < 1f)
            rgb = new float3(c, x, 0f);
        else if (hue6 < 2f)
            rgb = new float3(x, c, 0f);
        else if (hue6 < 3f)
            rgb = new float3(0f, c, x);
        else if (hue6 < 4f)
            rgb = new float3(0f, x, c);
        else if (hue6 < 5f)
            rgb = new float3(x, 0f, c);
        else
            rgb = new float3(c, 0f, x);

        return new float4(rgb.X + m, rgb.Y + m, rgb.Z + m, 1f);
    }
}
