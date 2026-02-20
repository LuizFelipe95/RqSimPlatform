using ComputeSharp;

namespace RQSimulation.GPUOptimized.HawkingRadiation;

/// <summary>
/// Double-precision compute shaders for emergent Hawking radiation.
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 5: Emergent Black Hole Evaporation
/// ================================================================
/// Implements spontaneous pair creation from vacuum fluctuations
/// near regions of high curvature tension (effective horizons).
/// 
/// The probability of pair creation follows the Unruh-Hawking formula:
///   P_pair = exp(-2? * m_eff / T)
/// 
/// where T is computed from the lapse gradient: T = |?N| / (2?)
/// </summary>

/// <summary>
/// Compute Unruh temperature from lapse gradient: T = |?N| / (2?)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct UnruhTemperatureKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> lapse;
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> csrWeights;
    public readonly ReadWriteBuffer<double> temperature;
    public readonly int nodeCount;

    public UnruhTemperatureKernelDouble(
        ReadOnlyBuffer<double> lapse,
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> csrWeights,
        ReadWriteBuffer<double> temperature,
        int nodeCount)
    {
        this.lapse = lapse;
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.csrWeights = csrWeights;
        this.temperature = temperature;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        int start = csrOffsets[i];
        int end = csrOffsets[i + 1];

        double lapseNode = lapse[i];
        double gradientSum = 0.0;
        int count = 0;

        for (int k = start; k < end; k++)
        {
            int j = csrNeighbors[k];
            double lapseNeighbor = lapse[j];
            double dN = lapseNode - lapseNeighbor;
            double absN = dN < 0 ? -dN : dN;

            // Geodesic distance from edge weight
            double w = csrWeights[k];
            // Use float cast for Hlsl.Log
            double dist = w > 1e-10 ? -Hlsl.Log((float)w) : 10.0;

            // Gradient magnitude
            double localGrad = absN / (dist + 0.1);
            gradientSum += localGrad;
            count++;
        }

        if (count == 0)
        {
            temperature[i] = 0.0;
            return;
        }

        // Average gradient
        double avgGrad = gradientSum / count;

        // Unruh-Hawking temperature: T = |?N| / (2?)
        double T = avgGrad / (2.0 * 3.14159265358979323846);

        temperature[i] = T;
    }
}

/// <summary>
/// Compute pair creation probability: P = exp(-2? * m / T)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct PairProbabilityKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> temperature;
    public readonly ReadWriteBuffer<double> probability;
    public readonly double massThreshold;

    public PairProbabilityKernelDouble(
        ReadOnlyBuffer<double> temperature,
        ReadWriteBuffer<double> probability,
        double massThreshold)
    {
        this.temperature = temperature;
        this.probability = probability;
        this.massThreshold = massThreshold;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= probability.Length) return;

        double T = temperature[i];

        if (T < 1e-15)
        {
            probability[i] = 0.0;
            return;
        }

        double exponent = -2.0 * 3.14159265358979323846 * massThreshold / T;

        if (exponent < -50.0)
            probability[i] = 0.0;
        else
            // Use float cast for Hlsl.Exp
            probability[i] = Hlsl.Exp((float)exponent);
    }
}

/// <summary>
/// Stochastic pair creation based on probability (Xorshift RNG).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct StochasticPairCreationKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> probability;
    public readonly ReadWriteBuffer<int> pairCreated;
    public readonly ReadWriteBuffer<uint> rngState;

    public StochasticPairCreationKernelDouble(
        ReadOnlyBuffer<double> probability,
        ReadWriteBuffer<int> pairCreated,
        ReadWriteBuffer<uint> rngState)
    {
        this.probability = probability;
        this.pairCreated = pairCreated;
        this.rngState = rngState;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= pairCreated.Length) return;

        double p = probability[i];

        // Xorshift32 RNG
        uint state = rngState[i];
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        rngState[i] = state;

        double r = (state & 0x00FFFFFF) / 16777216.0;
        pairCreated[i] = (r < p) ? 1 : 0;
    }
}

/// <summary>
/// Horizon detachment kernel: break bonds based on curvature tension.
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 5: Emergent Black Hole Evaporation
/// Break probability P ~ exp(-1 / (R * ?))
/// High curvature R ? High probability (horizon instability)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HorizonDetachKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> ricciScalar;
    public readonly ReadOnlyBuffer<int> horizonNodes;
    public readonly ReadWriteBuffer<int> detached;
    public readonly ReadWriteBuffer<uint> rngState;
    public readonly double hBar;
    public readonly int horizonCount;

    public HorizonDetachKernelDouble(
        ReadOnlyBuffer<double> ricciScalar,
        ReadOnlyBuffer<int> horizonNodes,
        ReadWriteBuffer<int> detached,
        ReadWriteBuffer<uint> rngState,
        double hBar,
        int horizonCount)
    {
        this.ricciScalar = ricciScalar;
        this.horizonNodes = horizonNodes;
        this.detached = detached;
        this.rngState = rngState;
        this.hBar = hBar;
        this.horizonCount = horizonCount;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= horizonCount) return;

        int node = horizonNodes[idx];
        double R = ricciScalar[node];
        double absR = R < 0 ? -R : R;

        // Break probability P ~ exp(-1 / (R * ?))
        double exponent = -1.0 / (absR * hBar + 1e-9);
        // Use float cast for Hlsl.Exp
        double breakProb = Hlsl.Exp((float)exponent);

        // Xorshift32 RNG
        uint state = rngState[idx];
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        rngState[idx] = state;

        double r = (state & 0x00FFFFFF) / 16777216.0;
        detached[idx] = (r < breakProb) ? 1 : 0;
    }
}

/// <summary>
/// Backreaction: reduce edge weights when pair is created.
/// Energy conservation: curvature energy ? particle mass.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct BackreactionKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<int> pairCreated;
    public readonly ReadWriteBuffer<double> energyExtracted;
    public readonly ReadWriteBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly double pairEnergy;
    public readonly double planckThreshold;
    public readonly int nodeCount;

    public BackreactionKernelDouble(
        ReadOnlyBuffer<int> pairCreated,
        ReadWriteBuffer<double> energyExtracted,
        ReadWriteBuffer<double> edgeWeights,
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        double pairEnergy,
        double planckThreshold,
        int nodeCount)
    {
        this.pairCreated = pairCreated;
        this.energyExtracted = energyExtracted;
        this.edgeWeights = edgeWeights;
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.pairEnergy = pairEnergy;
        this.planckThreshold = planckThreshold;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        if (pairCreated[i] == 0)
        {
            energyExtracted[i] = 0.0;
            return;
        }

        int start = csrOffsets[i];
        int end = csrOffsets[i + 1];

        double totalWeight = 0.0;
        for (int k = start; k < end; k++)
            totalWeight += edgeWeights[k];

        if (totalWeight < 1e-15)
        {
            energyExtracted[i] = 0.0;
            return;
        }

        double fractionToRemove = pairEnergy / totalWeight;
        if (fractionToRemove > 0.1) fractionToRemove = 0.1;

        double extracted = 0.0;
        for (int k = start; k < end; k++)
        {
            double oldWeight = edgeWeights[k];
            double newWeight = oldWeight * (1.0 - fractionToRemove);
            if (newWeight < planckThreshold) newWeight = planckThreshold;

            extracted += oldWeight - newWeight;
            edgeWeights[k] = newWeight;
        }

        energyExtracted[i] = extracted;
    }
}
