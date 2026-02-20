using ComputeSharp;

namespace RQSimulation.GPUOptimized.Rendering;

/// <summary>
/// GPU compute shader for converting PhysicsNodeState to PackedNodeData.
/// Optimized for GPU memory bandwidth with 32-byte output structures.
/// </summary>
/// <remarks>
/// Features:
/// - Position: double -> float (truncation)
/// - Color: RGBA8 encoded from quantum phase or mode selector
/// - Flags: computed from physics state
/// - Energy: normalized from potential
/// </remarks>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PackedNodeMapperShader : IComputeShader
{
    private readonly ReadOnlyBuffer<PhysicsNodeState> _physics;
    private readonly ReadWriteBuffer<PackedNodeData> _packed;
    private readonly int _colorMode;
    private readonly float _baseScale;
    private readonly float _scaleMultiplier;
    private readonly float _horizonThreshold;
    private readonly float _singularityThreshold;

    /// <summary>
    /// Creates a new PackedNodeMapperShader.
    /// </summary>
    /// <param name="physics">Source physics data buffer.</param>
    /// <param name="packed">Destination packed data buffer.</param>
    /// <param name="colorMode">Color mode: 0=Phase, 1=Energy, 2=Mass.</param>
    /// <param name="baseScale">Base node scale.</param>
    /// <param name="scaleMultiplier">Scale multiplier for probability density.</param>
    /// <param name="horizonThreshold">Potential threshold for horizon detection.</param>
    /// <param name="singularityThreshold">Mass threshold for singularity detection.</param>
    public PackedNodeMapperShader(
        ReadOnlyBuffer<PhysicsNodeState> physics,
        ReadWriteBuffer<PackedNodeData> packed,
        int colorMode,
        float baseScale,
        float scaleMultiplier,
        float horizonThreshold,
        float singularityThreshold)
    {
        _physics = physics;
        _packed = packed;
        _colorMode = colorMode;
        _baseScale = baseScale;
        _scaleMultiplier = scaleMultiplier;
        _horizonThreshold = horizonThreshold;
        _singularityThreshold = singularityThreshold;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= _physics.Length)
            return;

        PhysicsNodeState p = _physics[i];

        // Position: double -> float
        Float3 position = new((float)p.X, (float)p.Y, (float)p.Z);

        // Compute |Psi|^2 (probability density)
        float probDensity = (float)(p.PsiReal * p.PsiReal + p.PsiImag * p.PsiImag);

        // Scale: base + probability scaling
        float scale = _baseScale + probDensity * _scaleMultiplier;

        // Compute phase angle in [-PI, PI]
        float phase = Hlsl.Atan2((float)p.PsiImag, (float)p.PsiReal);

        // Compute color based on mode
        uint colorEncoded = ComputeColor(phase, (float)p.Potential, (float)p.Mass);

        // Compute flags
        uint flags = ComputeFlags((float)p.Potential, (float)p.Mass, probDensity);

        // Energy: normalized from potential
        float energy = Hlsl.Saturate((float)p.Potential * 0.1f + 0.5f);

        PackedNodeData packed;
        packed.Position = position;
        packed.Scale = scale;
        packed.ColorEncoded = colorEncoded;
        packed.Energy = energy;
        packed.Flags = flags;
        packed.Padding = 0f;

        _packed[i] = packed;
    }

    private uint ComputeColor(float phase, float potential, float mass)
    {
        float r, g, b;

        if (_colorMode == 1)
        {
            // Energy mode: grayscale from potential
            float brightness = Hlsl.Saturate(potential * 0.5f + 0.5f);
            r = g = b = brightness;
        }
        else if (_colorMode == 2)
        {
            // Mass mode: blue to red gradient
            float massFactor = Hlsl.Saturate(mass / 10f);
            r = massFactor;
            g = 0.2f;
            b = 1f - massFactor;
        }
        else
        {
            // Phase mode: HSV rainbow
            float hue = (phase + 3.14159265f) / (2f * 3.14159265f); // [0,1]
            HsvToRgb(hue, 1f, 1f, out r, out g, out b);
        }

        // Encode RGBA8
        uint rByte = (uint)Hlsl.Clamp((int)(r * 255f), 0, 255);
        uint gByte = (uint)Hlsl.Clamp((int)(g * 255f), 0, 255);
        uint bByte = (uint)Hlsl.Clamp((int)(b * 255f), 0, 255);
        uint aByte = 255u;

        return rByte | (gByte << 8) | (bByte << 16) | (aByte << 24);
    }

    private uint ComputeFlags(float potential, float mass, float probDensity)
    {
        uint flags = 0u;

        // Horizon detection (high gravitational potential)
        if (potential > _horizonThreshold)
            flags |= 1u; // NodeFlags.Horizon

        // Singularity detection (extreme mass concentration)
        if (mass > _singularityThreshold)
            flags |= 2u; // NodeFlags.Singularity

        // High energy state
        if (probDensity > 1f)
            flags |= 32u; // NodeFlags.HighEnergy

        return flags;
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float c = v * s;
        float x = c * (1f - Hlsl.Abs(Hlsl.Fmod(h * 6f, 2f) - 1f));
        float m = v - c;

        float hue6 = h * 6f;

        if (hue6 < 1f)
        {
            r = c + m;
            g = x + m;
            b = m;
        }
        else if (hue6 < 2f)
        {
            r = x + m;
            g = c + m;
            b = m;
        }
        else if (hue6 < 3f)
        {
            r = m;
            g = c + m;
            b = x + m;
        }
        else if (hue6 < 4f)
        {
            r = m;
            g = x + m;
            b = c + m;
        }
        else if (hue6 < 5f)
        {
            r = x + m;
            g = m;
            b = c + m;
        }
        else
        {
            r = c + m;
            g = m;
            b = x + m;
        }
    }
}

/// <summary>
/// GPU compute shader for converting PhysicsNodeState to PackedNodeDataDouble.
/// Preserves double precision for positions in large-scale simulations.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PackedNodeMapperDoubleShader : IComputeShader
{
    private readonly ReadOnlyBuffer<PhysicsNodeState> _physics;
    private readonly ReadWriteBuffer<PackedNodeDataDouble> _packed;
    private readonly int _colorMode;
    private readonly double _baseScale;
    private readonly double _scaleMultiplier;
    private readonly double _horizonThreshold;
    private readonly double _singularityThreshold;

    public PackedNodeMapperDoubleShader(
        ReadOnlyBuffer<PhysicsNodeState> physics,
        ReadWriteBuffer<PackedNodeDataDouble> packed,
        int colorMode,
        double baseScale,
        double scaleMultiplier,
        double horizonThreshold,
        double singularityThreshold)
    {
        _physics = physics;
        _packed = packed;
        _colorMode = colorMode;
        _baseScale = baseScale;
        _scaleMultiplier = scaleMultiplier;
        _horizonThreshold = horizonThreshold;
        _singularityThreshold = singularityThreshold;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= _physics.Length)
            return;

        PhysicsNodeState p = _physics[i];

        // Position: preserved as double
        Double3 position = new(p.X, p.Y, p.Z);

        // Compute |Psi|^2 (probability density)
        double probDensity = p.PsiReal * p.PsiReal + p.PsiImag * p.PsiImag;

        // Scale: base + probability scaling
        double scale = _baseScale + probDensity * _scaleMultiplier;

        // Compute phase angle
        float phase = Hlsl.Atan2((float)p.PsiImag, (float)p.PsiReal);

        // Compute color based on mode (float precision sufficient for color)
        uint colorEncoded = ComputeColor(phase, (float)p.Potential, (float)p.Mass);

        // Compute flags
        uint flags = ComputeFlags(p.Potential, p.Mass, probDensity);

        // Energy: normalized from potential
        double energy = Hlsl.Saturate((float)(p.Potential * 0.1 + 0.5));

        PackedNodeDataDouble packed;
        packed.Position = position;
        packed.Scale = scale;
        packed.ColorEncoded = colorEncoded;
        packed.Flags = flags;
        packed.Energy = energy;

        _packed[i] = packed;
    }

    private uint ComputeColor(float phase, float potential, float mass)
    {
        float r, g, b;

        if (_colorMode == 1)
        {
            float brightness = Hlsl.Saturate(potential * 0.5f + 0.5f);
            r = g = b = brightness;
        }
        else if (_colorMode == 2)
        {
            float massFactor = Hlsl.Saturate(mass / 10f);
            r = massFactor;
            g = 0.2f;
            b = 1f - massFactor;
        }
        else
        {
            float hue = (phase + 3.14159265f) / (2f * 3.14159265f);
            HsvToRgb(hue, 1f, 1f, out r, out g, out b);
        }

        uint rByte = (uint)Hlsl.Clamp((int)(r * 255f), 0, 255);
        uint gByte = (uint)Hlsl.Clamp((int)(g * 255f), 0, 255);
        uint bByte = (uint)Hlsl.Clamp((int)(b * 255f), 0, 255);
        uint aByte = 255u;

        return rByte | (gByte << 8) | (bByte << 16) | (aByte << 24);
    }

    private uint ComputeFlags(double potential, double mass, double probDensity)
    {
        uint flags = 0u;

        if (potential > _horizonThreshold)
            flags |= 1u;

        if (mass > _singularityThreshold)
            flags |= 2u;

        if (probDensity > 1.0)
            flags |= 32u;

        return flags;
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float c = v * s;
        float x = c * (1f - Hlsl.Abs(Hlsl.Fmod(h * 6f, 2f) - 1f));
        float m = v - c;

        float hue6 = h * 6f;

        if (hue6 < 1f)
        {
            r = c + m; g = x + m; b = m;
        }
        else if (hue6 < 2f)
        {
            r = x + m; g = c + m; b = m;
        }
        else if (hue6 < 3f)
        {
            r = m; g = c + m; b = x + m;
        }
        else if (hue6 < 4f)
        {
            r = m; g = x + m; b = c + m;
        }
        else if (hue6 < 5f)
        {
            r = x + m; g = m; b = c + m;
        }
        else
        {
            r = c + m; g = m; b = x + m;
        }
    }
}
