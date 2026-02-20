using System;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Amplitudes of a real scalar field defined on the nodes of the graph.  The
        /// field can be interpreted as a coarse grained order parameter or as the
        /// discretised value of a quantum field in a lattice field theory.  Each
        /// node holds a scalar value φ that evolves according to a Klein–Gordon
        /// type equation with a quartic self interaction (φ^4 theory) and
        /// nearest neighbour coupling.  The field dynamics are distinct from
        /// the complex wavefunction in RQGraph.QuantumDynamics.cs and are
        /// intended as a stepping stone towards quantum field theoretic behaviour.
        /// </summary>
        public double[] ScalarField { get; private set; }

        /// <summary>
        /// Conjugate momenta π for the scalar field.  The dynamics are
        /// implemented in Hamiltonian form with π̇ = −∂H/∂φ and φ̇ = π.
        /// </summary>
        private double[] _scalarMomentum;

        /// <summary>
        /// Coupling constant for nearest neighbour interactions.  Larger values
        /// lead to faster propagation of field excitations between nodes.  This
        /// plays the role of the Laplacian coupling in a lattice discretisation
        /// of the Klein–Gordon equation.
        /// </summary>
        public double ScalarCoupling { get; set; } = 0.1;

        /// <summary>
        /// Mass parameter m in the Klein–Gordon part of the field equation.
        /// Determines the natural oscillation frequency of the field.  Zero
        /// mass yields massless propagation.
        /// </summary>
        public double ScalarMass { get; set; } = 1.0;

        /// <summary>
        /// Self‑interaction strength λ for the φ^4 potential.  Positive values
        /// produce a double well potential; negative values generate an
        /// unstable upside‑down potential.  Set to zero to recover a free
        /// Klein–Gordon field.
        /// </summary>
        public double ScalarSelfCoupling { get; set; } = 0.0;

        /// <summary>
        /// Use Mexican Hat (Higgs) potential instead of standard Klein-Gordon.
        /// V(φ) = -μ²φ² + λφ⁴ which creates spontaneous symmetry breaking
        /// and two stable vacuum states at ±v where v = √(μ²/2λ).
        /// Implements RQ-Hypothesis Checklist A.1.
        /// </summary>
        public bool UseMexicanHatPotential { get; set; } = false;

        /// <summary>
        /// μ² parameter for Mexican Hat potential. When positive, creates 
        /// spontaneous symmetry breaking.
        /// </summary>
        public double HiggsMuSquared { get; set; } = PhysicsConstants.HiggsMuSquared;

        /// <summary>
        /// λ parameter for Mexican Hat potential (quartic coupling).
        /// </summary>
        public double HiggsLambda { get; set; } = PhysicsConstants.HiggsLambda;

        /// <summary>
        /// Threshold on |φ| for marking a node as excited.  When the absolute
        /// value of the field exceeds this value the node's state will be set
        /// to Excited in UpdateFieldExcitations().
        /// </summary>
        public double FieldExcitationThreshold { get; set; } = 1.0;

        /// <summary>
        /// Enable Gauss law projection after each field update step.
        /// When enabled, ensures ∇·E = ρ (charge conservation).
        /// 
        /// PHYSICS: Without Gauss law enforcement, numerical errors accumulate
        /// and break gauge invariance. The projection solves:
        ///   ∇²χ = ∇·E - ρ
        ///   A_new = A - ∇χ
        /// 
        /// Implements RQ-Hypothesis Checklist: Gauss Law Integration (Step 5).
        /// </summary>
        public bool EnforceGaugeConstraintsAfterFieldUpdate { get; set; } = true;

        /// <summary>
        /// Counter for Gauss law enforcement (to reduce frequency).
        /// </summary>
        private int _gaussLawEnforcementCounter = 0;

        /// <summary>
        /// Interval for Gauss law enforcement (every N steps).
        /// </summary>
        public int GaussLawEnforcementInterval { get; set; } = PhysicsConstants.GaugeConstraintInterval;

        /// <summary>
        /// Initialise the scalar field and its conjugate momentum.  By
        /// default the field is initialised with small random fluctuations
        /// around zero.  Call this after constructing the graph and setting
        /// its topology.
        /// </summary>
        public void InitScalarField(double amplitude = 0.01)
        {
            if (N <= 0)
            {
                ScalarField = Array.Empty<double>();
                _scalarMomentum = Array.Empty<double>();
                return;
            }
            ScalarField = new double[N];
            _scalarMomentum = new double[N];
            var rnd = _rng;
            for (int i = 0; i < N; i++)
            {
                // small random fluctuations around zero
                ScalarField[i] = (rnd.NextDouble() * 2.0 - 1.0) * amplitude;
                _scalarMomentum[i] = 0.0;
            }
        }

        /// <summary>
        /// Advance the scalar field by one time step using Velocity Verlet symplectic
        /// integration with Lattice Gauge Covariant Derivative.
        /// 
        /// The covariant Laplacian uses: D_μφ = (φ[j] * exp(i*θ_ij)) - φ[i]
        /// where θ_ij is the gauge phase on the edge, ensuring gauge invariance.
        /// 
        /// Velocity Verlet (symplectic):
        ///   Step 1: π(t+½dt) = π(t) + ½dt * F(φ(t))
        ///   Step 2: φ(t+dt) = φ(t) + dt * π(t+½dt)
        ///   Step 3: π(t+dt) = π(t+½dt) + ½dt * F(φ(t+dt))
        /// 
        /// For standard Klein-Gordon: V'(φ) = m²φ + λφ³
        /// For Mexican Hat (Higgs): V'(φ) = -2μ²φ + 4λφ³
        /// 
        /// Implements RQ-Hypothesis Checklist E.1 and G.2.
        /// </summary>
        /// <param name="dt">Time step.</param>
        public void UpdateScalarField(double dt)
        {
            if (ScalarField == null || ScalarField.Length != N) return;
            if (_scalarMomentum == null || _scalarMomentum.Length != N)
            {
                _scalarMomentum = new double[N];
            }

            // ===== VELOCITY VERLET STEP 1: Half-step momentum update =====
            // π(t+½dt) = π(t) + ½dt * F(φ(t))
            double[] halfMomentum = new double[N];
            for (int i = 0; i < N; i++)
            {
                double force = ComputeScalarFieldForce(i);
                halfMomentum[i] = _scalarMomentum[i] + 0.5 * dt * force;
            }

            // ===== VELOCITY VERLET STEP 2: Full-step position update =====
            // φ(t+dt) = φ(t) + dt * π(t+½dt)
            for (int i = 0; i < N; i++)
            {
                ScalarField[i] += dt * halfMomentum[i];
            }

            // ===== VELOCITY VERLET STEP 3: Full-step momentum update =====
            // π(t+dt) = π(t+½dt) + ½dt * F(φ(t+dt))
            for (int i = 0; i < N; i++)
            {
                double force = ComputeScalarFieldForce(i);
                _scalarMomentum[i] = halfMomentum[i] + 0.5 * dt * force;
            }

            // Update node excitations based on field amplitude
            UpdateFieldExcitations();

            // ===== GAUSS LAW PROJECTION (RQ-Hypothesis Checklist Step 5) =====
            // Enforce ∇·E = ρ periodically to maintain gauge invariance
            if (EnforceGaugeConstraintsAfterFieldUpdate)
            {
                _gaussLawEnforcementCounter++;
                if (_gaussLawEnforcementCounter >= GaussLawEnforcementInterval)
                {
                    EnforceGaussLawWithLogging();
                    _gaussLawEnforcementCounter = 0;
                }
            }
        }

        /// <summary>
        /// Enforce Gauss law with violation logging.
        /// Calls EnforceGaussLaw() and logs any remaining violation.
        /// </summary>
        private void EnforceGaussLawWithLogging()
        {
            // Measure violation before
            double violationBefore = ComputeGaussLawViolation();

            // Apply projection
            EnforceGaussLaw();

            // Measure violation after
            double violationAfter = ComputeGaussLawViolation();

            // Log if significant violation remains
            if (violationAfter > 1e-4)
            {
                //Console.WriteLine($"[GAUSS] Violation: {violationBefore:E3} → {violationAfter:E3}");
            }
        }

        /// <summary>
        /// Compute the force F = -∂H/∂φ for node i using Gauge Covariant Derivative.
        /// F_i = ∇²φ_i - V'(φ_i) where ∇² is the covariant Laplacian.
        /// 
        /// Lattice Gauge Covariant Derivative (Checklist E.1):
        /// D_μφ = U_ij * φ_j - φ_i where U_ij = exp(i * θ_ij)
        /// 
        /// For real scalar field, we use: Re[U_ij * φ_j] - φ_i = cos(θ_ij) * φ_j - φ_i
        /// </summary>
        internal double ComputeScalarFieldForce(int i)
        {
            // Gauge Covariant Laplacian: sum over neighbors
            // D²φ_i = Σ_j w_ij * (Re[U_ij * φ_j] - φ_i)
            // For real scalar: Re[exp(i*θ) * φ] = cos(θ) * φ
            double covariantLaplacian = 0.0;
            double phi_i = ScalarField[i];

            foreach (int j in Neighbors(i))
            {
                double w = Weights[i, j];
                double phi_j = ScalarField[j];

                // Get gauge phase θ_ij for this edge (Checklist E.1)
                double theta_ij = GetEdgeGaugePhase(i, j);

                // Gauge covariant difference: U_ij * φ_j - φ_i
                // For real scalar field: cos(θ_ij) * φ_j - φ_i
                // This ensures gauge invariance under φ → e^{iα}φ, θ → θ + ∇α
                double covariantDiff = Math.Cos(theta_ij) * phi_j - phi_i;

                covariantLaplacian += w * covariantDiff;
            }
            covariantLaplacian *= ScalarCoupling;

            // Potential derivative depends on potential type
            double potentialDerivative;

            if (UseMexicanHatPotential)
            {
                // Mexican Hat: V(φ) = -μ²φ² + λφ⁴
                // V'(φ) = -2μ²φ + 4λφ³
                // This creates two minima at φ = ±v where v = √(μ²/(2λ))
                potentialDerivative = -2.0 * HiggsMuSquared * phi_i + 4.0 * HiggsLambda * phi_i * phi_i * phi_i;
            }
            else
            {
                // Standard Klein-Gordon: V(φ) = ½m²φ² + ¼λφ⁴
                // V'(φ) = m²φ + λφ³
                potentialDerivative = ScalarMass * ScalarMass * phi_i;
                if (ScalarSelfCoupling != 0.0)
                {
                    potentialDerivative += ScalarSelfCoupling * phi_i * phi_i * phi_i;
                }
            }

            // Equation of motion: F = ∇²φ - V'(φ)
            return covariantLaplacian - potentialDerivative;
        }

        /// <summary>
        /// Get the U(1) gauge phase θ_ij for edge (i,j).
        /// Returns 0 if gauge phases are not initialized.
        /// </summary>
        private double GetEdgeGaugePhase(int i, int j)
        {
            var gaugeData = GetEdgeGaugeData(i, j);
            return gaugeData.PhaseU1;
        }

        /// <summary>
        /// Compute the scalar field current J_μ on edge (i,j).
        /// J_ij = Im[φ*_i * D_ij φ] = |φ_i| * |φ_j| * sin(θ_ij + arg(φ_j) - arg(φ_i))
        /// For real scalar field: J_ij = φ_i * φ_j * sin(θ_ij)
        /// 
        /// This current couples back to the gauge field (Checklist E.2).
        /// </summary>
        public double ComputeScalarFieldCurrent(int i, int j)
        {
            if (ScalarField == null || !Edges[i, j])
                return 0.0;

            double phi_i = ScalarField[i];
            double phi_j = ScalarField[j];
            double theta_ij = GetEdgeGaugePhase(i, j);

            // For real scalar field: J_ij = g * φ_i * φ_j * sin(θ_ij)
            // The coupling constant g ensures correct dimensional units
            return ScalarCoupling * phi_i * phi_j * Math.Sin(theta_ij);
        }

        /// <summary>
        /// Initialize scalar field with Mexican Hat potential for spontaneous symmetry breaking.
        /// Field starts near the unstable maximum (φ=0) with small random fluctuations.
        /// The field will naturally roll to one of the stable minima at ±v.
        /// Implements RQ-Hypothesis Checklist A.1.
        /// </summary>
        /// <param name="amplitude">Initial fluctuation amplitude (should be small)</param>
        public void InitScalarFieldMexicanHat(double amplitude = 0.01)
        {
            InitScalarField(amplitude);
            UseMexicanHatPotential = true;
            HiggsMuSquared = PhysicsConstants.HiggsMuSquared;
            HiggsLambda = PhysicsConstants.HiggsLambda;
        }

        /// <summary>
        /// Initialize scalar field with hot start for annealing.
        /// Field starts with large random fluctuations that will cool down
        /// through the dynamics, naturally forming stable structures.
        /// Implements RQ-Hypothesis Checklist A.2 (replaces impulse).
        /// </summary>
        /// <param name="temperature">Initial temperature (controls fluctuation amplitude)</param>
        public void InitScalarFieldHotStart(double temperature = 1.0)
        {
            if (N <= 0)
            {
                ScalarField = Array.Empty<double>();
                _scalarMomentum = Array.Empty<double>();
                return;
            }

            ScalarField = new double[N];
            _scalarMomentum = new double[N];

            // Hot start: random field values distributed according to temperature
            // Using Gaussian distribution with σ = √T
            double sigma = Math.Sqrt(temperature);
            for (int i = 0; i < N; i++)
            {
                // Box-Muller for Gaussian random numbers
                double u1 = _rng.NextDouble();
                double u2 = _rng.NextDouble();
                double z = Math.Sqrt(-2.0 * Math.Log(u1 + 1e-10)) * Math.Cos(2.0 * Math.PI * u2);

                ScalarField[i] = z * sigma;
                _scalarMomentum[i] = z * sigma * 0.5; // Also random initial momentum
            }
        }

        /// <summary>
        /// Mark nodes as excited if the magnitude of the scalar field
        /// amplitude exceeds the configured threshold.  This provides a
        /// coupling between field theory and the excitable medium dynamics.
        /// </summary>
        private void UpdateFieldExcitations()
        {
            if (ScalarField == null || ScalarField.Length != N) return;
            for (int i = 0; i < N; i++)
            {
                double absPhi = Math.Abs(ScalarField[i]);
                if (absPhi > FieldExcitationThreshold)
                {
                    // Only excite Rest nodes; refrain from overriding
                    if (State[i] == NodeState.Rest)
                    {
                        State[i] = NodeState.Excited;
                        // deposit energy for field-induced excitation
                        if (_nodeEnergy != null && i < _nodeEnergy.Length)
                            _nodeEnergy[i] += absPhi;
                    }
                }
            }
        }

        /// <summary>
        /// Computes the total energy stored in the scalar field.  The energy
        /// consists of kinetic, gradient and potential contributions summed
        /// over all nodes.  This diagnostic is useful to monitor energy
        /// conservation and to study field excitations.
        /// 
        /// Uses Gauge Covariant Gradient Energy (Checklist E.1):
        /// E_grad = ½ Σ w_ij |D_ij φ|² = ½ Σ w_ij |φ_j e^{iθ_ij} - φ_i|²
        /// For real scalar: = ½ Σ w_ij (φ_i² + φ_j² - 2φ_i φ_j cos(θ_ij))
        /// 
        /// Supports both standard Klein-Gordon and Mexican Hat (Higgs) potentials.
        /// </summary>
        /// <returns>Total scalar field energy.</returns>
        public double ComputeScalarFieldEnergy()
        {
            if (ScalarField == null || ScalarField.Length != N) return 0.0;
            if (_scalarMomentum == null || _scalarMomentum.Length != N) return 0.0;

            double energy = 0.0;

            // Kinetic: 1/2 Σ π_i²
            for (int i = 0; i < N; i++)
            {
                energy += 0.5 * _scalarMomentum[i] * _scalarMomentum[i];
            }

            // Gauge Covariant Gradient Energy (Checklist E.1):
            // E_grad = ½ Σ w_ij |D_ij φ|² where D_ij φ = φ_j e^{iθ_ij} - φ_i
            // For real scalar field: |D_ij φ|² = (cos(θ)φ_j - φ_i)² + (sin(θ)φ_j)²
            //                                  = φ_i² + φ_j² - 2φ_i φ_j cos(θ_ij)
            // Each edge counted twice; divide by 2
            for (int i = 0; i < N; i++)
            {
                double phi_i = ScalarField[i];
                foreach (int j in Neighbors(i))
                {
                    double w = Weights[i, j];
                    double phi_j = ScalarField[j];
                    double theta_ij = GetEdgeGaugePhase(i, j);

                    // |D_ij φ|² = φ_i² + φ_j² - 2φ_i φ_j cos(θ_ij)
                    double covariantGradSq = phi_i * phi_i + phi_j * phi_j
                                           - 2.0 * phi_i * phi_j * Math.Cos(theta_ij);

                    energy += 0.25 * ScalarCoupling * w * covariantGradSq;
                }
            }

            // Potential energy depends on potential type
            for (int i = 0; i < N; i++)
            {
                double phi = ScalarField[i];

                if (UseMexicanHatPotential)
                {
                    // Mexican Hat: V(φ) = -μ²φ² + λφ⁴
                    // Note: This gives negative energy near minimum, which is correct
                    // The minima are at φ = ±v = ±√(μ²/(2λ)) with V(v) = -μ⁴/(4λ)
                    energy += -HiggsMuSquared * phi * phi + HiggsLambda * phi * phi * phi * phi;
                }
                else
                {
                    // Standard Klein-Gordon: V(φ) = ½m²φ² + ¼λφ⁴
                    energy += 0.5 * ScalarMass * ScalarMass * phi * phi;
                    if (ScalarSelfCoupling != 0.0)
                    {
                        energy += 0.25 * ScalarSelfCoupling * phi * phi * phi * phi;
                    }
                }
            }

            return energy;
        }
    }
}