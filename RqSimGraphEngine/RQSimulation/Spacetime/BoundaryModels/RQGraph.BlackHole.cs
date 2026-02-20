using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RQSimulation
{
    /// <summary>
    /// Implements black hole physics based on correlation density singularities.
    /// When correlation density exceeds a critical threshold, an event horizon forms
    /// and Hawking-like radiation emerges from the boundary.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Represents a detected black hole region in the graph.
        /// </summary>
        public class BlackHoleRegion
        {
            public int CenterNode { get; init; }
            public List<int> InteriorNodes { get; init; } = new();
            public List<int> HorizonNodes { get; init; } = new();
            public double Mass { get; set; }
            public double SchwarzschildRadius { get; set; }
            public double Temperature { get; set; }
            public double Entropy { get; set; }
            public double AngularMomentum { get; set; }
            public double Charge { get; set; }
        }

        // Critical correlation density for horizon formation
        public double HorizonDensityThreshold { get; set; } = 2.0;

        // List of detected black hole regions
        private List<BlackHoleRegion>? _blackHoles;

        // Hawking radiation accumulator
        private double[]? _hawkingRadiation;

        // Information paradox tracking: entanglement with interior
        private double[,]? _horizonEntanglement;

        // Thermal energy (kinetic) for detached nodes
        private double[]? _thermalEnergy;

        /// <summary>
        /// Initialize black hole detection and Hawking radiation tracking.
        /// </summary>
        public void InitBlackHolePhysics()
        {
            _blackHoles = new List<BlackHoleRegion>();
            _hawkingRadiation = new double[N];
            _horizonEntanglement = new double[N, N];
            _thermalEnergy = new double[N];
        }

        /// <summary>
        /// Detects black hole regions based on correlation density exceeding threshold.
        /// </summary>
        public List<BlackHoleRegion> DetectBlackHoles()
        {
            if (_blackHoles == null) InitBlackHolePhysics();
            _blackHoles!.Clear();

            // Find nodes with supercritical correlation density
            var supercritical = new HashSet<int>();
            for (int i = 0; i < N; i++)
            {
                double density = GetLocalCorrelationDensity(i);
                if (density >= HorizonDensityThreshold)
                {
                    supercritical.Add(i);
                }
            }

            if (supercritical.Count == 0) return _blackHoles;

            // Cluster supercritical nodes into black hole regions
            var visited = new HashSet<int>();
            foreach (int seed in supercritical)
            {
                if (visited.Contains(seed)) continue;

                var bh = new BlackHoleRegion { CenterNode = seed };
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                visited.Add(seed);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    bh.InteriorNodes.Add(current);

                    foreach (int nb in Neighbors(current))
                    {
                        if (visited.Contains(nb)) continue;

                        if (supercritical.Contains(nb))
                        {
                            visited.Add(nb);
                            queue.Enqueue(nb);
                        }
                        else
                        {
                            // Boundary node = horizon
                            bh.HorizonNodes.Add(nb);
                        }
                    }
                }

                // Compute black hole properties
                ComputeBlackHoleProperties(bh);
                _blackHoles.Add(bh);
            }

            return _blackHoles;
        }

        /// <summary>
        /// Computes physical properties of a black hole region.
        /// Uses spectral (graph-based) coordinates instead of external coordinates
        /// to comply with RQ-hypothesis principles.
        /// </summary>
        private void ComputeBlackHoleProperties(BlackHoleRegion bh)
        {
            // Mass from correlation mass of interior
            double totalMass = 0;
            foreach (int node in bh.InteriorNodes)
            {
                double m = _correlationMass != null ? _correlationMass[node] : 1.0;
                totalMass += m;
            }
            bh.Mass = totalMass;

            // Center of mass using spectral coordinates (RQ-compliant)
            var (cx, cy, cz) = ComputeSpectralCenterOfMassWeighted(bh.InteriorNodes);

            // Schwarzschild radius: r_s = 2GM/c²
            double G = 1.0;
            double c2 = VectorMath.SpeedOfLight * VectorMath.SpeedOfLight;
            bh.SchwarzschildRadius = 2.0 * G * totalMass / c2;

            // Hawking temperature: T_H = ℏc³/(8πGMk_B)
            // In natural units with k_B = 1:
            double c3 = VectorMath.SpeedOfLight * c2;
            bh.Temperature = totalMass > 0.1
                ? VectorMath.HBar * c3 / (8.0 * Math.PI * G * totalMass)
                : 100.0;  // Small black holes are very hot

            // Bekenstein-Hawking entropy: S = A/(4l_P²) where A = 4πr_s²
            // In natural units: S = π r_s² / (G ℏ)
            double rs = bh.SchwarzschildRadius;
            bh.Entropy = Math.PI * rs * rs / (G * VectorMath.HBar);

            // Angular momentum from cluster rotation (using spectral coordinates)
            bh.AngularMomentum = ComputeClusterAngularMomentumSpectral(bh.InteriorNodes, cx, cy);

            // Electric charge from node charges
            bh.Charge = 0;
            if (_charges != null)
            {
                foreach (int node in bh.InteriorNodes)
                {
                    if (node < _charges.Length)
                        bh.Charge += _charges[node];
                }
            }
        }

        /// <summary>
        /// Computes angular momentum of a cluster using spectral coordinates.
        /// RQ-compliant: uses only graph-derived coordinates.
        /// </summary>
        private double ComputeClusterAngularMomentumSpectral(List<int> nodes, double cx, double cy)
        {
            if (_correlationMass == null) return 0;
            
            // Ensure spectral coordinates are computed
            if (_spectralX == null || _spectralX.Length != N)
            {
                UpdateSpectralCoordinates();
            }

            double L = 0;
            foreach (int node in nodes)
            {
                if (node < 0 || node >= N) continue;
                
                double rx = _spectralX![node] - cx;
                double ry = (_spectralY != null && node < _spectralY.Length ? _spectralY[node] : 0) - cy;

                // Approximate velocity from proper time gradient (relational)
                double vx = 0.1 * ry;  // Rough rotational velocity
                double vy = -0.1 * rx;

                double m = node < _correlationMass.Length ? _correlationMass[node] : 1.0;
                L += m * (rx * vy - ry * vx);
            }
            return L;
        }

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 4: Emergent Unruh-Hawking Temperature
        /// ====================================================================
        /// Computes local temperature from gradient of lapse function (acceleration).
        /// 
        /// PHYSICS:
        /// In curved spacetime, accelerated observers see thermal radiation at T = |a|/(2π)
        /// where |a| is proper acceleration. On a graph:
        /// 
        ///   T_i = |∇N_i| / (2π)
        /// 
        /// where N_i = lapse function at node i, and ∇N is the "acceleration" 
        /// (rate of change of proper time flow).
        /// 
        /// Near black hole horizons, N → 0 and |∇N| → ∞, giving high temperature.
        /// In vacuum, N ≈ const, |∇N| ≈ 0, giving T ≈ 0 (no radiation).
        /// 
        /// This replaces scripted Hawking radiation with emergent phenomenon.
        /// </summary>
        /// <param name="node">Node index</param>
        /// <returns>Local Unruh-Hawking temperature</returns>
        public double GetLocalUnruhTemperature(int node)
        {
            if (node < 0 || node >= N)
                return 0.0;
                
            double lapseNode = GetLocalLapse(node);
            double gradientN = 0.0;
            int neighborCount = 0;
            
            foreach (int neighbor in Neighbors(node))
            {
                double lapseNeighbor = GetLocalLapse(neighbor);
                double dN = lapseNode - lapseNeighbor;
                
                // Geodesic distance from edge weight
                double weight = Weights[node, neighbor];
                double dist = weight > 1e-10 ? -Math.Log(weight) : 10.0; // Regularized
                
                // Accumulate gradient magnitude
                double localGrad = Math.Abs(dN) / (dist + 0.1); // Regularize for short distances
                gradientN += localGrad;
                neighborCount++;
            }
            
            if (neighborCount == 0)
                return 0.0;
                
            // Average gradient
            gradientN /= neighborCount;
            
            // Unruh-Hawking temperature: T = |∇N| / (2π)
            // In natural units with ℏ = c = k_B = 1
            double temperature = gradientN / (2.0 * Math.PI);
            
            return temperature;
        }
        
        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 4: Probabilistic Particle Pair Creation
        /// ======================================================================
        /// Computes probability of spontaneous particle-antiparticle pair creation
        /// based on local Unruh-Hawking temperature.
        /// 
        /// PHYSICS:
        /// The vacuum is unstable near horizons due to virtual pair separation.
        /// The probability of pair creation is:
        /// 
        ///   P_pair = exp(-2πm / T)
        /// 
        /// where m = effective mass threshold and T = local temperature.
        /// 
        /// High temperature (strong curvature) → high pair creation rate
        /// Low temperature (flat space) → negligible pair creation
        /// 
        /// This is the emergent Hawking radiation mechanism.
        /// </summary>
        /// <param name="node">Node index</param>
        /// <param name="massThreshold">Effective mass for pair creation</param>
        /// <returns>Probability of pair creation at this node</returns>
        public double GetPairCreationProbability(int node, double massThreshold = 0.1)
        {
            double T = GetLocalUnruhTemperature(node);
            
            if (T < 1e-15)
                return 0.0;
                
            // Boltzmann factor for particle production
            // P = exp(-2π m_eff / T) = exp(-m_eff / T_Unruh)
            double probability = Math.Exp(-2.0 * Math.PI * massThreshold / T);
            
            return Math.Min(probability, 1.0);
        }
        
        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 4: Process Emergent Hawking Radiation
        /// ===================================================================
        /// Evolves the vacuum state with probabilistic pair creation near horizons.
        /// 
        /// Called from the main physics step. Replaces scripted EmitHawkingRadiation().
        /// 
        /// Energy conservation: pair creation energy comes from the gravitational field
        /// (edge weights decrease → curvature energy converts to particle mass).
        /// </summary>
        public void ProcessEmergentHawkingRadiation()
        {
            if (_rng == null) return;
            
            for (int i = 0; i < N; i++)
            {
                double probability = GetPairCreationProbability(i, PhysicsConstants.PairCreationMassThreshold);
                
                if (_rng.NextDouble() < probability)
                {
                    // Spontaneous pair creation!
                    CreateParticlePair(i);
                }
            }
        }
        
        /// <summary>
        /// Creates a particle-antiparticle pair at a node.
        /// Energy is extracted from local curvature (edge weights).
        /// </summary>
        private void CreateParticlePair(int node)
        {
            if (_hawkingRadiation != null && node < _hawkingRadiation.Length)
            {
                // Record radiation event
                _hawkingRadiation[node] += GetLocalUnruhTemperature(node);
            }
            
            // Excite the node (represents particle creation)
            State[node] = NodeState.Excited;
            
            // Energy conservation: reduce local edge weights (curvature -> matter)
            // This is the backreaction on geometry
            double energyExtracted = PhysicsConstants.PairCreationEnergy;
            double totalWeight = 0.0;
            
            foreach (int neighbor in Neighbors(node))
            {
                totalWeight += Weights[node, neighbor];
            }
            
            if (totalWeight > 1e-10)
            {
                double fractionToRemove = Math.Min(energyExtracted / totalWeight, 0.1);
                foreach (int neighbor in Neighbors(node))
                {
                    double newWeight = Weights[node, neighbor] * (1.0 - fractionToRemove);
                    Weights[node, neighbor] = Math.Max(newWeight, PhysicsConstants.PlanckWeightThreshold);
                    Weights[neighbor, node] = Weights[node, neighbor];
                }
            }
            
            // If spinor fields exist, create actual fermion excitation
            if (_spinorA != null && node < _spinorA.Length)
            {
                double T = GetLocalUnruhTemperature(node);
                double phase = _rng!.NextDouble() * 2.0 * Math.PI;
                double amplitude = Math.Sqrt(T * 0.1); // Thermal amplitude
                _spinorA[node] += new Complex(amplitude * Math.Cos(phase), amplitude * Math.Sin(phase));
            }
        }

        /// <summary>
        /// Evolve black holes using emergent dynamics (evaporation via node detachment).
        /// </summary>
        public void EvolveBlackHoles()
        {
            if (_blackHoles == null) return;

            for (int i = 0; i < _blackHoles.Count; i++)
            {
                var bh = _blackHoles[i];
                ProcessHorizonDynamics(i, bh.HorizonNodes);
            }
        }

        /// <summary>
        /// RQ-HYPOTHESIS CHECKLIST ITEM 5: Emergent Black Hole Evaporation
        /// ================================================================
        /// Replaces rigid Hawking formula with emergent node detachment mechanism.
        /// </summary>
        public void ProcessHorizonDynamics(int clusterId, List<int> horizonNodes)
        {
            if (_ricciScalar == null) return;

            foreach (var node in horizonNodes)
            {
                // Physics: On the horizon, "tension" of bonds (curvature) is maximal.
                // Quantum fluctuations can break the bond.
                // If a node loses its last bond to the BH, it becomes "radiation".
                
                double localTension = Math.Abs(_ricciScalar[node]);
                // Planck constant h_bar ~ 1 in natural units, but we use PhysicsConstants
                double h_bar = PhysicsConstants.HBar;
                
                // Break probability P ~ exp(-1 / (R * h))
                // High curvature R -> High probability (horizon instability)
                double breakProbability = Math.Exp(-1.0 / (localTension * h_bar + 1e-9));
                
                if (_rng.NextDouble() < breakProbability)
                {
                     // Detach node from BH cluster -> BH mass decrease naturally
                     DetachNodeFromCluster(node, clusterId);
                     
                     // Bond energy converts to kinetic energy (heat) of the free node
                     if (_thermalEnergy == null) _thermalEnergy = new double[N];
                     _thermalEnergy[node] += CalculateBondEnergy(node);
                }
            }
        }

        private void DetachNodeFromCluster(int node, int clusterId)
        {
            if (_blackHoles == null || clusterId < 0 || clusterId >= _blackHoles.Count) return;
            var bh = _blackHoles[clusterId];
            
            // Identify neighbors that are part of the black hole (interior or horizon)
            var interiorSet = new HashSet<int>(bh.InteriorNodes);
            interiorSet.UnionWith(bh.HorizonNodes);

            foreach (int neighbor in Neighbors(node))
            {
                if (interiorSet.Contains(neighbor))
                {
                    // Break the bond (set weight to 0 and queue for removal)
                    Weights[node, neighbor] = 0;
                    Weights[neighbor, node] = 0;
                    _gravityEdgeRemovalQueue.Enqueue((node, neighbor));
                }
            }
        }

        private double CalculateBondEnergy(int node)
        {
            double energy = 0;
            foreach (int neighbor in Neighbors(node))
            {
                energy += Weights[node, neighbor];
            }
            return energy;
        }
    }
}
