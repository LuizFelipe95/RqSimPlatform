using ComputeSharp;

namespace RQSimulation.GPUOptimized.Constraint;

/// <summary>
/// RQG-HYPOTHESIS: Hamiltonian Constraint Evaluation Shaders
/// 
/// Implements the Wheeler-DeWitt Hamiltonian constraint:
/// H_i = (K?_ij - K? + R_ij) + T??
/// 
/// where:
/// - K_ij = extrinsic curvature tensor (graph analog: edge curvature)
/// - K = trace of extrinsic curvature (mean curvature)
/// - R_ij = Ricci curvature (derived from Ollivier-Ricci or Forman)
/// - T?? = stress-energy tensor (matter contribution from mass)
/// 
/// PHYSICS:
/// In Wheeler-DeWitt formalism, physical states satisfy H|?? = 0.
/// The Hamiltonian constraint generates diffeomorphisms (gauge symmetry).
/// Non-zero H_i indicates constraint violation - either unphysical state
/// or presence of matter/energy.
/// 
/// RQG-HYPOTHESIS CONNECTION:
/// The Lapse function N_i = 1/(1 + ?|H_i|) is computed from H_i.
/// Time stops (N?0) where H?? (singularity censorship).
/// </summary>

/// <summary>
/// Full Hamiltonian constraint evaluation kernel.
/// Computes H_i = (R - K?) + T?? at each node.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HamiltonianEvalShaderDouble : IComputeShader
{
    /// <summary>Mean curvature K at each node (trace of extrinsic curvature)</summary>
    public readonly ReadOnlyBuffer<double> meanCurvature;
    
    /// <summary>Ricci scalar R at each node</summary>
    public readonly ReadOnlyBuffer<double> ricciScalar;
    
    /// <summary>Stress-energy T?? at each node (matter contribution)</summary>
    public readonly ReadOnlyBuffer<double> stressEnergy;
    
    /// <summary>Output: Hamiltonian constraint violation H_i</summary>
    public readonly ReadWriteBuffer<double> hamiltonianViolations;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public HamiltonianEvalShaderDouble(
        ReadOnlyBuffer<double> meanCurvature,
        ReadOnlyBuffer<double> ricciScalar,
        ReadOnlyBuffer<double> stressEnergy,
        ReadWriteBuffer<double> hamiltonianViolations,
        int nodeCount)
    {
        this.meanCurvature = meanCurvature;
        this.ricciScalar = ricciScalar;
        this.stressEnergy = stressEnergy;
        this.hamiltonianViolations = hamiltonianViolations;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double K = meanCurvature[i];
        double k_sq = K * K;
        double ricci = ricciScalar[i];
        double matter = stressEnergy[i];
        
        // Wheeler-DeWitt Hamiltonian constraint:
        // H = (R - K?) + T??
        // Physical states satisfy H ? 0
        hamiltonianViolations[i] = (ricci - k_sq) + matter;
    }
}

/// <summary>
/// Simplified Hamiltonian from graph topology.
/// Uses degree-based curvature proxy when full Ricci is not available.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct HamiltonianFromDegreeShaderDouble : IComputeShader
{
    /// <summary>CSR row pointers for degree computation</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Node masses (matter contribution)</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Average degree (precomputed)</summary>
    public readonly double avgDegree;
    
    /// <summary>Gravitational coupling ?</summary>
    public readonly double kappa;
    
    /// <summary>Output: Hamiltonian constraint H_i</summary>
    public readonly ReadWriteBuffer<double> hamiltonianViolations;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public HamiltonianFromDegreeShaderDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> masses,
        double avgDegree,
        double kappa,
        ReadWriteBuffer<double> hamiltonianViolations,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.masses = masses;
        this.avgDegree = avgDegree;
        this.kappa = kappa;
        this.hamiltonianViolations = hamiltonianViolations;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        // Degree as curvature proxy
        int deg = rowPtr[i + 1] - rowPtr[i];
        
        // Scalar curvature from degree: R = (k - ?k?) / ?k?
        double R_i = avgDegree > 1e-10 ? (deg - avgDegree) / avgDegree : 0.0;
        
        // Matter contribution: T?? ? mass
        double T_00 = masses[i];
        
        // Hamiltonian: H = R - ?·T??
        // (simplified from full Wheeler-DeWitt)
        hamiltonianViolations[i] = R_i - kappa * T_00;
    }
}

/// <summary>
/// Compute extrinsic curvature K from edge weight rates.
/// K_ij = ?w_ij/?t (time derivative of edge weight)
/// 
/// RQG-HYPOTHESIS: In discrete setting, K tracks how topology changes.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ExtrinsicCurvatureShaderDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Current edge weights</summary>
    public readonly ReadOnlyBuffer<double> currentWeights;
    
    /// <summary>Previous edge weights (from last step)</summary>
    public readonly ReadOnlyBuffer<double> previousWeights;
    
    /// <summary>Time step dt</summary>
    public readonly double dt;
    
    /// <summary>Output: Mean curvature K_i at each node</summary>
    public readonly ReadWriteBuffer<double> meanCurvature;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public ExtrinsicCurvatureShaderDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> currentWeights,
        ReadOnlyBuffer<double> previousWeights,
        double dt,
        ReadWriteBuffer<double> meanCurvature,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.currentWeights = currentWeights;
        this.previousWeights = previousWeights;
        this.dt = dt;
        this.meanCurvature = meanCurvature;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double sumK = 0.0;
        int count = 0;
        
        for (int k = start; k < end; k++)
        {
            double w_curr = currentWeights[k];
            double w_prev = previousWeights[k];
            
            // K_ij = dw/dt
            double k_ij = dt > 1e-10 ? (w_curr - w_prev) / dt : 0.0;
            sumK += k_ij;
            count++;
        }
        
        // Mean curvature: K = (1/n) ? K_ij
        meanCurvature[i] = count > 0 ? sumK / count : 0.0;
    }
}

/// <summary>
/// Compute Ricci scalar from Ollivier-Ricci edge curvatures.
/// R_i = ?_j ?(i,j) where ? is Ollivier-Ricci curvature.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct RicciScalarFromEdgesShaderDouble : IComputeShader
{
    /// <summary>CSR row pointers</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>Edge curvatures (Ollivier-Ricci)</summary>
    public readonly ReadOnlyBuffer<double> edgeCurvatures;
    
    /// <summary>Output: Ricci scalar at each node</summary>
    public readonly ReadWriteBuffer<double> ricciScalar;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public RicciScalarFromEdgesShaderDouble(
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> edgeCurvatures,
        ReadWriteBuffer<double> ricciScalar,
        int nodeCount)
    {
        this.rowPtr = rowPtr;
        this.edgeCurvatures = edgeCurvatures;
        this.ricciScalar = ricciScalar;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double R = 0.0;
        for (int k = start; k < end; k++)
        {
            R += edgeCurvatures[k];
        }
        
        ricciScalar[i] = R;
    }
}

/// <summary>
/// Compute stress-energy tensor T?? from matter fields.
/// T?? = ? + p where ? is energy density and p is pressure.
/// 
/// In RQG discrete setting: T?? ? correlation mass.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct StressEnergyShaderDouble : IComputeShader
{
    /// <summary>Node masses (energy density proxy)</summary>
    public readonly ReadOnlyBuffer<double> masses;
    
    /// <summary>Node potentials (interaction contribution)</summary>
    public readonly ReadOnlyBuffer<double> potentials;
    
    /// <summary>CSR row pointers for neighbor counting</summary>
    public readonly ReadOnlyBuffer<int> rowPtr;
    
    /// <summary>CSR edge weights</summary>
    public readonly ReadOnlyBuffer<double> weights;
    
    /// <summary>Output: Stress-energy T??</summary>
    public readonly ReadWriteBuffer<double> stressEnergy;
    
    /// <summary>Number of nodes</summary>
    public readonly int nodeCount;
    
    public StressEnergyShaderDouble(
        ReadOnlyBuffer<double> masses,
        ReadOnlyBuffer<double> potentials,
        ReadOnlyBuffer<int> rowPtr,
        ReadOnlyBuffer<double> weights,
        ReadWriteBuffer<double> stressEnergy,
        int nodeCount)
    {
        this.masses = masses;
        this.potentials = potentials;
        this.rowPtr = rowPtr;
        this.weights = weights;
        this.stressEnergy = stressEnergy;
        this.nodeCount = nodeCount;
    }
    
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= nodeCount) return;
        
        double mass = masses[i];
        double potential = potentials[i];
        
        // Kinetic energy proxy: sum of weighted connections
        int start = rowPtr[i];
        int end = rowPtr[i + 1];
        
        double kinetic = 0.0;
        for (int k = start; k < end; k++)
        {
            kinetic += weights[k];
        }
        
        // T?? = ? = mass + kinetic + potential
        stressEnergy[i] = mass + 0.5 * kinetic + potential;
    }
}
