// ============================================================
// ConservationShaders.cs
// GPU shaders for energy/charge conservation during edge deletion
// Part of Dynamic Topology pipeline: MARK ? CONSERVE ? SWEEP
// 
// HARD SCIENCE AUDIT v3.2 - SATURATING ARITHMETIC & 64-BIT EMULATION
// ===================================================================
// ComputeSharp/HLSL does not support long (int64) buffers.
// Using int32 fixed-point with:
// - Saturating arithmetic (CAS-based overflow protection)
// - 64-bit software emulation via int2 (Hi/Lo) buffers
// - Integrity flags for CPU-side violation detection
// ============================================================

using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// CONSERVATION SHADERS FOR DYNAMIC TOPOLOGY
/// ==========================================
/// 
/// When edges are deleted during topology evolution, their physical content
/// (metric energy, gauge flux, etc.) must be conserved by transferring to nodes.
/// 
/// HARD SCIENCE AUDIT v3.2:
/// =========================
/// - ScientificConservationKernel: Uses int32 fixed-point with SATURATING ARITHMETIC
/// - ScientificEnergyAuditKernel64: Uses 64-bit emulation via int2 (Hi/Lo) buffers
/// - SafeAtomicAddWithSaturation: CAS-based overflow protection
/// - DoubleConservationKernel: DEPRECATED (race conditions, visual mode only)
/// </summary>

// ============================================================
// SATURATING ARITHMETIC HELPER KERNELS
// ============================================================

/// <summary>
/// Saturating atomic add kernel - adds a value to a buffer element with overflow protection.
/// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Prevents silent integer overflow.</para>
/// <para>
/// When the result would exceed INT32_MAX or fall below INT32_MIN,
/// the value is clamped to the limit and an overflow flag is set.
/// </para>
/// </summary>
/// <remarks>
/// This kernel is designed to be called as a helper from other kernels
/// using the SaturatingAtomicAdd static method pattern in ComputeSharp.
/// </remarks>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct SaturatingAtomicAddKernel : IComputeShader
{
    /// <summary>Target buffer for atomic operations.</summary>
    public readonly ReadWriteBuffer<int> TargetBuffer;
    
    /// <summary>Delta values to add.</summary>
    public readonly ReadOnlyBuffer<int> Deltas;
    
    /// <summary>Target indices in TargetBuffer.</summary>
    public readonly ReadOnlyBuffer<int> Indices;
    
    /// <summary>Integrity flags buffer (single element, atomically ORed).</summary>
    public readonly ReadWriteBuffer<int> IntegrityFlags;
    
    /// <summary>Number of operations to perform.</summary>
    public readonly int Count;

    /// <summary>
    /// Maximum safe value for saturation (~95% of INT32_MAX).
    /// </summary>
    private const int MAX_SAFE = 2000000000;
    
    /// <summary>
    /// Minimum safe value for saturation (symmetric).
    /// </summary>
    private const int MIN_SAFE = -2000000000;
    
    /// <summary>
    /// Overflow detected flag (matches PhysicsConstants.IntegrityFlags.FLAG_OVERFLOW_DETECTED).
    /// </summary>
    private const int FLAG_OVERFLOW = 1;
    
    /// <summary>
    /// Underflow detected flag (matches PhysicsConstants.IntegrityFlags.FLAG_UNDERFLOW_DETECTED).
    /// </summary>
    private const int FLAG_UNDERFLOW = 2;

    public SaturatingAtomicAddKernel(
        ReadWriteBuffer<int> targetBuffer,
        ReadOnlyBuffer<int> deltas,
        ReadOnlyBuffer<int> indices,
        ReadWriteBuffer<int> integrityFlags,
        int count)
    {
        TargetBuffer = targetBuffer;
        Deltas = deltas;
        Indices = indices;
        IntegrityFlags = integrityFlags;
        Count = count;
    }

    public void Execute()
    {
        int tid = ThreadIds.X;
        if (tid >= Count) return;
        
        int index = Indices[tid];
        int delta = Deltas[tid];
        
        // CAS loop with saturation
        int maxRetries = 64;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            int currentValue = TargetBuffer[index];
            int newValue;
            
            // Check for overflow (adding positive to positive)
            if (delta > 0 && currentValue > MAX_SAFE - delta)
            {
                newValue = MAX_SAFE; // Saturate to max
                Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_OVERFLOW);
            }
            // Check for underflow (adding negative to negative)
            else if (delta < 0 && currentValue < MIN_SAFE - delta)
            {
                newValue = MIN_SAFE; // Saturate to min
                Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_UNDERFLOW);
            }
            else
            {
                newValue = currentValue + delta;
            }
            
            // If already saturated and trying to add more in same direction, exit early
            if (newValue == currentValue && delta != 0)
            {
                return;
            }
            
            int originalValue;
            Hlsl.InterlockedCompareExchange(ref TargetBuffer[index], currentValue, newValue, out originalValue);
            
            if (originalValue == currentValue)
            {
                // Success
                return;
            }
            // CAS failed, retry
        }
        
        // Max retries exceeded - this shouldn't happen in practice
        Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_OVERFLOW);
    }
}

/// <summary>
/// 64-bit accumulator kernel using int2 (Hi/Lo) representation.
/// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Enables correct global energy summation.</para>
/// <para>
/// Standard int32 atomics overflow when summing large graphs.
/// This kernel uses software-emulated 64-bit arithmetic via Hi/Lo int pairs.
/// </para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct Int64AccumulatorKernel : IComputeShader
{
    /// <summary>Source values to accumulate (fixed-point int32).</summary>
    public readonly ReadOnlyBuffer<int> SourceValues;
    
    /// <summary>
    /// Accumulator buffer as int2: x = Low 32 bits (unsigned), y = High 32 bits (signed).
    /// Only element [0] is used as the global accumulator.
    /// </summary>
    public readonly ReadWriteBuffer<int> AccumulatorHi;
    public readonly ReadWriteBuffer<int> AccumulatorLo;
    
    /// <summary>Integrity flags for 64-bit overflow detection.</summary>
    public readonly ReadWriteBuffer<int> IntegrityFlags;
    
    /// <summary>Number of values to accumulate.</summary>
    public readonly int Count;
    
    /// <summary>64-bit overflow flag.</summary>
    private const int FLAG_64BIT_OVERFLOW = 32;

    public Int64AccumulatorKernel(
        ReadOnlyBuffer<int> sourceValues,
        ReadWriteBuffer<int> accumulatorHi,
        ReadWriteBuffer<int> accumulatorLo,
        ReadWriteBuffer<int> integrityFlags,
        int count)
    {
        SourceValues = sourceValues;
        AccumulatorHi = accumulatorHi;
        AccumulatorLo = accumulatorLo;
        IntegrityFlags = integrityFlags;
        Count = count;
    }

    public void Execute()
    {
        int tid = ThreadIds.X;
        if (tid >= Count) return;
        
        int value = SourceValues[tid];
        
        // Sign-extend value to 64-bit representation
        int signExtension = (value < 0) ? -1 : 0;
        uint lowPart = (uint)value;
        
        // Step 1: Atomically add to low part and detect carry
        int oldLow;
        Hlsl.InterlockedAdd(ref AccumulatorLo[0], value, out oldLow);
        
        uint oldLowU = (uint)oldLow;
        uint newLowU = oldLowU + lowPart;
        
        // Step 2: Handle carry/borrow for high part
        int carry = 0;
        
        // Carry occurs when: adding positive and result wrapped around
        if (value >= 0 && newLowU < oldLowU)
        {
            carry = 1;
        }
        // Borrow occurs when: adding negative and result wrapped around (upward)
        else if (value < 0 && newLowU > oldLowU)
        {
            carry = -1;
        }
        
        // Step 3: Add sign extension and carry to high part
        int highDelta = signExtension + carry;
        if (highDelta != 0)
        {
            int oldHigh;
            Hlsl.InterlockedAdd(ref AccumulatorHi[0], highDelta, out oldHigh);
            
            // Check for 64-bit overflow (high part saturating)
            if ((highDelta > 0 && oldHigh > 2000000000) || 
                (highDelta < 0 && oldHigh < -2000000000))
            {
                Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_64BIT_OVERFLOW);
            }
        }
    }
}

/// <summary>
/// Zero a 64-bit accumulator (Hi/Lo pair).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ZeroInt64AccumulatorKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> AccumulatorHi;
    public readonly ReadWriteBuffer<int> AccumulatorLo;

    public ZeroInt64AccumulatorKernel(
        ReadWriteBuffer<int> accumulatorHi,
        ReadWriteBuffer<int> accumulatorLo)
    {
        AccumulatorHi = accumulatorHi;
        AccumulatorLo = accumulatorLo;
    }

    public void Execute()
    {
        if (ThreadIds.X == 0)
        {
            AccumulatorHi[0] = 0;
            AccumulatorLo[0] = 0;
        }
    }
}

/// <summary>
/// GPU Kernel: Count dying edges for statistics.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct CountDyingEdgesKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    public readonly ReadOnlyBuffer<int> PreviousExistence;
    public readonly ReadWriteBuffer<int> DyingCount;
    public readonly int EdgeCount;

    public CountDyingEdgesKernel(
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> previousExistence,
        ReadWriteBuffer<int> dyingCount,
        int edgeCount)
    {
        DeletionFlags = deletionFlags;
        PreviousExistence = previousExistence;
        DyingCount = dyingCount;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int edgeIndex = ThreadIds.X;
        if (edgeIndex >= EdgeCount) return;

        if (DeletionFlags[edgeIndex] == 1 && PreviousExistence[edgeIndex] == 1)
        {
            Hlsl.InterlockedAdd(ref DyingCount[0], 1);
        }
    }
}

/// <summary>
/// GPU Kernel: Sum edge weights for dying edges (standard precision).
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct SumDyingEdgeWeightsKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    public readonly ReadOnlyBuffer<int> PreviousExistence;
    public readonly ReadWriteBuffer<int> TotalWeightScaled;
    public readonly int EdgeCount;

    public SumDyingEdgeWeightsKernel(
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> previousExistence,
        ReadWriteBuffer<int> totalWeightScaled,
        int edgeCount)
    {
        EdgeWeightsScaled = edgeWeightsScaled;
        DeletionFlags = deletionFlags;
        PreviousExistence = previousExistence;
        TotalWeightScaled = totalWeightScaled;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int edgeIndex = ThreadIds.X;
        if (edgeIndex >= EdgeCount) return;

        if (DeletionFlags[edgeIndex] == 1 && PreviousExistence[edgeIndex] == 1)
        {
            int weightScaled = EdgeWeightsScaled[edgeIndex];
            Hlsl.InterlockedAdd(ref TotalWeightScaled[0], weightScaled);
        }
    }
}

/// <summary>
/// Initialize existence flags from weights.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct InitExistenceFlagsFromWeightsKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadWriteBuffer<int> ExistenceFlags;
    public readonly int MinWeightScaled;
    public readonly int EdgeCount;

    public InitExistenceFlagsFromWeightsKernel(
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadWriteBuffer<int> existenceFlags,
        int minWeightScaled,
        int edgeCount)
    {
        EdgeWeightsScaled = edgeWeightsScaled;
        ExistenceFlags = existenceFlags;
        MinWeightScaled = minWeightScaled;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= EdgeCount) return;

        ExistenceFlags[i] = EdgeWeightsScaled[i] >= MinWeightScaled ? 1 : 0;
    }
}

/// <summary>
/// Zero a single-element integer buffer.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ZeroIntBufferKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Buffer;

    public ZeroIntBufferKernel(ReadWriteBuffer<int> buffer)
    {
        Buffer = buffer;
    }

    public void Execute()
    {
        if (ThreadIds.X == 0)
        {
            Buffer[0] = 0;
        }
    }
}

// ============================================================
// Q1-COMPLIANT SCIENTIFIC KERNELS (Int32 Fixed-Point with Saturation)
// ============================================================

/// <summary>
/// Q1-COMPLIANT: High-precision conservation kernel using int32 atomics WITH SATURATION.
/// <para><strong>HARD SCIENCE AUDIT STATUS: ? APPROVED v3.2</strong></para>
/// <para><strong>CONSERVATION LAW:</strong></para>
/// <para>
/// When edge (i,j) is deleted with energy E:<br/>
/// - Node i receives E/2 (plus remainder for odd values)<br/>
/// - Node j receives E/2<br/>
/// - Total energy conserved exactly (within integer arithmetic)<br/>
/// - Overflow protection via CAS-based saturating arithmetic
/// </para>
/// <para><strong>PRECISION:</strong></para>
/// <para>
/// Using scale = 2^24, precision is ~7 decimal digits.
/// Maximum energy per node: ~119.2 units before saturation.
/// </para>
/// <para><strong>INTEGRITY:</strong></para>
/// <para>
/// If saturation occurs, IntegrityFlags[0] is atomically ORed with FLAG_OVERFLOW_DETECTED.
/// CPU-side code MUST check this flag after kernel completion.
/// </para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ScientificConservationKernel : IComputeShader
{
    /// <summary>Node masses in fixed-point (int, scale=2^24).</summary>
    public readonly ReadWriteBuffer<int> NodeMassesScaled;
    
    /// <summary>Edge weights in fixed-point (int, scale=2^24).</summary>
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    
    public readonly ReadOnlyBuffer<int> EdgeSources;
    public readonly ReadOnlyBuffer<int> EdgeTargets;
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    public readonly ReadOnlyBuffer<int> ExistenceFlags;
    
    /// <summary>
    /// Integrity flags buffer for overflow detection.
    /// Single-element buffer, atomically ORed on saturation.
    /// </summary>
    public readonly ReadWriteBuffer<int> IntegrityFlags;
    
    public readonly int EdgeCount;
    
    /// <summary>Maximum safe accumulator value (~95% of INT32_MAX).</summary>
    private const int MAX_SAFE = 2000000000;
    
    /// <summary>Minimum safe accumulator value (symmetric).</summary>
    private const int MIN_SAFE = -2000000000;
    
    /// <summary>Overflow detected flag.</summary>
    private const int FLAG_OVERFLOW = 1;

    public ScientificConservationKernel(
        ReadWriteBuffer<int> nodeMassesScaled,
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> edgeSources,
        ReadOnlyBuffer<int> edgeTargets,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> existenceFlags,
        ReadWriteBuffer<int> integrityFlags,
        int edgeCount)
    {
        NodeMassesScaled = nodeMassesScaled;
        EdgeWeightsScaled = edgeWeightsScaled;
        EdgeSources = edgeSources;
        EdgeTargets = edgeTargets;
        DeletionFlags = deletionFlags;
        ExistenceFlags = existenceFlags;
        IntegrityFlags = integrityFlags;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int edgeIdx = ThreadIds.X;
        if (edgeIdx >= EdgeCount) return;
        
        // Only process dying edges
        if (DeletionFlags[edgeIdx] != 1 || ExistenceFlags[edgeIdx] != 1) return;
        
        int sourceNode = EdgeSources[edgeIdx];
        int targetNode = EdgeTargets[edgeIdx];
        int energyScaled = EdgeWeightsScaled[edgeIdx];
        
        // Exact integer division for conservation
        int halfEnergy = energyScaled / 2;
        int remainder = energyScaled - (halfEnergy * 2); // 0 or 1
        
        // SATURATING atomic adds with overflow detection
        SafeAtomicAdd(sourceNode, halfEnergy + remainder);
        SafeAtomicAdd(targetNode, halfEnergy);
    }
    
    /// <summary>
    /// CAS-based saturating atomic add with overflow detection.
    /// </summary>
    private void SafeAtomicAdd(int nodeIndex, int delta)
    {
        if (delta == 0) return;
        
        const int maxRetries = 64;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            int currentValue = NodeMassesScaled[nodeIndex];
            int newValue;
            bool saturated = false;
            
            // Overflow check (adding positive)
            if (delta > 0 && currentValue > MAX_SAFE - delta)
            {
                newValue = MAX_SAFE;
                saturated = true;
            }
            // Underflow check (adding negative)
            else if (delta < 0 && currentValue < MIN_SAFE - delta)
            {
                newValue = MIN_SAFE;
                saturated = true;
            }
            else
            {
                newValue = currentValue + delta;
            }
            
            // Signal overflow to CPU
            if (saturated)
            {
                Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_OVERFLOW);
            }
            
            // If already at saturation limit and trying to exceed, exit early
            if (newValue == currentValue)
            {
                return;
            }
            
            int originalValue;
            Hlsl.InterlockedCompareExchange(ref NodeMassesScaled[nodeIndex], currentValue, newValue, out originalValue);
            
            if (originalValue == currentValue)
            {
                // CAS succeeded
                return;
            }
            // CAS failed due to contention, retry
        }
        
        // Exceeded max retries - signal potential issue
        Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_OVERFLOW);
    }
}

/// <summary>
/// Q1-COMPLIANT: Energy audit using 64-bit emulation via Hi/Lo int pairs.
/// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Prevents global sum overflow.</para>
/// <para>
/// For large graphs with many nodes, standard int32 atomic sum overflows.
/// This kernel uses software-emulated 64-bit addition for correct totals.
/// </para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ScientificEnergyAuditKernel64 : IComputeShader
{
    public readonly ReadOnlyBuffer<int> NodeMassesScaled;
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadOnlyBuffer<int> EdgeExistenceFlags;
    
    /// <summary>64-bit node mass total: High part.</summary>
    public readonly ReadWriteBuffer<int> TotalNodeMassHi;
    /// <summary>64-bit node mass total: Low part.</summary>
    public readonly ReadWriteBuffer<int> TotalNodeMassLo;
    
    /// <summary>64-bit edge weight total: High part.</summary>
    public readonly ReadWriteBuffer<int> TotalEdgeWeightHi;
    /// <summary>64-bit edge weight total: Low part.</summary>
    public readonly ReadWriteBuffer<int> TotalEdgeWeightLo;
    
    /// <summary>Integrity flags for 64-bit overflow detection.</summary>
    public readonly ReadWriteBuffer<int> IntegrityFlags;
    
    public readonly int NodeCount;
    public readonly int EdgeCount;
    
    /// <summary>64-bit overflow flag.</summary>
    private const int FLAG_64BIT_OVERFLOW = 32;

    public ScientificEnergyAuditKernel64(
        ReadOnlyBuffer<int> nodeMassesScaled,
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> edgeExistenceFlags,
        ReadWriteBuffer<int> totalNodeMassHi,
        ReadWriteBuffer<int> totalNodeMassLo,
        ReadWriteBuffer<int> totalEdgeWeightHi,
        ReadWriteBuffer<int> totalEdgeWeightLo,
        ReadWriteBuffer<int> integrityFlags,
        int nodeCount,
        int edgeCount)
    {
        NodeMassesScaled = nodeMassesScaled;
        EdgeWeightsScaled = edgeWeightsScaled;
        EdgeExistenceFlags = edgeExistenceFlags;
        TotalNodeMassHi = totalNodeMassHi;
        TotalNodeMassLo = totalNodeMassLo;
        TotalEdgeWeightHi = totalEdgeWeightHi;
        TotalEdgeWeightLo = totalEdgeWeightLo;
        IntegrityFlags = integrityFlags;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int tid = ThreadIds.X;
        
        // Sum node masses with 64-bit emulation
        if (tid < NodeCount)
        {
            int mass = NodeMassesScaled[tid];
            AddToNodeMass64(mass);
        }
        
        // Sum edge weights (only existing edges) with 64-bit emulation
        if (tid < EdgeCount && EdgeExistenceFlags[tid] == 1)
        {
            int weight = EdgeWeightsScaled[tid];
            AddToEdgeWeight64(weight);
        }
    }
    
    /// <summary>
    /// Add a signed int32 value to the 64-bit node mass accumulator.
    /// </summary>
    private void AddToNodeMass64(int value)
    {
        // Sign-extend value for high part
        int signExtension = (value < 0) ? -1 : 0;
        uint lowPart = (uint)value;
        
        // Step 1: Atomically add to low part
        int oldLow;
        Hlsl.InterlockedAdd(ref TotalNodeMassLo[0], value, out oldLow);
        
        uint oldLowU = (uint)oldLow;
        uint newLowU = oldLowU + lowPart;
        
        // Step 2: Detect carry/borrow
        int carry = 0;
        if (value >= 0 && newLowU < oldLowU)
        {
            carry = 1; // Overflow in unsigned addition
        }
        else if (value < 0 && newLowU > oldLowU)
        {
            carry = -1; // Underflow (borrow)
        }
        
        // Step 3: Add sign extension and carry to high part
        int highDelta = signExtension + carry;
        if (highDelta != 0)
        {
            int oldHigh;
            Hlsl.InterlockedAdd(ref TotalNodeMassHi[0], highDelta, out oldHigh);
            
            // Detect 64-bit overflow
            if ((highDelta > 0 && oldHigh > 2000000000) || 
                (highDelta < 0 && oldHigh < -2000000000))
            {
                Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_64BIT_OVERFLOW);
            }
        }
    }
    
    /// <summary>
    /// Add a signed int32 value to the 64-bit edge weight accumulator.
    /// </summary>
    private void AddToEdgeWeight64(int value)
    {
        // Sign-extend value for high part
        int signExtension = (value < 0) ? -1 : 0;
        uint lowPart = (uint)value;
        
        // Step 1: Atomically add to low part
        int oldLow;
        Hlsl.InterlockedAdd(ref TotalEdgeWeightLo[0], value, out oldLow);
        
        uint oldLowU = (uint)oldLow;
        uint newLowU = oldLowU + lowPart;
        
        // Step 2: Detect carry/borrow
        int carry = 0;
        if (value >= 0 && newLowU < oldLowU)
        {
            carry = 1; // Overflow in unsigned addition
        }
        else if (value < 0 && newLowU > oldLowU)
        {
            carry = -1; // Underflow (borrow)
        }
        
        // Step 3: Add sign extension and carry to high part
        int highDelta = signExtension + carry;
        if (highDelta != 0)
        {
            int oldHigh;
            Hlsl.InterlockedAdd(ref TotalEdgeWeightHi[0], highDelta, out oldHigh);
            
            // Detect 64-bit overflow
            if ((highDelta > 0 && oldHigh > 2000000000) || 
                (highDelta < 0 && oldHigh < -2000000000))
            {
                Hlsl.InterlockedOr(ref IntegrityFlags[0], FLAG_64BIT_OVERFLOW);
            }
        }
    }
}

/// <summary>
/// [LEGACY] Q1-COMPLIANT: Energy audit using int32 atomics.
/// <para><strong>WARNING:</strong> Overflows for large graphs. Use ScientificEnergyAuditKernel64 instead.</para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[Obsolete("Use ScientificEnergyAuditKernel64 for correct 64-bit global summation.")]
public readonly partial struct ScientificEnergyAuditKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> NodeMassesScaled;
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadOnlyBuffer<int> EdgeExistenceFlags;
    public readonly ReadWriteBuffer<int> TotalNodeMass;
    public readonly ReadWriteBuffer<int> TotalEdgeWeight;
    public readonly int NodeCount;
    public readonly int EdgeCount;

    public ScientificEnergyAuditKernel(
        ReadOnlyBuffer<int> nodeMassesScaled,
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> edgeExistenceFlags,
        ReadWriteBuffer<int> totalNodeMass,
        ReadWriteBuffer<int> totalEdgeWeight,
        int nodeCount,
        int edgeCount)
    {
        NodeMassesScaled = nodeMassesScaled;
        EdgeWeightsScaled = edgeWeightsScaled;
        EdgeExistenceFlags = edgeExistenceFlags;
        TotalNodeMass = totalNodeMass;
        TotalEdgeWeight = totalEdgeWeight;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int tid = ThreadIds.X;
        
        // Sum node masses
        if (tid < NodeCount)
        {
            int mass = NodeMassesScaled[tid];
            Hlsl.InterlockedAdd(ref TotalNodeMass[0], mass);
        }
        
        // Sum edge weights (only existing edges)
        if (tid < EdgeCount && EdgeExistenceFlags[tid] == 1)
        {
            int weight = EdgeWeightsScaled[tid];
            Hlsl.InterlockedAdd(ref TotalEdgeWeight[0], weight);
        }
    }
}

/// <summary>
/// [DEPRECATED - Visual Mode Only]
/// Double-precision conservation using split-uint CAS.
/// <para><strong>WARNING: RACE CONDITIONS - NOT Q1-COMPLIANT</strong></para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
[Obsolete("Use ScientificConservationKernel for Q1-compliant conservation.")]
public readonly partial struct DoubleConservationKernel : IComputeShader
{
    public readonly ReadWriteBuffer<uint> NodeMassesAsUint;
    public readonly ReadOnlyBuffer<double> EdgeWeights;
    public readonly ReadOnlyBuffer<int> EdgeSources;
    public readonly ReadOnlyBuffer<int> EdgeTargets;
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    public readonly ReadOnlyBuffer<int> ExistenceFlags;
    public readonly int EdgeCount;
    public readonly int MaxRetries;

    public DoubleConservationKernel(
        ReadWriteBuffer<uint> nodeMassesAsUint,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> edgeSources,
        ReadOnlyBuffer<int> edgeTargets,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> existenceFlags,
        int edgeCount,
        int maxRetries = 100)
    {
        NodeMassesAsUint = nodeMassesAsUint;
        EdgeWeights = edgeWeights;
        EdgeSources = edgeSources;
        EdgeTargets = edgeTargets;
        DeletionFlags = deletionFlags;
        ExistenceFlags = existenceFlags;
        EdgeCount = edgeCount;
        MaxRetries = maxRetries;
    }

    public void Execute()
    {
        int edgeIdx = ThreadIds.X;
        if (edgeIdx >= EdgeCount) return;
        
        if (DeletionFlags[edgeIdx] != 1 || ExistenceFlags[edgeIdx] != 1) return;
        
        double energy = EdgeWeights[edgeIdx];
        double halfEnergy = energy * 0.5;
        
        int sourceNode = EdgeSources[edgeIdx];
        int targetNode = EdgeTargets[edgeIdx];
        
        AtomicAddDoubleToNode(sourceNode, halfEnergy);
        AtomicAddDoubleToNode(targetNode, halfEnergy);
    }
    
    private void AtomicAddDoubleToNode(int nodeIdx, double delta)
    {
        int baseIdx = nodeIdx * 2;
        
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            uint currentLow = NodeMassesAsUint[baseIdx];
            uint currentHigh = NodeMassesAsUint[baseIdx + 1];
            
            double currentValue = Hlsl.AsDouble(currentHigh, currentLow);
            double newValue = currentValue + delta;
            
            if (newValue != newValue) return;
            
            float newFloat = (float)newValue;
            uint newLow = Hlsl.AsUInt(newFloat);
            double highPart = newValue - (double)newFloat;
            uint newHigh = Hlsl.AsUInt((float)(highPart * 4294967296.0));
            
            uint originalLow = 0;
            Hlsl.InterlockedCompareExchange(ref NodeMassesAsUint[baseIdx], currentLow, newLow, out originalLow);
            
            if (originalLow == currentLow)
            {
                uint originalHigh = 0;
                Hlsl.InterlockedCompareExchange(ref NodeMassesAsUint[baseIdx + 1], currentHigh, newHigh, out originalHigh);
                
                if (originalHigh == currentHigh) return;
                Hlsl.InterlockedExchange(ref NodeMassesAsUint[baseIdx], currentLow, out _);
            }
        }
    }
}

/// <summary>
/// [DEPRECATED - Visual Mode Only]
/// Energy audit with High/Low int accumulation.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[Obsolete("Use ScientificEnergyAuditKernel for Q1-compliant energy audit.")]
public readonly partial struct EnergyAuditKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> NodeMassesScaled;
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadOnlyBuffer<int> EdgeExistenceFlags;
    public readonly ReadWriteBuffer<int> TotalNodeMassHigh;
    public readonly ReadWriteBuffer<int> TotalNodeMassLow;
    public readonly ReadWriteBuffer<int> TotalEdgeWeightHigh;
    public readonly ReadWriteBuffer<int> TotalEdgeWeightLow;
    public readonly int NodeCount;
    public readonly int EdgeCount;

    public EnergyAuditKernel(
        ReadOnlyBuffer<int> nodeMassesScaled,
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> edgeExistenceFlags,
        ReadWriteBuffer<int> totalNodeMassHigh,
        ReadWriteBuffer<int> totalNodeMassLow,
        ReadWriteBuffer<int> totalEdgeWeightHigh,
        ReadWriteBuffer<int> totalEdgeWeightLow,
        int nodeCount,
        int edgeCount)
    {
        NodeMassesScaled = nodeMassesScaled;
        EdgeWeightsScaled = edgeWeightsScaled;
        EdgeExistenceFlags = edgeExistenceFlags;
        TotalNodeMassHigh = totalNodeMassHigh;
        TotalNodeMassLow = totalNodeMassLow;
        TotalEdgeWeightHigh = totalEdgeWeightHigh;
        TotalEdgeWeightLow = totalEdgeWeightLow;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int tid = ThreadIds.X;
        
        if (tid < NodeCount)
        {
            int mass = NodeMassesScaled[tid];
            Hlsl.InterlockedAdd(ref TotalNodeMassLow[0], mass);
            if (mass > 1000000)
            {
                Hlsl.InterlockedAdd(ref TotalNodeMassHigh[0], mass / 1000000);
            }
        }
        
        if (tid < EdgeCount && EdgeExistenceFlags[tid] == 1)
        {
            int weight = EdgeWeightsScaled[tid];
            Hlsl.InterlockedAdd(ref TotalEdgeWeightLow[0], weight);
            if (weight > 1000000)
            {
                Hlsl.InterlockedAdd(ref TotalEdgeWeightHigh[0], weight / 1000000);
            }
        }
    }
}

/// <summary>
/// NaN/Inf detection kernel for simulation health monitoring.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
public readonly partial struct NanDetectionKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> Buffer;
    public readonly ReadWriteBuffer<int> NanCount;
    public readonly ReadWriteBuffer<int> InfCount;
    public readonly ReadWriteBuffer<int> FirstNanIndex;
    public readonly int Count;

    public NanDetectionKernel(
        ReadOnlyBuffer<double> buffer,
        ReadWriteBuffer<int> nanCount,
        ReadWriteBuffer<int> infCount,
        ReadWriteBuffer<int> firstNanIndex,
        int count)
    {
        Buffer = buffer;
        NanCount = nanCount;
        InfCount = infCount;
        FirstNanIndex = firstNanIndex;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= Count) return;
        
        double val = Buffer[i];
        
        if (val != val)
        {
            Hlsl.InterlockedAdd(ref NanCount[0], 1);
            Hlsl.InterlockedMin(ref FirstNanIndex[0], i);
        }
        else if (val > 1e300 || val < -1e300)
        {
            Hlsl.InterlockedAdd(ref InfCount[0], 1);
        }
    }
}

/// <summary>
/// Zero int buffer array.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct ZeroIntBufferArrayKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> Buffer;
    public readonly int Count;

    public ZeroIntBufferArrayKernel(ReadWriteBuffer<int> buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i < Count)
        {
            Buffer[i] = 0;
        }
    }
}
