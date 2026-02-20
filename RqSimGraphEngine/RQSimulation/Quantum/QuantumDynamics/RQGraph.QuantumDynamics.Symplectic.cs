using System;
using System.Linq;
using System.Numerics;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // ==================== SYMPLECTIC INTEGRATOR ====================
        // Implements checklist item 6: Numerical stability via leapfrog
        
        // Field momenta (conjugate to phases)
        private double[]? _fieldMomenta;
        
        /// <summary>
        /// Initialize field momenta for symplectic integration.
        /// </summary>
        private void EnsureFieldMomenta()
        {
            if (_fieldMomenta == null || _fieldMomenta.Length != N)
            {
                _fieldMomenta = new double[N];
            }
        }
        
        /// <summary>
        /// Perform symplectic (leapfrog) integration step for field dynamics.
        /// The leapfrog method is time-reversible and conserves phase space volume,
        /// preventing energy drift common in simple Euler methods.
        /// 
        /// Steps:
        /// 1. p(t + dt/2) = p(t) - dH/dq * dt/2  (half-step momentum)
        /// 2. q(t + dt)   = q(t) + dH/dp * dt    (full-step position)
        /// 3. p(t + dt)   = p(t + dt/2) - dH/dq * dt/2 (half-step momentum)
        /// 
        /// Implements checklist item 6.1: Symplectic integrator (Leapfrog).
        /// </summary>
        /// <param name="dt">Time step</param>
        public void StepSymplectic(double dt)
        {
            EnsureFieldMomenta();
            
            // Step 1: Half-step momentum update
            // p(t + dt/2) = p(t) - dH/dq * dt/2
            UpdateMomenta(dt / 2.0);
            
            // Step 2: Full-step field (phase) update
            // q(t + dt) = q(t) + dH/dp * dt
            UpdateFields(dt);
            
            // Step 3: Half-step momentum update
            // p(t + dt) = p(t + dt/2) - dH/dq * dt/2
            UpdateMomenta(dt / 2.0);
        }
        
        /// <summary>
        /// Update field momenta based on Hamiltonian gradient.
        /// dp/dt = -dH/dq where q are the field values (phases).
        /// </summary>
        /// <param name="dt">Time step</param>
        private void UpdateMomenta(double dt)
        {
            if (_fieldMomenta == null)
                return;
            
            // For each node, compute dH/dq (gradient of Hamiltonian w.r.t. field)
            for (int i = 0; i < N; i++)
            {
                double dHdq = ComputeFieldGradient(i);
                
                // Update momentum: p -= dH/dq * dt
                _fieldMomenta[i] -= dHdq * dt;
            }
        }
        
        /// <summary>
        /// Update field values (phases) based on momenta.
        /// dq/dt = dH/dp = p (for quadratic kinetic energy)
        /// </summary>
        /// <param name="dt">Time step</param>
        private void UpdateFields(double dt)
        {
            if (_fieldMomenta == null || LocalPotential == null)
                return;
            
            // For quadratic kinetic energy: dH/dp = p
            // So q += p * dt
            for (int i = 0; i < N; i++)
            {
                LocalPotential[i] += _fieldMomenta[i] * dt;
                
                // Apply boundary conditions / clamping
                if (LocalPotential[i] < 0)
                    LocalPotential[i] = 0;
                if (LocalPotential[i] > 5.0)
                    LocalPotential[i] = 5.0;
            }
            
            // Also update wavefunction phases symplectically
            if (_waveMulti != null)
            {
                int d = GaugeDimension;
                for (int i = 0; i < N; i++)
                {
                    // Scale factor for phase rotation based on momentum
                    double phaseIncrement = _fieldMomenta[i] * dt * PhysicsConstants.SymplecticPhaseScale;
                    Complex phaseRotation = Complex.FromPolarCoordinates(1.0, phaseIncrement);
                    
                    for (int a = 0; a < d; a++)
                    {
                        int idx = i * d + a;
                        _waveMulti[idx] *= phaseRotation;
                    }
                }
            }
        }
        
        /// <summary>
        /// Compute gradient of Hamiltonian with respect to field at node i.
        /// dH/dq_i includes kinetic, potential, and interaction terms.
        /// </summary>
        /// <param name="nodeId">Node index</param>
        /// <returns>Gradient dH/dq at node</returns>
        private double ComputeFieldGradient(int nodeId)
        {
            double gradient = 0.0;
            
            // Potential term: V(q) contribution
            double localPotential = (LocalPotential != null && nodeId < LocalPotential.Length) 
                ? LocalPotential[nodeId] : 0.0;
            
            // Simple harmonic potential: V = (1/2) * ?? * q? ? dV/dq = ?? * q
            double omega = PhysicsConstants.FieldHarmonicFrequency;
            gradient += omega * omega * localPotential;
            
            // Interaction term: Laplacian contribution from neighbors
            // For diffusive coupling: H_int = -? w_ij * q_i * q_j
            // ? dH/dq_i = -? w_ij * q_j
            foreach (int j in Neighbors(nodeId))
            {
                double neighborPotential = (LocalPotential != null && j < LocalPotential.Length) 
                    ? LocalPotential[j] : 0.0;
                gradient -= Weights[nodeId, j] * neighborPotential;
            }
            
            // Curvature coupling: gravity affects field dynamics
            double curvature = GetLocalCurvature(nodeId);
            gradient += 0.1 * curvature * localPotential;
            
            return gradient;
        }
        
        /// <summary>
        /// Verify energy conservation after symplectic step.
        /// Returns relative energy change (should be small for symplectic integrator).
        /// </summary>
        /// <param name="previousEnergy">Energy before step</param>
        /// <returns>Relative energy change |?E/E|</returns>
        public double VerifySymplecticConservation(double previousEnergy)
        {
            double currentEnergy = ComputeSymplecticEnergy();
            
            if (Math.Abs(previousEnergy) < 1e-10)
                return Math.Abs(currentEnergy - previousEnergy);
            
            return Math.Abs(currentEnergy - previousEnergy) / Math.Abs(previousEnergy);
        }
        
        /// <summary>
        /// Compute total energy in symplectic coordinates (H = T + V).
        /// </summary>
        /// <returns>Total Hamiltonian energy</returns>
        public double ComputeSymplecticEnergy()
        {
            EnsureFieldMomenta();
            
            double kineticEnergy = 0.0;
            double potentialEnergy = 0.0;
            
            // Kinetic energy: T = (1/2) * ? p?
            if (_fieldMomenta != null)
            {
                for (int i = 0; i < N; i++)
                {
                    kineticEnergy += 0.5 * _fieldMomenta[i] * _fieldMomenta[i];
                }
            }
            
            // Potential energy from local potentials
            if (LocalPotential != null)
            {
                double omega = PhysicsConstants.FieldHarmonicFrequency;
                for (int i = 0; i < LocalPotential.Length; i++)
                {
                    // Harmonic potential
                    potentialEnergy += 0.5 * omega * omega * LocalPotential[i] * LocalPotential[i];
                    
                    // Interaction energy
                    foreach (int j in Neighbors(i))
                    {
                        if (j > i && j < LocalPotential.Length)
                        {
                            potentialEnergy -= 0.5 * Weights[i, j] * LocalPotential[i] * LocalPotential[j];
                        }
                    }
                }
            }
            
            return kineticEnergy + potentialEnergy;
        }
    }
}
