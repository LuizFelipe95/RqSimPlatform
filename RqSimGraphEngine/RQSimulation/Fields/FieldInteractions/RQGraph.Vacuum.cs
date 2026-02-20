using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RQSimulation
{
    /// <summary>
    /// Implements quantum vacuum fluctuations and Casimir-like effects in the RQ framework.
    /// Vacuum energy arises from zero-point fluctuations of the correlation field.
    /// </summary>
    public partial class RQGraph
    {
        // Vacuum energy density per node
        private double[]? _vacuumEnergy;

        // Zero-point fluctuation amplitudes
        private double[]? _zeroPointAmplitude;

        // Casimir pressure between node pairs
        private double[,]? _casimirPressure;

        // Vacuum polarization field
        private Complex[]? _vacuumPolarization;

        /// <summary>
        /// Read-only access to the per-node vacuum energy density array.
        /// Returns empty span if the vacuum field has not been initialized.
        /// </summary>
        public ReadOnlySpan<double> VacuumEnergyField =>
            _vacuumEnergy is not null ? _vacuumEnergy.AsSpan() : ReadOnlySpan<double>.Empty;

        /// <summary>
        /// Whether the vacuum energy field has been initialized.
        /// </summary>
        public bool IsVacuumFieldInitialized => _vacuumEnergy is not null;

        /// <summary>
        /// Ensures the vacuum energy field is initialized with zero-point energies.
        /// Safe to call multiple times — only initializes if not already done.
        ///
        /// Use this when vacuum energy data is needed for analysis
        /// even if UseVacuumFluctuations is disabled.
        /// </summary>
        public void EnsureVacuumFieldInitialized()
        {
            if (_vacuumEnergy is null)
            {
                InitVacuumField();
            }
        }

        /// <summary>
        /// Initialize vacuum field with zero-point fluctuations.
        /// </summary>
        public void InitVacuumField()
        {
            _vacuumEnergy = new double[N];
            _zeroPointAmplitude = new double[N];
            _vacuumPolarization = new Complex[N];
            _casimirPressure = new double[N, N];

            for (int i = 0; i < N; i++)
            {
                // Zero-point energy: E_0 = (1/2)ℏω where ω is from local connectivity
                int deg = Degree(i);
                double omega = Math.Max(1.0, deg);  // Natural frequency from degree
                _vacuumEnergy[i] = 0.5 * VectorMath.HBar * omega;

                // Fluctuation amplitude ~ sqrt(ℏ/2mω)
                double mass = _correlationMass != null ? Math.Max(0.1, _correlationMass[i]) : 1.0;
                _zeroPointAmplitude[i] = Math.Sqrt(VectorMath.HBar / (2.0 * mass * omega));

                // Random vacuum polarization phase
                double phase = _rng.NextDouble() * 2.0 * Math.PI;
                _vacuumPolarization[i] = Complex.FromPolarCoordinates(0.01, phase);
            }
        }

        /// <summary>
        /// Updates vacuum fluctuations stochastically.
        /// </summary>
        public void UpdateVacuumFluctuations()
        {
            if (_vacuumEnergy == null) InitVacuumField();

            for (int i = 0; i < N; i++)
            {
                // Quantum fluctuation: Gaussian with variance = zero-point amplitude
                double sigma = _zeroPointAmplitude![i];
                double fluctuation = SampleGaussian(0, sigma);

                // Update vacuum energy with fluctuation
                _vacuumEnergy![i] += 0.1 * fluctuation;

                // Ensure positive vacuum energy (can be negative for Casimir effect)
                if (_vacuumEnergy[i] < -1.0) _vacuumEnergy[i] = -1.0;
                if (_vacuumEnergy[i] > 10.0) _vacuumEnergy[i] = 10.0;

                // Update vacuum polarization phase
                double dPhase = 0.05 * _rng.NextDouble();
                _vacuumPolarization![i] *= Complex.FromPolarCoordinates(1.0, dPhase);
            }
        }



        /// <summary>
        /// Samples from a Gaussian distribution using Box-Muller transform.
        /// </summary>
        private double SampleGaussian(double mean, double stdDev)
        {
            // Use rejection sampling to avoid edge cases with 0 or 1
            double u1, u2;
            do
            {
                u1 = _rng.NextDouble();
            } while (u1 <= 1e-15); // Avoid log(0)
            u2 = _rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        /// <summary>
        /// Computes the Casimir pressure between two nodes based on their separation
        /// and the correlation "plates" formed by edge structure.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ComputeCasimirPressure(int i, int j)
        {
            if (Coordinates == null) return 0;

            double d = GetPhysicalDistance(i, j);
            if (d < 0.01) return 0;

            // Casimir pressure: P = -π²ℏc/(240 * d⁴)
            // Sign convention: negative = attractive
            double hbarc = VectorMath.HBar * VectorMath.SpeedOfLight;
            double d4 = d * d * d * d;

            // Scale factor for lattice
            double scaleFactor = 0.001;

            return -scaleFactor * Math.PI * Math.PI * hbarc / (240.0 * d4);
        }

        /// <summary>
        /// Updates all Casimir pressures between neighboring nodes.
        /// </summary>
        public void UpdateCasimirPressures()
        {
            if (_casimirPressure == null) InitVacuumField();

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    double pressure = ComputeCasimirPressure(i, j);
                    _casimirPressure![i, j] = pressure;
                    _casimirPressure[j, i] = pressure;
                }
            }
        }

        /// <summary>
        /// Computes the total vacuum energy contribution.
        /// </summary>
        public double TotalVacuumEnergy()
        {
            if (_vacuumEnergy == null) return 0;

            double sum = 0;
            for (int i = 0; i < N; i++)
            {
                sum += _vacuumEnergy[i];
            }
            return sum;
        }

        /// <summary>
        /// Gets the local vacuum energy density at a node.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVacuumEnergyDensity(int node)
        {
            if (_vacuumEnergy == null) return 0;
            return _vacuumEnergy[node];
        }

        /// <summary>
        /// Applies Casimir force to move nodes towards each other.
        /// </summary>
        public void ApplyCasimirForces(double dt)
        {
            if (Coordinates == null || _casimirPressure == null) return;

            var forces = new (double fx, double fy)[N];

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    double pressure = _casimirPressure[i, j];
                    if (Math.Abs(pressure) < 1e-10) continue;

                    double dx = Coordinates[j].X - Coordinates[i].X;
                    double dy = Coordinates[j].Y - Coordinates[i].Y;
                    double r = Math.Sqrt(dx * dx + dy * dy);
                    if (r < 0.01) continue;

                    // Force from pressure (negative pressure = attraction)
                    double forceMag = pressure;  // P * A where A ~ 1
                    double fx = forceMag * dx / r;
                    double fy = forceMag * dy / r;

                    forces[i].fx += fx;
                    forces[i].fy += fy;
                }
            }

            // Apply forces
            double damping = 0.9;
            for (int i = 0; i < N; i++)
            {
                Coordinates[i] = (
                    Coordinates[i].X + dt * forces[i].fx * damping,
                    Coordinates[i].Y + dt * forces[i].fy * damping
                );
            }
        }

        /// <summary>
        /// Computes virtual particle pair creation rate from vacuum energy density.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double VirtualPairCreationRate(int node)
        {
            if (_vacuumEnergy == null) return 0;

            double E = _vacuumEnergy[node];
            double threshold = 0.5;  // Minimum energy for pair creation

            if (E < threshold) return 0;

            // Rate ~ exp(-2m/ℏω) in Schwinger mechanism
            double omega = Math.Max(1.0, Degree(node));
            double m = _correlationMass != null ? _correlationMass[node] : 1.0;

            return Math.Exp(-2.0 * m / (VectorMath.HBar * omega));
        }

        /// <summary>
        /// Triggers spontaneous pair creation events from vacuum.
        /// </summary>
        public int TriggerVacuumPairCreation()
        {
            if (_vacuumEnergy == null) return 0;

            int pairsCreated = 0;

            for (int i = 0; i < N; i++)
            {
                double rate = VirtualPairCreationRate(i);
                if (_rng.NextDouble() < rate)
                {
                    // Create particle-antiparticle pair
                    // Find neighboring node for antiparticle
                    var neighbors = new System.Collections.Generic.List<int>();
                    foreach (int nb in Neighbors(i)) neighbors.Add(nb);

                    if (neighbors.Count > 0)
                    {
                        int partner = neighbors[_rng.Next(neighbors.Count)];

                        // Mark as temporary excitations
                        State[i] = NodeState.Excited;
                        State[partner] = NodeState.Excited;

                        // Reduce vacuum energy (conservation)
                        _vacuumEnergy[i] -= 0.5;

                        pairsCreated++;
                    }
                }
            }

            return pairsCreated;
        }

        /// <summary>
        /// Computes vacuum polarization contribution to edge weights.
        /// Virtual pairs screen correlations at short distances.
        /// </summary>
        public void ApplyVacuumPolarization()
        {
            if (_vacuumPolarization == null) return;

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;

                    // Vacuum polarization screening
                    Complex pi = _vacuumPolarization[i];
                    Complex pj = _vacuumPolarization[j];
                    double screening = (pi * Complex.Conjugate(pj)).Real;

                    // Modify edge weight slightly
                    double w = Weights[i, j];
                    double newW = w * (1.0 + 0.01 * screening);
                    newW = Math.Clamp(newW, 0.0, 1.0);

                    Weights[i, j] = newW;
                    Weights[j, i] = newW;
                }
            }
        }

        /// <summary>
        /// Computes the cosmological constant analog from average vacuum energy.
        /// </summary>
        public double ComputeCosmologicalConstant()
        {
            if (_vacuumEnergy == null) return 0;

            double totalVacuum = TotalVacuumEnergy();
            double volume = EstimateGraphVolume();

            if (volume < 1e-10) return 0;

            // Λ = 8πG * ρ_vacuum / c²
            double rhoVacuum = totalVacuum / volume;
            double G = 1.0;  // In natural units
            double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;

            return 8.0 * Math.PI * G * rhoVacuum / c2;
        }

        /// <summary>
        /// Estimates the effective volume of the graph from coordinate extent.
        /// </summary>
        private double EstimateGraphVolume()
        {
            if (Coordinates == null || N == 0) return 1.0;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int i = 0; i < N; i++)
            {
                var (x, y) = Coordinates[i];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            double dx = Math.Max(0.1, maxX - minX);
            double dy = Math.Max(0.1, maxY - minY);

            return dx * dy;  // 2D "volume"
        }

        /// <summary>
        /// Simulates vacuum decay (false vacuum transition).
        /// Rare catastrophic events where local vacuum collapses to lower energy state.
        /// </summary>
        public bool CheckVacuumDecay()
        {
            if (_vacuumEnergy == null) return false;

            // Find highest vacuum energy node (most unstable)
            int maxNode = 0;
            double maxEnergy = _vacuumEnergy[0];
            for (int i = 1; i < N; i++)
            {
                if (_vacuumEnergy[i] > maxEnergy)
                {
                    maxEnergy = _vacuumEnergy[i];
                    maxNode = i;
                }
            }

            // Decay probability increases with energy above threshold
            double threshold = 5.0;
            if (maxEnergy < threshold) return false;

            double decayProb = 1e-6 * (maxEnergy - threshold);
            if (_rng.NextDouble() > decayProb) return false;

            // Vacuum decay occurs! Cascade through neighbors
            TriggerVacuumDecayCascade(maxNode);
            return true;
        }

        /// <summary>
        /// Triggers a vacuum decay cascade from a nucleation point.
        /// </summary>
        private void TriggerVacuumDecayCascade(int nucleationPoint)
        {
            if (_vacuumEnergy == null) return;

            var visited = new System.Collections.Generic.HashSet<int>();
            var queue = new System.Collections.Generic.Queue<int>();

            queue.Enqueue(nucleationPoint);
            visited.Add(nucleationPoint);

            double decayEnergy = 2.0;  // Energy released per node

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                // Decay this node's vacuum
                _vacuumEnergy[current] = Math.Max(0, _vacuumEnergy[current] - decayEnergy);

                // Excite the node from energy release
                State[current] = NodeState.Excited;
                if (_nodeEnergy != null && current < _nodeEnergy.Length)
                {
                    _nodeEnergy[current] += decayEnergy;
                }

                // Propagate to neighbors with probability
                foreach (int nb in Neighbors(current))
                {
                    if (visited.Contains(nb)) continue;

                    double propagateProb = 0.5;  // 50% chance to spread
                    if (_rng.NextDouble() < propagateProb)
                    {
                        visited.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
            }
        }
    }
}
