using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.Rendering;

/// <summary>
/// Manages GPU-accelerated physics->render data conversion.
/// Provides both GPU and CPU fallback paths with support for packed data structures.
/// </summary>
public sealed class RenderDataMapper : IDisposable
{
    private ReadOnlyBuffer<PhysicsNodeState>? _physicsBuffer;
    private ReadWriteBuffer<RenderNodeVertex>? _vertexBuffer;
    private ReadWriteBuffer<PackedNodeData>? _packedFloatBuffer;
    private ReadWriteBuffer<PackedNodeDataDouble>? _packedDoubleBuffer;
    private int _capacity;
    private bool _disposed;

    /// <summary>
    /// Whether GPU double precision is supported.
    /// </summary>
    public bool IsDoublePrecisionSupported { get; private set; }

    /// <summary>
    /// Current capacity (max nodes).
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Current precision mode for packed data.
    /// </summary>
    public RenderPrecisionMode PrecisionMode { get; set; } = RenderPrecisionMode.Float;

    /// <summary>
    /// Initialize GPU buffers.
    /// </summary>
    /// <param name="maxNodes">Maximum number of nodes to process</param>
    public void Initialize(int maxNodes)
    {
        Dispose();
        _disposed = false;

        _capacity = maxNodes;

        try
        {
            // Check device capabilities
            IsDoublePrecisionSupported = GraphicsDevice.GetDefault().IsDoublePrecisionSupportAvailable();

            _physicsBuffer = GraphicsDevice.GetDefault().AllocateReadOnlyBuffer<PhysicsNodeState>(maxNodes);
            _vertexBuffer = GraphicsDevice.GetDefault().AllocateReadWriteBuffer<RenderNodeVertex>(maxNodes);
            _packedFloatBuffer = GraphicsDevice.GetDefault().AllocateReadWriteBuffer<PackedNodeData>(maxNodes);
            
            if (IsDoublePrecisionSupported)
            {
                _packedDoubleBuffer = GraphicsDevice.GetDefault().AllocateReadWriteBuffer<PackedNodeDataDouble>(maxNodes);
            }
        }
        catch (Exception)
        {
            IsDoublePrecisionSupported = false;
            _physicsBuffer = null;
            _vertexBuffer = null;
            _packedFloatBuffer = null;
            _packedDoubleBuffer = null;
        }
    }

    /// <summary>
    /// Map physics data to render vertices on GPU.
    /// Falls back to CPU if GPU unavailable.
    /// </summary>
    /// <param name="physics">Source physics data (CPU memory)</param>
    /// <param name="vertices">Destination vertex data (CPU memory)</param>
    /// <param name="colorMode">0=Phase, 1=Energy, 2=Mass</param>
    /// <param name="baseSize">Base vertex size</param>
    /// <param name="sizeScale">Size scaling factor for probability</param>
    public void Map(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<RenderNodeVertex> vertices,
        int colorMode = 0,
        float baseSize = 0.5f,
        float sizeScale = 2f)
    {
        int count = Math.Min(physics.Length, vertices.Length);
        count = Math.Min(count, _capacity);

        if (_physicsBuffer is not null && _vertexBuffer is not null && IsDoublePrecisionSupported)
        {
            MapOnGpu(physics[..count], vertices[..count], colorMode, baseSize, sizeScale);
        }
        else
        {
            MapOnCpu(physics[..count], vertices[..count], colorMode, baseSize, sizeScale);
        }
    }

    #region Packed Data Mapping (New API)

    /// <summary>
    /// Configuration for packed data mapping.
    /// </summary>
    public record struct PackedMappingOptions
    {
        /// <summary>Color mode: 0=Phase, 1=Energy, 2=Mass.</summary>
        public int ColorMode { get; init; }

        /// <summary>Base node scale.</summary>
        public float BaseScale { get; init; }

        /// <summary>Scale multiplier for probability density.</summary>
        public float ScaleMultiplier { get; init; }

        /// <summary>Potential threshold for horizon detection.</summary>
        public float HorizonThreshold { get; init; }

        /// <summary>Mass threshold for singularity detection.</summary>
        public float SingularityThreshold { get; init; }

        /// <summary>Default options.</summary>
        public static PackedMappingOptions Default => new()
        {
            ColorMode = 0,
            BaseScale = 0.5f,
            ScaleMultiplier = 2f,
            HorizonThreshold = 100f,
            SingularityThreshold = 1000f
        };
    }

    /// <summary>
    /// Map physics data to packed node data (float precision).
    /// Optimized for GPU memory bandwidth.
    /// </summary>
    /// <param name="physics">Source physics data (CPU memory).</param>
    /// <param name="packed">Destination packed data (CPU memory).</param>
    /// <param name="options">Mapping options.</param>
    public void MapToPacked(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<PackedNodeData> packed,
        PackedMappingOptions options = default)
    {
        if (options == default)
            options = PackedMappingOptions.Default;

        int count = Math.Min(physics.Length, packed.Length);
        count = Math.Min(count, _capacity);

        if (_physicsBuffer is not null && _packedFloatBuffer is not null && IsDoublePrecisionSupported)
        {
            MapToPackedOnGpu(physics[..count], packed[..count], options);
        }
        else
        {
            MapToPackedOnCpu(physics[..count], packed[..count], options);
        }
    }

    /// <summary>
    /// Map physics data to packed node data (double precision).
    /// Preserves position precision for large-scale simulations.
    /// </summary>
    /// <param name="physics">Source physics data (CPU memory).</param>
    /// <param name="packed">Destination packed data (CPU memory).</param>
    /// <param name="options">Mapping options.</param>
    public void MapToPackedDouble(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<PackedNodeDataDouble> packed,
        PackedMappingOptions options = default)
    {
        if (options == default)
            options = PackedMappingOptions.Default;

        int count = Math.Min(physics.Length, packed.Length);
        count = Math.Min(count, _capacity);

        if (_physicsBuffer is not null && _packedDoubleBuffer is not null && IsDoublePrecisionSupported)
        {
            MapToPackedDoubleOnGpu(physics[..count], packed[..count], options);
        }
        else
        {
            MapToPackedDoubleOnCpu(physics[..count], packed[..count], options);
        }
    }

    private void MapToPackedOnGpu(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<PackedNodeData> packed,
        PackedMappingOptions options)
    {
        int count = physics.Length;

        _physicsBuffer!.CopyFrom(physics);

        GraphicsDevice.GetDefault().For(
            count,
            new PackedNodeMapperShader(
                _physicsBuffer,
                _packedFloatBuffer!,
                options.ColorMode,
                options.BaseScale,
                options.ScaleMultiplier,
                options.HorizonThreshold,
                options.SingularityThreshold));

        _packedFloatBuffer!.CopyTo(packed);
    }

    private void MapToPackedDoubleOnGpu(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<PackedNodeDataDouble> packed,
        PackedMappingOptions options)
    {
        int count = physics.Length;

        _physicsBuffer!.CopyFrom(physics);

        GraphicsDevice.GetDefault().For(
            count,
            new PackedNodeMapperDoubleShader(
                _physicsBuffer,
                _packedDoubleBuffer!,
                options.ColorMode,
                options.BaseScale,
                options.ScaleMultiplier,
                options.HorizonThreshold,
                options.SingularityThreshold));

        _packedDoubleBuffer!.CopyTo(packed);
    }

    private static void MapToPackedOnCpu(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<PackedNodeData> packed,
        PackedMappingOptions options)
    {
        for (int i = 0; i < physics.Length; i++)
        {
            ref readonly PhysicsNodeState p = ref physics[i];

            Float3 position = new((float)p.X, (float)p.Y, (float)p.Z);

            float probDensity = (float)(p.PsiReal * p.PsiReal + p.PsiImag * p.PsiImag);
            float scale = options.BaseScale + probDensity * options.ScaleMultiplier;

            float phase = MathF.Atan2((float)p.PsiImag, (float)p.PsiReal);

            uint colorEncoded = ComputeColorCpu(phase, (float)p.Potential, (float)p.Mass, options.ColorMode);
            uint flags = ComputeFlagsCpu((float)p.Potential, (float)p.Mass, probDensity, options);
            float energy = Math.Clamp((float)p.Potential * 0.1f + 0.5f, 0f, 1f);

            packed[i] = new PackedNodeData
            {
                Position = position,
                Scale = scale,
                ColorEncoded = colorEncoded,
                Energy = energy,
                Flags = flags,
                Padding = 0f
            };
        }
    }

    private static void MapToPackedDoubleOnCpu(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<PackedNodeDataDouble> packed,
        PackedMappingOptions options)
    {
        for (int i = 0; i < physics.Length; i++)
        {
            ref readonly PhysicsNodeState p = ref physics[i];

            Double3 position = new(p.X, p.Y, p.Z);

            double probDensity = p.PsiReal * p.PsiReal + p.PsiImag * p.PsiImag;
            double scale = options.BaseScale + probDensity * options.ScaleMultiplier;

            float phase = MathF.Atan2((float)p.PsiImag, (float)p.PsiReal);

            uint colorEncoded = ComputeColorCpu(phase, (float)p.Potential, (float)p.Mass, options.ColorMode);
            uint flags = ComputeFlagsCpu((float)p.Potential, (float)p.Mass, (float)probDensity, options);
            double energy = Math.Clamp(p.Potential * 0.1 + 0.5, 0.0, 1.0);

            packed[i] = new PackedNodeDataDouble
            {
                Position = position,
                Scale = scale,
                ColorEncoded = colorEncoded,
                Flags = flags,
                Energy = energy
            };
        }
    }

    private static uint ComputeColorCpu(float phase, float potential, float mass, int colorMode)
    {
        float r, g, b;

        if (colorMode == 1)
        {
            float brightness = Math.Clamp(potential * 0.5f + 0.5f, 0f, 1f);
            r = g = b = brightness;
        }
        else if (colorMode == 2)
        {
            float massFactor = Math.Clamp(mass / 10f, 0f, 1f);
            r = massFactor;
            g = 0.2f;
            b = 1f - massFactor;
        }
        else
        {
            float hue = (phase + MathF.PI) / (2f * MathF.PI);
            HsvToRgb(hue, 1f, 1f, out r, out g, out b);
        }

        return ColorEncoding.EncodeRgba8(r, g, b, 1f);
    }

    private static uint ComputeFlagsCpu(float potential, float mass, float probDensity, PackedMappingOptions options)
    {
        uint flags = 0u;

        if (potential > options.HorizonThreshold)
            flags |= (uint)NodeFlags.Horizon;

        if (mass > options.SingularityThreshold)
            flags |= (uint)NodeFlags.Singularity;

        if (probDensity > 1f)
            flags |= (uint)NodeFlags.HighEnergy;

        return flags;
    }

    #endregion

    private void MapOnGpu(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<RenderNodeVertex> vertices,
        int colorMode,
        float baseSize,
        float sizeScale)
    {
        int count = physics.Length;

        _physicsBuffer!.CopyFrom(physics);

        GraphicsDevice.GetDefault().For(
            count,
            new RenderMapperShader(_physicsBuffer, _vertexBuffer!, colorMode, baseSize, sizeScale));

        _vertexBuffer!.CopyTo(vertices);
    }

    private static void MapOnCpu(
        ReadOnlySpan<PhysicsNodeState> physics,
        Span<RenderNodeVertex> vertices,
        int colorMode,
        float baseSize,
        float sizeScale)
    {
        for (int i = 0; i < physics.Length; i++)
        {
            ref readonly PhysicsNodeState p = ref physics[i];

            float x = (float)p.X;
            float y = (float)p.Y;
            float z = (float)p.Z;

            float probDensity = (float)(p.PsiReal * p.PsiReal + p.PsiImag * p.PsiImag);
            float phase = MathF.Atan2((float)p.PsiImag, (float)p.PsiReal);

            float hue = (phase + MathF.PI) / (2f * MathF.PI);
            HsvToRgb(hue, 1f, 1f, out float r, out float g, out float b);

            if (colorMode == 1)
            {
                float brightness = Math.Clamp((float)p.Potential * 0.5f + 0.5f, 0f, 1f);
                r = g = b = brightness;
            }
            else if (colorMode == 2)
            {
                float massFactor = Math.Clamp((float)p.Mass / 10f, 0f, 1f);
                r = massFactor;
                g = 0.2f;
                b = 1f - massFactor;
            }

            float size = baseSize + probDensity * sizeScale;

            vertices[i] = new RenderNodeVertex
            {
                X = x,
                Y = y,
                Z = z,
                R = r,
                G = g,
                B = b,
                A = 1f,
                Size = size
            };
        }
    }

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _physicsBuffer?.Dispose();
        _physicsBuffer = null;

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;

        _packedFloatBuffer?.Dispose();
        _packedFloatBuffer = null;

        _packedDoubleBuffer?.Dispose();
        _packedDoubleBuffer = null;
    }
}
