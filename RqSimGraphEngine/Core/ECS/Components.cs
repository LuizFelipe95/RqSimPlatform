using System.Numerics;
using System.Runtime.InteropServices;

namespace RQSimulation.Core.ECS;

/// <summary>
/// ECS Components for high-performance graph simulation rendering.
/// All structs are unmanaged (blittable) for zero-copy GPU transfer.
/// 
/// DESIGN PRINCIPLES:
/// - Each component is a single responsibility (SoA - Structure of Arrays)
/// - All fields are value types for cache-friendly iteration
/// - Components can be transferred directly to GPU buffers via Span<T>
/// </summary>

// ============================================================================
// RENDERING COMPONENTS
// ============================================================================

/// <summary>
/// 3D position for visualization.
/// Separated from physics coordinates (which may be relational/abstract).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderPosition
{
    public Vector3 Value;

    public RenderPosition(float x, float y, float z) => Value = new Vector3(x, y, z);
    public RenderPosition(Vector3 v) => Value = v;

    public static RenderPosition Zero => new(Vector3.Zero);
}

/// <summary>
/// RGBA color for node/edge rendering.
/// Values in [0,1] range for GPU compatibility.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderColor
{
    public Vector4 Value;

    public RenderColor(float r, float g, float b, float a = 1f) => Value = new Vector4(r, g, b, a);
    public RenderColor(Vector4 v) => Value = v;

    public static RenderColor White => new(1, 1, 1, 1);
    public static RenderColor Red => new(1, 0, 0, 1);
    public static RenderColor Green => new(0, 1, 0, 1);
    public static RenderColor Blue => new(0, 0, 1, 1);
    public static RenderColor Yellow => new(1, 1, 0, 1);
    public static RenderColor Cyan => new(0, 1, 1, 1);
    public static RenderColor Magenta => new(1, 0, 1, 1);
    public static RenderColor Black => new(0, 0, 0, 1);
}

/// <summary>
/// Visual size/scale for rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderScale
{
    public float Value;

    public RenderScale(float scale) => Value = scale;

    public static RenderScale Default => new(1f);
}

/// <summary>
/// Visibility flag for conditional rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderVisible
{
    public bool IsVisible;

    public RenderVisible(bool visible) => IsVisible = visible;

    public static RenderVisible Visible => new(true);
    public static RenderVisible Hidden => new(false);
}

// ============================================================================
// PHYSICS COMPONENTS
// ============================================================================

/// <summary>
/// Node mass for physics simulation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeMass
{
    public double Value;

    public NodeMass(double mass) => Value = mass;
}

/// <summary>
/// Quantum phase (complex wavefunction amplitude).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct QuantumPhase
{
    public double Real;
    public double Imaginary;

    public QuantumPhase(double real, double imag)
    {
        Real = real;
        Imaginary = imag;
    }

    public QuantumPhase(Complex c)
    {
        Real = c.Real;
        Imaginary = c.Imaginary;
    }

    public double MagnitudeSquared => Real * Real + Imaginary * Imaginary;
    public double Magnitude => System.Math.Sqrt(MagnitudeSquared);
}

/// <summary>
/// Node state (rest/excited/refractory) for event-driven model.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeStateComponent
{
    public int State; // 0=Rest, 1=Excited, 2=Refractory

    public NodeStateComponent(int state) => State = state;

    public bool IsExcited => State == 1;
    public bool IsRest => State == 0;
}

/// <summary>
/// Node energy for RQ-Hypothesis energy conservation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeEnergy
{
    public double Value;

    public NodeEnergy(double energy) => Value = energy;
}

/// <summary>
/// Local potential V(x) at node.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LocalPotential
{
    public double Value;

    public LocalPotential(double potential) => Value = potential;
}

// ============================================================================
// GRAPH STRUCTURE COMPONENTS
// ============================================================================

/// <summary>
/// Node identifier for cross-referencing with RQGraph.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeId
{
    public int Value;

    public NodeId(int id) => Value = id;
}

/// <summary>
/// Edge connection (source -> target).
/// Used when edges are modeled as entities.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EdgeConnection
{
    public int SourceEntityId;
    public int TargetEntityId;

    public EdgeConnection(int source, int target)
    {
        SourceEntityId = source;
        TargetEntityId = target;
    }
}

/// <summary>
/// Edge weight for graph connectivity.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EdgeWeight
{
    public double Value;

    public EdgeWeight(double weight) => Value = weight;
}

/// <summary>
/// Node degree (number of connections).
/// Cached for performance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeDegree
{
    public int Value;

    public NodeDegree(int degree) => Value = degree;
}

// ============================================================================
// CLUSTER/GROUPING COMPONENTS (Tags)
// ============================================================================

/// <summary>
/// Tag component for heavy cluster membership.
/// Entities with this component are part of a heavy cluster.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HeavyClusterTag
{
    public int ClusterId;

    public HeavyClusterTag(int clusterId) => ClusterId = clusterId;
}

/// <summary>
/// Tag component for clock nodes (proper time reference).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ClockNodeTag
{
    public double ProperTime;

    public ClockNodeTag(double time) => ProperTime = time;
}

// ============================================================================
// GPU VERTEX STRUCTURES (Combined for rendering)
// ============================================================================

/// <summary>
/// Combined vertex data for GPU point rendering.
/// Matches Veldrid vertex buffer layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PointVertex
{
    public Vector3 Position;
    public Vector4 Color;
    public float Size;

    public PointVertex(Vector3 pos, Vector4 color, float size)
    {
        Position = pos;
        Color = color;
        Size = size;
    }

    public static uint SizeInBytes => (uint)(3 * 4 + 4 * 4 + 4); // 32 bytes
}

/// <summary>
/// Combined vertex data for GPU line rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LineVertex
{
    public Vector3 Position;
    public Vector4 Color;

    public LineVertex(Vector3 pos, Vector4 color)
    {
        Position = pos;
        Color = color;
    }

    public static uint SizeInBytes => (uint)(3 * 4 + 4 * 4); // 28 bytes
}

// ============================================================================
// CSR PHYSICS-TO-GRAPHICS INTEGRATION COMPONENTS
// ============================================================================

/// <summary>
/// Visual data for GPU rendering, computed from physics state.
/// Blittable for zero-copy GPU transfer via Veldrid.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeVisualData
{
    /// <summary>
    /// 3D position for rendering (spectral or force-directed layout).
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// Visual size based on |Psi|^2 (probability amplitude).
    /// </summary>
    public float Size;

    /// <summary>
    /// Quantum phase angle from Arg(Psi) in radians [-PI, PI].
    /// Used for HSV color mapping in shaders.
    /// </summary>
    public float Phase;

    /// <summary>
    /// Gravitational/local potential at this node.
    /// Used for brightness modulation.
    /// </summary>
    public float Potential;

    /// <summary>
    /// Color mode selector: 0 = Phase, 1 = Energy, 2 = Curvature.
    /// </summary>
    public uint ColorMode;

    /// <summary>
    /// Padding for 16-byte alignment (GPU-friendly).
    /// </summary>
    private float _padding;

    public NodeVisualData(Vector3 position, float size = 1f, float phase = 0f, float potential = 0f, uint colorMode = 0)
    {
        Position = position;
        Size = size;
        Phase = phase;
        Potential = potential;
        ColorMode = colorMode;
        _padding = 0f;
    }

    public static uint SizeInBytes => 32; // 3*4 + 4 + 4 + 4 + 4 + 4 = 32 bytes
}

/// <summary>
/// Tag component for filtering active nodes in visualization.
/// Entities with this tag are included in render passes.
/// </summary>
public struct ActiveNodeTag { }

/// <summary>
/// Links ECS entity to physics index in CSR arrays.
/// Enables efficient lookup during GPU readback synchronization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsIndex
{
    /// <summary>
    /// Index into CSR wavefunction/potential arrays.
    /// </summary>
    public int Value;

    public PhysicsIndex(int index) => Value = index;
}

/// <summary>
/// Curvature data for geometric visualization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NodeCurvature
{
    /// <summary>
    /// Ricci scalar curvature at this node.
    /// Positive = spherical, Negative = hyperbolic.
    /// </summary>
    public float RicciScalar;

    public NodeCurvature(float ricci) => RicciScalar = ricci;
}

/// <summary>
/// Energy flow visualization data.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EnergyFlowData
{
    /// <summary>
    /// Direction of net energy flow (normalized).
    /// </summary>
    public Vector3 FlowDirection;

    /// <summary>
    /// Magnitude of energy flow for arrow scaling.
    /// </summary>
    public float FlowMagnitude;

    public EnergyFlowData(Vector3 direction, float magnitude)
    {
        FlowDirection = direction;
        FlowMagnitude = magnitude;
    }
}
