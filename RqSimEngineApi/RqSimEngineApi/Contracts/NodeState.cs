using System.Numerics;
using System.Runtime.InteropServices;

namespace RqSimEngineApi.Contracts;

/// <summary>
/// GPU-compatible node state for physics simulation - API version.
/// 
/// CRITICAL: This struct is designed for GPU interop.
/// - Pack=16 ensures HLSL-compatible alignment (float4 boundary)
/// - All fields are blittable (no managed references)
/// - Field order matches HLSL struct definition
/// 
/// Memory layout (64 bytes total, 16-byte aligned):
/// Offset 0:  Position (12 bytes) + _pad1 (4 bytes) = 16 bytes
/// Offset 16: Velocity (12 bytes) + _pad2 (4 bytes) = 16 bytes  
/// Offset 32: Mass (4 bytes) + Charge (4 bytes) + Potential (4 bytes) + Degree (4 bytes) = 16 bytes
/// Offset 48: WaveFunctionReal (4 bytes) + WaveFunctionImag (4 bytes) + _reserved (8 bytes) = 16 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct ApiNodeState
{
    /// <summary>
    /// Position in 3D space.
    /// </summary>
    public Vector3 Position;
    
    private readonly float _pad1;

    /// <summary>
    /// Velocity vector.
    /// </summary>
    public Vector3 Velocity;
    
    private readonly float _pad2;

    /// <summary>
    /// Node mass (correlation mass from edge weights).
    /// Computed as m_i = sqrt(sum_j w_ij^2).
    /// </summary>
    public float Mass;

    /// <summary>
    /// Electric or color charge for gauge coupling.
    /// </summary>
    public float Charge;

    /// <summary>
    /// Local potential V(x) at this node.
    /// </summary>
    public float Potential;

    /// <summary>
    /// Node degree (number of neighbors).
    /// Used for Laplacian normalization.
    /// </summary>
    public int Degree;

    /// <summary>
    /// Real part of complex wavefunction psi.
    /// </summary>
    public float WaveFunctionReal;

    /// <summary>
    /// Imaginary part of complex wavefunction psi.
    /// </summary>
    public float WaveFunctionImag;

    /// <summary>
    /// Reserved for future use (alignment padding).
    /// </summary>
    private readonly double _reserved;

    /// <summary>
    /// Gets the squared magnitude |psi|^2.
    /// </summary>
    public readonly float ProbabilityDensity => WaveFunctionReal * WaveFunctionReal + WaveFunctionImag * WaveFunctionImag;

    /// <summary>
    /// Creates a new node state with specified position and mass.
    /// </summary>
    public ApiNodeState(Vector3 position, float mass)
    {
        Position = position;
        Velocity = Vector3.Zero;
        Mass = mass;
        Charge = 0;
        Potential = 0;
        Degree = 0;
        WaveFunctionReal = 1;
        WaveFunctionImag = 0;
        _pad1 = 0;
        _pad2 = 0;
        _reserved = 0;
    }

    /// <summary>
    /// Zero-initialized node state.
    /// </summary>
    public static ApiNodeState Zero => default;
}

/// <summary>
/// GPU-compatible edge state for physics simulation - API version.
/// 
/// Memory layout (32 bytes, 16-byte aligned):
/// Offset 0:  Weight (8 bytes) + GaugePhase (8 bytes) = 16 bytes
/// Offset 16: SourceNode (4 bytes) + TargetNode (4 bytes) + Exists (4 bytes) + _pad (4 bytes) = 16 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct ApiEdgeState
{
    /// <summary>
    /// Edge weight (coupling strength between nodes).
    /// </summary>
    public double Weight;

    /// <summary>
    /// Gauge phase for U(1) gauge fields.
    /// For SU(N), this would be replaced with matrix elements.
    /// </summary>
    public double GaugePhase;

    /// <summary>
    /// Source node index in CSR format.
    /// </summary>
    public int SourceNode;

    /// <summary>
    /// Target node index (column index in CSR).
    /// </summary>
    public int TargetNode;

    /// <summary>
    /// Whether the edge exists (1) or is virtual (0).
    /// </summary>
    public int Exists;

    private readonly int _pad;

    /// <summary>
    /// Creates an edge between two nodes.
    /// </summary>
    public ApiEdgeState(int source, int target, double weight)
    {
        SourceNode = source;
        TargetNode = target;
        Weight = weight;
        GaugePhase = 0;
        Exists = 1;
        _pad = 0;
    }

    /// <summary>
    /// Zero-initialized edge state.
    /// </summary>
    public static ApiEdgeState Zero => default;
}
