using System.Runtime.InteropServices;

namespace RqSimEngineApi.Contracts;

/// <summary>
/// GPU-compatible physics parameters for dynamic configuration.
/// 
/// This struct is blittable (contains only primitive types) for direct
/// upload to GPU constant buffers via ComputeSharp.
/// 
/// USAGE:
/// - UI creates PhysicsSettingsConfig with user settings
/// - PhysicsSettingsConfig.ToGpuParameters() converts to this struct
/// - SimulationContext.Params carries this to all physics modules
/// - GPU shaders receive values through readonly fields (not const!)
/// 
/// HARD SCIENCE AUDIT v3.0 - MEMORY ALIGNMENT FIX
/// ===============================================
/// HLSL cbuffer packing rules (std140/std430):
/// - double requires 8-byte alignment
/// - int after double needs 4-byte padding to align next double
/// - Total size should be multiple of 16 bytes
/// 
/// This struct now has explicit padding to match GPU memory layout.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public partial struct SimulationParameters
{
    // ============================================================
    // TIME & EVOLUTION (24 bytes)
    // ============================================================
    
    /// <summary>Time step (dt) for integration.</summary>
    public double DeltaTime;                    // Offset 0, Size 8
    
    /// <summary>Current simulation time.</summary>
    public double CurrentTime;                  // Offset 8, Size 8
    
    /// <summary>Current tick/frame ID.</summary>
    public long TickId;                         // Offset 16, Size 8

    // ============================================================
    // GRAVITY & GEOMETRY (48 bytes)
    // ============================================================
    
    /// <summary>Gravitational coupling constant G.</summary>
    public double GravitationalCoupling;        // Offset 24, Size 8
    
    /// <summary>Ricci flow rate ? for dw/dt = -?(R - T).</summary>
    public double RicciFlowAlpha;               // Offset 32, Size 8
    
    /// <summary>Lapse function parameter for ADM formalism.</summary>
    public double LapseFunctionAlpha;           // Offset 40, Size 8
    
    /// <summary>Cosmological constant ?.</summary>
    public double CosmologicalConstant;         // Offset 48, Size 8
    
    /// <summary>Vacuum energy density scale.</summary>
    public double VacuumEnergyScale;            // Offset 56, Size 8
    
    /// <summary>
    /// Lazy random walk parameter ? ? [0,1] for Ollivier-Ricci curvature.
    /// Probability of staying at current node in random walk.
    /// Default: 0.1 (10% stay, 90% move to neighbor)
    /// </summary>
    public double LazyWalkAlpha;                // Offset 64, Size 8

    // ============================================================
    // THERMODYNAMICS (32 bytes)
    // ============================================================
    
    /// <summary>Temperature T for thermal fluctuations.</summary>
    public double Temperature;                  // Offset 72, Size 8
    
    /// <summary>Inverse temperature ? = 1/kT for Boltzmann factors.</summary>
    public double InverseBeta;                  // Offset 80, Size 8
    
    /// <summary>Annealing cooling rate for simulated annealing.</summary>
    public double AnnealingRate;                // Offset 88, Size 8
    
    /// <summary>Decoherence rate for quantum-classical transition.</summary>
    public double DecoherenceRate;              // Offset 96, Size 8

    // ============================================================
    // GAUGE FIELDS - YANG-MILLS (24 bytes)
    // ============================================================
    
    /// <summary>Gauge coupling constant g for SU(2)/SU(3).</summary>
    public double GaugeCoupling;                // Offset 104, Size 8
    
    /// <summary>Wilson action parameter.</summary>
    public double WilsonParameter;              // Offset 112, Size 8
    
    /// <summary>Gauge field damping coefficient.</summary>
    public double GaugeFieldDamping;            // Offset 120, Size 8

    // ============================================================
    // TOPOLOGY DYNAMICS (32 bytes)
    // ============================================================
    
    /// <summary>Probability of edge creation per trial.</summary>
    public double EdgeCreationProbability;      // Offset 128, Size 8
    
    /// <summary>Probability of edge deletion per trial.</summary>
    public double EdgeDeletionProbability;      // Offset 136, Size 8
    
    /// <summary>Energy threshold for topology changes.</summary>
    public double TopologyBreakThreshold;       // Offset 144, Size 8
    
    /// <summary>Edge trial probability per step.</summary>
    public double EdgeTrialProbability;         // Offset 152, Size 8

    // ============================================================
    // QUANTUM FIELDS (32 bytes)
    // ============================================================
    
    /// <summary>Measurement threshold for wavefunction collapse.</summary>
    public double MeasurementThreshold;         // Offset 160, Size 8
    
    /// <summary>Scalar field mass squared m? for Klein-Gordon.</summary>
    public double ScalarFieldMassSquared;       // Offset 168, Size 8
    
    /// <summary>Fermion mass for Dirac equation.</summary>
    public double FermionMass;                  // Offset 176, Size 8
    
    /// <summary>Pair creation energy threshold.</summary>
    public double PairCreationEnergy;           // Offset 184, Size 8

    // ============================================================
    // SPECTRAL GEOMETRY (24 bytes)
    // ============================================================
    
    /// <summary>Spectral cutoff ? for spectral action.</summary>
    public double SpectralCutoff;               // Offset 192, Size 8
    
    /// <summary>Target spectral dimension (usually 4.0).</summary>
    public double TargetSpectralDimension;      // Offset 200, Size 8
    
    /// <summary>Spectral dimension potential strength.</summary>
    public double SpectralDimensionStrength;    // Offset 208, Size 8

    // ============================================================
    // NUMERICAL PARAMETERS (32 bytes with padding)
    // HARD SCIENCE AUDIT: Fixed alignment issue
    // ============================================================
    
    /// <summary>Sinkhorn iterations for optimal transport.</summary>
    public int SinkhornIterations;              // Offset 216, Size 4
    
    /// <summary>
    /// AUDIT FIX: Explicit padding after int to align next double.
    /// Without this, GPU reads SinkhornEpsilon from wrong offset.
    /// </summary>
    private int _paddingSinkhorn;               // Offset 220, Size 4 (ALIGNMENT FIX)
    
    /// <summary>Entropic regularization ? for Sinkhorn.</summary>
    public double SinkhornEpsilon;              // Offset 224, Size 8 (now correctly aligned)
    
    /// <summary>Convergence threshold for iterative solvers.</summary>
    public double ConvergenceThreshold;         // Offset 232, Size 8

    // ============================================================
    // FLAGS (16 bytes with padding)
    // ============================================================
    
    /// <summary>
    /// Packed boolean flags for GPU efficiency.
    /// Bit 0: UseDoublePrecision
    /// Bit 1: ScientificMode
    /// Bit 2: EnableVacuumReservoir
    /// Bit 3: EnableOllivierRicci
    /// Bit 4: EnableSpectralAction
    /// Bit 5: EnableHamiltonianGravity
    /// Bit 6: EnableTopologyCompensation
    /// Bit 7: EnableWilsonProtection
    /// </summary>
    public int Flags;                           // Offset 240, Size 4
    
    /// <summary>
    /// GPU integrity flags for fixed-point overflow detection.
    /// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Saturating arithmetic flag reporting.</para>
    /// <para>
    /// Bit 0 (1): FLAG_OVERFLOW_DETECTED - Fixed-point overflow in atomic add<br/>
    /// Bit 1 (2): FLAG_UNDERFLOW_DETECTED - Fixed-point underflow in atomic add<br/>
    /// Bit 2 (4): FLAG_TDR_TRUNCATION - GPU timeout truncated computation<br/>
    /// Bit 3 (8): FLAG_CONSERVATION_VIOLATION - Energy conservation exceeded tolerance<br/>
    /// Bit 4 (16): FLAG_NAN_DETECTED - NaN/Infinity in critical buffer<br/>
    /// Bit 5 (32): FLAG_64BIT_OVERFLOW - 64-bit accumulator overflow
    /// </para>
    /// <para>
    /// CPU-side code MUST check this after GPU kernel completion.
    /// Non-zero value indicates scientific data integrity failure.
    /// </para>
    /// </summary>
    public int IntegrityFlags;                  // Offset 244, Size 4
    
    /// <summary>Additional padding to reach 16-byte boundary.</summary>
    private long _padding16ByteAlign;           // Offset 248, Size 8
    
    // Total size: 256 bytes (16-byte aligned, multiple of 16)

    // ============================================================
    // FLAG ACCESSORS
    // ============================================================
    
    /// <summary>Use double precision (fp64) for calculations.</summary>
    public bool UseDoublePrecision
    {
        readonly get => (Flags & (1 << 0)) != 0;
        set => Flags = value ? Flags | (1 << 0) : Flags & ~(1 << 0);
    }
    
    /// <summary>Enable strict scientific validation mode.</summary>
    public bool ScientificMode
    {
        readonly get => (Flags & (1 << 1)) != 0;
        set => Flags = value ? Flags | (1 << 1) : Flags & ~(1 << 1);
    }
    
    /// <summary>Enable vacuum energy reservoir.</summary>
    public bool EnableVacuumReservoir
    {
        readonly get => (Flags & (1 << 2)) != 0;
        set => Flags = value ? Flags | (1 << 2) : Flags & ~(1 << 2);
    }
    
    /// <summary>Prefer Ollivier-Ricci over Forman-Ricci curvature.</summary>
    public bool EnableOllivierRicci
    {
        readonly get => (Flags & (1 << 3)) != 0;
        set => Flags = value ? Flags | (1 << 3) : Flags & ~(1 << 3);
    }
    
    /// <summary>Enable spectral action formulation.</summary>
    public bool EnableSpectralAction
    {
        readonly get => (Flags & (1 << 4)) != 0;
        set => Flags = value ? Flags | (1 << 4) : Flags & ~(1 << 4);
    }
    
    /// <summary>Use Hamiltonian gravity formulation.</summary>
    public bool EnableHamiltonianGravity
    {
        readonly get => (Flags & (1 << 5)) != 0;
        set => Flags = value ? Flags | (1 << 5) : Flags & ~(1 << 5);
    }
    
    /// <summary>Enable topology-energy compensation.</summary>
    public bool EnableTopologyCompensation
    {
        readonly get => (Flags & (1 << 6)) != 0;
        set => Flags = value ? Flags | (1 << 6) : Flags & ~(1 << 6);
    }
    
    /// <summary>Enable Wilson loop protection.</summary>
    public bool EnableWilsonProtection
    {
        readonly get => (Flags & (1 << 7)) != 0;
        set => Flags = value ? Flags | (1 << 7) : Flags & ~(1 << 7);
    }

    // ============================================================
    // INTEGRITY FLAG ACCESSORS
    // ============================================================

    /// <summary>
    /// Check if any fixed-point overflow was detected on GPU.
    /// </summary>
    public readonly bool HasOverflowDetected => (IntegrityFlags & 1) != 0;

    /// <summary>
    /// Check if any fixed-point underflow was detected on GPU.
    /// </summary>
    public readonly bool HasUnderflowDetected => (IntegrityFlags & 2) != 0;

    /// <summary>
    /// Check if GPU TDR (timeout) truncated computation.
    /// </summary>
    public readonly bool HasTdrTruncation => (IntegrityFlags & 4) != 0;

    /// <summary>
    /// Check if energy conservation violation was detected.
    /// </summary>
    public readonly bool HasConservationViolation => (IntegrityFlags & 8) != 0;

    /// <summary>
    /// Check if NaN or Infinity was detected in critical buffers.
    /// </summary>
    public readonly bool HasNanDetected => (IntegrityFlags & 16) != 0;

    /// <summary>
    /// Check if 64-bit accumulator overflow occurred.
    /// </summary>
    public readonly bool Has64BitOverflow => (IntegrityFlags & 32) != 0;

    /// <summary>
    /// Check if any integrity issue was detected.
    /// </summary>
    public readonly bool HasAnyIntegrityIssue => IntegrityFlags != 0;

    /// <summary>
    /// Clear all integrity flags (call before kernel dispatch).
    /// </summary>
    public void ClearIntegrityFlags()
    {
        IntegrityFlags = 0;
    }

    /// <summary>
    /// Get a human-readable description of current integrity issues.
    /// </summary>
    public readonly string GetIntegrityReport()
    {
        if (IntegrityFlags == 0)
            return "OK";

        var issues = new System.Text.StringBuilder();
        if (HasOverflowDetected) issues.Append("OVERFLOW ");
        if (HasUnderflowDetected) issues.Append("UNDERFLOW ");
        if (HasTdrTruncation) issues.Append("TDR ");
        if (HasConservationViolation) issues.Append("CONSERVATION ");
        if (HasNanDetected) issues.Append("NAN ");
        if (Has64BitOverflow) issues.Append("64BIT_OVERFLOW ");
        return issues.ToString().TrimEnd();
    }
}
