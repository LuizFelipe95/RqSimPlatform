using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// GPU-accelerated heat kernel engine for spectral dimension computation.
    /// Uses heat diffusion method: P(t) = Tr(e^(-tL)) where L is the graph Laplacian.
    /// 
    /// Spectral dimension d_s is computed from heat trace:
    /// Tr(e^(-tL)) ~ t^(-d_s/2) for small t
    /// 
    /// This is an alternative to SpectralWalkEngine (random walk method):
    /// - Heat Kernel: Better for dense graphs, more stable numerically
    /// - Random Walk: Better for sparse graphs, simpler implementation
    /// 
    /// NOTE: This is a stub implementation. Full GPU heat kernel requires
    /// implementing Laplacian matrix operations on GPU, which is more complex
    /// than random walks. Currently delegates to SpectralWalkEngine internally.
    /// </summary>
    public class GpuSpectralEngine : IDisposable
    {
        private SpectralWalkEngine? _walkEngine;
        private int _topologyVersion = -1;
        private bool _disposed;

        /// <summary>
        /// Current cached topology version.
        /// </summary>
        public int TopologyVersion => _topologyVersion;

        /// <summary>
        /// Whether the engine is initialized.
        /// </summary>
        public bool IsInitialized => _walkEngine?.IsInitialized ?? false;

        public GpuSpectralEngine()
        {
            _walkEngine = new SpectralWalkEngine();
        }

        /// <summary>
        /// Update topology from RQGraph.
        /// </summary>
        /// <param name="graph">The RQGraph to read topology from</param>
        public void UpdateTopology(RQGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _walkEngine?.UpdateTopologyFromGraph(graph, walkerCount: 10000);
            _topologyVersion = graph.TopologyVersion;
        }

        /// <summary>
        /// Compute spectral dimension using heat kernel approximation.
        /// 
        /// NOTE: Currently this delegates to random walk method internally.
        /// A true heat kernel implementation would use:
        /// 1. Stochastic Lanczos quadrature for Tr(e^(-tL))
        /// 2. Or matrix-free Chebyshev polynomial approximation
        /// 
        /// These require more complex GPU shader implementations.
        /// </summary>
        /// <param name="graph">The RQGraph to compute d_S for</param>
        /// <param name="dt">Time step for diffusion (not used in current implementation)</param>
        /// <param name="numSteps">Number of time steps</param>
        /// <param name="numProbeVectors">Number of random vectors for trace estimation (not used)</param>
        /// <param name="enableCpuComparison">If true, compare with CPU version</param>
        /// <returns>Spectral dimension estimate</returns>
        public double ComputeSpectralDimension(
            RQGraph graph,
            double dt = 0.01,
            int numSteps = 100,
            int numProbeVectors = 8,
            bool enableCpuComparison = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (_walkEngine == null)
            {
                return double.NaN;
            }

            // Check for stale topology
            if (_topologyVersion != graph.TopologyVersion)
            {
                UpdateTopology(graph);
            }

            // Delegate to random walk engine (heat kernel stub)
            // A full implementation would use heat diffusion instead
            double ds = _walkEngine.ComputeSpectralDimensionWithSyncCheck(
                graph,
                numSteps: numSteps,
                walkerCount: 10000,
                skipInitial: 10);

            if (enableCpuComparison && !double.IsNaN(ds))
            {
                // CPU comparison for debugging
                double cpuDs = graph.ComputeSpectralDimension(t_max: numSteps, num_walkers: 100);
                double diff = Math.Abs(ds - cpuDs);
                // Log difference if significant
                // Console.WriteLine($"[GpuSpectralEngine] GPU: {ds:F4}, CPU: {cpuDs:F4}, diff: {diff:F4}");
            }

            return ds;
        }

        /// <summary>
        /// Compute spectral dimension using raw topology data.
        /// This is the low-level API for testing.
        /// </summary>
        /// <param name="weights">Edge weights array</param>
        /// <param name="offsets">CSR row offsets</param>
        /// <param name="neighbors">CSR column indices</param>
        /// <param name="nodeCount">Number of nodes</param>
        /// <param name="dt">Time step</param>
        /// <param name="numSteps">Number of steps</param>
        /// <returns>Spectral dimension estimate</returns>
        public double ComputeSpectralDimensionGpu(
            double[] weights,
            int[] offsets,
            int[] neighbors,
            int nodeCount,
            double dt = 0.05,
            int numSteps = 60)
        {
            if (_walkEngine == null)
            {
                return double.NaN;
            }

            // Convert double weights to float for SpectralWalkEngine
            float[] floatWeights = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                floatWeights[i] = (float)weights[i];
            }

            // Initialize if needed
            if (!_walkEngine.IsInitialized || _walkEngine.NodeCount != nodeCount)
            {
                _walkEngine.Initialize(10000, nodeCount, neighbors.Length);
            }

            _walkEngine.UpdateTopology(offsets, neighbors, floatWeights);
            _walkEngine.InitializeWalkersRandom(new Random());

            // Run random walks
            int[] returns = _walkEngine.RunSteps(numSteps);

            // Compute spectral dimension
            return _walkEngine.ComputeSpectralDimension(returns, skipInitial: 10);
        }

        /// <summary>
        /// Estimates the mass gap (spectral gap ??) using heat kernel decay analysis.
        /// 
        /// The mass gap is the smallest nonzero eigenvalue of the graph Laplacian.
        /// It determines the rate of approach to equilibrium and represents
        /// the mass of the lightest excitation in Yang-Mills theory.
        /// 
        /// Method: Analyze heat kernel trace decay
        /// Tr(e^{-tL}) ~ N * e^{-??t} for large t (after ??=0 mode dominates)
        /// 
        /// So: ?? ? -ln(Trace(t) / Trace(t-1)) / ?t
        /// </summary>
        /// <param name="graph">The RQGraph to analyze</param>
        /// <param name="iterations">Number of diffusion steps for decay analysis</param>
        /// <returns>Estimated mass gap ?? (returns NaN if estimation fails)</returns>
        public double EstimateMassGap(RQGraph graph, int iterations = 100)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (_walkEngine == null || graph.N < 3)
            {
                return double.NaN;
            }

            // Update topology if stale
            if (_topologyVersion != graph.TopologyVersion)
            {
                UpdateTopology(graph);
            }

            // Use random walk return probabilities as proxy for heat kernel trace
            // P(t) = sum_i p_i(t) where p_i(t) is probability of returning to start
            // For connected graph: P(t) ~ 1/N + (N-1)/N * e^{-??t}

            int walkerCount = Math.Min(graph.N, 5000);
            
            // Initialize walkers at random starting positions
            _walkEngine.InitializeWalkersRandom(new Random());

            // Run walks and collect return counts at different times
            int midPoint = iterations / 2;
            int[] returnsMid = _walkEngine.RunSteps(midPoint);
            int[] returnsFinal = _walkEngine.RunSteps(iterations - midPoint);

            // Calculate return probabilities
            double pMid = returnsMid.Sum() / (double)(walkerCount * midPoint);
            double pFinal = returnsFinal.Sum() / (double)(walkerCount * (iterations - midPoint));

            // Baseline probability (equilibrium) = 1/N
            double pEquilibrium = 1.0 / graph.N;

            // Excess probability above equilibrium decays as e^{-??t}
            double excessMid = Math.Max(pMid - pEquilibrium, 1e-10);
            double excessFinal = Math.Max(pFinal - pEquilibrium, 1e-10);

            // ?? ? -ln(excessFinal / excessMid) / ?t
            double deltaT = iterations - midPoint;
            double lambda1 = -Math.Log(excessFinal / excessMid) / deltaT;

            // Sanity checks
            if (double.IsNaN(lambda1) || double.IsInfinity(lambda1) || lambda1 < 0)
            {
                return double.NaN;
            }

            return lambda1;
        }

        /// <summary>
        /// Calculates the ratio between consecutive spectral gaps.
        /// Useful for detecting gap stability across graph sizes.
        /// </summary>
        /// <param name="graph">The RQGraph to analyze</param>
        /// <param name="shortIterations">Iterations for short-time estimate</param>
        /// <param name="longIterations">Iterations for long-time estimate</param>
        /// <returns>Ratio ??(short) / ??(long), should be ~1 for stable gap</returns>
        public double CalculateSpectralGapStability(RQGraph graph, int shortIterations = 50, int longIterations = 150)
        {
            double gapShort = EstimateMassGap(graph, shortIterations);
            double gapLong = EstimateMassGap(graph, longIterations);

            if (double.IsNaN(gapShort) || double.IsNaN(gapLong) || gapLong < 1e-10)
            {
                return double.NaN;
            }

            return gapShort / gapLong;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _walkEngine?.Dispose();
                _walkEngine = null;
                _disposed = true;
            }
        }
    }
}
