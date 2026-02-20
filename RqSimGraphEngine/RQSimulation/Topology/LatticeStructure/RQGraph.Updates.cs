using System;
using System.Threading.Tasks;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // New diagnostic fields
        private int _lastExcitedCount; // count of excited nodes after step
        private int _lastFlipCount;    // count of state changes in current step

        // Local invariants for probability calculations
        public double GetLocalCurvatureNorm(int i)
        {
            // Use degree-based curvature already available
            double k = GetLocalCurvature(i);
            // Approximate average curvature as mean deviation of degrees
            double sum = 0.0;
            for (int n = 0; n < N; n++) sum += Math.Abs(GetLocalCurvature(n));
            double avg = sum > 0 ? sum / N : 1.0;
            return avg == 0 ? 0.0 : k / avg;
        }

        public double GetLocalExcitedDensity(int i)
        {
            int cnt = 0, exc = 0;
            foreach (int j in Neighbors(i)) { cnt++; if (State[j] == NodeState.Excited) exc++; }
            return cnt == 0 ? 0.0 : (double)exc / cnt;
        }

        private double ComputeSpontaneousFlipProb(int node)
        {
            double kNorm = GetLocalCurvatureNorm(node);
            double p = 1.0 - Math.Exp(-kNorm);
            return Math.Clamp(p, 0.0, 0.999);
        }

        // Single excitable medium step with flip counting
        public void StepExcitableMedium()
        {
            if (State == null || State.Length != N) State = new NodeState[N];
            if (_nextState == null || _nextState.Length != N) _nextState = new NodeState[N];
            if (_refractoryCounter == null || _refractoryCounter.Length != N) _refractoryCounter = new int[N];

            int flips = 0;
            _lastExcitedCount = 0;
            for (int i = 0; i < N; i++)
            {
                var prev = State[i];
                var next = ComputeNextState(i, prev);
                _nextState[i] = next;
                if (next != prev) flips++;
                if (next == NodeState.Excited) _lastExcitedCount++;
            }
            for (int i = 0; i < N; i++)
            {
                State[i] = _nextState[i];
                if (ProperTime != null) ProperTime[i] += LocalTimeStep(i);
            }
            _lastFlipCount = flips;
            LastNodesFlipped = flips; // expose via existing public property

            UpdateCorrelationWeights();
        }

        private NodeState ComputeNextState(int i, NodeState current)
        {
            switch (current)
            {
                case NodeState.Rest:
                    return ComputeRestNextState(i);
                case NodeState.Excited:
                    double m = (_correlationMass != null && _correlationMass.Length == N) ? _correlationMass[i] : 0.0;
                    double avgM = _avgCorrelationMass > 0.0 ? _avgCorrelationMass : 1.0;
                    double x = avgM > 0 ? m / avgM : 0.0;
                    int extra = (int)Math.Round(x);
                    if (extra < 0) extra = 0;
                    _refractoryCounter[i] = DynamicBaseRefractorySteps + extra;
                    return NodeState.Refractory;
                case NodeState.Refractory:
                    int c = _refractoryCounter[i];
                    if (c <= 0)
                    {
                        _refractoryCounter[i] = 0;
                        return NodeState.Rest;
                    }
                    _refractoryCounter[i] = c - 1;
                    return NodeState.Refractory;
                default:
                    return current;
            }
        }

        private NodeState ComputeRestNextState(int i)
        {
            double pSpont = ComputeSpontaneousFlipProb(i);
            int excitedNeighbors = 0; double wSum = 0.0; int degLocal = 0;
            foreach (int j in Neighbors(i)) { degLocal++; if (!CanInfluence(j, i)) continue; if (State[j] == NodeState.Excited) { excitedNeighbors++; wSum += Weights[i, j]; } }
            double pFromNeighbors = 0.0;
            if (excitedNeighbors > 0)
            {
                double meanW = wSum / excitedNeighbors;
                var es = GetEdgeStats(); double refDeg = es.avgDegree > 0 ? es.avgDegree : 1.0;
                double sumWLocal = 0.0; int degWLocal = 0; foreach (int nb in Neighbors(i)) { sumWLocal += Weights[i, nb]; degWLocal++; }
                double refW = degWLocal > 0 ? sumWLocal / degWLocal : 1.0;
                double density = (double)excitedNeighbors / refDeg; double weightRatio = refW > 0 ? meanW / refW : 0.0; double arg = density * weightRatio;
                pFromNeighbors = 1.0 - Math.Exp(-arg);
            }
            double baseP = pSpont + pFromNeighbors;
            // fractal level boost
            int fl = FractalLevel != null && i < FractalLevel.Length ? FractalLevel[i] : 0;
            double fractalBoost = 1.0 + fl * 0.1;
            // local potential boost
            double potBoost = LocalPotential != null && i < LocalPotential.Length ? 1.0 + LocalPotential[i] : 1.0;
            // energy accumulator boost (checklist item 1 influence)
            double energyBoost = StoredEnergy != null ? 1.0 + 0.3 * StoredEnergy[i] : 1.0;
            // fractal gradient (item 3)
            double fractalGradient = 0.0; int nbCntFG = 0;
            foreach (int nb in Neighbors(i)) { fractalGradient += Math.Abs((FractalLevel?[i] ?? 0) - (FractalLevel?[nb] ?? 0)); nbCntFG++; }
            if (nbCntFG > 0) fractalGradient /= nbCntFG;
            double fractalGradientBoost = 1.0 + 0.05 * fractalGradient;
            // quantum amplitude boost
            double qBoost = 1.0;
            if (_waveMulti != null)
            {
                int d = GaugeDimension; double amp = 0.0;
                for (int a = 0; a < d; a++) amp += _waveMulti[i * d + a].Magnitude;
                qBoost = 1.0 + amp;
            }
            // coherence boost (item 8)
            double cohBoost = 1.0;
            if (_waveMulti != null && _qCoherence != null) cohBoost = 1.0 + 0.4 * _qCoherence[i];
            // nonlinear cluster density boost (quadratic positive feedback)
            double clusterDensity = 0.0; int nbCount = 0;
            foreach (int nb in Neighbors(i)) { nbCount++; if (State[nb] == NodeState.Excited) clusterDensity += 1.0; }
            clusterDensity = nbCount > 0 ? clusterDensity / nbCount : 0.0;
            double nonLinearBoost = 1.0 + clusterDensity * clusterDensity * 0.5;
            // local phase boost (engine updates NodePhase)
            double phaseBoost = 1.0;
            if (NodePhase != null && i < NodePhase.Length)
            {
                phaseBoost = NodePhase[i] switch
                {
                    LocalPhase.Cold => 0.8,
                    LocalPhase.Warm => 1.0,
                    LocalPhase.Hot => 1.2,
                    _ => 1.0
                };
            }
            // degree-based stochastic noise
            int deg = degLocal;
            double noise = _rng.NextDouble() * (1.0 / (1 + deg));
            // global regulator applied to baseP before combining
            // phase influence (checklist 6) applied to base probability prior to boosts
            if (CurrentPhase == GraphPhase.Active) baseP *= 1.15;
            else if (CurrentPhase == GraphPhase.Quiet) baseP *= 0.9;
            baseP *= GlobalExcitationRegulator;
            double p = baseP * fractalBoost * potBoost * energyBoost * fractalGradientBoost * qBoost * cohBoost * nonLinearBoost * phaseBoost + noise;
            // domain influence (checklist 3 & 8)
            double avgDomain = 0.0; if (Domain != null) { for (int k = 0; k < Domain.Length; k++) avgDomain += Domain[k]; avgDomain /= Math.Max(1, Domain.Length); }
            if (Domain != null) { double domainBoost = 1.0 + 0.03 * Math.Abs(Domain[i] - avgDomain); p *= domainBoost; }
            // triple nonlinearity (checklist 8)
            double damping = clusterDensity; // reuse clusterDensity as proxy
            if (_correlationMass != null) p = Math.Pow(p, 1.0 + 0.3 * _correlationMass[i]);
            p = p / (1.0 + 0.2 * damping);
            if (Domain != null) p *= (1.0 + 0.1 * Math.Abs(Domain[i] - avgDomain));
            p *= (1.0 + 0.2 * (_correlationMass != null ? _correlationMass[i] : 0.0)); // checklist 1 mass influence on decay
            p = Math.Clamp(p, 0.0, 1.0);
            bool excite = _rng.NextDouble() < p;
            if (excite && _correlationMass != null && LocalPotential != null && StoredEnergy != null)
            {
                if (LocalPotential[i] > 0.7 && StoredEnergy[i] > 0.3)
                    _correlationMass[i] += 0.01 * StoredEnergy[i];
            }
            return excite ? NodeState.Excited : NodeState.Rest;
        }

        private double GetLocalExcitedFraction(int i)
        {
            int cnt = 0, exc = 0;
            foreach (int j in Neighbors(i)) { cnt++; if (State[j] == NodeState.Excited) exc++; }
            return cnt == 0 ? 0.0 : (double)exc / cnt;
        }

        private (double pNoise, double pNeighbourBase) GetLocalProbabilities(int i)
        {
            double sumW = 0.0, sumW2 = 0.0; int deg = 0;
            for (int j = 0; j < N; j++) { double w = Weights[i, j]; if (w <= 0.0) continue; deg++; sumW += w; sumW2 += w * w; }
            if (deg == 0) return (0.0, 0.0);
            double mean = sumW / deg; double var = sumW2 / deg - mean * mean; if (var < 0.0) var = 0.0;
            double chi = Math.Sqrt(var) / (mean + 1e-12);
            double pNoise = chi / (1.0 + chi); double pNeighbourBase = mean / (1.0 + mean);
            if (pNoise < 0.0) pNoise = 0.0; if (pNoise > 0.5) pNoise = 0.5; if (pNeighbourBase < 0.0) pNeighbourBase = 0.0; if (pNeighbourBase > 0.9) pNeighbourBase = 0.9;
            return (pNoise, pNeighbourBase);
        }

        private double LocalTimeStep(int i)
        {
            const double baseDt = 1.0; // TODO: derive from invariants
            double m = _correlationMass != null && _correlationMass.Length == N ? _correlationMass[i] : ComputeNodeMass(i);
            double avgM = _avgCorrelationMass > 0 ? _avgCorrelationMass : 1.0;
            double x = m / avgM;
            return baseDt / (1.0 + x);
        }

        private bool CanInfluence(int from, int to)
        {
            if (ProperTime == null || EdgeDelay == null) return true;
            double dt = ProperTime[from] - ProperTime[to];
            double delay = EdgeDelay[from, to];
            return dt >= delay - 1e-9;
        }

        private double[,] _pairCorrelation; // sliding average joint excitations

        private void UpdatePairCorrelation()
        {
            if (_pairCorrelation == null || _pairCorrelation.GetLength(0) != N || _pairCorrelation.GetLength(1) != N)
                _pairCorrelation = new double[N, N];
            double alpha = 0.01; // TEMP: replace with function of variance of excitation distribution
            for (int i = 0; i < N; i++)
            {
                bool ei = State[i] == NodeState.Excited;
                foreach (int j in Neighbors(i))
                {
                    if (j <= i) continue;
                    bool ej = State[j] == NodeState.Excited;
                    double prev = _pairCorrelation[i, j];
                    double target = (ei && ej) ? 1.0 : 0.0;
                    double updated = (1.0 - alpha) * prev + alpha * target;
                    _pairCorrelation[i, j] = updated; _pairCorrelation[j, i] = updated;
                }
            }
        }

        private double GetGlobalQuantumTemperature()
        {
            double sum = 0.0; int count = 0;
            for (int i = 0; i < N; i++)
                for (int j = i + 1; j < N; j++)
                    if (Edges[i, j]) { sum += Weights[i, j]; count++; }
            if (count == 0) return 0.0;
            double avg = sum / count;
            return Math.Clamp(avg, 0.0, 1.0);
        }

        private void RenormaliseWeightsPerNode()
        {
            for (int i = 0; i < N; i++)
            {
                double sum = 0.0;
                for (int j = 0; j < N; j++) { double w = Weights[i, j]; if (w > 0.0) sum += w; }
                if (sum <= 0.0) continue;
                double inv = 1.0 / sum;
                for (int j = 0; j < N; j++)
                {
                    double w = Weights[i, j]; if (w > 0.0) { double nw = w * inv; Weights[i, j] = nw; Weights[j, i] = nw; }
                }
            }
        }

        public void UpdateCorrelationWeights()
        {
            if (N <= 0) return;
            double T = GetGlobalQuantumTemperature();
            // INCREASED Hebbian rate for stronger, faster clustering
            // Factor 2.0 instead of 1.0 for more aggressive weight strengthening
            double hebb = _hebbRate * (2.0 + T);
            // REDUCED decay significantly - weights should persist longer
            // Only apply decay proportional to (1-T), so hot systems decay less
            double decay = _decayRate * Math.Max(0.0, 0.3 - 0.2 * T);
            if (hebb < 0.0) hebb = 0.0; if (decay < 0.0) decay = 0.0;
            UpdatePairCorrelation();

            // SOC constants
            double socDrift = 0.008;
            double socTarget = 0.6;
            double oneMinusDecay = 1.0 - decay;

            // OPTIMIZED: Parallelize the weight update loop
            // Use Parallel.For with local state to avoid false sharing
            Parallel.For(0, N, () => 0, (i, loopState, localCount) =>
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    double w = Weights[i, j];
                    if (w <= 0.0) continue;

                    // Apply decay
                    w *= oneMinusDecay;

                    // Hebbian learning based on state correlation
                    var stateI = State[i];
                    var stateJ = State[j];

                    // Strengthen weight when both nodes are excited (Hebbian learning)
                    // INCREASED multiplier from 1.0 to 1.5 for faster strengthening
                    if (stateI == NodeState.Excited && stateJ == NodeState.Excited)
                    {
                        w += 1.5 * hebb * (1.0 - w);
                    }
                    // Also strengthen when one is excited and one is refractory (recent correlation)
                    // This captures "just-fired-together" correlations
                    else if ((stateI == NodeState.Excited && stateJ == NodeState.Refractory) ||
                             (stateI == NodeState.Refractory && stateJ == NodeState.Excited))
                    {
                        w += 0.8 * hebb * (1.0 - w);
                    }
                    // NEW: Also strengthen when both are refractory (fired together recently)
                    else if (stateI == NodeState.Refractory && stateJ == NodeState.Refractory)
                    {
                        w += 0.3 * hebb * (1.0 - w);
                    }

                    // Pair correlation regularization
                    double corr = _pairCorrelation?[i, j] ?? 0.0;
                    // Only reduce weight for very high correlation to prevent runaway
                    if (corr > 0.98)
                    {
                        double lambda = 0.2;
                        w -= lambda * hebb * corr * w;
                    }

                    // Stronger SOC drift toward target (higher than 0.5 for more clustering)
                    w += socDrift * (socTarget - w);

                    // Clamp
                    if (w < 0.0) w = 0.0;
                    if (w > 1.0) w = 1.0;

                    // Symmetric update
                    Weights[i, j] = w;
                    Weights[j, i] = w;
                }
                return localCount;
            }, _ => { });

            // REMOVED: RenormaliseWeightsPerNode() - this was preventing weights from growing!
            // Instead, use soft normalization only if total weight becomes extreme
            SoftNormalizeWeightsIfNeeded();
        }

        /// <summary>
        /// Soft normalization - only applies if weights become extreme (>0.9 average)
        /// This allows clustering while preventing runaway weight growth
        /// </summary>
        private void SoftNormalizeWeightsIfNeeded()
        {
            double totalWeight = 0.0;
            int edgeCount = 0;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    totalWeight += Weights[i, j];
                    edgeCount++;
                }
            }
            if (edgeCount == 0) return;
            double avgWeight = totalWeight / edgeCount;
            // Only normalize if average weight exceeds 0.8 (allowing clustering)
            if (avgWeight <= 0.8) return;
            double scale = 0.8 / avgWeight;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    double w = Weights[i, j] * scale;
                    Weights[i, j] = w;
                    Weights[j, i] = w;
                }
            }
        }
        private void UpdateNodeStates() => StepExcitableMedium();

        public void UpdateNodeStatesFromWavefunction(double quantile = 0.9)
        {
            if (_waveMulti == null) return;
            int n = N;
            int d = GaugeDimension;
            var probs = new double[n];
            for (int i = 0; i < n; i++)
            {
                double rho = 0.0;
                for (int a = 0; a < d; a++)
                {
                    int idx = i * d + a;
                    rho += _waveMulti[idx].Magnitude * _waveMulti[idx].Magnitude;
                }
                probs[i] = rho;
            }
            var sorted = (double[])probs.Clone();
            Array.Sort(sorted);
            int k = (int)Math.Clamp(quantile * (sorted.Length - 1), 0, sorted.Length - 1);
            double thr = sorted[k];
            for (int i = 0; i < n; i++)
            {
                bool excited = probs[i] >= thr;
                if (excited)
                {
                    State[i] = NodeState.Excited;
                    _refractoryCounter[i] = DynamicBaseRefractorySteps;
                }
                else if (State[i] == NodeState.Excited)
                {
                    State[i] = NodeState.Refractory;
                }
                else if (State[i] == NodeState.Refractory && _refractoryCounter[i] <= 0)
                {
                    State[i] = NodeState.Rest;
                }
            }
        }
        private double[] _qCoherence; // checklist item 8 coherence tracking

        /// <summary>
        /// Main constructor for RQGraph - initializes all arrays and creates initial topology.
        /// </summary>
        /// <param name="nodeCount">Number of nodes in the graph</param>
        /// <param name="initialEdgeProb">Probability of initial edge between any two nodes</param>
        /// <param name="initialExcitedProb">Probability of node starting in excited state</param>
        /// <param name="targetDegree">Target average degree for topology updates</param>
        /// <param name="lambdaState">State transition parameter (unused, kept for API compatibility)</param>
        /// <param name="temperature">Initial temperature for annealing</param>
        /// <param name="edgeTrialProbability">Probability of edge trial in step (unused, kept for API)</param>
        /// <param name="measurementThreshold">Threshold for measurement triggering</param>
        /// <param name="seed">Random seed for reproducibility</param>
        public RQGraph(int nodeCount, double initialEdgeProb, double initialExcitedProb,
                       int targetDegree, double lambdaState, double temperature,
                       double edgeTrialProbability, double measurementThreshold, int seed)
        {
            // Store parameters
            N = nodeCount;
            _targetDegree = targetDegree;
            _lambdaState = lambdaState;
            _temperature = temperature;
            _edgeTrialProbability = edgeTrialProbability;
            _measurementThreshold = measurementThreshold;
            _rng = new Random(seed);

            // Initialize core arrays
            Edges = new bool[N, N];
            Weights = new double[N, N];
            NodeWeights = new double[N];
            State = new NodeState[N];
            _nextState = new NodeState[N];
            _degree = new int[N];
            _charges = new int[N];
            _refractoryCounter = new int[N];
            _measurementBond = new bool[N, N];
            _nodeEnergy = new double[N];
            PhysicsProperties = new NodePhysics[N];
            Coordinates = new (double X, double Y)[N];

            // Initialize per-node target degree
            _targetDegreePerNode = new int[N];
            for (int i = 0; i < N; i++)
            {
                _targetDegreePerNode[i] = targetDegree;
                NodeWeights[i] = 1.0; // Default node weight for unweighted graphs
            }

            // Initialize checklist arrays
            StoredEnergy = new double[N];
            ParticleTag = new bool[N];
            PathWeight = new double[N, N];
            // RQ-FIX: Removed StructuralMass initialization
            Domain = new int[N];

            for (int i = 0; i < N; i++)
            {
                StoredEnergy[i] = 0.0;
                ParticleTag[i] = false;
                // RQ-FIX: Removed StructuralMass initialization
                Domain[i] = _rng.Next(4 * Math.Max(1, (int)Math.Log2(Math.Max(2, N))));
                for (int j = 0; j < N; j++)
                    PathWeight[i, j] = 0.0;
            }

            // Initialize correlation mass
            RecomputeCorrelationMass();

            // Create initial random edges
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (_rng.NextDouble() < initialEdgeProb)
                    {
                        Edges[i, j] = true;
                        Edges[j, i] = true;
                        _degree[i]++;
                        _degree[j]++;
                        double w = 0.1 + 0.4 * _rng.NextDouble();
                        Weights[i, j] = w;
                        Weights[j, i] = w;
                    }
                }
                // Initialize state
                State[i] = (_rng.NextDouble() < initialExcitedProb) ? NodeState.Excited : NodeState.Rest;
            }

            // Initialize time-related arrays
            InitProperTime();
            InitEdgeData();

            // Initialize physics properties
            for (int i = 0; i < N; i++)
            {
                PhysicsProperties[i] = new NodePhysics
                {
                    Type = ParticleType.Vacuum,
                    Mass = 0.0,
                    Charge = 0.0,
                    Spin = 0.0,
                    Occupation = 0,
                    MaxOccupation = 1,
                    IsClock = false
                };
            }
        }

        // expose coherence publicly (checklist 8)
        public double[] QuantumCoherence => _qCoherence ?? Array.Empty<double>();

        public void UpdateCoherence()
        {
            if (_waveMulti == null) return;
            int n = N; int d = GaugeDimension;
            if (_qCoherence == null || _qCoherence.Length != n) _qCoherence = new double[n];
            for (int i = 0; i < n; i++)
            {
                double amp = 0.0;
                for (int a = 0; a < d; a++) amp += _waveMulti[i * d + a].Magnitude;
                double val = amp; // use magnitude sum as proxy
                _qCoherence[i] = 0.5 * _qCoherence[i] + 0.5 * val; // exponential smoothing
            }
        }

        public void ComputeGlobalNeighbourSpontFactors()
        {
            // coherence update each external recompute call
            UpdateCoherence();
        }
    }
}
