using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using RQSimulation.Physics;
using RQSimulation.GPUOptimized;

namespace RQSimulation;

/// <summary>
/// Unified mass model integration for RQGraph.
/// 
/// CHECKLIST ITEM 6: Integrate NodeMassModel into gravity evolution.
/// 
/// This provides:
/// - NodeMassModel array storing all mass contributions per node
/// - UpdateNodeMasses() to aggregate all fields into unified mass
/// - Integration with EvolveNetworkGeometry via TotalMass
/// 
/// Physics: In Einstein's equations, the stress-energy tensor T_?? is the
/// source of gravity. Here, NodeMassModel.TotalMass represents T_00 (energy density),
/// which determines how strongly each node curves spacetime.
/// 
/// The mass contributions are:
/// - FermionMass: from Dirac spinor field ?†?
/// - CorrelationMass: from graph connectivity structure
/// - GaugeFieldEnergy: from Yang-Mills F^2 terms
/// - ScalarFieldEnergy: from Higgs-like ? field
/// - VacuumEnergy: cosmological constant term
/// - KineticEnergy: from field momenta
/// </summary>
public partial class RQGraph
{
    // ================================================================
    // NODE MASS MODEL STORAGE
    // ================================================================
    
    /// <summary>
    /// Unified mass model for each node.
    /// Contains all contributions to the node's mass/energy.
    /// </summary>
    private NodeMassModel[]? _nodeMassModels;
    
    /// <summary>
    /// Check if node mass models are initialized.
    /// </summary>
    public bool HasNodeMassModels => _nodeMassModels != null && _nodeMassModels.Length == N;
    
    /// <summary>
    /// Public accessor for node mass models.
    /// </summary>
    public NodeMassModel[]? NodeMassModels => _nodeMassModels;
    
    /// <summary>
    /// Get total mass at a node (unified mass model).
    /// Falls back to correlation mass if models not initialized.
    /// </summary>
    public double GetNodeTotalMass(int i)
    {
        if (_nodeMassModels != null && i >= 0 && i < _nodeMassModels.Length)
            return _nodeMassModels[i].TotalMass;
        
        // Fallback to correlation mass
        if (_correlationMass != null && i >= 0 && i < _correlationMass.Length)
            return _correlationMass[i];
        
        return 0.0;
    }
    
    // ================================================================
    // INITIALIZATION
    // ================================================================
    
    /// <summary>
    /// Initialize node mass models array.
    /// Must be called before UpdateNodeMasses().
    /// </summary>
    public void InitNodeMassModels()
    {
        _nodeMassModels = new NodeMassModel[N];
        for (int i = 0; i < N; i++)
        {
            _nodeMassModels[i] = new NodeMassModel();
        }
    }
    
    // ================================================================
    // MASS AGGREGATION
    // ================================================================
    
    /// <summary>
    /// Update all node mass models by aggregating contributions from all fields.
    /// 
    /// CHECKLIST ITEM 6: Unified mass computation for Einstein coupling.
    /// 
    /// Call this once per physics step BEFORE EvolveNetworkGeometry().
    /// The resulting TotalMass values are used as the source term for gravity.
    /// 
    /// Aggregates:
    /// 1. FermionMass: from Dirac spinor density ?†?
    /// 2. CorrelationMass: from graph connectivity (existing _correlationMass)
    /// 3. GaugeFieldEnergy: from Yang-Mills field strength F?
    /// 4. ScalarFieldEnergy: from scalar field ? gradient and potential
    /// 5. VacuumEnergy: constant per node (cosmological constant)
    /// 6. KineticEnergy: from geometry momenta (gravitational waves)
    /// </summary>
    public void UpdateNodeMasses()
    {
        if (_nodeMassModels == null)
            InitNodeMassModels();
        
        // Ensure correlation mass is computed
        if (_correlationMass == null || _correlationMass.Length != N)
            _correlationMass = ComputePerNodeCorrelationMass();
        
        // Compute vacuum energy per node
        double vacuumEnergyPerNode = PhysicsConstants.CosmologicalConstant * GetAverageWeight();
        
        Parallel.For(0, N, i =>
        {
            var model = _nodeMassModels![i];
            model.Reset();
            
            // 1. FERMION MASS: from Dirac spinor field
            // m_f = ?†? * coupling
            if (_spinorA != null)
            {
                double spinorDensity = SpinorDensity(i);
                model.FermionMass = spinorDensity * PhysicsConstants.DiracCoupling;
            }
            
            // Color spinor contribution (if using SU(3))
            if (_colorSpinorField != null && i < _colorSpinorField.Length)
            {
                double colorDensity = _colorSpinorField[i].FermionDensity;
                model.FermionMass += colorDensity * PhysicsConstants.DiracCoupling;
            }
            
            // 2. CORRELATION MASS: from graph structure
            // This is the "geometric" mass from connectivity
            model.CorrelationMass = _correlationMass![i];
            
            // 3. GAUGE FIELD ENERGY: from Yang-Mills F? terms
            // E_gauge = (1/4) ?_a F^a_?? F^a^??
            model.GaugeFieldEnergy = ComputeLocalGaugeFieldEnergy(i);
            
            // 4. SCALAR FIELD ENERGY: from ? field
            // E_scalar = (1/2)(??)? + V(?)
            if (ScalarField != null && i < ScalarField.Length)
            {
                double phi = ScalarField[i];
                double gradientEnergy = ComputeLocalScalarGradientEnergy(i);
                double potentialEnergy = ComputeHiggsPotential(phi);
                model.ScalarFieldEnergy = gradientEnergy + potentialEnergy;
            }
            
            // 5. VACUUM ENERGY: cosmological constant contribution
            model.VacuumEnergy = vacuumEnergyPerNode;
            
            // 6. KINETIC ENERGY: from geometry momenta (gravitational waves)
            if (_geometryMomenta != null)
            {
                double kineticSum = 0.0;
                foreach (int j in Neighbors(i))
                {
                    double p_ij = _geometryMomenta[i, j];
                    kineticSum += 0.5 * p_ij * p_ij / PhysicsConstants.GeometryMomentumMass;
                }
                // Distribute kinetic energy to both endpoints
                model.KineticEnergy = kineticSum * 0.5; // Half goes to each node
            }
        });
    }
    
    /// <summary>
    /// Compute local gauge field energy at a node.
    /// Sums contributions from U(1), SU(2), SU(3) gauge fields.
    /// </summary>
    private double ComputeLocalGaugeFieldEnergy(int node)
    {
        double energy = 0.0;
        
        // U(1) electromagnetic contribution
        if (_edgePhaseU1 != null)
        {
            foreach (int j in Neighbors(node))
            {
                double phase = _edgePhaseU1[node, j];
                // E? term from phase gradient
                energy += 0.5 * phase * phase;
            }
        }
        
        // SU(3) gluon field contribution
        if (_gluonField != null)
        {
            foreach (int j in Neighbors(node))
            {
                for (int a = 0; a < 8; a++)
                {
                    double A = _gluonField[node, j, a];
                    energy += 0.5 * A * A;
                }
            }
        }
        
        // SU(2) weak field contribution
        if (_weakField != null)
        {
            foreach (int j in Neighbors(node))
            {
                for (int a = 0; a < 3; a++)
                {
                    double W = _weakField[node, j, a];
                    energy += 0.5 * W * W;
                }
            }
        }
        
        return energy * PhysicsConstants.GaugeFieldEnergyWeight;
    }
    
    /// <summary>
    /// Compute scalar field gradient energy at a node.
    /// E_grad = (1/2) ?_j w_ij (?_i - ?_j)?
    /// </summary>
    private double ComputeLocalScalarGradientEnergy(int node)
    {
        if (ScalarField == null || node >= ScalarField.Length)
            return 0.0;
        
        double phi_i = ScalarField[node];
        double gradientEnergy = 0.0;
        
        foreach (int j in Neighbors(node))
        {
            if (j >= ScalarField.Length) continue;
            
            double phi_j = ScalarField[j];
            double diff = phi_i - phi_j;
            double weight = Weights[node, j];
            gradientEnergy += 0.5 * weight * diff * diff;
        }
        
        return gradientEnergy * PhysicsConstants.ScalarFieldEnergyWeight;
    }
    
    /// <summary>
    /// Compute Higgs potential V(?) = -????/2 + ???/4
    /// </summary>
    private double ComputeHiggsPotential(double phi)
    {
        double mu2 = PhysicsConstants.HiggsMuSquared;
        double lambda = PhysicsConstants.HiggsLambda;
        
        return -0.5 * mu2 * phi * phi + 0.25 * lambda * Math.Pow(phi, 4);
    }
    
    /// <summary>
    /// Get average edge weight (used for vacuum energy scaling).
    /// </summary>
    private double GetAverageWeight()
    {
        double sum = 0.0;
        int count = 0;
        
        for (int i = 0; i < N; i++)
        {
            foreach (int j in Neighbors(i))
            {
                if (j > i)
                {
                    sum += Weights[i, j];
                    count++;
                }
            }
        }
        
        return count > 0 ? sum / count : 0.5;
    }
    
    // ================================================================
    // IMPROVED GRAVITY EVOLUTION USING TOTAL MASS
    // ================================================================
    
    /// <summary>
    /// Evolve network geometry using unified NodeMassModel.
    /// 
    /// CHECKLIST ITEM 6: Use TotalMass as source for Einstein equations.
    /// 
    /// This replaces the simple correlation mass with the full stress-energy
    /// contribution from all fields (fermions, gauge, scalar, vacuum).
    /// 
    /// Einstein-like equation on graph:
    ///   dw_ij/dt = -G * (R_ij - T_ij/2 + ?)
    /// 
    /// where:
    ///   R_ij = Ricci curvature on edge
    ///   T_ij = (M_i + M_j)/2 = average total mass at endpoints
    ///   ? = cosmological constant
    /// 
    /// RQ-HYPOTHESIS CHECKLIST FIX #2: Lapse-Synchronized Geometry
    /// ============================================================
    /// When EnableLapseSynchronizedGeometry is true:
    ///   dt_edge(i,j) = dt_global ? ?(N_i ? N_j)
    /// 
    /// This ensures geometry evolves at the LOCAL proper time rate.
    /// Near "black holes" (high-entropy nodes with N ? 0), both matter
    /// AND geometry freeze together, maintaining covariance.
    /// 
    /// Physics: In GR, Einstein's equations G_?? = 8?G T_?? are tensor
    /// equations that hold in ALL reference frames. The lapse function N
    /// enters both the matter evolution AND the ADM evolution of 3-geometry.
    /// Without this synchronization, geometry can "run ahead" of matter.
    /// </summary>
    /// <param name="dt">Time step</param>
    public void EvolveNetworkGeometryUnified(double dt)
    {
        if (Weights == null || Edges == null)
            return;
        
        // Update mass models if not done this step
        if (_nodeMassModels == null)
            UpdateNodeMasses();
        
        // RQ-FIX #2: Ensure lapse functions are up to date for synchronized evolution
        if (PhysicsConstants.EnableLapseSynchronizedGeometry && _lapseFunction == null)
        {
            UpdateLapseFunctions();
        }
        
        // Use GPU if available
        if (GpuGravity != null && GpuGravity.IsTopologyInitialized)
        {
            // Get unified masses instead of just correlation mass
            float[] masses = GetUnifiedMassesFlat();
            float[] weights = GetAllWeightsFlat();
            int[] edgesFrom = FlatEdgesFrom;
            int[] edgesTo = FlatEdgesTo;
            
            GpuGravity.EvolveFullGpuStep(
                weights,
                masses,
                edgesFrom,
                edgesTo,
                (float)dt,
                (float)PhysicsConstants.GravitationalCoupling,
                (float)PhysicsConstants.CosmologicalConstant
            );
            
            UpdateWeightsFromFlat(weights);
            UpdateTargetDistancesFromWeights();
            return;
        }
        
        // CPU fallback with unified mass
        double[,] deltaWeights = new double[N, N];
        double baseLearningRate = PhysicsConstants.GravitationalCoupling * dt;
        
        Parallel.For(0, N, i =>
        {
            double massI = GetNodeTotalMass(i);
            
            // RQ-FIX #2: Get lapse function for node i
            double lapseI = PhysicsConstants.EnableLapseSynchronizedGeometry 
                ? GetLocalLapse(i) 
                : 1.0;
            
            foreach (int j in Neighbors(i))
            {
                if (j <= i) continue;
                
                double massJ = GetNodeTotalMass(j);
                
                // RQ-HYPOTHESIS CHECKLIST FIX #2: Lapse-Synchronized Geometry
                // ============================================================
                // The learning rate (effective dt) for edge (i,j) is scaled by
                // the geometric mean of lapse functions at both endpoints:
                //   dt_edge = dt_global ? ?(N_i ? N_j)
                // 
                // This ensures that geometry near high-entropy (massive) regions
                // evolves slowly, synchronizing with matter evolution.
                // 
                // Physics: In ADM formalism, the lapse N appears in both:
                //   ?g_ij/?t = -2N K_ij + ...  (geometry)
                //   ??/?t = N ? (...)          (matter)
                // So both must use the same local time scale.
                double edgeLapse = PhysicsConstants.EnableLapseSynchronizedGeometry
                    ? Math.Sqrt(lapseI * GetLocalLapse(j))
                    : 1.0;
                
                double learningRate = baseLearningRate * edgeLapse;
                
                // Curvature term (prefer OllivierRicci if available)
                double curvature;
                if (PhysicsConstants.PreferOllivierRicciCurvature)
                    curvature = OllivierRicciCurvature.ComputeOllivierRicciSinkhorn(this, i, j);
                else
                    curvature = ComputeFormanRicciCurvature(i, j);
                
                // Stress-energy from unified mass model
                double stressEnergy = (massI + massJ) * 0.5 * PhysicsConstants.CurvatureTermScale;
                
                // Cosmological constant
                double lambda = PhysicsConstants.CosmologicalConstant;
                
                // Einstein equation: dw/dt = -G * (R - T + ?)
                double dS = curvature - stressEnergy + lambda;
                double delta = -learningRate * dS;
                
                deltaWeights[i, j] = delta;
                deltaWeights[j, i] = delta;
            }
        });
        
        // Apply updates with soft walls
        Parallel.For(0, N, i =>
        {
            foreach (int j in Neighbors(i))
            {
                if (j <= i) continue;
                
                double newWeight = Weights[i, j] + deltaWeights[i, j];
                // RQ-FIX: No soft walls - pure gradient flow
                if (newWeight < 0) newWeight = 0;
                
                Weights[i, j] = newWeight;
                Weights[j, i] = newWeight;
            }
        });
        
        UpdateTargetDistancesFromWeights();
    }
    
    /// <summary>
    /// Get unified masses (TotalMass from NodeMassModel) as flat array for GPU.
    /// </summary>
    public float[] GetUnifiedMassesFlat()
    {
        // Ensure mass models are updated
        if (_nodeMassModels == null)
            UpdateNodeMasses();
        
        float[] masses = new float[N];
        for (int i = 0; i < N; i++)
        {
            masses[i] = (float)_nodeMassModels![i].TotalMass;
        }
        
        return masses;
    }
    
    // ================================================================
    // DIAGNOSTICS
    // ================================================================
    
    /// <summary>
    /// Get breakdown of mass contributions for a node.
    /// Useful for debugging and visualization.
    /// </summary>
    public (double fermion, double correlation, double gauge, double scalar, double vacuum, double kinetic, double total) 
        GetMassBreakdown(int node)
    {
        if (_nodeMassModels == null || node < 0 || node >= N)
            return (0, 0, 0, 0, 0, 0, 0);
        
        var model = _nodeMassModels[node];
        return (
            model.FermionMass,
            model.CorrelationMass,
            model.GaugeFieldEnergy,
            model.ScalarFieldEnergy,
            model.VacuumEnergy,
            model.KineticEnergy,
            model.TotalMass
        );
    }
    
    /// <summary>
    /// Compute total mass in the system (sum over all nodes).
    /// </summary>
    public double ComputeTotalSystemMass()
    {
        if (_nodeMassModels == null)
            UpdateNodeMasses();
        
        double total = 0.0;
        for (int i = 0; i < N; i++)
            total += _nodeMassModels![i].TotalMass;
        
        return total;
    }
    
    /// <summary>
    /// Compute average mass per node.
    /// </summary>
    public double ComputeAverageMass()
    {
        return ComputeTotalSystemMass() / N;
    }
    
    /// <summary>
    /// Get mass distribution statistics.
    /// </summary>
    public (double min, double max, double mean, double stddev) GetMassStatistics()
    {
        if (_nodeMassModels == null)
            UpdateNodeMasses();
        
        double[] masses = new double[N];
        for (int i = 0; i < N; i++)
            masses[i] = _nodeMassModels![i].TotalMass;
        
        double min = masses.Min();
        double max = masses.Max();
        double mean = masses.Average();
        double variance = masses.Sum(m => (m - mean) * (m - mean)) / N;
        double stddev = Math.Sqrt(variance);
        
        return (min, max, mean, stddev);
    }
}
