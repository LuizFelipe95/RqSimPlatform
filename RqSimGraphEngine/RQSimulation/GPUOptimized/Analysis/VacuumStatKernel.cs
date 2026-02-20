using ComputeSharp;

namespace RQSimulation.GPUOptimized.Analysis;

/// <summary>
/// GPU compute shaders for vacuum energy masked reduction.
///
/// Strategy (avoiding InterlockedAdd for float/double):
/// 1. MaskedEnergyCopyKernel: Copies vacuum energy values for vacuum nodes,
///    writes 0.0 for non-vacuum nodes → produces a "masked" array.
/// 2. MaskedCountKernel: Counts vacuum nodes via atomic int increment.
/// 3. The engine then uses block-level parallel reduction on the masked array
///    to compute sum, sumSq for mean/variance, using the existing
///    SquaredNormKernel pattern from ReductionKernel.cs.
///
/// This approach:
/// - Avoids float atomics entirely
/// - Uses existing double-precision block reduction patterns
/// - Separates masking from reduction for composability
/// </summary>

/// <summary>
/// Copies vacuum energy values where the vacuum mask is 1, writes 0.0 otherwise.
/// This produces a dense array suitable for standard parallel reduction.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct MaskedEnergyCopyKernel : IComputeShader
{
    /// <summary>Per-node vacuum energy values (from CPU upload).</summary>
    public readonly ReadOnlyBuffer<double> Energy;

    /// <summary>Per-node vacuum mask: 1 = vacuum, 0 = matter.</summary>
    public readonly ReadOnlyBuffer<int> VacuumMask;

    /// <summary>Output: masked energy (E[i] if vacuum, 0.0 otherwise).</summary>
    public readonly ReadWriteBuffer<double> MaskedEnergy;

    /// <summary>Output: masked energy squared for variance calculation.</summary>
    public readonly ReadWriteBuffer<double> MaskedEnergySq;

    public readonly int NodeCount;

    public MaskedEnergyCopyKernel(
        ReadOnlyBuffer<double> energy,
        ReadOnlyBuffer<int> vacuumMask,
        ReadWriteBuffer<double> maskedEnergy,
        ReadWriteBuffer<double> maskedEnergySq,
        int nodeCount)
    {
        Energy = energy;
        VacuumMask = vacuumMask;
        MaskedEnergy = maskedEnergy;
        MaskedEnergySq = maskedEnergySq;
        NodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= NodeCount) return;

        if (VacuumMask[i] == 1)
        {
            double e = Energy[i];
            MaskedEnergy[i] = e;
            MaskedEnergySq[i] = e * e;
        }
        else
        {
            MaskedEnergy[i] = 0.0;
            MaskedEnergySq[i] = 0.0;
        }
    }
}

/// <summary>
/// Counts vacuum nodes and computes min/max energy via atomics.
/// Count uses InterlockedAdd on int. Min/max use InterlockedMin/Max on scaled int.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VacuumCountMinMaxKernel : IComputeShader
{
    /// <summary>Per-node vacuum energy values.</summary>
    public readonly ReadOnlyBuffer<double> Energy;

    /// <summary>Per-node vacuum mask: 1 = vacuum, 0 = matter.</summary>
    public readonly ReadOnlyBuffer<int> VacuumMask;

    /// <summary>
    /// Output buffer [0] = vacuumCount, [1] = scaledMin, [2] = scaledMax.
    /// Initialize: [0]=0, [1]=int.MaxValue, [2]=int.MinValue.
    /// </summary>
    public readonly ReadWriteBuffer<int> Results;

    public readonly int NodeCount;

    /// <summary>Scale factor for converting double energy to int for atomics.</summary>
    public readonly double EnergyScale;

    public VacuumCountMinMaxKernel(
        ReadOnlyBuffer<double> energy,
        ReadOnlyBuffer<int> vacuumMask,
        ReadWriteBuffer<int> results,
        int nodeCount,
        double energyScale)
    {
        Energy = energy;
        VacuumMask = vacuumMask;
        Results = results;
        NodeCount = nodeCount;
        EnergyScale = energyScale;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= NodeCount) return;

        if (VacuumMask[i] != 1) return;

        // Count
        Hlsl.InterlockedAdd(ref Results[0], 1);

        // Min/Max via scaled int atomics
        int scaled = (int)(Energy[i] * EnergyScale);
        Hlsl.InterlockedMin(ref Results[1], scaled);
        Hlsl.InterlockedMax(ref Results[2], scaled);
    }
}

/// <summary>
/// Block-level parallel sum reduction for double arrays.
/// Each thread block leader accumulates a partial sum over its block.
///
/// Pattern reused from ReductionKernel.cs (SquaredNormKernel),
/// adapted for single real-valued arrays.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct BlockSumDoubleKernel : IComputeShader
{
    public readonly ReadWriteBuffer<double> Input;
    public readonly ReadWriteBuffer<double> PartialSums;
    public readonly int Length;
    public readonly int BlockSize;

    public BlockSumDoubleKernel(
        ReadWriteBuffer<double> input,
        ReadWriteBuffer<double> partialSums,
        int length,
        int blockSize)
    {
        Input = input;
        PartialSums = partialSums;
        Length = length;
        BlockSize = blockSize;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        int blockId = idx / BlockSize;
        int localId = idx % BlockSize;

        // Only first thread in each block computes the sum
        if (localId == 0)
        {
            int start = blockId * BlockSize;
            int end = start + BlockSize;
            if (end > Length) end = Length;

            double blockSum = 0.0;
            for (int i = start; i < end; i++)
            {
                blockSum += Input[i];
            }
            PartialSums[blockId] = blockSum;
        }
    }
}
