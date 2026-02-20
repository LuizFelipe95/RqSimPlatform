using System;
using System.Collections.Generic;
using System.Numerics;
using RQSimulation.Gauge;

namespace RQSimulation
{
    public partial class RQGraph
    {
        public void ComputeGluonFieldStrength()
        {
            // Redirect to optimized version in RQGraph.YangMills.Optimized.cs
            ComputeGluonFieldStrengthOptimized();
        }

        public void ComputeWeakFieldStrength()
        {
            // Redirect to optimized version in RQGraph.YangMills.Optimized.cs
            ComputeWeakFieldStrengthOptimized();
        }

        public void ComputeHyperchargeFieldStrength()
        {
            if (_hyperchargeField == null || _hyperchargeFieldStrength == null) return;

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    double curl = 0;
                    foreach (int k in Neighbors(i))
                    {
                        if (k == j) continue;
                        if (Edges[k, j])
                            curl += _hyperchargeField[i, k] - _hyperchargeField[k, j];
                    }

                    _hyperchargeFieldStrength[i, j] = curl;
                    _hyperchargeFieldStrength[j, i] = -curl;
                }
            }
        }
        
        /// <summary>
        /// Get U(1) link variable U_ab = exp(i * phase) for edge (nodeA, nodeB).
        /// In lattice gauge theory, links carry the gauge field.
        /// </summary>
        /// <param name="nodeA">Start node</param>
        /// <param name="nodeB">End node</param>
        /// <returns>Complex link variable exp(i*phase)</returns>
        public Complex GetLinkVariable(int nodeA, int nodeB)
        {
            if (_edgePhaseU1 == null || !Edges[nodeA, nodeB])
                return Complex.One;
            
            double phase = _edgePhaseU1[nodeA, nodeB];
            return Complex.FromPolarCoordinates(1.0, phase);
        }
        
        /// <summary>
        /// Calculate plaquette flux (Wilson loop) for triangle (nodeA, nodeB, nodeC).
        /// Returns the product of link variables U_ab * U_bc * U_ca as complex number.
        /// For vacuum, this should be ~1 (trivial holonomy).
        /// Implements RQ-hypothesis checklist item 4.1.
        /// </summary>
        /// <param name="nodeA">First vertex</param>
        /// <param name="nodeB">Second vertex</param>
        /// <param name="nodeC">Third vertex</param>
        /// <returns>Complex Wilson loop flux: U_ab * U_bc * U_ca</returns>
        public Complex CalculatePlaquetteFlux(int nodeA, int nodeB, int nodeC)
        {
            // Product of phases U_ab * U_bc * U_ca
            var U_ab = GetLinkVariable(nodeA, nodeB);
            var U_bc = GetLinkVariable(nodeB, nodeC);
            var U_ca = GetLinkVariable(nodeC, nodeA);

            return U_ab * U_bc * U_ca; // Should be ~1 for vacuum
        }
        
        // ================================================================
        // RQ-HYPOTHESIS CHECKLIST FIX #4: PLAQUETTE-BASED FIELD STRENGTH
        // ================================================================
        
        /// <summary>
        /// Compute U(1) field strength on edge (i,j) using plaquette (Wilson loop) definition.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #4: Plaquette Definition for Irregular Graphs
        /// ==========================================================================
        /// On a regular lattice, the field strength tensor F_?? is defined via plaquettes:
        ///   F_??(x) ? Im[U_?(x) U_?(x+?) U†_?(x+?) U†_?(x)]
        /// 
        /// On an irregular graph, we use TRIANGLES as minimal plaquettes:
        ///   F_ij ? ?_{triangles containing (i,j)} Im[W_{ijk}] / N_triangles
        /// 
        /// where W_{ijk} = U_ij * U_jk * U_ki is the Wilson loop around triangle (i,j,k).
        /// 
        /// Physics:
        /// - On regular lattice: F_?? is anti-symmetric tensor ? 6 components in 4D
        /// - On graph: F_ij is anti-symmetric on edges ? captures magnetic flux
        /// - Triangles are minimal cycles (plaquettes) on arbitrary graph
        /// - This definition is gauge-invariant under local phase rotations
        /// 
        /// Advantages over neighbor-based curl:
        /// 1. Gauge-invariant by construction (Wilson loop is gauge-invariant)
        /// 2. Well-defined on any graph topology (not just lattice-like)
        /// 3. Correctly captures magnetic flux through minimal cycles
        /// </summary>
        /// <param name="i">First node of edge</param>
        /// <param name="j">Second node of edge</param>
        /// <returns>Field strength F_ij from plaquettes</returns>
        public double ComputeU1FieldStrengthPlaquette(int i, int j)
        {
            if (!Edges[i, j])
                return 0.0;
            
            double fieldStrength = 0.0;
            int triangleCount = 0;
            
            // Sum over all triangles containing edge (i,j)
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                if (!Edges[j, k]) continue;
                
                // Triangle (i, j, k) exists
                // Wilson loop: W = exp(i * (?_ij + ?_jk + ?_ki))
                double phase_ij = _edgePhaseU1 != null ? _edgePhaseU1[i, j] : 0.0;
                double phase_jk = _edgePhaseU1 != null ? _edgePhaseU1[j, k] : 0.0;
                double phase_ki = _edgePhaseU1 != null ? _edgePhaseU1[k, i] : 0.0;
                
                double totalPhase = phase_ij + phase_jk + phase_ki;
                
                // Field strength contribution: F_ij ? Im(W) = sin(total_phase)
                // For small phases: F ? total_phase (linear approximation)
                // We use the exact form for correctness
                fieldStrength += Math.Sin(totalPhase);
                triangleCount++;
            }
            
            // Average over triangles (if any)
            if (triangleCount > 0)
            {
                fieldStrength /= triangleCount;
            }
            
            return fieldStrength;
        }
        
        /// <summary>
        /// Compute SU(N) field strength on edge (i,j) using plaquette definition.
        /// 
        /// For non-abelian groups, the Wilson loop is:
        ///   W = Tr[U_ij * U_jk * U_ki] / N
        /// 
        /// And the field strength is extracted from:
        ///   F_ij ? Im(W) / g? where g is the coupling constant
        /// 
        /// This is gauge-invariant because Tr[U] is invariant under conjugation.
        /// </summary>
        public double ComputeSU3FieldStrengthPlaquette(int i, int j, int colorIndex)
        {
            if (!Edges[i, j] || _gaugeSU3 == null)
                return 0.0;
            
            double fieldStrength = 0.0;
            int triangleCount = 0;
            
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                if (!Edges[j, k]) continue;
                
                // Triangle (i, j, k) exists
                // Wilson loop for SU(3): W = Tr[U_ij * U_jk * U_ki] / 3
                SU3Matrix U_ij = _gaugeSU3[i, j];
                SU3Matrix U_jk = _gaugeSU3[j, k];
                SU3Matrix U_ki = _gaugeSU3[k, i];
                
                // Product of matrices
                SU3Matrix product = U_ij.Multiply(U_jk).Multiply(U_ki);
                
                // Trace gives gauge-invariant flux
                Complex trace = product.Trace();
                
                // Field strength ~ Im(Tr(W) - 3) / g?
                // For trivial configuration (vacuum): Tr(W) = 3, Im = 0
                fieldStrength += trace.Imaginary / 3.0;
                triangleCount++;
            }
            
            if (triangleCount > 0)
            {
                fieldStrength /= triangleCount;
            }
            
            // Scale by coupling constant
            return fieldStrength / (StrongCoupling * StrongCoupling + 1e-10);
        }
        
        /// <summary>
        /// Find all triangles (minimal plaquettes) in the graph.
        /// Used for Yang-Mills action computation on irregular graphs.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #4: Plaquette enumeration
        /// </summary>
        /// <returns>List of triangles as (i, j, k) tuples with i < j < k</returns>
        public List<(int i, int j, int k)> FindAllTriangles()
        {
            var triangles = new List<(int i, int j, int k)>();
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;
                    
                    foreach (int k in Neighbors(j))
                    {
                        if (k <= j) continue;
                        if (!Edges[k, i]) continue;
                        
                        // Found triangle (i, j, k)
                        triangles.Add((i, j, k));
                    }
                }
            }
            
            return triangles;
        }
        
        /// <summary>
        /// Compute full Yang-Mills action using plaquette definition.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST FIX #4: Lattice-QCD-style action
        /// =========================================================
        /// S_YM = ? ?_plaquettes (1 - Re(Tr(W))) / N_c
        /// 
        /// where ? = 2N_c / g? is the lattice coupling and W is the Wilson loop.
        /// 
        /// For U(1): S = ? ? (1 - cos(total_phase))
        /// For SU(3): S = ? ? (1 - Re(Tr(U_ij U_jk U_ki))/3)
        /// 
        /// This definition:
        /// 1. Is gauge-invariant by construction
        /// 2. Works on any graph topology (not just regular lattice)
        /// 3. Reduces to continuum F_?? F^?? in appropriate limit
        /// </summary>
        public double ComputeYangMillsActionPlaquette()
        {
            var triangles = FindAllTriangles();
            double action = 0.0;
            
            // U(1) contribution
            if (_edgePhaseU1 != null)
            {
                double beta_U1 = 1.0 / (PhysicsConstants.FineStructureConstant + 1e-10);
                
                foreach (var (i, j, k) in triangles)
                {
                    double phase_ij = _edgePhaseU1[i, j];
                    double phase_jk = _edgePhaseU1[j, k];
                    double phase_ki = _edgePhaseU1[k, i];
                    double totalPhase = phase_ij + phase_jk + phase_ki;
                    
                    // Wilson action: S = ? (1 - cos(phase))
                    action += beta_U1 * (1.0 - Math.Cos(totalPhase));
                }
            }
            
            // SU(3) contribution
            if (_gaugeSU3 != null)
            {
                double beta_SU3 = 6.0 / (StrongCoupling * StrongCoupling + 1e-10);
                
                foreach (var (i, j, k) in triangles)
                {
                    SU3Matrix U_ij = _gaugeSU3[i, j];
                    SU3Matrix U_jk = _gaugeSU3[j, k];
                    SU3Matrix U_ki = _gaugeSU3[k, i];
                    
                    SU3Matrix product = U_ij.Multiply(U_jk).Multiply(U_ki);
                    double reTrace = product.TraceReal();
                    
                    // Wilson action: S = ? (1 - Re(Tr(W))/3)
                    action += beta_SU3 * (1.0 - reTrace / 3.0);
                }
            }
            
            return action;
        }
    }
}
