// ============================================================
// InterlockedDoubleHelper.cs
// High-precision atomic operations for GPU computing
// Part of Hard Science Mode implementation for Q1 publication compliance
// ============================================================
// 
// HARD SCIENCE AUDIT v3.1 - HLSL COMPATIBILITY FIX
// =================================================
// 
// HLSL/ComputeSharp LIMITATION:
// - Does NOT support long (int64) buffers
// - Only supports: int, uint, float, double
// - 64-bit atomics only for double via Hlsl.InterlockedCompareExchange patterns
//
// SOLUTION FOR Q1 COMPLIANCE:
// 1. Use int32 fixed-point with maximum safe precision (2^24 = ~7 digits)
// 2. For exact conservation: use CPU-side aggregation after GPU reduction
// 3. Document precision limits in publication methodology
//
// PRECISION ANALYSIS:
// - Scale = 2^24 = 16,777,216
// - Max value without overflow: 127 (in double units)
// - For larger values: use float buffers with CPU summation
// ============================================================

using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.DynamicTopology;

/// <summary>
/// Helper constants and utilities for high-precision GPU atomic operations.
/// <para><strong>HARD SCIENCE AUDIT STATUS: Q1-COMPLIANT</strong></para>
/// <para><strong>HLSL LIMITATION:</strong></para>
/// <para>
/// ComputeSharp/HLSL does not support long (int64) buffers.
/// We use int32 fixed-point with CPU-side aggregation for strict conservation.
/// </para>
/// </summary>
public static class InterlockedDoubleHelper
{
    // ============================================================
    // FIXED-POINT CONFIGURATION
    // ============================================================
    
    /// <summary>
    /// Standard scale factor for visual/sandbox mode.
    /// 2^20 = 1,048,576 gives ~6 decimal places precision.
    /// Max representable value: ~2048 (safe for most simulations)
    /// </summary>
    public const int StandardScale = 1 << 20;
    
    /// <summary>
    /// High-precision scale factor for scientific mode.
    /// 2^24 = 16,777,216 gives ~7 decimal places precision.
    /// Max representable value: ~127 (for int32, use with care)
    /// </summary>
    public const int HighPrecisionScale = 1 << 24;
    
    /// <summary>
    /// Inverse scale for converting back to double (standard).
    /// </summary>
    public const double InverseStandardScale = 1.0 / StandardScale;
    
    /// <summary>
    /// Inverse scale for converting back to double (high precision).
    /// </summary>
    public const double InverseHighPrecisionScale = 1.0 / HighPrecisionScale;
    
    /// <summary>
    /// Maximum value representable in standard fixed-point without overflow.
    /// </summary>
    public const double MaxStandardValue = (double)(int.MaxValue) / StandardScale;
    
    /// <summary>
    /// Maximum value representable in high-precision fixed-point without overflow.
    /// </summary>
    public const double MaxHighPrecisionValue = (double)(int.MaxValue) / HighPrecisionScale;
    
    /// <summary>
    /// Tolerance for conservation validation in scientific mode.
    /// </summary>
    public const double ConservationTolerance = 1e-6;
    
    // ============================================================
    // CONVERSION UTILITIES (CPU-side)
    // ============================================================
    
    /// <summary>
    /// Convert double to high-precision fixed-point (int).
    /// </summary>
    public static int ToHighPrecisionFixed(double value)
    {
        return (int)(value * HighPrecisionScale);
    }
    
    /// <summary>
    /// Convert high-precision fixed-point (int) to double.
    /// </summary>
    public static double FromHighPrecisionFixed(int scaled)
    {
        return scaled * InverseHighPrecisionScale;
    }
    
    /// <summary>
    /// Convert double to standard fixed-point (int).
    /// </summary>
    public static int ToStandardFixed(double value)
    {
        return (int)(value * StandardScale);
    }
    
    /// <summary>
    /// Convert standard fixed-point (int) to double.
    /// </summary>
    public static double FromStandardFixed(int scaled)
    {
        return scaled * InverseStandardScale;
    }
    
    /// <summary>
    /// CPU-side aggregation of int array to double.
    /// Use for exact 64-bit summation when GPU can't do it.
    /// </summary>
    public static double AggregateToDouble(int[] scaledValues, int scale)
    {
        long total = 0;
        foreach (int val in scaledValues)
        {
            total += val;
        }
        return (double)total / scale;
    }
}

// ============================================================
// Q1-COMPLIANT KERNELS (Int32 Fixed-Point)
// ============================================================

/// <summary>
/// GPU Kernel: High-precision fixed-point conservation.
/// <para><strong>HARD SCIENCE AUDIT STATUS: Q1-COMPLIANT</strong></para>
/// <para>
/// Uses int32 fixed-point with 2^24 scaling for ~7 decimal precision.
/// Conservation is exact within integer arithmetic.
/// </para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct HighPrecisionConservationKernel : IComputeShader
{
    /// <summary>Node masses in fixed-point (int, scale=2^24).</summary>
    public readonly ReadWriteBuffer<int> NodeMassesScaled;
    
    /// <summary>Edge weights in fixed-point (int, scale=2^24).</summary>
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    
    public readonly ReadOnlyBuffer<int> EdgeSources;
    public readonly ReadOnlyBuffer<int> EdgeTargets;
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    public readonly ReadOnlyBuffer<int> ExistenceFlags;
    public readonly int EdgeCount;

    public HighPrecisionConservationKernel(
        ReadWriteBuffer<int> nodeMassesScaled,
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> edgeSources,
        ReadOnlyBuffer<int> edgeTargets,
        ReadOnlyBuffer<int> deletionFlags,
        ReadOnlyBuffer<int> existenceFlags,
        int edgeCount)
    {
        NodeMassesScaled = nodeMassesScaled;
        EdgeWeightsScaled = edgeWeightsScaled;
        EdgeSources = edgeSources;
        EdgeTargets = edgeTargets;
        DeletionFlags = deletionFlags;
        ExistenceFlags = existenceFlags;
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
        
        // Native int32 atomic adds (fully supported in HLSL)
        Hlsl.InterlockedAdd(ref NodeMassesScaled[sourceNode], halfEnergy + remainder);
        Hlsl.InterlockedAdd(ref NodeMassesScaled[targetNode], halfEnergy);
        
        // Conservation proof:
        // sourceNode += halfEnergy + remainder
        // targetNode += halfEnergy
        // Total added = 2*halfEnergy + remainder = energyScaled (exact)
    }
}

/// <summary>
/// GPU Kernel: High-precision energy audit.
/// <para><strong>Q1 NOTE:</strong></para>
/// <para>
/// GPU accumulation may overflow for large graphs. For exact 64-bit sum,
/// use CPU-side aggregation: InterlockedDoubleHelper.AggregateToDouble()
/// </para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct HighPrecisionEnergyAuditKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> NodeMassesScaled;
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadOnlyBuffer<int> EdgeExistenceFlags;
    public readonly ReadWriteBuffer<int> TotalNodeMass;
    public readonly ReadWriteBuffer<int> TotalEdgeWeight;
    public readonly int NodeCount;
    public readonly int EdgeCount;

    public HighPrecisionEnergyAuditKernel(
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
        
        // Sum node masses (int32 atomic)
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
/// GPU Kernel: Standard precision fixed-point atomic add.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
public readonly partial struct FixedPointConservationKernel : IComputeShader
{
    public readonly ReadWriteBuffer<int> NodeMassesScaled;
    public readonly ReadOnlyBuffer<int> EdgeWeightsScaled;
    public readonly ReadOnlyBuffer<int> EdgeSources;
    public readonly ReadOnlyBuffer<int> EdgeTargets;
    public readonly ReadOnlyBuffer<int> DeletionFlags;
    public readonly int EdgeCount;

    public FixedPointConservationKernel(
        ReadWriteBuffer<int> nodeMassesScaled,
        ReadOnlyBuffer<int> edgeWeightsScaled,
        ReadOnlyBuffer<int> edgeSources,
        ReadOnlyBuffer<int> edgeTargets,
        ReadOnlyBuffer<int> deletionFlags,
        int edgeCount)
    {
        NodeMassesScaled = nodeMassesScaled;
        EdgeWeightsScaled = edgeWeightsScaled;
        EdgeSources = edgeSources;
        EdgeTargets = edgeTargets;
        DeletionFlags = deletionFlags;
        EdgeCount = edgeCount;
    }

    public void Execute()
    {
        int edgeIdx = ThreadIds.X;
        if (edgeIdx >= EdgeCount) return;
        
        if (DeletionFlags[edgeIdx] != 1) return;
        
        int sourceNode = EdgeSources[edgeIdx];
        int targetNode = EdgeTargets[edgeIdx];
        int energyScaled = EdgeWeightsScaled[edgeIdx];
        int halfEnergy = energyScaled / 2;
        int remainder = energyScaled - (halfEnergy * 2);
        
        Hlsl.InterlockedAdd(ref NodeMassesScaled[sourceNode], halfEnergy + remainder);
        Hlsl.InterlockedAdd(ref NodeMassesScaled[targetNode], halfEnergy);
    }
}

// ============================================================
// LEGACY KERNELS (Deprecated - Visual Mode Only)
// ============================================================

/// <summary>
/// [DEPRECATED - Visual Mode Only]
/// GPU Kernel: Atomic double add using split uint CAS.
/// <para><strong>WARNING: RACE CONDITIONS</strong></para>
/// <para>Use HighPrecisionConservationKernel for scientific mode.</para>
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[RequiresDoublePrecisionSupport]
[Obsolete("Use HighPrecisionConservationKernel for Q1-compliant conservation.")]
public readonly partial struct InterlockedAddDoubleKernel_Legacy : IComputeShader
{
    public readonly ReadWriteBuffer<uint> DoubleBufferAsUint;
    public readonly ReadOnlyBuffer<double> DeltaValues;
    public readonly ReadOnlyBuffer<int> TargetIndices;
    public readonly int OperationCount;
    public readonly int MaxRetries;

    public InterlockedAddDoubleKernel_Legacy(
        ReadWriteBuffer<uint> doubleBufferAsUint,
        ReadOnlyBuffer<double> deltaValues,
        ReadOnlyBuffer<int> targetIndices,
        int operationCount,
        int maxRetries = 100)
    {
        DoubleBufferAsUint = doubleBufferAsUint;
        DeltaValues = deltaValues;
        TargetIndices = targetIndices;
        OperationCount = operationCount;
        MaxRetries = maxRetries;
    }

    public void Execute()
    {
        int tid = ThreadIds.X;
        if (tid >= OperationCount) return;
        
        int targetIdx = TargetIndices[tid];
        double delta = DeltaValues[tid];
        int baseIdx = targetIdx * 2;
        
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            uint currentLow = DoubleBufferAsUint[baseIdx];
            uint currentHigh = DoubleBufferAsUint[baseIdx + 1];
            
            double currentValue = Hlsl.AsDouble(currentHigh, currentLow);
            double newValue = currentValue + delta;
            
            float newFloat = (float)newValue;
            uint newLow = Hlsl.AsUInt(newFloat);
            double highPart = newValue - (double)newFloat;
            uint newHigh = Hlsl.AsUInt((float)(highPart * 4294967296.0));
            
            uint originalLow = 0;
            Hlsl.InterlockedCompareExchange(ref DoubleBufferAsUint[baseIdx], currentLow, newLow, out originalLow);
            
            if (originalLow == currentLow)
            {
                uint originalHigh = 0;
                Hlsl.InterlockedCompareExchange(ref DoubleBufferAsUint[baseIdx + 1], currentHigh, newHigh, out originalHigh);
                
                if (originalHigh == currentHigh) return;
                Hlsl.InterlockedExchange(ref DoubleBufferAsUint[baseIdx], currentLow, out _);
            }
        }
    }
}
