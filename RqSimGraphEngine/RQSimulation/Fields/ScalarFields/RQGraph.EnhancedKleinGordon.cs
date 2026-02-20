using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace RQSimulation
{
    /// <summary>
    /// Enhanced Klein-Gordon dynamics with proper relativistic dispersion relation.
    /// Mass emerges from the correlation structure following RQ hypothesis.
    /// E² = p²c² + m²c⁴
    /// </summary>
    public partial class RQGraph
    {
        // Enhanced scalar field with velocity components
        private double[]? _scalarVelocityX;
        private double[]? _scalarVelocityY;

        // Relativistic energy-momentum
        private double[]? _relativEnergy;
        private double[]? _spatialMomentumMagnitude; // Replaces _momentumX/Y for coordinate independence

        // Effective mass field from correlations
        private double[]? _effectiveMass;

        // Field acceleration buffer
        private double[]? _scalarAcceleration;

        /// <summary>
        /// Initialize enhanced Klein-Gordon dynamics.
        /// </summary>
        public void InitEnhancedKleinGordon(double amplitude = 0.01)
        {
            // Use existing scalar field or create new
            if (ScalarField == null || ScalarField.Length != N)
            {
                ScalarField = new double[N];
                _scalarMomentum = new double[N];
            }

            _scalarVelocityX = new double[N];
            _scalarVelocityY = new double[N];
            _relativEnergy = new double[N];
            _spatialMomentumMagnitude = new double[N];
            _effectiveMass = new double[N];
            _scalarAcceleration = new double[N];

            // Initialize fields
            for (int i = 0; i < N; i++)
            {
                ScalarField[i] = (_rng.NextDouble() - 0.5) * amplitude;
                _scalarMomentum[i] = 0;

                // Mass from local correlation structure
                _effectiveMass[i] = ComputeEffectiveMassFromCorrelation(i);

                // Initialize with rest energy E = mc²
                double c = VectorMath.SpeedOfLight;
                _relativEnergy[i] = _effectiveMass[i] * c * c;
            }
        }

        /// <summary>
        /// Computes effective mass from local correlation density.
        /// Mass emerges from the strength of correlations (Higgs-like mechanism).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ComputeEffectiveMassFromCorrelation(int node)
        {
            double correlationSum = 0;
            int deg = 0;

            foreach (int nb in Neighbors(node))
            {
                correlationSum += Weights[node, nb] * Weights[node, nb];
                deg++;
            }

            if (deg == 0) return ScalarMass;

            // Mass proportional to sqrt of total correlation strength
            // This implements m ∝ g*v where v is the "VEV" of correlations
            double couplingConstant = 0.1;
            double vev = Math.Sqrt(correlationSum / deg);

            return ScalarMass + couplingConstant * vev;
        }

        /// <summary>
        /// Updates the effective mass field from current correlations.
        /// </summary>
        public void UpdateEffectiveMasses()
        {
            if (_effectiveMass == null) return;

            for (int i = 0; i < N; i++)
            {
                _effectiveMass[i] = ComputeEffectiveMassFromCorrelation(i);
            }
        }

        /// <summary>
        /// Evolves Klein-Gordon field with proper relativistic dynamics.
        /// Uses symplectic integration for energy conservation.
        /// </summary>
        public void UpdateEnhancedKleinGordon(double dt)
        {
            if (ScalarField == null) InitEnhancedKleinGordon();

            // First: compute forces (negative gradient of potential)
            ComputeKleinGordonForces();

            // Symplectic Euler step (momentum first, then position)
            int i = 0;
            if (Avx.IsSupported && N >= 4)
            {
                unsafe
                {
                    fixed (double* pMom = _scalarMomentum,
                                   pAcc = _scalarAcceleration,
                                   pField = ScalarField,
                                   pMass = _effectiveMass)
                    {
                        var vDt = Vector256.Create(dt);

                        for (; i <= N - 4; i += 4)
                        {
                            // p += F * dt
                            var mom = Avx.LoadVector256(pMom + i);
                            var acc = Avx.LoadVector256(pAcc + i);
                            var newMom = Avx.Add(mom, Avx.Multiply(acc, vDt));
                            Avx.Store(pMom + i, newMom);

                            // φ += π/m * dt
                            var mass = Avx.LoadVector256(pMass + i);
                            var field = Avx.LoadVector256(pField + i);
                            var velocity = Avx.Divide(newMom, mass);
                            var newField = Avx.Add(field, Avx.Multiply(velocity, vDt));
                            Avx.Store(pField + i, newField);
                        }
                    }
                }
            }

            // Scalar fallback for remainder
            for (; i < N; i++)
            {
                double m = _effectiveMass![i];
                if (m < 1e-10) m = ScalarMass;

                // Update momentum: π += F * dt
                _scalarMomentum[i] += _scalarAcceleration![i] * dt;

                // Update field: φ += π/m * dt
                ScalarField![i] += (_scalarMomentum[i] / m) * dt;
            }

            // Update energy-momentum relation
            UpdateRelativisticEnergy();

            // Update node excitations
            UpdateKleinGordonExcitations();
        }

        /// <summary>
        /// Computes Klein-Gordon forces from field gradients.
        /// F = ∇²φ - m²c²φ - λφ³ (including self-interaction)
        /// </summary>
        private void ComputeKleinGordonForces()
        {
            if (ScalarField == null || _scalarAcceleration == null || _effectiveMass == null)
                return;

            double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;

            for (int i = 0; i < N; i++)
            {
                double phi = ScalarField[i];
                double m = _effectiveMass[i];
                double m2c2 = m * m * c2;

                // Laplacian term: coupling * Σ(φ_j - φ_i)
                double laplacian = 0;
                int neighborCount = 0;
                foreach (int j in Neighbors(i))
                {
                    double w = Weights[i, j];
                    laplacian += w * (ScalarField[j] - phi);
                    neighborCount++;
                }
                laplacian *= ScalarCoupling;

                // Mass term: -m²c²φ
                double massTerm = -m2c2 * phi;

                // Self-interaction: -λφ³
                double selfInteraction = 0;
                if (ScalarSelfCoupling != 0)
                {
                    selfInteraction = -ScalarSelfCoupling * phi * phi * phi;
                }

                // Gravitational coupling to curvature
                double gravityCoupling = 0;
                if (_correlationMass != null && _correlationMass.Length == N)
                {
                    double localCurvature = GetLocalCurvature(i);
                    gravityCoupling = -0.1 * localCurvature * phi;
                }

                _scalarAcceleration[i] = laplacian + massTerm + selfInteraction + gravityCoupling;
            }
        }

        /// <summary>
        /// Updates relativistic energy and momentum from field values.
        /// E² = p²c² + m²c⁴
        /// RQ-Hypothesis Compliant: Uses graph gradients instead of coordinates.
        /// </summary>
        private void UpdateRelativisticEnergy()
        {
            if (_relativEnergy == null || _spatialMomentumMagnitude == null)
                return;

            double c = VectorMath.SpeedOfLight;
            double c2 = c * c;
            double c4 = c2 * c2;

            for (int i = 0; i < N; i++)
            {
                // Compute spatial momentum magnitude from field gradients on graph
                // p² ≈ (∇φ)² ≈ Σ w_ij (φ_j - φ_i)²
                double p2 = 0;
                foreach (int j in Neighbors(i))
                {
                    double dPhi = ScalarField![j] - ScalarField[i];
                    double w = Weights[i, j];
                    // Gradient contribution weighted by link strength
                    // In continuum limit: (dPhi/dx)^2. On graph: w * dPhi^2
                    p2 += w * dPhi * dPhi;
                }

                _spatialMomentumMagnitude[i] = Math.Sqrt(p2);

                // Relativistic energy-momentum relation
                // Total momentum includes conjugate momentum π (time component) and spatial gradient
                // But usually E² = π²c² + (∇φ)²c² + m²c⁴ ?
                // Hamiltonian density H = 1/2 π² + 1/2 (∇φ)² + 1/2 m²φ²
                // Here we want particle-like energy E = sqrt(p²c² + m²c⁴)
                // We treat field excitations as particles.
                
                double m = _effectiveMass![i];
                double m2c4 = m * m * c4;

                // Total energy density at node
                _relativEnergy[i] = Math.Sqrt(p2 * c2 + m2c4);
            }
        }

        /// <summary>
        /// Updates node excitations based on field energy density.
        /// </summary>
        private void UpdateKleinGordonExcitations()
        {
            if (ScalarField == null || _scalarMomentum == null) return;

            for (int i = 0; i < N; i++)
            {
                double phi = ScalarField[i];
                double pi = _scalarMomentum[i];

                // Energy density: (1/2)(π² + (∇φ)² + m²φ²)
                double kineticEnergy = 0.5 * pi * pi;

                double gradientEnergy = 0;
                foreach (int j in Neighbors(i))
                {
                    double dPhi = ScalarField[j] - phi;
                    gradientEnergy += 0.5 * ScalarCoupling * dPhi * dPhi;
                }

                double m = _effectiveMass != null ? _effectiveMass[i] : ScalarMass;
                double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;
                double potentialEnergy = 0.5 * m * m * c2 * phi * phi;

                double totalEnergy = kineticEnergy + gradientEnergy + potentialEnergy;

                // Excite nodes with energy above threshold
                if (totalEnergy > FieldExcitationThreshold)
                {
                    if (State[i] == NodeState.Rest)
                    {
                        State[i] = NodeState.Excited;
                    }
                }
            }
        }

        /// <summary>
        /// Computes the stress-energy tensor component T_00 (energy density).
        /// </summary>
        public double StressEnergyT00(int node)
        {
            if (ScalarField == null || _scalarMomentum == null || _effectiveMass == null)
                return 0;

            double phi = ScalarField[node];
            double pi = _scalarMomentum[node];
            double m = _effectiveMass[node];
            double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;

            // T_00 = (1/2)(π² + (∇φ)² + m²c²φ²)
            double T00 = 0.5 * pi * pi;

            foreach (int j in Neighbors(node))
            {
                double dPhi = ScalarField[j] - phi;
                T00 += 0.5 * ScalarCoupling * dPhi * dPhi;
            }

            T00 += 0.5 * m * m * c2 * phi * phi;

            return T00;
        }

        /// <summary>
        /// Computes the total field energy.
        /// </summary>
        public double TotalKleinGordonEnergy()
        {
            double total = 0;
            for (int i = 0; i < N; i++)
            {
                total += StressEnergyT00(i);
            }
            return total;
        }

        /// <summary>
        /// Computes dispersion relation ω(k) from field modes.
        /// ω² = k²c² + m²c⁴/ℏ²
        /// </summary>
        public (double[] frequencies, double[] wavenumbers) ComputeDispersionRelation()
        {
            if (ScalarField == null || N == 0)
                return (Array.Empty<double>(), Array.Empty<double>());

            var frequencies = new double[N];
            var wavenumbers = new double[N];

            double c = VectorMath.SpeedOfLight;
            double c2 = c * c;
            double hbar = VectorMath.HBar;

            for (int i = 0; i < N; i++)
            {
                // Wavenumber from spatial frequency (local)
                double kSquared = 0;
                foreach (int j in Neighbors(i))
                {
                    double dPhi = ScalarField[j] - ScalarField[i];
                    double d = GetPhysicalDistance(i, j);
                    if (d > 1e-10)
                    {
                        kSquared += (dPhi / d) * (dPhi / d);
                    }
                }
                wavenumbers[i] = Math.Sqrt(kSquared);

                // Frequency from dispersion relation
                double m = _effectiveMass != null ? _effectiveMass[i] : ScalarMass;
                double m2c4_hbar2 = (m * m * c2 * c2) / (hbar * hbar);
                double omega2 = kSquared * c2 + m2c4_hbar2;
                frequencies[i] = Math.Sqrt(omega2);
            }

            return (frequencies, wavenumbers);
        }

        /// <summary>
        /// Applies a Lorentz boost to the scalar field (for relativistic transformations).
        /// RQ-Hypothesis: Removed as it relies on external coordinate embedding.
        /// </summary>
        public void BoostScalarField(double velocity)
        {
            // Removed to comply with RQ-Hypothesis (no external coordinates)
        }

        /// <summary>
        /// Computes the group velocity of field propagation.
        /// v_g = ∂ω/∂k = kc²/ω
        /// </summary>
        public double GroupVelocity(int node)
        {
            var (frequencies, wavenumbers) = ComputeDispersionRelation();
            if (node >= frequencies.Length || frequencies[node] < 1e-10)
                return 0;

            double k = wavenumbers[node];
            double omega = frequencies[node];
            double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;

            return k * c2 / omega;
        }

        /// <summary>
        /// Computes the phase velocity of field propagation.
        /// v_p = ω/k
        /// </summary>
        public double PhaseVelocity(int node)
        {
            var (frequencies, wavenumbers) = ComputeDispersionRelation();
            if (node >= wavenumbers.Length || wavenumbers[node] < 1e-10)
                return VectorMath.SpeedOfLight;

            return frequencies[node] / wavenumbers[node];
        }

        /// <summary>
        /// Gets the effective mass at a node.
        /// </summary>
        public double GetEffectiveMass(int node)
        {
            return _effectiveMass != null && node < _effectiveMass.Length
                ? _effectiveMass[node]
                : ScalarMass;
        }

        // Update scalar field (Klein-Gordon) with parallel loops
        public void UpdateScalarFieldParallel(double dt)
        {
            if (ScalarField == null || _scalarMomentum == null) return;

            Parallel.For(0, N, i =>
            {
                double laplacian = 0.0;
                foreach (int j in Neighbors(i))
                {
                    laplacian += Weights[i, j] * (ScalarField[j] - ScalarField[i]);
                }

                double phi = ScalarField[i];
                double potentialGrad = PhysicsConstants.HiggsLambda * phi * phi * phi
                                     - PhysicsConstants.HiggsMuSquared * phi;
                double force = PhysicsConstants.FieldDiffusionRate * laplacian - potentialGrad;
                _scalarMomentum![i] += dt * force;
            });

            Parallel.For(0, N, i =>
            {
                ScalarField![i] += dt * _scalarMomentum![i];
            });
        }
    }
}
