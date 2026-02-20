using System;
using System.Runtime.InteropServices;
using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Data;

/// <summary>
/// GPU-compatible node state for quantum simulation.
/// Contains wavefunction and potential per node.
/// 
/// Memory layout is optimized for coalesced GPU access.
/// All fields are blittable for direct GPU buffer transfer.
/// 
/// CRITICAL: Pack = 16 ensures HLSL-compatible alignment (float4 boundary).
/// Double2 (16 bytes) aligns to 16-byte boundary.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public readonly struct NodeStateGpu
{
    /// <summary>
    /// Complex wavefunction value at this node.
    /// Stored as Double2(Real, Imaginary).
    /// </summary>
    public readonly Double2 WaveFunction;

    /// <summary>
    /// Local potential V(x) at this node.
    /// </summary>
    public readonly double Potential;

    /// <summary>
    /// Padding to ensure 8-byte alignment for GPU access.
    /// </summary>
    private readonly double _padding;

    public NodeStateGpu(Double2 waveFunction, double potential)
    {
        WaveFunction = waveFunction;
        Potential = potential;
        _padding = 0;
    }

    public NodeStateGpu(double psiReal, double psiImag, double potential)
    {
        WaveFunction = new Double2(psiReal, psiImag);
        Potential = potential;
        _padding = 0;
    }

    public static NodeStateGpu Zero => new(Double2.Zero, 0);
}

/// <summary>
/// RQG-HYPOTHESIS: Extended node state with relational time fields.
/// 
/// Implements emergent time via Lapse function (N) where time flow
/// is determined by local Hamiltonian constraint violation.
/// 
/// Physics:
/// - N_i = 1 / (1 + ?|H_i|) where H_i is constraint violation
/// - Time stops (N ? 0) at singularities where H ? ?
/// - Time flows normally (N ? 1) in flat space where H ? 0
/// 
/// Memory layout: 64 bytes aligned for optimal GPU coalescing.
/// CRITICAL: Pack = 16 ensures HLSL-compatible alignment.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public readonly struct NodeStateRqgGpu
{
    /// <summary>
    /// Complex wavefunction value at this node.
    /// Stored as Double2(Real, Imaginary).
    /// </summary>
    public readonly Double2 WaveFunction;

    /// <summary>
    /// Local potential V(x) at this node.
    /// </summary>
    public readonly double Potential;

    /// <summary>
    /// Local Lapse function N_i ? (0, 1].
    /// Controls local proper time flow: d?_i = N_i · d?
    /// 
    /// RQG-HYPOTHESIS: Computed from Hamiltonian constraint:
    /// N_i = 1 / (1 + ?|H_i|)
    /// </summary>
    public readonly double Lapse;

    /// <summary>
    /// Current Hamiltonian constraint violation H_i.
    /// 
    /// In Wheeler-DeWitt formalism: H|?? = 0
    /// Physical states satisfy H_i ? 0.
    /// 
    /// H_i = (K?_ij - K? + R_ij) + T_00
    /// where K is extrinsic curvature, R is Ricci scalar, T_00 is matter energy.
    /// </summary>
    public readonly double HamiltonianVal;

    /// <summary>
    /// Complex phase for quantum edge amplitude.
    /// Stored as Double2(Real, Imaginary) for exp(i?).
    /// 
    /// RQG-HYPOTHESIS: All randomness comes from initial superposition,
    /// evolution is strictly unitary via phase rotation.
    /// </summary>
    public readonly Double2 Phase;

    /// <summary>
    /// Creates a new RQG node state with all fields.
    /// </summary>
    public NodeStateRqgGpu(
        Double2 waveFunction,
        double potential,
        double lapse,
        double hamiltonianVal,
        Double2 phase)
    {
        WaveFunction = waveFunction;
        Potential = potential;
        Lapse = lapse;
        HamiltonianVal = hamiltonianVal;
        Phase = phase;
    }

    /// <summary>
    /// Creates an RQG node state from basic parameters.
    /// Lapse and Hamiltonian are set to physical defaults (flat space).
    /// </summary>
    public NodeStateRqgGpu(double psiReal, double psiImag, double potential)
    {
        WaveFunction = new Double2(psiReal, psiImag);
        Potential = potential;
        Lapse = 1.0;           // Flat space: normal time flow
        HamiltonianVal = 0.0;  // Constraint satisfied
        Phase = new Double2(1.0, 0.0);  // Real positive phase
    }

    /// <summary>
    /// Creates an RQG node state with specified Lapse and Hamiltonian.
    /// </summary>
    public NodeStateRqgGpu(
        double psiReal, double psiImag,
        double potential,
        double lapse,
        double hamiltonianVal)
    {
        WaveFunction = new Double2(psiReal, psiImag);
        Potential = potential;
        Lapse = lapse;
        HamiltonianVal = hamiltonianVal;
        Phase = new Double2(1.0, 0.0);
    }

    /// <summary>
    /// Zero state: vacuum with normal time flow.
    /// </summary>
    public static NodeStateRqgGpu Zero => new(Double2.Zero, 0.0, 1.0, 0.0, new Double2(1.0, 0.0));

    /// <summary>
    /// Creates a state at a singularity (frozen time).
    /// Lapse ? 0, Hamiltonian ? large value.
    /// </summary>
    public static NodeStateRqgGpu Singularity(Double2 waveFunction, double potential, double hamiltonianVal)
    {
        double lapse = 1.0 / (1.0 + System.Math.Abs(hamiltonianVal));
        return new NodeStateRqgGpu(waveFunction, potential, lapse, hamiltonianVal, new Double2(1.0, 0.0));
    }

    /// <summary>
    /// Computes Lapse from Hamiltonian using RQG formula: N = 1 / (1 + ?|H|)
    /// </summary>
    /// <param name="hamiltonianVal">Hamiltonian constraint violation</param>
    /// <param name="alpha">Regularization constant (default 1.0 in Planck units)</param>
    /// <returns>Lapse function value in (0, 1]</returns>
    public static double ComputeLapse(double hamiltonianVal, double alpha = 1.0)
    {
        return 1.0 / (1.0 + alpha * System.Math.Abs(hamiltonianVal));
    }

    /// <summary>
    /// Returns the probability density |?|? at this node.
    /// </summary>
    public double ProbabilityDensity => WaveFunction.X * WaveFunction.X + WaveFunction.Y * WaveFunction.Y;

    /// <summary>
    /// Returns the local proper time step given a coordinate time step.
    /// d? = N · d?
    /// </summary>
    public double ProperTimeStep(double coordinateStep) => Lapse * coordinateStep;
}

/// <summary>
/// Float precision version for GPUs without double support.
/// CRITICAL: Pack = 16 ensures HLSL-compatible alignment.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public readonly struct NodeStateGpuFloat
{
    public readonly Float2 WaveFunction;
    public readonly float Potential;
    private readonly float _padding;

    public NodeStateGpuFloat(Float2 waveFunction, float potential)
    {
        WaveFunction = waveFunction;
        Potential = potential;
        _padding = 0;
    }
}

/// <summary>
/// Extended node state including gauge field components.
/// For Yang-Mills coupled evolution.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct NodeStateGaugeGpu
{
    /// <summary>
    /// Complex wavefunction value at this node.
    /// </summary>
    public readonly Double2 WaveFunction;

    /// <summary>
    /// Local potential V(x) at this node.
    /// </summary>
    public readonly double Potential;

    /// <summary>
    /// Local mass term m(x).
    /// </summary>
    public readonly double Mass;

    /// <summary>
    /// Color charge for gauge coupling (SU(N)).
    /// </summary>
    public readonly double ColorCharge;

    /// <summary>
    /// Node degree (number of neighbors) for Laplacian normalization.
    /// </summary>
    public readonly int Degree;

    /// <summary>
    /// Padding for alignment.
    /// </summary>
    private readonly int _padding;

    public NodeStateGaugeGpu(
        Double2 waveFunction,
        double potential,
        double mass,
        double colorCharge,
        int degree)
    {
        WaveFunction = waveFunction;
        Potential = potential;
        Mass = mass;
        ColorCharge = colorCharge;
        Degree = degree;
        _padding = 0;
    }
}

/// <summary>
/// Spinor state for Dirac equation on graph.
/// Four-component spinor stored as two Double2 pairs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct SpinorStateGpu
{
    /// <summary>
    /// Upper two components of 4-spinor (positive energy).
    /// </summary>
    public readonly Double2 Upper0;
    public readonly Double2 Upper1;

    /// <summary>
    /// Lower two components of 4-spinor (negative energy).
    /// </summary>
    public readonly Double2 Lower0;
    public readonly Double2 Lower1;

    public SpinorStateGpu(Double2 u0, Double2 u1, Double2 l0, Double2 l1)
    {
        Upper0 = u0;
        Upper1 = u1;
        Lower0 = l0;
        Lower1 = l1;
    }

    public static SpinorStateGpu Zero => new(Double2.Zero, Double2.Zero, Double2.Zero, Double2.Zero);
}

/// <summary>
/// Klein-Gordon field state for scalar field evolution.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct KleinGordonStateGpu
{
    /// <summary>
    /// Current field value phi(x,t).
    /// </summary>
    public readonly double Phi;

    /// <summary>
    /// Previous field value phi(x,t-dt) for Verlet integration.
    /// </summary>
    public readonly double PhiPrev;

    /// <summary>
    /// Field mass m.
    /// </summary>
    public readonly double Mass;

    /// <summary>
    /// Padding for alignment.
    /// </summary>
    private readonly double _padding;

    public KleinGordonStateGpu(double phi, double phiPrev, double mass)
    {
        Phi = phi;
        PhiPrev = phiPrev;
        Mass = mass;
        _padding = 0;
    }
}

/// <summary>
/// Buffer pair for ping-pong double buffering.
/// Avoids memory copies by swapping references.
/// </summary>
/// <typeparam name="T">Element type (must be unmanaged for GPU)</typeparam>
public sealed class PingPongBuffer<T> : IDisposable where T : unmanaged
{
    private ReadWriteBuffer<T>? _bufferA;
    private ReadWriteBuffer<T>? _bufferB;
    private bool _aIsCurrent;
    private bool _disposed;

    public int Length { get; private set; }

    public ReadWriteBuffer<T> Current => _aIsCurrent ? _bufferA! : _bufferB!;
    public ReadWriteBuffer<T> Next => _aIsCurrent ? _bufferB! : _bufferA!;

    public void Allocate(GraphicsDevice device, int length)
    {
        ArgumentNullException.ThrowIfNull(device);
        
        Dispose();
        
        Length = length;
        _bufferA = device.AllocateReadWriteBuffer<T>(length);
        _bufferB = device.AllocateReadWriteBuffer<T>(length);
        _aIsCurrent = true;
        _disposed = false;
    }

    /// <summary>
    /// Swap current and next buffers (no memory copy).
    /// </summary>
    public void Swap()
    {
        _aIsCurrent = !_aIsCurrent;
    }

    /// <summary>
    /// Copy data to current buffer.
    /// </summary>
    public void UploadToCurrent(ReadOnlySpan<T> data)
    {
        if (data.Length != Length)
            throw new ArgumentException($"Data length {data.Length} != buffer length {Length}");
        
        Current.CopyFrom(data);
    }

    /// <summary>
    /// Copy data from current buffer.
    /// </summary>
    public void DownloadFromCurrent(Span<T> destination)
    {
        if (destination.Length != Length)
            throw new ArgumentException($"Destination length {destination.Length} != buffer length {Length}");
        
        Current.CopyTo(destination);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _bufferA?.Dispose();
        _bufferB?.Dispose();
        _bufferA = null;
        _bufferB = null;
        _disposed = true;
    }
}

/// <summary>
/// Conservation data for energy/charge transfer during topology changes.
/// Tracks accumulated mass and spinor contributions from dying edges.
/// 
/// RQ-HYPOTHESIS INTEGRATION:
/// When edges are deleted, their physical content is not lost but transferred
/// to the endpoint nodes. This ensures:
/// - Energy conservation: E_total = const
/// - Gauge charge conservation: Q_total = const
/// 
/// Used by EdgeDyingConservationKernel in the CONSERVE phase of topology updates.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public readonly struct NodeConservationState
{
    /// <summary>
    /// Accumulated mass from dying edges (energy converted to mass).
    /// M_node += ?(E_edge / 2) for all dying edges incident to this node.
    /// </summary>
    public readonly double AccumulatedMass;

    /// <summary>
    /// Accumulated spinor/charge from dying edges (gauge flux transfer).
    /// Stored as Double2 for complex spinor (real, imag).
    /// ?_node += ?(?_edge) with sign based on edge direction.
    /// </summary>
    public readonly Double2 AccumulatedSpinor;

    /// <summary>
    /// Number of dying edges that contributed to this node.
    /// Useful for averaging or debugging.
    /// </summary>
    public readonly int DyingEdgeCount;

    /// <summary>
    /// Padding for 16-byte alignment.
    /// </summary>
    private readonly int _padding;

    public NodeConservationState(double accumulatedMass, Double2 accumulatedSpinor, int dyingEdgeCount)
    {
        AccumulatedMass = accumulatedMass;
        AccumulatedSpinor = accumulatedSpinor;
        DyingEdgeCount = dyingEdgeCount;
        _padding = 0;
    }

    /// <summary>Zero state (no accumulated conservation content).</summary>
    public static NodeConservationState Zero => new(0.0, Double2.Zero, 0);
}

/// <summary>
/// Edge gauge state for GPU computation.
/// Stores gauge field data (U(1) phase) in GPU-compatible format.
/// 
/// Used by ConservationShaders to track gauge flux on edges.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public readonly struct EdgeGaugeStateGpu
{
    /// <summary>
    /// U(1) gauge phase as complex number (cos ?, sin ?).
    /// Represents parallel transport factor: U = exp(i?).
    /// </summary>
    public readonly Double2 Phase;

    /// <summary>
    /// Gauge field amplitude (for non-abelian extensions).
    /// For U(1): typically 1.0.
    /// </summary>
    public readonly double Amplitude;

    /// <summary>
    /// Padding for 16-byte alignment.
    /// </summary>
    private readonly double _padding;

    public EdgeGaugeStateGpu(Double2 phase, double amplitude = 1.0)
    {
        Phase = phase;
        Amplitude = amplitude;
        _padding = 0;
    }

    public EdgeGaugeStateGpu(double theta)
    {
        Phase = new Double2(System.Math.Cos(theta), System.Math.Sin(theta));
        Amplitude = 1.0;
        _padding = 0;
    }

    /// <summary>Zero phase (identity transport).</summary>
    public static EdgeGaugeStateGpu Identity => new(new Double2(1.0, 0.0), 1.0);

    /// <summary>Gets the phase angle theta from the complex representation.</summary>
    public double GetPhaseAngle() => System.Math.Atan2(Phase.Y, Phase.X);
}

/// <summary>
/// Conservation statistics for a single topology update step.
/// Used to validate energy/charge conservation in Science mode.
/// </summary>
public sealed class ConservationStats
{
    /// <summary>Total energy in edges before deletion.</summary>
    public double EnergyBefore { get; set; }

    /// <summary>Total energy transferred to nodes.</summary>
    public double EnergyTransferred { get; set; }

    /// <summary>Conservation error (|EnergyBefore - EnergyTransferred|).</summary>
    public double ConservationError { get; set; }

    /// <summary>Whether conservation was validated successfully.</summary>
    public bool IsConserved { get; set; }

    /// <summary>Total gauge flux before deletion.</summary>
    public Double2 FluxBefore { get; set; }

    /// <summary>Total gauge flux transferred.</summary>
    public Double2 FluxTransferred { get; set; }

    /// <summary>Number of dying edges processed.</summary>
    public int DyingEdgeCount { get; set; }

    /// <summary>Time spent in conservation phase (ms).</summary>
    public double ConservationTimeMs { get; set; }
}
