using ComputeSharp;

namespace RQSimulation.GPUOptimized.RelationalTime;

/// <summary>
/// Double-precision compute shaders for relational time evolution.
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 1: Local Time (Lapse Function)
/// ============================================================
/// Implements N(x) ~ 1 / (1 + |R(x)|) where R is local Ricci curvature.
/// Time flows slower in regions of high curvature (gravitational time dilation).
/// </summary>

/// <summary>
/// Compute Ricci scalar at each node from edge curvatures (sum over neighbors).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeRicciScalarKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> edgeCurvatures;
    public readonly ReadWriteBuffer<double> ricciScalar;
    public readonly int nodeCount;

    public ComputeRicciScalarKernelDouble(
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> edgeCurvatures,
        ReadWriteBuffer<double> ricciScalar,
        int nodeCount)
    {
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.edgeCurvatures = edgeCurvatures;
        this.ricciScalar = ricciScalar;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        int start = csrOffsets[i];
        int end = csrOffsets[i + 1];

        double R = 0.0;
        for (int k = start; k < end; k++)
        {
            R += edgeCurvatures[k];
        }

        ricciScalar[i] = R;
    }
}

/// <summary>
/// Compute lapse function from Ricci scalar: N = 1 / (1 + Alpha * |R|)
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 1: Relativistic Lapse Function
/// ============================================================
/// In GR, lapse function N determines proper time flow: d? = N dt
/// 
/// The Alpha parameter (PhysicsConstants.LapseFunctionAlpha) controls
/// the coupling strength between curvature and time dilation.
/// 
/// High curvature (strong gravity) ? small N ? time dilation.
/// Alpha = 1.0 corresponds to natural Planck units coupling.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LapseFromRicciKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> ricciScalar;
    public readonly ReadWriteBuffer<double> lapse;
    public readonly double alpha;
    public readonly double minLapse;
    public readonly double maxLapse;

    public LapseFromRicciKernelDouble(
        ReadOnlyBuffer<double> ricciScalar,
        ReadWriteBuffer<double> lapse,
        double alpha,
        double minLapse,
        double maxLapse)
    {
        this.ricciScalar = ricciScalar;
        this.lapse = lapse;
        this.alpha = alpha;
        this.minLapse = minLapse;
        this.maxLapse = maxLapse;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= lapse.Length) return;

        double R = ricciScalar[i];
        double absR = R < 0 ? -R : R;
        
        // RQ-HYPOTHESIS: N = 1 / (1 + Alpha * |R|)
        // Alpha controls gravitational time dilation coupling strength
        double n = 1.0 / (1.0 + alpha * absR);

        // Clamp to valid range
        if (n < minLapse) n = minLapse;
        if (n > maxLapse) n = maxLapse;

        lapse[i] = n;
    }
}

/// <summary>
/// Compute local time step: localDt = baseDt * lapse[i]
/// 
/// RQ-HYPOTHESIS: Each node evolves with its own proper time step.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalTimeStepKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> lapse;
    public readonly ReadWriteBuffer<double> localDt;
    public readonly double baseDt;
    public readonly double minDt;
    public readonly double maxDt;

    public LocalTimeStepKernelDouble(
        ReadOnlyBuffer<double> lapse,
        ReadWriteBuffer<double> localDt,
        double baseDt,
        double minDt,
        double maxDt)
    {
        this.lapse = lapse;
        this.localDt = localDt;
        this.baseDt = baseDt;
        this.minDt = minDt;
        this.maxDt = maxDt;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= localDt.Length) return;

        double dt = baseDt * lapse[i];

        // Clamp to valid range
        if (dt < minDt) dt = minDt;
        if (dt > maxDt) dt = maxDt;

        localDt[i] = dt;
    }
}

/// <summary>
/// Compute entropic lapse function: N = exp(-? * S)
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 3: Entropic Time Dilation
/// High entanglement entropy ? small N ? time dilation.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EntropicLapseKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> entropy;
    public readonly ReadWriteBuffer<double> lapse;
    public readonly double alpha;
    public readonly double minLapse;
    public readonly double maxLapse;

    public EntropicLapseKernelDouble(
        ReadOnlyBuffer<double> entropy,
        ReadWriteBuffer<double> lapse,
        double alpha,
        double minLapse,
        double maxLapse)
    {
        this.entropy = entropy;
        this.lapse = lapse;
        this.alpha = alpha;
        this.minLapse = minLapse;
        this.maxLapse = maxLapse;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= lapse.Length) return;

        double s = entropy[i];
        // Use Hlsl.Exp with float cast, then back to double
        double n = Hlsl.Exp((float)(-alpha * s));

        // Clamp to valid range
        if (n < minLapse) n = minLapse;
        if (n > maxLapse) n = maxLapse;

        lapse[i] = n;
    }
}

/// <summary>
/// Compute entanglement entropy from edge weights: S = -? p_j log(p_j)
/// where p_j = w_ij / ?_k w_ik
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeEntropyKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> csrWeights;
    public readonly ReadWriteBuffer<double> entropy;
    public readonly int nodeCount;

    public ComputeEntropyKernelDouble(
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> csrWeights,
        ReadWriteBuffer<double> entropy,
        int nodeCount)
    {
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.csrWeights = csrWeights;
        this.entropy = entropy;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        int start = csrOffsets[i];
        int end = csrOffsets[i + 1];

        double totalWeight = 0.0;
        for (int k = start; k < end; k++)
        {
            totalWeight += csrWeights[k];
        }

        if (totalWeight < 1e-15)
        {
            entropy[i] = 0.0;
            return;
        }

        double s = 0.0;
        for (int k = start; k < end; k++)
        {
            double w = csrWeights[k];
            if (w < 1e-15) continue;
            double p = w / totalWeight;
            // Use Hlsl.Log with float cast
            s -= p * Hlsl.Log((float)p);
        }

        entropy[i] = s > 0.0 ? s : 0.0;
    }
}

/// <summary>
/// RQ-HYPOTHESIS CHECKLIST ITEM 1: Page-Wootters Proper Time Accumulation.
/// 
/// Accumulates proper time for each node: ?_i += localDt_i
/// 
/// Physics: In GR, each worldline has its own proper time.
/// For discrete graphs: ?_i = ? N_i dt, integrated over simulation steps.
/// Observable time emerges from correlations between node clocks.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct AccumulateNodeClocksKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> localDt;
    public readonly ReadWriteBuffer<double> nodeClocks;

    public AccumulateNodeClocksKernelDouble(
        ReadOnlyBuffer<double> localDt,
        ReadWriteBuffer<double> nodeClocks)
    {
        this.localDt = localDt;
        this.nodeClocks = nodeClocks;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeClocks.Length) return;

        // Accumulate proper time: ?_i += dt_i
        nodeClocks[i] += localDt[i];
    }
}
