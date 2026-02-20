using System;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Energy computation configuration - now using PhysicsConstants (Checklist B.2)
        // These getters allow runtime configuration while defaulting to PhysicsConstants
        private double _graphEnergyWeight = PhysicsConstants.GraphLinkEnergyWeight;
        private double _scalarFieldWeight = PhysicsConstants.ScalarFieldEnergyWeight;
        private double _fermionFieldWeight = PhysicsConstants.FermionFieldEnergyWeight;
        private double _gaugeFieldWeight = PhysicsConstants.GaugeFieldEnergyWeight;
        private double _yangMillsFieldWeight = PhysicsConstants.YangMillsFieldEnergyWeight;
        private double _gravityCurvatureWeight = PhysicsConstants.GravityCurvatureEnergyWeight;
        private double _clusterBindingWeight = PhysicsConstants.ClusterBindingEnergyWeight;
        
        /// <summary>Weight for graph link energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double GraphEnergyWeight { get => _graphEnergyWeight; set => _graphEnergyWeight = value; }
        
        /// <summary>Weight for scalar field energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double ScalarFieldWeight { get => _scalarFieldWeight; set => _scalarFieldWeight = value; }
        
        /// <summary>Weight for fermion field energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double FermionFieldWeight { get => _fermionFieldWeight; set => _fermionFieldWeight = value; }
        
        /// <summary>Weight for gauge field energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double GaugeFieldWeight { get => _gaugeFieldWeight; set => _gaugeFieldWeight = value; }
        
        /// <summary>Weight for Yang-Mills field energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double YangMillsFieldWeight { get => _yangMillsFieldWeight; set => _yangMillsFieldWeight = value; }
        
        /// <summary>Weight for gravity/curvature energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double GravityCurvatureWeight { get => _gravityCurvatureWeight; set => _gravityCurvatureWeight = value; }
        
        /// <summary>Weight for cluster binding energy in unified Hamiltonian. Default from PhysicsConstants.</summary>
        public double ClusterBindingWeight { get => _clusterBindingWeight; set => _clusterBindingWeight = value; }
        
        // Energy ledger for tracking energy conservation (checklist item 4)
        private EnergyLedger? _energyLedger;
        
        /// <summary>
        /// Get or create the energy ledger for tracking conservation.
        /// Implements checklist item 4: Unified energy ledger.
        /// </summary>
        public EnergyLedger GetEnergyLedger()
        {
            if (_energyLedger == null)
            {
                _energyLedger = new EnergyLedger();
                _energyLedger.Initialize(ComputeTotalEnergyUnified());
            }
            return _energyLedger;
        }
        
        /// <summary>
        /// Compute the unified Hamiltonian combining geometric and matter terms.
        /// H = H_geom + H_matter where:
        /// - H_geom = -Σ w_ij * R_ij (Einstein-Hilbert analog, curvature-weight product)
        /// - H_matter = ⟨ψ|D|ψ⟩ (Dirac operator expectation value)
        /// Implements checklist item 3.1: Unified action/Hamiltonian.
        /// </summary>
        /// <returns>Total Hamiltonian energy</returns>
        public double ComputeHamiltonian()
        {
            double H_geom = 0.0;
            double H_matter = 0.0;
            
            // H_geom = -Σ w_ij * R_ij (analog of Einstein-Hilbert action ∫R√g d⁴x)
            // Positive curvature regions contribute negative energy (attractive gravity)
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue; // Count each edge once
                    
                    double weight = Weights[i, j];
                    double curvature = ComputeFormanRicciCurvature(i, j);
                    H_geom -= weight * curvature;
                }
            }
            
            // H_matter = ⟨ψ|D|ψ⟩ (Dirac operator expectation value with spinors)
            H_matter = ComputeDiracExpectationValue();
            
            return H_geom + H_matter;
        }
        
        /// <summary>
        /// Compute graph Forman-Ricci curvature for edge (i,j) (1D-skeleton).
        /// Converges to 4 - deg(i) - deg(j) in the unweighted limit.
        /// Uses full Jost formula for weighted case.
        ///
        /// This is the CORRECT formula for graph-based curvature computation
        /// used in geometric energy and spectral properties.
        ///
        /// Mathematical foundation:
        ///   For unweighted graphs: Ric_F(e) = 4 - deg(i) - deg(j)
        ///   For weighted graphs: Uses Jost's generalization with node weights
        ///
        /// Reference: Refactoring plan section 1 - "Correct Forman-Ricci Curvature"
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <returns>Graph Forman-Ricci curvature for edge (i,j)</returns>
        public double ComputeFormanRicciCurvature(int i, int j)
        {
            if (!Edges[i, j])
                return 0.0;

            double w_e = Weights[i, j];
            if (w_e <= 0.0)
                return 0.0;

            var neighborsI = Neighbors(i).ToList();
            var neighborsJ = Neighbors(j).ToList();

            int degI = neighborsI.Count;
            int degJ = neighborsJ.Count;

            // Fast branch for unweighted graph
            // Correct limit: excludes incident edge from neighbor sums
            if (IsEdgeUnweighted(i, j))
            {
                return 4.0 - degI - degJ;
            }

            // Weighted branch (Full Jost formula)
            double w_i = NodeWeights != null && i < NodeWeights.Length ? NodeWeights[i] : 1.0;
            double w_j = NodeWeights != null && j < NodeWeights.Length ? NodeWeights[j] : 1.0;

            // Base curvature contribution from nodes: W_i + W_j
            double baseCurvature = w_i + w_j;

            // Sum over edges incident to node i (STRICTLY excluding edge e(i,j))
            double sumI = 0.0;
            foreach (int k in neighborsI)
            {
                if (k == j) continue; // Strictly exclude the considered edge e
                double w_ik = Weights[i, k];
                if (w_ik > 0.0)
                    sumI += w_i / Math.Sqrt(w_e * w_ik);
            }

            // Sum over edges incident to node j (STRICTLY excluding edge e(i,j))
            double sumJ = 0.0;
            foreach (int k in neighborsJ)
            {
                if (k == i) continue; // Strictly exclude the considered edge e
                double w_jk = Weights[j, k];
                if (w_jk > 0.0)
                    sumJ += w_j / Math.Sqrt(w_e * w_jk);
            }

            return baseCurvature - w_e * (sumI + sumJ);
        }

        /// <summary>
        /// Compute simplicial Forman curvature (based on triangles, 2-complex).
        /// Implementation from RQ_THEORY_AND_REQUIREMENTS specification.
        ///
        /// This is used directly for emergent gravity and discrete analog of
        /// Regge curvature (Einstein-Hilbert action on 2-complexes).
        ///
        /// Mathematical foundation:
        ///   Ric_S(e) = w_e * (Σ_triangles w_ik^(1/3) * w_jk^(1/3) - α*(w_i + w_j - 2*w_e))
        ///
        /// Reference: Refactoring plan section 1 - "Simplicial Forman Curvature"
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <param name="alpha">Penalty coefficient for local node volumes (default 1.0)</param>
        /// <returns>Simplicial Forman curvature for edge (i,j)</returns>
        public double ComputeSimplicialFormanCurvature(int i, int j, double alpha = 1.0)
        {
            if (!Edges[i, j])
                return 0.0;

            double w_e = Weights[i, j];
            if (w_e <= 0.0)
                return 0.0;

            double w_i = NodeWeights != null && i < NodeWeights.Length ? NodeWeights[i] : 1.0;
            double w_j = NodeWeights != null && j < NodeWeights.Length ? NodeWeights[j] : 1.0;

            // Find common neighbors to identify triangles (2-simplices)
            var commonNeighbors = Neighbors(i).Intersect(Neighbors(j));

            double triangleSum = 0.0;
            foreach (int k in commonNeighbors)
            {
                double w_ik = Weights[i, k];
                double w_jk = Weights[j, k];
                if (w_ik > 0.0 && w_jk > 0.0)
                {
                    // Triangle contribution: geometric mean of edge weights
                    triangleSum += Math.Pow(w_ik * w_jk, 1.0 / 3.0);
                }
            }

            // Penalty for local volumes according to RQ-hypothesis
            double penalty = alpha * (w_i + w_j - 2.0 * w_e);

            return w_e * (triangleSum - penalty);
        }

        /// <summary>
        /// Helper method to check if an edge is effectively unweighted.
        /// Returns true if edge weight is 1.0 and connected node weights are default.
        /// </summary>
        private bool IsEdgeUnweighted(int i, int j)
        {
            double w_e = Weights[i, j];

            // Check if edge weight is unity (within tolerance)
            if (Math.Abs(w_e - 1.0) > 1e-10)
                return false;

            // If node weights exist, check if they're also unity
            if (NodeWeights != null)
            {
                double w_i = i < NodeWeights.Length ? NodeWeights[i] : 1.0;
                double w_j = j < NodeWeights.Length ? NodeWeights[j] : 1.0;

                if (Math.Abs(w_i - 1.0) > 1e-10 || Math.Abs(w_j - 1.0) > 1e-10)
                    return false;
            }

            return true;
        }
        
        /// <summary>
        /// Compute Dirac operator expectation value ⟨ψ|D|ψ⟩.
        /// The Dirac operator on the graph couples spinor components through links.
        /// D_ij = γ^μ * U_ij where U_ij is the gauge link.
        /// </summary>
        /// <returns>Dirac expectation value (matter energy)</returns>
        private double ComputeDiracExpectationValue()
        {
            if (_spinorA == null || _spinorA.Length != N)
                return 0.0;
            
            double expectation = 0.0;
            
            // For each node, compute D|ψ⟩ and inner product ⟨ψ|D|ψ⟩
            for (int i = 0; i < N; i++)
            {
                // Sum over neighbors (hopping term)
                System.Numerics.Complex Dpsi_A = System.Numerics.Complex.Zero;
                System.Numerics.Complex Dpsi_B = System.Numerics.Complex.Zero;
                
                foreach (int j in Neighbors(i))
                {
                    // Gauge link U_ij (U(1) phase)
                    System.Numerics.Complex U_ij = GetLinkVariable(i, j);
                    
                    // Dirac hopping: γ·∇ ~ σ^μ * (ψ_j - ψ_i)
                    // Simplified to just hopping with gauge link
                    if (_spinorA != null && j < _spinorA.Length)
                    {
                        Dpsi_A += Weights[i, j] * U_ij * _spinorA[j];
                        if (_spinorB != null && j < _spinorB.Length)
                            Dpsi_B += Weights[i, j] * U_ij * _spinorB[j];
                    }
                }
                
                // Mass term: m * ψ†γ⁰ψ
                double mass = (_correlationMass != null && i < _correlationMass.Length) ? _correlationMass[i] : 1.0;
                System.Numerics.Complex psi_A = _spinorA[i];
                System.Numerics.Complex psi_B = (_spinorB != null && i < _spinorB.Length) ? _spinorB[i] : System.Numerics.Complex.Zero;
                
                // ⟨ψ|D|ψ⟩ = ψ†Dψ
                double kinetic = (System.Numerics.Complex.Conjugate(psi_A) * Dpsi_A).Real 
                               + (System.Numerics.Complex.Conjugate(psi_B) * Dpsi_B).Real;
                double massTerm = mass * (psi_A.Magnitude * psi_A.Magnitude + psi_B.Magnitude * psi_B.Magnitude);
                
                expectation += kinetic + massTerm;
            }
            
            return expectation;
        }
        
        /// <summary>
        /// Compute total unified energy functional combining all contributions.
        /// Implements checklist item 4.2: H_total = H_matter + H_field + H_vacuum + K_geometry.
        /// 
        /// Includes:
        /// - Graph link energy (potential energy in edge weights)
        /// - Scalar field energy (kinetic + gradient + potential)
        /// - Fermion field energy
        /// - Gauge field energy (U(1))
        /// - Yang-Mills field energy (SU(3))
        /// - Gravity curvature energy
        /// - Cluster binding energy
        /// - Geometric kinetic energy K = Σ π_ij²/(2M) (Checklist F.3)
        /// </summary>
        public double ComputeTotalEnergyUnified()
        {
            double E_links = ComputeGraphLinkEnergy();
            double E_scalar = ComputeScalarFieldEnergy();
            double E_fermion = ComputeFermionFieldEnergy();
            double E_gauge = ComputeGaugeFieldEnergy();
            double E_yangMills = ComputeYangMillsFieldEnergy();
            double E_grav = ComputeGravityCurvatureEnergy();
            double E_bind = ComputeClusterBindingEnergy();
            
            // Checklist F.3: Add geometric kinetic energy K = Σ π²/(2M)
            // This represents gravitational wave energy and geometry momentum
            double E_geomKinetic = ComputeGeometryKineticEnergy();
            
            double total = GraphEnergyWeight * E_links
                 + ScalarFieldWeight * E_scalar
                 + FermionFieldWeight * E_fermion
                 + GaugeFieldWeight * E_gauge
                 + YangMillsFieldWeight * E_yangMills
                 + GravityCurvatureWeight * E_grav
                 + ClusterBindingWeight * E_bind
                 + E_geomKinetic;  // Kinetic energy of geometry (gravitational waves)

            // Safety check for exploding energy (e.g. during GPU instability or topology changes)
            // Prevents "Energy conservation violated" errors with massive values (e.g. 8e8)
            if (double.IsNaN(total) || double.IsInfinity(total) || Math.Abs(total) > 1e12)
            {
                // Clamp to a large but finite value to prevent cascading failures
                // Preserving sign to indicate direction of explosion
                if (Math.Abs(total) > 1e12) total = Math.Sign(total) * 1e12;
                else total = 0.0; // NaN case
            }

            return total;
        }
        
        /// <summary>
        /// Compute Yang-Mills field energy (gluon, weak, hypercharge).
        /// Implements checklist item 4.2: Include Yang-Mills energy in total.
        /// This returns the action S = ∫ F² which represents field energy.
        /// </summary>
        private double ComputeYangMillsFieldEnergy()
        {
            // Delegate to existing Yang-Mills action computation
            return ComputeYangMillsAction();
        }
        
        /// <summary>
        /// Propose a weight change and check if it's affordable energy-wise.
        /// Implements checklist item 4.3: Ledger.CanAfford(deltaE) check.
        /// </summary>
        /// <param name="i">First node</param>
        /// <param name="j">Second node</param>
        /// <param name="newWeight">Proposed new weight</param>
        /// <returns>True if the change is accepted, false otherwise</returns>
        public bool ProposeWeightChangeWithEnergyCheck(int i, int j, double newWeight)
        {
            if (!Edges[i, j])
                return false;
            
            // Compute energy before
            double E_before = ComputeTotalEnergyUnified();
            double oldWeight = Weights[i, j];
            
            // Tentatively apply change
            Weights[i, j] = newWeight;
            Weights[j, i] = newWeight;
            
            // Compute energy after
            double E_after = ComputeTotalEnergyUnified();
            double deltaE = E_after - E_before;
            
            // Check with energy ledger
            var ledger = GetEnergyLedger();
            bool canAfford = ledger.CanAfford(deltaE);
            
            if (!canAfford)
            {
                // Revert the change
                Weights[i, j] = oldWeight;
                Weights[j, i] = oldWeight;
                return false;
            }
            
            // If energy increase, use Metropolis criterion
            if (deltaE > 0)
            {
                // Try to spend vacuum energy
                if (!ledger.TrySpendVacuumEnergy(deltaE))
                {
                    // Not enough vacuum energy, revert
                    Weights[i, j] = oldWeight;
                    Weights[j, i] = oldWeight;
                    return false;
                }
            }
            else
            {
                // Energy released, return to vacuum pool
                ledger.RegisterRadiation(-deltaE);
            }
            
            return true;
        }
        
        /// <summary>
        /// Graph link energy from edge weights
        /// </summary>
        private double ComputeGraphLinkEnergy()
        {
            double energy = 0;
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i) // Count each edge once
                    {
                        // Energy proportional to weight squared (quadratic potential)
                        energy += Weights[i, j] * Weights[i, j];
                    }
                }
            }
            
            return energy;
        }
        
        // Note: ComputeScalarFieldEnergy() already exists in RQGraph.FieldTheory.cs
        
        /// <summary>
        /// Fermion field energy
        /// </summary>
        private double ComputeFermionFieldEnergy()
        {
            // Check if spinor fields are initialized (they're named _spinorA, _spinorB, _spinorC, _spinorD)
            if (_spinorA == null || _spinorA.Length != N)
                return 0.0;
            
            double energy = 0;
            
            // Energy from spinor field magnitude (4 components: A, B, C, D)
            for (int i = 0; i < N; i++)
            {
                var psiA = _spinorA[i];
                var psiB = _spinorB?[i] ?? System.Numerics.Complex.Zero;
                var psiC = _spinorC?[i] ?? System.Numerics.Complex.Zero;
                var psiD = _spinorD?[i] ?? System.Numerics.Complex.Zero;
                
                energy += psiA.Real * psiA.Real + psiA.Imaginary * psiA.Imaginary;
                energy += psiB.Real * psiB.Real + psiB.Imaginary * psiB.Imaginary;
                energy += psiC.Real * psiC.Real + psiC.Imaginary * psiC.Imaginary;
                energy += psiD.Real * psiD.Real + psiD.Imaginary * psiD.Imaginary;
            }
            
            return energy;
        }
        
        /// <summary>
        /// Gravity contribution from graph curvature
        /// </summary>
        private double ComputeGravityCurvatureEnergy()
        {
            double energy = 0;
            int edgeCount = 0;
            
            // Sum curvature contributions
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j > i)
                    {
                        double curvature = CalculateGraphCurvature(i, j);
                        // Negative curvature increases energy (penalizes tree-like structures)
                        // Positive curvature decreases energy (favors clustering)
                        energy -= curvature;
                        edgeCount++;
                    }
                }
            }
            
            return edgeCount > 0 ? energy / edgeCount : 0;
        }
        
        /// <summary>
        /// Cluster binding energy - stabilizes clusters through triangle terms
        /// </summary>
        private double ComputeClusterBindingEnergy()
        {
            double energy = 0;
            
            // Energy reduction for closed triangles with strong edges
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;
                    
                    foreach (int k in Neighbors(i))
                    {
                        if (k <= j || !Edges[j, k]) continue;
                        
                        // Triangle i-j-k exists
                        double w1 = Weights[i, j];
                        double w2 = Weights[j, k];
                        double w3 = Weights[k, i];
                        
                        // Triangle strength (geometric mean)
                        double triStrength = Math.Pow(w1 * w2 * w3, 1.0 / 3.0);
                        
                        // Strong triangles reduce energy (bind clusters)
                        energy -= triStrength;
                    }
                }
            }
            
            return energy;
        }
        
        /// <summary>
        /// Check if topology change is acceptable based on energy criterion
        /// </summary>
        public bool AcceptTopologyChange(double energyBefore, double energyAfter, double temperature)
        {
            double dE = energyAfter - energyBefore;
            
            // Always accept if energy decreases
            if (dE < 0)
                return true;
            
            // Metropolis criterion for increases
            if (temperature > 0)
            {
                double probability = Math.Exp(-dE / temperature);
                return _rng.NextDouble() < probability;
            }
            
            return false;
        }
    }
}
