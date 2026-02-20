using ComputeSharp;

namespace RQSimulation.GPUOptimized.KleinGordon;

/// <summary>
/// Double-precision compute shaders for Klein-Gordon field evolution.
/// 
/// RQ-HYPOTHESIS CHECKLIST ITEM 2: Relativistic Scalar Field
/// ==========================================================
/// Discrete Klein-Gordon equation: (d?/dt? - Laplacian + m?) ? = 0
/// Ensures finite light cone and causality.
/// Uses Verlet integration for 2nd-order wave equation.
/// </summary>

/// <summary>
/// Compute graph Laplacian: ??_i = ?_j w_ij (?_i - ?_j)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct GraphLaplacianKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<int> csrOffsets;
    public readonly ReadOnlyBuffer<int> csrNeighbors;
    public readonly ReadOnlyBuffer<double> csrWeights;
    public readonly ReadOnlyBuffer<double> field;
    public readonly ReadWriteBuffer<double> laplacian;
    public readonly int nodeCount;

    public GraphLaplacianKernelDouble(
        ReadOnlyBuffer<int> csrOffsets,
        ReadOnlyBuffer<int> csrNeighbors,
        ReadOnlyBuffer<double> csrWeights,
        ReadOnlyBuffer<double> field,
        ReadWriteBuffer<double> laplacian,
        int nodeCount)
    {
        this.csrOffsets = csrOffsets;
        this.csrNeighbors = csrNeighbors;
        this.csrWeights = csrWeights;
        this.field = field;
        this.laplacian = laplacian;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        int start = csrOffsets[i];
        int end = csrOffsets[i + 1];

        double phi_i = field[i];
        double lap = 0.0;

        for (int k = start; k < end; k++)
        {
            int j = csrNeighbors[k];
            double w_ij = csrWeights[k];
            double phi_j = field[j];
            lap += w_ij * (phi_i - phi_j);
        }

        laplacian[i] = lap;
    }
}

/// <summary>
/// Klein-Gordon Verlet integration step.
/// 
/// ?_next = 2?_current - ?_prev - dt? * (Laplacian + m? * ?_current)
/// 
/// Uses local time step from relational time engine.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct KleinGordonVerletKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> phiCurrent;
    public readonly ReadOnlyBuffer<double> phiPrev;
    public readonly ReadWriteBuffer<double> phiNext;
    public readonly ReadOnlyBuffer<double> laplacian;
    public readonly ReadOnlyBuffer<double> mass;
    public readonly ReadOnlyBuffer<double> localDt;
    public readonly int nodeCount;

    public KleinGordonVerletKernelDouble(
        ReadOnlyBuffer<double> phiCurrent,
        ReadOnlyBuffer<double> phiPrev,
        ReadWriteBuffer<double> phiNext,
        ReadOnlyBuffer<double> laplacian,
        ReadOnlyBuffer<double> mass,
        ReadOnlyBuffer<double> localDt,
        int nodeCount)
    {
        this.phiCurrent = phiCurrent;
        this.phiPrev = phiPrev;
        this.phiNext = phiNext;
        this.laplacian = laplacian;
        this.mass = mass;
        this.localDt = localDt;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        double phi_curr = phiCurrent[i];
        double phi_old = phiPrev[i];
        double lap = laplacian[i];
        double m = mass[i];
        double dt = localDt[i];

        // Klein-Gordon: d??/dt? = ?? - m??
        // Verlet: ?_new = 2? - ?_old + dt? * acceleration
        double massTerm = m * m * phi_curr;
        double acceleration = -lap - massTerm; // Note: laplacian sign convention
        double phi_new = 2.0 * phi_curr - phi_old + dt * dt * acceleration;

        phiNext[i] = phi_new;
    }
}

/// <summary>
/// Swap buffers: copy phiCurrent to phiPrev, phiNext to phiCurrent
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct SwapBuffersKernelDouble : IComputeShader
{
    public readonly ReadWriteBuffer<double> phiCurrent;
    public readonly ReadWriteBuffer<double> phiPrev;
    public readonly ReadOnlyBuffer<double> phiNext;
    public readonly int nodeCount;

    public SwapBuffersKernelDouble(
        ReadWriteBuffer<double> phiCurrent,
        ReadWriteBuffer<double> phiPrev,
        ReadOnlyBuffer<double> phiNext,
        int nodeCount)
    {
        this.phiCurrent = phiCurrent;
        this.phiPrev = phiPrev;
        this.phiNext = phiNext;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        // Swap: prev ? current, current ? next
        phiPrev[i] = phiCurrent[i];
        phiCurrent[i] = phiNext[i];
    }
}

/// <summary>
/// Compute field energy: E = ?_i [?(??/?t)? + ?(??)? + ?m???]
/// Uses finite difference for time derivative.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct FieldEnergyKernelDouble : IComputeShader
{
    public readonly ReadOnlyBuffer<double> phiCurrent;
    public readonly ReadOnlyBuffer<double> phiPrev;
    public readonly ReadOnlyBuffer<double> laplacian;
    public readonly ReadOnlyBuffer<double> mass;
    public readonly ReadOnlyBuffer<double> localDt;
    public readonly ReadWriteBuffer<double> energy;
    public readonly int nodeCount;

    public FieldEnergyKernelDouble(
        ReadOnlyBuffer<double> phiCurrent,
        ReadOnlyBuffer<double> phiPrev,
        ReadOnlyBuffer<double> laplacian,
        ReadOnlyBuffer<double> mass,
        ReadOnlyBuffer<double> localDt,
        ReadWriteBuffer<double> energy,
        int nodeCount)
    {
        this.phiCurrent = phiCurrent;
        this.phiPrev = phiPrev;
        this.laplacian = laplacian;
        this.mass = mass;
        this.localDt = localDt;
        this.energy = energy;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;

        double phi = phiCurrent[i];
        double phi_old = phiPrev[i];
        double dt = localDt[i];
        double m = mass[i];
        double lap = laplacian[i];

        // Time derivative (finite difference)
        double dPhiDt = (phi - phi_old) / (dt + 1e-15);

        // Kinetic: ?(??/?t)?
        double kinetic = 0.5 * dPhiDt * dPhiDt;

        // Gradient: ?(??)? ? ? ? * Laplacian(?) (using integration by parts)
        double gradient = 0.5 * phi * lap;

        // Mass term: ?m???
        double massTerm = 0.5 * m * m * phi * phi;

        energy[i] = kinetic + gradient + massTerm;
    }
}
