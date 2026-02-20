using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RQSimulation
{
    /// <summary>
    /// Provides a more realistic simulation scenario that uses the KMC engine,
    /// scalar field dynamics and evaluates heavy clusters whose total
    /// correlation mass exceeds a specified threshold.  This allows the
    /// exploration of rare, very heavy structures (W &gt; threshold) and
    /// demonstrates how the extended diagnostics can be used.
    /// </summary>
    public static class ExampleModernSimulation
    {
        public sealed class ScenarioResult
        {
            public double FinalTime { get; init; }
            public int ExcitedCount { get; init; }
            public int HeavyClusterCount { get; init; }
            public double HeavyClusterTotalMass { get; init; }
            public double HeavyClusterMaxMass { get; init; }
            public double HeavyClusterMeanMass { get; init; }
            public double ScalarFieldEnergy { get; init; }
            public double HiggsFieldEnergy { get; init; }
        }

        /// <summary>
        /// Runs the simulation and returns statistics about heavy clusters with
        /// total correlation mass ≥ minMass.  The KMC engine and scalar field
        /// are integrated as in ExampleKMCSimulation.  The heavy cluster
        /// statistics allow analysing the prevalence of realistic heavy
        /// structures.
        /// </summary>
        /// <param name="nodeCount">Number of nodes.</param>
        /// <param name="simulationTime">Time horizon.</param>
        /// <param name="initialEdgeProb">Initial edge probability.</param>
        /// <param name="matterFraction">Fraction of fermionic nodes.</param>
        /// <param name="minHeavyMass">Minimum total mass for a cluster to be considered heavy.</param>
        /// <param name="seed">Random seed.</param>
        public static ScenarioResult RunScenario(
            int nodeCount = 1000,            // Increased for better statistics
            double simulationTime = 100.0,
            double initialEdgeProb = 0.05,   // "Big Bang" start: ~2500 edges for 1000 nodes
                                              // Creates initial primordial medium, not empty void
            double matterFraction = 0.5,
            double minHeavyMass = 3.0,
            int seed = 2468)
        {
            // === BIG BANG CONFIGURATION ===
            // Hot start with high temperature to "melt" defects and allow restructuring
            var graph = new RQGraph(
                nodeCount,
                initialEdgeProb: initialEdgeProb,
                initialExcitedProb: 0.3,       // Moderate initial excitation
                targetDegree: 8,               // Target degree for connectivity
                lambdaState: 0.8,              // State coupling
                temperature: PhysicsConstants.InitialAnnealingTemperature,  // Uses 5.0 (hot start)
                edgeTrialProbability: PhysicsConstants.TopologyTunnelingRate, // Uses 0.2
                measurementThreshold: 0.6,
                seed: seed);

            // Configure quantum state to include both strong (3 colour) and weak (2 isospin) components.
            // The total number of components must match the number of gauge sectors used.  Here we
            // allocate 5 components: indices 0–2 for SU(3) colour and 3–4 for SU(2) isospin.  Without
            // this call the weak gauge sector would not act on any wavefunction components.
            graph.ConfigureQuantumComponents(5);
            graph.ConfigureGaugeDimension(3);
            graph.ConfigureStrongGauge();
            graph.ConfigureWeakGaugeDimensions();

            // Enable a modest mixing between the two isospin components; now computed internally.
            // graph.WeakSpinCoupling = 0.05; // removed: property is read-only
            graph.ConfigureRelationalCouplings();

            // Relax locality and increase propagation; reduce diffusion per checklist
            graph.EdgeDecayLength = 0.5;
            graph.PropagationLength = 0.4;
            graph.EnergyDiffusionRate = 0.02;

            graph.InitPhysics(matterFraction);
            // Initialise node coordinates for gravitational dynamics.  We use
            // a modest range so that distances are order unity.  Without this
            // call the Coordinates array remains null and the gravitational
            // update is a no‑op.
            graph.InitCoordinatesRandom(range: 1.0);
            graph.InitQuantumWavefunction();
            graph.InitScalarField(amplitude: 0.01);
            graph.ScalarMass = 0.5;
            graph.ScalarCoupling = 0.1;
            graph.ScalarSelfCoupling = 0.1;
            graph.FieldExcitationThreshold = 1.0;

            // Ensure some initial activity by flipping a few nodes explicitly
            for (int i = 0; i < Math.Min(5, nodeCount); i++) graph.FlipNode(i);

            var kmc = new KineticMonteCarloEngine(graph, seed: seed + 1);
            var rng = new Random(seed + 2);

            double quantumDt = 0.02;
            double scalarDt = 0.1;
            double topoDt = 1.0;
            double nextTopo = 0.0;
            double nextScalar = 0.0;
            
            // Step counter for annealing
            int stepCount = 0;
            
            // RQ-FIX: Use centralized PhysicsConstants for warmup and annealing
            double warmupDuration = PhysicsConstants.WarmupDuration; // 500 steps
            double gravityTransition = PhysicsConstants.GravityTransitionDuration; // ~137 steps
            double annealingTau = PhysicsConstants.ComputeAnnealingTimeConstant((int)simulationTime);
            
            while (kmc.Time < simulationTime)
            {
                stepCount++;
                
                // === RQ-FIX: ДИНАМИЧЕСКОЕ ОСТЫВАНИЕ с централизованными константами ===
                // Использует PhysicsConstants.FinalAnnealingTemperature и InitialAnnealingTemperature
                double targetTemp = PhysicsConstants.FinalAnnealingTemperature; // α ≈ 0.0073
                double startTemp = PhysicsConstants.InitialAnnealingTemperature; // 10.0
                double currentTemp = targetTemp + (startTemp - targetTemp) * Math.Exp(-kmc.Time / annealingTau);
                graph.NetworkTemperature = currentTemp;
                
                // === RQ-FIX: ВКЛЮЧЕНИЕ ГРАВИТАЦИИ с использованием PhysicsConstants ===
                // Фаза 1 (warmup): G = α (очень слабая)
                // Фаза 2 (переход): линейная интерполяция
                // Фаза 3 (стабильная): G = GravitationalCoupling
                double warmupG = PhysicsConstants.WarmupGravitationalCoupling; // α ≈ 0.0073
                double maxG = PhysicsConstants.GravitationalCoupling; // 0.2
                double effectiveG;
                if (kmc.Time < warmupDuration)
                {
                    effectiveG = warmupG;
                }
                else if (kmc.Time < warmupDuration + gravityTransition)
                {
                    double t = (kmc.Time - warmupDuration) / gravityTransition;
                    effectiveG = warmupG + (maxG - warmupG) * t;
                }
                else
                {
                    effectiveG = maxG;
                }

                double horizon = Math.Min(simulationTime, kmc.Time + quantumDt);
                kmc.Run(horizon);
                
                graph.UpdateQuantumState();
                graph.UpdateStrongGaugeFromColorCurrents();
                graph.PerturbGauge(epsilon: 0.01);
                graph.UpdateNonAbelianGauge(0.005);
                graph.UpdateBosonFields(quantumDt);
                
                if (kmc.Time >= nextScalar)
                {
                    graph.UpdateScalarField(scalarDt);
                    nextScalar += scalarDt;
                }
                
                if (kmc.Time >= nextTopo)
                {
                    graph.Step(); // Топология (рождение/смерть ребер)
                    nextTopo += topoDt;
                    
                    // Передаем динамическую G через GPU-оптимизированный метод
                    GPUOptimized.ImprovedNetworkGravity.EvolveNetworkGeometryOllivierDynamic(
                        graph, dt: 0.01, effectiveG: effectiveG);
                }

                // dynamic string formation between differently colored matter nodes
                var quarks = Enumerable.Range(0, graph.N).Where(i => graph.ColorCharges[i] != RQGraph.ColorCharge.None).ToList();
                for (int aIdx = 0; aIdx < quarks.Count; aIdx++)
                {
                    for (int bIdx = aIdx + 1; bIdx < quarks.Count; bIdx++)
                    {
                        int a = quarks[aIdx];
                        int b = quarks[bIdx];
                        if (graph.ColorCharges[a] == graph.ColorCharges[b]) continue;
                        var path = ShortestPath(graph, a, b);
                        if (path == null || path.Count < 2) continue;
                        for (int k = 0; k < path.Count - 1; k++)
                        {
                            int u = path[k];
                            int v = path[k + 1];
                            if (graph.Edges[u, v])
                            {
                                // use dynamic tension
                                graph.ApplyStringTension(u, v);
                            }
                        }
                    }
                }
                // promote heavy clusters using extended logic each loop
                graph.PromoteHeavyClustersToCompositesExtended(3.0);
            }

            int excitedCount = graph.State.Count(s => s == NodeState.Excited);
            // Compute heavy cluster stats for clusters with mass ≥ minHeavyMass
            var stats = graph.GetHeavyClusterStatsByMass(minHeavyMass);
            double fieldEnergy = graph.ComputeScalarFieldEnergy();
            DiagnosticsExport.ExportLaplacianSpectrum(graph, "laplacian.csv");
            DiagnosticsExport.ExportHeavyClusterMasses(graph, minHeavyMass, "clusters.csv");
            return new ScenarioResult
            {
                FinalTime = kmc.Time,
                ExcitedCount = excitedCount,
                HeavyClusterCount = stats.count,
                HeavyClusterTotalMass = stats.totalMass,
                HeavyClusterMaxMass = stats.maxMass,
                HeavyClusterMeanMass = stats.meanMass,
                ScalarFieldEnergy = fieldEnergy,
                HiggsFieldEnergy = fieldEnergy // placeholder until Higgs separate
            };
        }

        /// <summary>
        /// Runs the simulation asynchronously.
        /// </summary>
        /// <param name="graph">The RQGraph instance.</param>
        /// <param name="simulationTime">Time horizon.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task RunAsync(RQGraph graph, double simulationTime, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(graph);
            graph.InitPhysics(0.1);
            graph.ConfigureGaugeDimension(3);
            graph.ConfigureStrongGauge();
            graph.ConfigureWeakGaugeDimensions(2);
            graph.ConfigureQuantumComponents(12);
            graph.InitQuantumWavefunction();

            var kmc = new KineticMonteCarloEngine(graph);
            while (kmc.Time < simulationTime)
            {

                ct.ThrowIfCancellationRequested();
                double dtRel = graph.ComputeRelationalDt();
                double horizon = Math.Min(simulationTime, kmc.Time + dtRel);
                kmc.Run(horizon);
                graph.UpdateQuantumState();
                graph.UpdateStrongGaugeMetropolis(beta: 5.0);
                graph.UpdateBosonFields(dtRel);
                graph.PromoteHeavyClustersToComposites(3.0);
            }
            await Task.CompletedTask;
        }

        private static List<int> ShortestPath(RQGraph g, int start, int goal)
        {
            if (start == goal) return new List<int> { start };
            var prev = new int[g.N];
            for (int i = 0; i < g.N; i++) prev[i] = -1;
            var q = new Queue<int>();
            q.Enqueue(start);
            prev[start] = start;
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (int nb in g.Neighbors(cur))
                {
                    if (prev[nb] != -1) continue;
                    prev[nb] = cur;
                    if (nb == goal)
                    {
                        var path = new List<int>();
                        int x = goal;
                        while (x != start)
                        {
                            path.Add(x);
                            x = prev[x];
                        }
                        path.Add(start);
                        path.Reverse();
                        return path;
                    }
                    q.Enqueue(nb);
                }
            }
            return null;
        }
    }
}