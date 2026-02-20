using System.Runtime.InteropServices;
using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.BlackHole;

/// <summary>
/// GPU-compatible horizon detection state per node.
/// 
/// PHYSICS: Black hole detection based on:
/// 1. Local mass density (sum of neighbor masses via CSR)
/// 2. Schwarzschild radius comparison: r_s = 2GM (G=c=1)
/// 3. Lapse function freezing (N ? 0 at horizon)
/// 
/// Memory layout: 64 bytes aligned for optimal GPU coalescing.
/// Pack = 8 ensures double-aligned access.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct HorizonStateGpu
{
    /// <summary>
    /// Node mass (computed from local energy/wavefunction).
    /// </summary>
    public readonly double Mass;

    /// <summary>
    /// Effective radius of the node's influence region.
    /// Computed from average edge lengths to neighbors.
    /// </summary>
    public readonly double EffectiveRadius;

    /// <summary>
    /// Schwarzschild radius: r_s = 2*M (in Planck units).
    /// </summary>
    public readonly double SchwarzschildRadius;

    /// <summary>
    /// Mass density: ? = M / r_eff.
    /// High density indicates gravitational collapse.
    /// </summary>
    public readonly double Density;

    /// <summary>
    /// Horizon status flags packed as bits:
    /// Bit 0: IsHorizon (node is at event horizon)
    /// Bit 1: IsSingularity (node is inside horizon, r < r_s)
    /// Bit 2: IsTrapped (node cannot emit light outward)
    /// Bit 3: IsEvaporating (node is losing mass via Hawking radiation)
    /// </summary>
    public readonly int HorizonFlags;

    /// <summary>
    /// Hawking temperature: T_H = 1/(8?M).
    /// Higher for smaller black holes.
    /// </summary>
    public readonly double HawkingTemperature;

    /// <summary>
    /// Bekenstein-Hawking entropy: S = 4?M?.
    /// </summary>
    public readonly double Entropy;

    /// <summary>
    /// Padding for 64-byte alignment.
    /// </summary>
    private readonly double _padding;

    public HorizonStateGpu(
        double mass,
        double effectiveRadius,
        double density,
        int horizonFlags,
        double hawkingTemperature,
        double entropy)
    {
        Mass = mass;
        EffectiveRadius = effectiveRadius;
        SchwarzschildRadius = 2.0 * mass;
        Density = density;
        HorizonFlags = horizonFlags;
        HawkingTemperature = hawkingTemperature;
        Entropy = entropy;
        _padding = 0;
    }

    /// <summary>
    /// Zero/vacuum state - no horizon.
    /// </summary>
    public static HorizonStateGpu Zero => new(0, 0, 0, 0, 0, 0);

    // Flag accessors
    public bool IsHorizon => (HorizonFlags & 1) != 0;
    public bool IsSingularity => (HorizonFlags & 2) != 0;
    public bool IsTrapped => (HorizonFlags & 4) != 0;
    public bool IsEvaporating => (HorizonFlags & 8) != 0;
}

/// <summary>
/// Horizon flag constants for GPU shaders.
/// </summary>
public static class HorizonFlags
{
    public const int None = 0;
    public const int IsHorizon = 1;
    public const int IsSingularity = 2;
    public const int IsTrapped = 4;
    public const int IsEvaporating = 8;
}
