using System;
using System.Linq;
using System.Numerics;
using RQSimulation.Gauge;

namespace RQSimulation
{
    /// <summary>
    /// Extensions to RQGraph for event-driven simulation.
    /// These methods support the event-driven architecture.
    /// 
    /// NOTE: This is a NON-PHYSICS module - moved to NonPhysicsEngines folder.
    /// Provides event-driven update methods for local proper time evolution.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Compute local proper time increment for a specific node
        /// Takes into account local field energy and curvature
        /// </summary>
        public double ComputeLocalProperTime(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                return 0.01;

            // Base time step
            double dt_base = 0.01;

            // Modify by local energy density (higher energy ? slower time)
            if (LocalPotential != null && nodeId < LocalPotential.Length)
            {
                double energy = LocalPotential[nodeId];
                // Time dilation: dt_proper = dt_coordinate * sqrt(1 - 2GM/r)
                // Approximate: dt ? 1 / (1 + energy)
                double energyFactor = 1.0 / (1.0 + energy * 0.1);
                dt_base *= energyFactor;
            }

            return Math.Max(dt_base, 0.001); // Minimum time step
        }

        /// <summary>
        /// Update physics for a specific node and its local neighborhood.
        /// This is the core of the event-driven evolution.
        /// </summary>
        public void UpdateNodePhysics(int nodeId, double dt)
        {
            if (nodeId < 0 || nodeId >= N)
                return;

            // Update local scalar field (if using Mexican Hat potential)
            if (ScalarField != null && _scalarMomentum != null && nodeId < ScalarField.Length)
            {
                UpdateScalarFieldAtNodeEventDriven(nodeId, dt);
            }

            // Update local gauge fields
            if (_gluonField != null)
            {
                UpdateGaugeFieldsAtNodeEventDriven(nodeId, dt);
            }

            // Update spinor field (Dirac evolution)
            if (_spinorA != null && nodeId < _spinorA.Length)
            {
                UpdateSpinorAtNodeEventDriven(nodeId, dt);
            }

            // Update state based on quantum dynamics
            UpdateNodeStateEventDriven(nodeId);
        }

        /// <summary>
        /// Get time dilation factor for a node based on local curvature and energy.
        /// Returns factor by which proper time differs from coordinate time.
        /// </summary>
        public double GetTimeDilation(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                return 1.0;

            double dilationFactor = 1.0;

            // Factor 1: Energy density (higher energy ? slower time)
            if (LocalPotential != null && nodeId < LocalPotential.Length)
            {
                double energy = LocalPotential[nodeId];
                // GR-like time dilation: sqrt(1 - 2GM/rc?)
                dilationFactor *= Math.Sqrt(1.0 / (1.0 + energy * 0.1));
            }

            return dilationFactor;
        }

        /// <summary>
        /// Update scalar field at a single node using Klein-Gordon equation
        /// </summary>
        private void UpdateScalarFieldAtNodeEventDriven(int nodeId, double dt)
        {
            if (ScalarField == null || _scalarMomentum == null)
                return;

            if (nodeId < 0 || nodeId >= ScalarField.Length)
                return;

            double phi = ScalarField[nodeId];
            double pi = _scalarMomentum[nodeId];

            // Compute Laplacian term (discrete)
            double laplacian = 0.0;
            int degree = 0;

            foreach (int j in Neighbors(nodeId))
            {
                if (j < ScalarField.Length)
                {
                    laplacian += (ScalarField[j] - phi) * Weights[nodeId, j];
                    degree++;
                }
            }

            if (degree > 0)
                laplacian /= degree;

            // Mexican Hat potential: V(?) = -???? + ???
            double mu2 = PhysicsConstants.HiggsMuSquared;
            double lambda = PhysicsConstants.HiggsLambda;
            double dV_dphi = -2.0 * mu2 * phi + 4.0 * lambda * phi * phi * phi;

            // Klein-Gordon equation: d??/dt? = ??? - dV/d?
            double d2phi_dt2 = laplacian - dV_dphi;

            // Symplectic Euler integration
            _scalarMomentum[nodeId] += d2phi_dt2 * dt;
            ScalarField[nodeId] += _scalarMomentum[nodeId] * dt;
        }

        /// <summary>
        /// Update gauge fields at a single node for event-driven evolution.
        /// </summary>
        private void UpdateGaugeFieldsAtNodeEventDriven(int nodeId, double dt)
        {
            if (nodeId < 0 || nodeId >= N) return;
            
            // Update SU(3) gluon field links
            if (_gluonField != null && _gluonFieldStrength != null)
            {
                UpdateGluonFieldAtNodeEventDriven(nodeId, dt);
            }
            
            // Update SU(2) weak field links
            if (_weakField != null && _weakFieldStrength != null)
            {
                UpdateWeakFieldAtNodeEventDriven(nodeId, dt);
            }
            
            // Update U(1) hypercharge field links
            if (_hyperchargeField != null && _hyperchargeFieldStrength != null)
            {
                UpdateHyperchargeFieldAtNodeEventDriven(nodeId, dt);
            }
        }
        
        /// <summary>
        /// Update SU(3) gluon field links emanating from a node.
        /// </summary>
        private void UpdateGluonFieldAtNodeEventDriven(int nodeId, double dt)
        {
            int[] scratch = new int[N];
            var neighbors = GetNeighborSpan(nodeId, ref scratch);
            
            foreach (int j in neighbors)
            {
                for (int a = 0; a < 8; a++)
                {
                    // Compute divergence of field strength at this edge
                    double divF = 0.0;
                    foreach (int k in neighbors)
                    {
                        if (Edges[k, j])
                        {
                            divF += _gluonFieldStrength![k, j, a] - _gluonFieldStrength[nodeId, j, a];
                        }
                    }
                    
                    // Self-interaction term
                    double selfInt = 0.0;
                    for (int b = 0; b < 8; b++)
                    {
                        double Ab = _gluonField![nodeId, j, b];
                        if (Math.Abs(Ab) < 1e-12) continue;
                        
                        for (int c = 0; c < 8; c++)
                        {
                            if (TryGetStructureConstant(a, b, c, out double fabc))
                            {
                                selfInt += StrongCoupling * fabc * Ab * _gluonFieldStrength![nodeId, j, c];
                            }
                        }
                    }
                    
                    // Color current from matter field
                    double J = ComputeColorCurrentCached(nodeId, j, a);
                    
                    // Update field
                    _gluonField![nodeId, j, a] += dt * (divF + selfInt - J);
                }
            }
        }
        
        /// <summary>
        /// Update SU(2) weak field links emanating from a node.
        /// </summary>
        private void UpdateWeakFieldAtNodeEventDriven(int nodeId, double dt)
        {
            int[] scratch = new int[N];
            var neighbors = GetNeighborSpan(nodeId, ref scratch);
            
            foreach (int j in neighbors)
            {
                for (int a = 0; a < 3; a++)
                {
                    // Compute divergence
                    double divW = 0.0;
                    foreach (int k in neighbors)
                    {
                        if (Edges[k, j])
                        {
                            divW += _weakFieldStrength![k, j, a] - _weakFieldStrength[nodeId, j, a];
                        }
                    }
                    
                    // Self-interaction using SU(2) structure constants
                    double selfInt = 0.0;
                    for (int b = 0; b < 3; b++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            double fabc = Gauge.PauliMatrices.GetStructureConstant(a, b, c);
                            if (fabc != 0.0)
                            {
                                selfInt += WeakCoupling * fabc * _weakField![nodeId, j, b] * _weakFieldStrength![nodeId, j, c];
                            }
                        }
                    }
                    
                    // Weak current
                    double Jw = ComputeWeakCurrent(nodeId, j, a);
                    
                    // Update field
                    _weakField![nodeId, j, a] += dt * (divW + selfInt - Jw);
                }
            }
        }
        
        /// <summary>
        /// Update U(1) hypercharge field links emanating from a node.
        /// </summary>
        private void UpdateHyperchargeFieldAtNodeEventDriven(int nodeId, double dt)
        {
            int[] scratch = new int[N];
            var neighbors = GetNeighborSpan(nodeId, ref scratch);
            
            foreach (int j in neighbors)
            {
                // Compute divergence
                double divB = 0.0;
                foreach (int k in neighbors)
                {
                    if (Edges[k, j])
                    {
                        divB += _hyperchargeFieldStrength![k, j] - _hyperchargeFieldStrength[nodeId, j];
                    }
                }
                
                // Hypercharge current
                double Jh = ComputeHyperchargeCurrent(nodeId, j);
                
                // Update field
                _hyperchargeField![nodeId, j] += dt * (divB - Jh);
            }
        }

        /// <summary>
        /// Update spinor field at a single node using local Dirac equation.
        /// </summary>
        private void UpdateSpinorAtNodeEventDriven(int nodeId, double dt)
        {
            if (_spinorA == null || nodeId >= _spinorA.Length)
                return;

            double hbar = VectorMath.HBar;
            double c = VectorMath.SpeedOfLight;

            // Get dynamical fermion mass from scalar field (Yukawa coupling)
            double mass = ComputeDynamicalFermionMassEventDriven(nodeId);

            Complex deltaA = Complex.Zero, deltaB = Complex.Zero;
            Complex deltaC = Complex.Zero, deltaD = Complex.Zero;

            bool isEvenSite = (nodeId % 2 == 0);

            // Kinetic term: sum over neighbors with gauge-covariant parallel transport
            foreach (int j in Neighbors(nodeId))
            {
                double weight = Weights[nodeId, j];
                if (weight < 1e-12) continue;

                // Gauge-covariant parallel transport for U(1)
                Complex parallelTransport = Complex.One;
                if (_edgePhaseU1 != null)
                {
                    double phase = _edgePhaseU1[nodeId, j];
                    parallelTransport = Complex.FromPolarCoordinates(1.0, -phase);
                }

                // Staggered fermion sign
                bool isNeighborEven = (j % 2 == 0);
                double sign = (isEvenSite != isNeighborEven) ? 1.0 : -1.0;

                // Direction flavor based on edge parity
                int edgeDirection = Math.Abs(nodeId - j) % 2;

                // Apply parallel transport to neighbor spinor
                Complex gaugedA_j = _spinorA[j] * parallelTransport;
                Complex gaugedB_j = _spinorB![j] * parallelTransport;
                Complex gaugedC_j = _spinorC![j] * parallelTransport;
                Complex gaugedD_j = _spinorD![j] * parallelTransport;

                // Hopping terms with staggered signs
                if (edgeDirection == 0)
                {
                    deltaB += sign * weight * (gaugedA_j - _spinorA[nodeId]);
                    deltaA += sign * weight * (gaugedB_j - _spinorB[nodeId]);
                    deltaD += sign * weight * (gaugedC_j - _spinorC[nodeId]);
                    deltaC += sign * weight * (gaugedD_j - _spinorD[nodeId]);
                }
                else
                {
                    Complex iSign = Complex.ImaginaryOne * sign;
                    deltaB += iSign * weight * (gaugedA_j - _spinorA[nodeId]);
                    deltaA -= iSign * weight * (gaugedB_j - _spinorB[nodeId]);
                    deltaD -= iSign * weight * (gaugedC_j - _spinorC[nodeId]);
                    deltaC += iSign * weight * (gaugedD_j - _spinorD[nodeId]);
                }
            }

            // Mass term
            double mc = mass * c;
            Complex massTermA = -Complex.ImaginaryOne * mc / hbar * _spinorC![nodeId];
            Complex massTermB = -Complex.ImaginaryOne * mc / hbar * _spinorD![nodeId];
            Complex massTermC = -Complex.ImaginaryOne * mc / hbar * _spinorA[nodeId];
            Complex massTermD = -Complex.ImaginaryOne * mc / hbar * _spinorB![nodeId];

            // Dirac evolution
            double factor = -1.0 / hbar;

            _spinorA[nodeId] += dt * factor * (c * deltaA + massTermA);
            _spinorB[nodeId] += dt * factor * (c * deltaB + massTermB);
            _spinorC[nodeId] += dt * factor * (c * deltaC + massTermC);
            _spinorD[nodeId] += dt * factor * (c * deltaD + massTermD);
        }

        /// <summary>
        /// Compute dynamical fermion mass from scalar field (Yukawa coupling)
        /// </summary>
        private double ComputeDynamicalFermionMassEventDriven(int nodeId)
        {
            const double g_Yukawa = 0.1;

            double phi = 0.0;
            if (ScalarField != null && nodeId < ScalarField.Length)
            {
                phi = ScalarField[nodeId];
            }

            double mass = g_Yukawa * Math.Abs(phi);

            const double bareMass = 0.001;
            return bareMass + mass;
        }

        /// <summary>
        /// Update node state based on local physics and neighbor excitation.
        /// </summary>
        private void UpdateNodeStateEventDriven(int nodeId)
        {
            if (nodeId < 0 || nodeId >= N)
                return;

            // Refractory state
            if (State[nodeId] == NodeState.Refractory)
            {
                _refractoryCounter[nodeId]--;
                if (_refractoryCounter[nodeId] <= 0)
                {
                    State[nodeId] = NodeState.Rest;
                    _refractoryCounter[nodeId] = 0;
                }
                return;
            }

            // Excited state
            if (State[nodeId] == NodeState.Excited)
            {
                State[nodeId] = NodeState.Refractory;
                double massFactor = (_correlationMass != null && _correlationMass.Length == N && _avgCorrelationMass > 0)
                    ? _correlationMass[nodeId] / _avgCorrelationMass
                    : 0.0;
                int extraSteps = (int)Math.Round(massFactor);
                _refractoryCounter[nodeId] = DynamicBaseRefractorySteps + Math.Max(0, extraSteps);
                return;
            }

            // Rest state: probabilistic excitation
            int excitedNeighbors = 0;
            double weightedExcitation = 0.0;
            int degree = 0;

            foreach (int j in Neighbors(nodeId))
            {
                degree++;
                if (State[j] == NodeState.Excited)
                {
                    excitedNeighbors++;
                    weightedExcitation += Weights[nodeId, j];
                }
            }

            // Spontaneous excitation from curvature
            double spontProb = 0.0;
            {
                double kNorm = GetLocalCurvatureNorm(nodeId);
                spontProb = 1.0 - Math.Exp(-kNorm);
                spontProb = Math.Clamp(spontProb, 0.0, 0.5);
            }

            // Neighbor-driven excitation
            double neighborProb = 0.0;
            if (excitedNeighbors > 0 && degree > 0)
            {
                double meanW = weightedExcitation / excitedNeighbors;
                double density = (double)excitedNeighbors / degree;
                neighborProb = 1.0 - Math.Exp(-density * meanW);
            }

            double totalProb = spontProb + neighborProb;

            // LocalPotential boost
            if (LocalPotential != null && nodeId < LocalPotential.Length && LocalPotential[nodeId] > 0)
            {
                totalProb *= (1.0 + LocalPotential[nodeId]);
            }

            totalProb = Math.Clamp(totalProb, 0.0, 0.99);

            if (_rng.NextDouble() < totalProb)
            {
                State[nodeId] = NodeState.Excited;
            }
        }

        /// <summary>
        /// Get gauge field component along edge (i,j)
        /// </summary>
        public double GetGaugeFieldComponent(int i, int j)
        {
            if (!Edges[i, j])
                return 0.0;

            if (_edgePhaseU1 != null && i < _edgePhaseU1.GetLength(0) && j < _edgePhaseU1.GetLength(1))
            {
                return _edgePhaseU1[i, j];
            }

            if (_gluonField != null && i < _gluonField.GetLength(0) && j < _gluonField.GetLength(1))
            {
                return _gluonField[i, j, 0];
            }

            return 0.0;
        }

        /// <summary>
        /// Apply gauge transformation to edge (i,j) consistently.
        /// </summary>
        public void ApplyGaugeTransformation(int i, int j, double chi_diff)
        {
            if (!Edges[i, j])
                return;

            if (_edgePhaseU1 != null && i < _edgePhaseU1.GetLength(0) && j < _edgePhaseU1.GetLength(1))
            {
                _edgePhaseU1[i, j] -= chi_diff;
                _edgePhaseU1[j, i] += chi_diff;

                _edgePhaseU1[i, j] = NormalizeAngleEventDriven(_edgePhaseU1[i, j]);
                _edgePhaseU1[j, i] = NormalizeAngleEventDriven(_edgePhaseU1[j, i]);
            }

            if (_gluonField != null && i < _gluonField.GetLength(0) && j < _gluonField.GetLength(1))
            {
                double scale = 0.1;
                for (int a = 0; a < 8; a++)
                {
                    _gluonField[i, j, a] -= chi_diff * scale;
                    _gluonField[j, i, a] += chi_diff * scale;
                }
            }
        }

        private static double NormalizeAngleEventDriven(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }

        /// <summary>
        /// Update local geometry (gravity) for a node and its edges.
        /// </summary>
        public void UpdateLocalGeometry(int node, double dt)
        {
            if (node < 0 || node >= N) return;

            double learningRate = PhysicsConstants.GravitationalCoupling * dt;
            
            foreach (int neighbor in Neighbors(node))
            {
                EvolveGeometry(node, neighbor, learningRate);
            }
        }

        /// <summary>
        /// Wrapper for UpdateNodePhysics
        /// </summary>
        public void UpdateLocalFields(int node, double dt)
        {
            UpdateNodePhysics(node, dt);
        }

        /// <summary>
        /// Perform topological updates using Metropolis-Hastings with local action.
        /// </summary>
        public void MetropolisTopologyUpdate_Checked()
        {
            int attempts = Math.Max(10, N / 10);
            
            for (int i = 0; i < attempts; i++)
            {
                MetropolisEdgeStepLocalAction();
            }
        }

        /// <summary>
        /// Calculate total energy of the system from all components.
        /// </summary>
        public double CalculateTotalEnergy()
        {
            double total = 0.0;
            
            if (_nodeMasses != null)
            {
                for (int i = 0; i < N; i++)
                {
                    total += _nodeMasses[i].TotalMass;
                }
            }
            else
            {
                total += ComputeTotalEnergy();
            }
            
            total += ComputeGeometryKineticEnergy();
            total += Ledger.VacuumPool;
            
            return total;
        }
    }
}
