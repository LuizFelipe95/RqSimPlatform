using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.BlackHole;

/// <summary>
/// GPU kernel for computing local mass from neighbor energies via CSR.
/// 
/// PHYSICS: Mass at node i = ?_j w_ij * |?_j|?
/// This is a parallel reduction over CSR neighbors.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct LocalMassKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> rowOffsets;
    public readonly ReadOnlyBuffer<int> colIndices;
    public readonly ReadOnlyBuffer<double> edgeWeights;
    public readonly ReadOnlyBuffer<double> nodeEnergies;
    public readonly ReadWriteBuffer<double> localMass;
    public readonly double selfMassFactor;
    public readonly int nodeCount;

    public LocalMassKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeWeights,
        ReadOnlyBuffer<double> nodeEnergies,
        ReadWriteBuffer<double> localMass,
        double selfMassFactor,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeWeights = edgeWeights;
        this.nodeEnergies = nodeEnergies;
        this.localMass = localMass;
        this.selfMassFactor = selfMassFactor;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        // Start with self contribution
        double mass = selfMassFactor * nodeEnergies[node];

        // Sum neighbor contributions via CSR
        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];

        for (int k = rowStart; k < rowEnd; k++)
        {
            int neighbor = colIndices[k];
            double weight = edgeWeights[k];
            mass += weight * nodeEnergies[neighbor];
        }

        localMass[node] = mass;
    }
}

/// <summary>
/// GPU kernel for computing effective radius from edge lengths.
/// Uses precomputed edge distances instead of log in shader.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct EffectiveRadiusKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<int> rowOffsets;
    public readonly ReadOnlyBuffer<int> colIndices;
    public readonly ReadOnlyBuffer<double> edgeDistances;
    public readonly ReadWriteBuffer<double> effectiveRadius;
    public readonly double maxDistance;
    public readonly int nodeCount;

    public EffectiveRadiusKernel(
        ReadOnlyBuffer<int> rowOffsets,
        ReadOnlyBuffer<int> colIndices,
        ReadOnlyBuffer<double> edgeDistances,
        ReadWriteBuffer<double> effectiveRadius,
        double maxDistance,
        int nodeCount)
    {
        this.rowOffsets = rowOffsets;
        this.colIndices = colIndices;
        this.edgeDistances = edgeDistances;
        this.effectiveRadius = effectiveRadius;
        this.maxDistance = maxDistance;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        int rowStart = rowOffsets[node];
        int rowEnd = rowOffsets[node + 1];
        int degree = rowEnd - rowStart;

        if (degree == 0)
        {
            effectiveRadius[node] = maxDistance;
            return;
        }

        double sumDistance = 0.0;
        for (int k = rowStart; k < rowEnd; k++)
        {
            sumDistance += edgeDistances[k];
        }

        effectiveRadius[node] = sumDistance / degree;
    }
}

/// <summary>
/// GPU kernel for horizon detection using Schwarzschild criterion.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HorizonDetectionKernel : IComputeShader
{
    public readonly ReadOnlyBuffer<double> localMass;
    public readonly ReadOnlyBuffer<double> effectiveRadius;
    public readonly ReadWriteBuffer<int> horizonFlags;
    public readonly ReadWriteBuffer<double> schwarzschildRadius;
    public readonly ReadWriteBuffer<double> density;
    public readonly ReadWriteBuffer<double> hawkingTemperature;
    public readonly ReadWriteBuffer<double> entropy;
    public readonly double densityThreshold;
    public readonly double minMassThreshold;
    public readonly int nodeCount;

    public HorizonDetectionKernel(
        ReadOnlyBuffer<double> localMass,
        ReadOnlyBuffer<double> effectiveRadius,
        ReadWriteBuffer<int> horizonFlags,
        ReadWriteBuffer<double> schwarzschildRadius,
        ReadWriteBuffer<double> density,
        ReadWriteBuffer<double> hawkingTemperature,
        ReadWriteBuffer<double> entropy,
        double densityThreshold,
        double minMassThreshold,
        int nodeCount)
    {
        this.localMass = localMass;
        this.effectiveRadius = effectiveRadius;
        this.horizonFlags = horizonFlags;
        this.schwarzschildRadius = schwarzschildRadius;
        this.density = density;
        this.hawkingTemperature = hawkingTemperature;
        this.entropy = entropy;
        this.densityThreshold = densityThreshold;
        this.minMassThreshold = minMassThreshold;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        double mass = localMass[node];
        double radius = effectiveRadius[node];

        // Schwarzschild radius: r_s = 2M
        double rs = 2.0 * mass;
        schwarzschildRadius[node] = rs;

        // Density: ? = M / r (protecting against r = 0)
        double rho = (radius > 1e-15) ? mass / radius : 0.0;
        density[node] = rho;

        // Initialize flags
        int flags = 0;

        // Skip if mass below threshold
        if (mass < minMassThreshold)
        {
            horizonFlags[node] = 0;
            hawkingTemperature[node] = 0.0;
            entropy[node] = 0.0;
            return;
        }

        // Check horizon condition: r_eff ? r_s
        bool isCollapsed = (radius > 0) && (radius <= rs);
        
        // Check density threshold (alternative criterion)
        bool isHighDensity = rho >= densityThreshold;

        if (isCollapsed || isHighDensity)
        {
            flags |= 1; // IsHorizon

            // Check if fully collapsed (singularity)
            if (radius < 0.1 * rs && mass > minMassThreshold * 10.0)
            {
                flags |= 2; // IsSingularity
            }

            // Trapped surface (light cannot escape)
            flags |= 4; // IsTrapped
        }

        horizonFlags[node] = flags;

        // Hawking temperature: T_H = 1/(8?M)
        // Higher temperature for smaller black holes
        double temp = 0.0;
        if (mass > minMassThreshold)
        {
            const double eightPi = 8.0 * 3.14159265358979323846;
            temp = 1.0 / (eightPi * mass);
            
            // Mark as evaporating if temperature is high
            if (temp > 0.1 && (flags & 1) != 0)
            {
                horizonFlags[node] = flags | 8; // IsEvaporating
            }
        }
        hawkingTemperature[node] = temp;

        // Bekenstein-Hawking entropy: S = 4?M?
        const double fourPi = 4.0 * 3.14159265358979323846;
        entropy[node] = fourPi * mass * mass;
    }
}

/// <summary>
/// GPU kernel for Hawking radiation mass evaporation.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HawkingEvaporationKernel : IComputeShader
{
    public readonly ReadWriteBuffer<double> mass;
    public readonly ReadOnlyBuffer<double> hawkingTemperature;
    public readonly ReadOnlyBuffer<int> horizonFlags;
    public readonly double evaporationConstant;
    public readonly double dt;
    public readonly int nodeCount;

    public HawkingEvaporationKernel(
        ReadWriteBuffer<double> mass,
        ReadOnlyBuffer<double> hawkingTemperature,
        ReadOnlyBuffer<int> horizonFlags,
        double evaporationConstant,
        double dt,
        int nodeCount)
    {
        this.mass = mass;
        this.hawkingTemperature = hawkingTemperature;
        this.horizonFlags = horizonFlags;
        this.evaporationConstant = evaporationConstant;
        this.dt = dt;
        this.nodeCount = nodeCount;
    }

    public void Execute()
    {
        int node = ThreadIds.X;
        if (node >= nodeCount) return;

        // Only evaporate nodes with IsEvaporating flag
        int flags = horizonFlags[node];
        if ((flags & 8) == 0) return;

        double temp = hawkingTemperature[node];
        double currentMass = mass[node];

        // Stefan-Boltzmann: dM/dt ~ -T^4
        double temp4 = temp * temp * temp * temp;
        double dM = evaporationConstant * temp4 * dt;

        // Update mass (clamp to non-negative)
        double newMass = currentMass - dM;
        mass[node] = (newMass > 0.0) ? newMass : 0.0;
    }
}
