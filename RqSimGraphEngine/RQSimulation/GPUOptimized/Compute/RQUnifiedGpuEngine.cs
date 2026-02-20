using System;
using ComputeSharp;
using RQSimulation.GPUOptimized.CayleyEvolution;
using RQSimulation.GPUOptimized.SpinorField;
using RQSimulation.GPUOptimized.YangMills;
using RQSimulation.GPUOptimized.RelationalTime;
using RQSimulation.GPUOptimized.HawkingRadiation;
using RQSimulation.GPUOptimized.KleinGordon;
using RQSimulation.GPUOptimized.Gravity;

namespace RqSimGraphEngine.RQSimulation.GPUOptimized.Compute
{
    /// <summary>
    /// Unified GPU physics engine integrating all RQ-Hypothesis components.
    /// 
    /// RQ-HYPOTHESIS PRECISION POLICY:
    /// ================================
    /// - Double precision (64-bit) is PREFERRED for physics-critical operations
    /// - Falls back to float precision if GPU doesn't support double
    /// - Checks IsDoublePrecisionSupportAvailable() at initialization
    /// 
    /// PAGE-WOOTTERS RELATIONAL TIME:
    /// ==============================
    /// Use PerformPageWoottersStep() for true background-independent evolution
    /// where time emerges from quantum correlations rather than external dt.
    /// 
    /// ENGINES (Double Precision):
    /// - GpuCayleyEvolutionEngineDouble: Unitary quantum evolution (BiCGStab)
    /// - GpuRelationalTimeEngineDouble: Lapse function from Ricci curvature
    /// - GpuKleinGordonEngineDouble: Klein-Gordon field evolution (Verlet)
    /// 
    /// ENGINES (Float Precision):
    /// - GpuSpinorFieldEngine: Dirac spinor with Wilson term
    /// - GpuYangMillsEngine: SU(2)/SU(3) gauge field evolution
    /// - GpuHawkingEngine: Emergent pair creation
    /// </summary>
    public class RQUnifiedGpuEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly bool _useDoublePrecision;

        // Sub-engines (double precision preferred)
        private GpuCayleyEvolutionEngineDouble? _cayleyEngineDouble;
        private GpuCayleyEvolutionEngine? _cayleyEngineFloat;  // Fallback
        private GpuSpinorFieldEngine? _spinorEngine;
        private GpuYangMillsEngine? _yangMillsEngine;
        private GpuRelationalTimeEngine? _relationalTimeEngine;
        private GpuRelationalTimeEngineDouble? _relationalTimeEngineDouble;
        private GpuKleinGordonEngineDouble? _kleinGordonEngineDouble;
        private GpuHawkingEngine? _hawkingEngine;

        // Graph parameters
        private int _nodeCount;
        private int _edgeCount;
        private int _gaugeDim;
        private bool _initialized;

        // Work arrays for CPU-GPU data transfer
        private float[]? _stateVelocities;
        private float[]? _lapseFunction;
        private float[]? _unruhTemperature;
        private float[]? _edgeWeights;

        // Double precision work arrays
        private double[]? _psiReal;
        private double[]? _psiImag;
        private double[]? _lapseDouble;
        private double[]? _localDtDouble;
        private double[]? _ricciScalarDouble;
        private double[]? _phiCurrent;
        private double[]? _phiPrev;
        private double[]? _massDouble;
        
        // Page-Wootters quantum state arrays (float precision for standard GPU)
        private Float2[]? _waveFunctions;
        private float[]? _phases;
        private float[]? _hamiltonianEnergies;
        private int[]? _evolutionFlags;

        // Statistics
        private int _lastIterations;
        private int _lastPairsCreated;
        private int _lastEvolvedNodes;
        
        // Page-Wootters parameters
        private int _clockNodeIndex = 0;
        private float _entanglementThreshold = 0.001f;

        /// <summary>
        /// Whether double precision is being used.
        /// </summary>
        public bool IsDoublePrecision => _useDoublePrecision;
        
        /// <summary>
        /// Gets or sets the index of the clock node for Page-Wootters evolution.
        /// </summary>
        public int ClockNodeIndex 
        { 
            get => _clockNodeIndex;
            set => _clockNodeIndex = Math.Clamp(value, 0, Math.Max(0, _nodeCount - 1));
        }
        
        /// <summary>
        /// Gets or sets the entanglement threshold for Page-Wootters evolution.
        /// </summary>
        public float EntanglementThreshold
        {
            get => _entanglementThreshold;
            set => _entanglementThreshold = Math.Max(0f, value);
        }

        public RQUnifiedGpuEngine()
        {
            _device = GraphicsDevice.GetDefault();
            _useDoublePrecision = _device.IsDoublePrecisionSupportAvailable();

            if (!_useDoublePrecision)
            {
                Console.WriteLine("WARNING: GPU does not support double precision (SM 6.0+).");
                Console.WriteLine("Using float precision. Unitarity may degrade over long simulations.");
            }
            else
            {
                Console.WriteLine($"GPU: {_device.Name} - Double precision enabled (optimal for physics)");
            }
        }

        /// <summary>
        /// Initialize all GPU engines.
        /// </summary>
        public void Initialize(
            int nodeCount,
            int edgeCount,
            int gaugeDim = 3,
            int nnzHamiltonian = 0,
            int triangleCount = 0,
            int squareCount = 0)
        {
            _nodeCount = nodeCount;
            _edgeCount = edgeCount;
            _gaugeDim = gaugeDim;

            DisposeEngines();

            // Initialize Cayley evolution engine (prefer double precision)
            if (nnzHamiltonian > 0)
            {
                if (_useDoublePrecision)
                {
                    _cayleyEngineDouble = new GpuCayleyEvolutionEngineDouble();
                    _cayleyEngineDouble.Initialize(nodeCount, gaugeDim, nnzHamiltonian);
                    _psiReal = new double[nodeCount * gaugeDim];
                    _psiImag = new double[nodeCount * gaugeDim];
                }
                else
                {
                    _cayleyEngineFloat = new GpuCayleyEvolutionEngine();
                    _cayleyEngineFloat.Initialize(nodeCount, gaugeDim, nnzHamiltonian);
                }
            }

            // Initialize spinor engine
            _spinorEngine = new GpuSpinorFieldEngine();
            _spinorEngine.Initialize(nodeCount, edgeCount);

            // Initialize Yang-Mills engine
            _yangMillsEngine = new GpuYangMillsEngine();
            if (gaugeDim == 2)
                _yangMillsEngine.InitializeSU2(nodeCount, edgeCount / 2, triangleCount, squareCount);
            else if (gaugeDim == 3)
                _yangMillsEngine.InitializeSU3(nodeCount, edgeCount / 2, triangleCount, squareCount);

            // Initialize relational time engine (double or float)
            if (_useDoublePrecision)
            {
                _relationalTimeEngineDouble = new GpuRelationalTimeEngineDouble();
                _relationalTimeEngineDouble.Initialize(nodeCount, edgeCount);
                _lapseDouble = new double[nodeCount];
                _localDtDouble = new double[nodeCount];
                _ricciScalarDouble = new double[nodeCount];
            }
            else
            {
                _relationalTimeEngine = new GpuRelationalTimeEngine();
                _relationalTimeEngine.Initialize(nodeCount, edgeCount);
            }

            // Initialize Klein-Gordon engine (double precision)
            if (_useDoublePrecision)
            {
                _kleinGordonEngineDouble = new GpuKleinGordonEngineDouble();
                _kleinGordonEngineDouble.Initialize(nodeCount, edgeCount);
                _phiCurrent = new double[nodeCount];
                _phiPrev = new double[nodeCount];
                _massDouble = new double[nodeCount];
            }

            // Initialize Hawking radiation engine
            _hawkingEngine = new GpuHawkingEngine();
            _hawkingEngine.Initialize(nodeCount, edgeCount);

            // Work arrays
            _stateVelocities = new float[nodeCount];
            _lapseFunction = new float[nodeCount];
            _unruhTemperature = new float[nodeCount];
            _edgeWeights = new float[edgeCount];
            
            // Page-Wootters quantum state arrays
            _waveFunctions = new Float2[nodeCount];
            _phases = new float[nodeCount];
            _hamiltonianEnergies = new float[nodeCount];
            _evolutionFlags = new int[nodeCount];
            
            // Initialize wavefunctions to |1, 0>
            for (int i = 0; i < nodeCount; i++)
            {
                _waveFunctions[i] = new Float2(1.0f, 0.0f);
                _phases[i] = 0f;
                _hamiltonianEnergies[i] = 1.0f; // Default energy
            }

            _initialized = true;
        }
        
        /// <summary>
        /// Set physics parameters for all engines.
        /// </summary>
        public void SetPhysicsParameters(
            float speedOfLight = 1.0f,
            float hbar = 1.0f,
            float wilsonR = 1.0f,
            float wilsonMassPenalty = 2.0f,
            float timeDilationAlpha = 0.5f,
            float lapseFunctionAlpha = 1.0f,
            float minLapse = 0.1f,
            float maxLapse = 2.0f,
            float pairCreationMass = 0.1f,
            float pairCreationEnergy = 0.01f,
            float planckThreshold = 0.01f)
        {
            _spinorEngine?.SetPhysicsConstants(speedOfLight, hbar, wilsonR, wilsonMassPenalty);
            _relationalTimeEngine?.SetParameters(timeDilationAlpha, minLapse, maxLapse);
            _relationalTimeEngineDouble?.SetParameters(timeDilationAlpha, minLapse, maxLapse, 1e-6, 0.1);
            _relationalTimeEngineDouble?.SetLapseFunctionAlpha(lapseFunctionAlpha);
            _hawkingEngine?.SetParameters(pairCreationMass, pairCreationEnergy, planckThreshold);
        }
        
        /// <summary>
        /// Set Page-Wootters parameters for relational evolution.
        /// </summary>
        /// <param name="clockNodeIndex">Index of node serving as clock</param>
        /// <param name="entanglementThreshold">Minimum correlation for evolution</param>
        public void SetPageWoottersParameters(int clockNodeIndex, float entanglementThreshold)
        {
            _clockNodeIndex = Math.Clamp(clockNodeIndex, 0, Math.Max(0, _nodeCount - 1));
            _entanglementThreshold = Math.Max(0f, entanglementThreshold);
            
            if (_relationalTimeEngine != null)
            {
                _relationalTimeEngine.ClockNodeIndex = _clockNodeIndex;
                _relationalTimeEngine.EntanglementThreshold = _entanglementThreshold;
            }
        }

        /// <summary>
        /// Upload graph topology to all engines.
        /// </summary>
        public void UploadTopology(
            int[] csrOffsets,
            int[] csrNeighbors,
            float[] csrWeights,
            float[] edgePhases)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");

            _spinorEngine?.UploadTopology(csrOffsets, csrNeighbors, csrWeights, edgePhases);
            _relationalTimeEngine?.UploadTopology(csrOffsets, csrNeighbors, csrWeights);
            _hawkingEngine?.UploadTopology(csrOffsets, csrNeighbors);

            if (_useDoublePrecision)
            {
                var csrWeightsDouble = new double[csrWeights.Length];
                for (int i = 0; i < csrWeights.Length; i++)
                    csrWeightsDouble[i] = csrWeights[i];

                _relationalTimeEngineDouble?.UploadTopology(csrOffsets, csrNeighbors, csrWeightsDouble);
                _kleinGordonEngineDouble?.UploadTopology(csrOffsets, csrNeighbors, csrWeightsDouble);
            }

            Array.Copy(csrWeights, _edgeWeights!, Math.Min(csrWeights.Length, _edgeWeights!.Length));
        }
        
        /// <summary>
        /// Upload quantum state for Page-Wootters evolution.
        /// </summary>
        /// <param name="waveFunctions">Complex wavefunction per node</param>
        /// <param name="hamiltonianEnergies">Local Hamiltonian eigenvalue per node</param>
        public void UploadPageWoottersState(Float2[] waveFunctions, float[] hamiltonianEnergies)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            ArgumentNullException.ThrowIfNull(waveFunctions);
            ArgumentNullException.ThrowIfNull(hamiltonianEnergies);
            
            Array.Copy(waveFunctions, _waveFunctions!, Math.Min(waveFunctions.Length, _waveFunctions!.Length));
            Array.Copy(hamiltonianEnergies, _hamiltonianEnergies!, Math.Min(hamiltonianEnergies.Length, _hamiltonianEnergies!.Length));
            
            _relationalTimeEngine?.UploadQuantumState(_waveFunctions, _hamiltonianEnergies);
        }

        /// <summary>
        /// Upload Hamiltonian for Cayley evolution (double precision).
        /// </summary>
        public void UploadHamiltonian(
            int[] csrOffsets,
            int[] csrColumns,
            double[] csrValues,
            double[] potential)
        {
            if (_useDoublePrecision && _cayleyEngineDouble != null)
            {
                _cayleyEngineDouble.UploadHamiltonian(csrOffsets, csrColumns, csrValues, potential);
            }
            else if (_cayleyEngineFloat != null)
            {
                var floatValues = new float[csrValues.Length];
                var floatPotential = new float[potential.Length];
                for (int i = 0; i < csrValues.Length; i++)
                    floatValues[i] = (float)csrValues[i];
                for (int i = 0; i < potential.Length; i++)
                    floatPotential[i] = (float)potential[i];
                _cayleyEngineFloat.UploadHamiltonian(csrOffsets, csrColumns, floatValues, floatPotential);
            }
        }

        /// <summary>
        /// Upload edge curvatures for relational time engine.
        /// </summary>
        public void UploadEdgeCurvatures(double[] edgeCurvatures)
        {
            _relationalTimeEngineDouble?.UploadEdgeCurvatures(edgeCurvatures);
        }

        /// <summary>
        /// Upload Klein-Gordon field state.
        /// </summary>
        public void UploadKleinGordonField(double[] phiCurrent, double[] phiPrev, double[] mass)
        {
            if (_kleinGordonEngineDouble != null)
            {
                _kleinGordonEngineDouble.UploadField(phiCurrent, phiPrev);
                _kleinGordonEngineDouble.UploadMass(mass);
            }
        }
        
        // ================================================================
        // PAGE-WOOTTERS RELATIONAL EVOLUTION (Background Independent)
        // ================================================================
        
        /// <summary>
        /// PAGE-WOOTTERS RELATIONAL STEP
        /// =============================
        /// Performs physics evolution using the Page-Wootters mechanism where
        /// time emerges from quantum correlations rather than external dt.
        /// 
        /// This is the scientifically correct approach for background-independent
        /// quantum gravity simulation. Use this instead of PhysicsStep(dt) for
        /// true relational dynamics.
        /// 
        /// Algorithm:
        /// 1. Compute entanglement between each node and the clock node
        /// 2. Evolve only nodes that are correlated with the clock (P > threshold)
        /// 3. Apply unitary evolution Psi_new = Psi * exp(-i * H * delta_clock_phase)
        /// 4. Nodes NOT correlated remain "frozen" - no quantum event occurs
        /// </summary>
        /// <param name="clockNodeIndex">Index of the node serving as clock (optional override)</param>
        public void PerformPageWoottersStep(int? clockNodeIndex = null)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            int effectiveClockIndex = clockNodeIndex ?? _clockNodeIndex;
            effectiveClockIndex = Math.Clamp(effectiveClockIndex, 0, _nodeCount - 1);
            
            // Step 1: Perform Page-Wootters relational evolution
            if (_relationalTimeEngine != null)
            {
                _relationalTimeEngine.ClockNodeIndex = effectiveClockIndex;
                _relationalTimeEngine.EntanglementThreshold = _entanglementThreshold;
                _relationalTimeEngine.PerformRelationalStep(effectiveClockIndex);
                
                // Download results
                _relationalTimeEngine.DownloadEvolutionFlags(_evolutionFlags!);
                _relationalTimeEngine.DownloadQuantumState(_waveFunctions!, _phases!);
                
                // Count evolved nodes
                _lastEvolvedNodes = 0;
                for (int i = 0; i < _nodeCount; i++)
                {
                    if (_evolutionFlags![i] != 0)
                        _lastEvolvedNodes++;
                }
            }
            
            // Step 2: Update lapse function based on evolved state
            if (_relationalTimeEngine != null)
            {
                // Compute state velocities from wavefunction changes
                for (int i = 0; i < _nodeCount; i++)
                {
                    // State velocity is the phase change rate
                    _stateVelocities![i] = Math.Abs(_phases![i]);
                }
                
                _relationalTimeEngine.ComputeLapseFromVelocity(_stateVelocities!);
                _relationalTimeEngine.DownloadLapse(_lapseFunction!);
            }
            
            // Step 3: Hawking radiation (uses lapse gradient for temperature)
            if (_relationalTimeEngine != null)
            {
                _relationalTimeEngine.ComputeUnruhTemperature(_unruhTemperature!);
            }
            
            if (_hawkingEngine != null)
            {
                _hawkingEngine.UploadEdgeWeights(_edgeWeights!);
                _lastPairsCreated = _hawkingEngine.ProcessRadiation(_unruhTemperature!);
                _hawkingEngine.DownloadEdgeWeights(_edgeWeights!);
            }
        }
        
        /// <summary>
        /// Download Page-Wootters evolution results.
        /// </summary>
        /// <param name="waveFunctions">Updated complex wavefunctions</param>
        /// <param name="phases">Updated quantum phases</param>
        /// <param name="evolutionFlags">Which nodes evolved (1) vs frozen (0)</param>
        public void DownloadPageWoottersState(Float2[]? waveFunctions, float[]? phases, int[]? evolutionFlags)
        {
            if (waveFunctions != null && _waveFunctions != null)
                Array.Copy(_waveFunctions, waveFunctions, Math.Min(_waveFunctions.Length, waveFunctions.Length));
            if (phases != null && _phases != null)
                Array.Copy(_phases, phases, Math.Min(_phases.Length, phases.Length));
            if (evolutionFlags != null && _evolutionFlags != null)
                Array.Copy(_evolutionFlags, evolutionFlags, Math.Min(_evolutionFlags.Length, evolutionFlags.Length));
        }

        /// <summary>
        /// Perform one unified physics step (LEGACY: uses external dt parameter).
        /// 
        /// NOTE: For scientifically correct background-independent evolution,
        /// use PerformPageWoottersStep() instead.
        /// </summary>
        [Obsolete("Use PerformPageWoottersStep() for background-independent evolution")]
        public void PhysicsStep(float dt)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");

            // Step 1: Compute lapse function (relational time)
            if (_useDoublePrecision && _relationalTimeEngineDouble != null)
            {
                _relationalTimeEngineDouble.ComputeRelationalTimeStep(dt);
                _relationalTimeEngineDouble.DownloadLapse(_lapseDouble!);
                _relationalTimeEngineDouble.DownloadLocalDt(_localDtDouble!);

                for (int i = 0; i < _nodeCount; i++)
                    _lapseFunction![i] = (float)_lapseDouble![i];
            }
            else if (_relationalTimeEngine != null)
            {
                if (_stateVelocities![0] < 0)
                {
                    _relationalTimeEngine.ComputeLapseFromEntropy();
                }
                else
                {
                    _relationalTimeEngine.ComputeLapseCombined(_stateVelocities);
                }
                _relationalTimeEngine.DownloadLapse(_lapseFunction!);
            }

            // Step 2: Evolve Klein-Gordon scalar field (double precision)
            if (_useDoublePrecision && _kleinGordonEngineDouble != null)
            {
                _kleinGordonEngineDouble.UploadLocalDt(_localDtDouble!);
                _kleinGordonEngineDouble.Step();
            }

            // Step 3: Evolve spinor fields
            _spinorEngine!.UploadNodeParameters(
                new float[_nodeCount],
                _lapseFunction!,
                new int[_nodeCount]
            );
            _spinorEngine.EvolveSpinor(dt);
            _spinorEngine.DownloadStateVelocities(_stateVelocities!);

            // Step 4: Evolve gauge fields (Yang-Mills)
            if (_gaugeDim == 2)
                _yangMillsEngine!.EvolveSU2(dt);

            // Step 5: Cayley unitary evolution
            if (_useDoublePrecision && _cayleyEngineDouble != null && _psiReal != null)
            {
                _lastIterations = _cayleyEngineDouble.EvolveUnitary(_psiReal, _psiImag!, dt);
            }

            // Step 6: Hawking radiation
            if (_relationalTimeEngine != null)
            {
                _relationalTimeEngine.ComputeUnruhTemperature(_unruhTemperature!);
            }
            else
            {
                for (int i = 0; i < _nodeCount; i++)
                    _unruhTemperature![i] = 0.01f;
            }
            _hawkingEngine!.UploadEdgeWeights(_edgeWeights!);
            _lastPairsCreated = _hawkingEngine.ProcessRadiation(_unruhTemperature!);
            _hawkingEngine.DownloadEdgeWeights(_edgeWeights!);
        }

        /// <summary>
        /// Upload spinor state from CPU.
        /// </summary>
        public void UploadSpinorState(
            float[] aReal, float[] aImag,
            float[] bReal, float[] bImag,
            float[] cReal, float[] cImag,
            float[] dReal, float[] dImag)
        {
            _spinorEngine?.UploadSpinorState(
                aReal, aImag, bReal, bImag,
                cReal, cImag, dReal, dImag);
        }

        /// <summary>
        /// Download spinor state to CPU.
        /// </summary>
        public void DownloadSpinorState(
            float[] aReal, float[] aImag,
            float[] bReal, float[] bImag,
            float[] cReal, float[] cImag,
            float[] dReal, float[] dImag)
        {
            _spinorEngine?.DownloadSpinorState(
                aReal, aImag, bReal, bImag,
                cReal, cImag, dReal, dImag);
        }

        /// <summary>
        /// Upload wavefunction for Cayley evolution (double precision).
        /// </summary>
        public void UploadWavefunction(double[] psiReal, double[] psiImag)
        {
            if (_psiReal != null && _psiImag != null)
            {
                Array.Copy(psiReal, _psiReal, Math.Min(psiReal.Length, _psiReal.Length));
                Array.Copy(psiImag, _psiImag, Math.Min(psiImag.Length, _psiImag.Length));
            }
        }

        /// <summary>
        /// Download wavefunction after Cayley evolution.
        /// </summary>
        public void DownloadWavefunction(double[] psiReal, double[] psiImag)
        {
            if (_psiReal != null && _psiImag != null)
            {
                Array.Copy(_psiReal, psiReal, Math.Min(_psiReal.Length, psiReal.Length));
                Array.Copy(_psiImag, psiImag, Math.Min(_psiImag.Length, psiImag.Length));
            }
        }

        /// <summary>
        /// Download current lapse function.
        /// </summary>
        public void DownloadLapse(float[] lapse)
        {
            if (_lapseFunction != null)
                Array.Copy(_lapseFunction, lapse, Math.Min(_lapseFunction.Length, lapse.Length));
        }

        /// <summary>
        /// Download current lapse function (double precision).
        /// </summary>
        public void DownloadLapseDouble(double[] lapse)
        {
            if (_lapseDouble != null)
                Array.Copy(_lapseDouble, lapse, Math.Min(_lapseDouble.Length, lapse.Length));
        }

        /// <summary>
        /// Download Klein-Gordon field.
        /// </summary>
        public void DownloadKleinGordonField(double[] phi)
        {
            _kleinGordonEngineDouble?.DownloadField(phi);
        }

        /// <summary>
        /// Download current edge weights (after backreaction).
        /// </summary>
        public void DownloadEdgeWeights(float[] weights)
        {
            if (_edgeWeights != null)
                Array.Copy(_edgeWeights, weights, Math.Min(_edgeWeights.Length, weights.Length));
        }
        
        /// <summary>
        /// Download node proper times (Page-Wootters clocks).
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 1: Each node has its own proper time.
        /// </summary>
        public void DownloadNodeClocks(double[] nodeClocks)
        {
            _relationalTimeEngineDouble?.DownloadNodeClocks(nodeClocks);
        }
        
        /// <summary>
        /// Reset node clocks to zero.
        /// </summary>
        public void ResetNodeClocks()
        {
            _relationalTimeEngineDouble?.ResetNodeClocks();
        }

        /// <summary>
        /// Get last step statistics.
        /// </summary>
        public (int biCGStabIterations, int pairsCreated) GetLastStepStats()
        {
            return (_lastIterations, _lastPairsCreated);
        }
        
        /// <summary>
        /// Get Page-Wootters step statistics.
        /// </summary>
        /// <returns>Number of nodes that evolved in the last relational step</returns>
        public int GetLastEvolvedNodesCount() => _lastEvolvedNodes;

        /// <summary>
        /// Check if GPU is available.
        /// </summary>
        public static bool IsGpuAvailable()
        {
            try
            {
                var device = GraphicsDevice.GetDefault();
                return device != null && device.IsHardwareAccelerated;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get GPU device info.
        /// </summary>
        public static string GetGpuInfo()
        {
            try
            {
                var device = GraphicsDevice.GetDefault();
                bool doubleSupport = device.IsDoublePrecisionSupportAvailable();
                return $"{device.Name} (Hardware: {device.IsHardwareAccelerated}, Double: {doubleSupport})";
            }
            catch (Exception ex)
            {
                return $"GPU not available: {ex.Message}";
            }
        }

        private void DisposeEngines()
        {
            _cayleyEngineDouble?.Dispose();
            _cayleyEngineFloat?.Dispose();
            _spinorEngine?.Dispose();
            _yangMillsEngine?.Dispose();
            _relationalTimeEngine?.Dispose();
            _relationalTimeEngineDouble?.Dispose();
            _kleinGordonEngineDouble?.Dispose();
            _hawkingEngine?.Dispose();

            _cayleyEngineDouble = null;
            _cayleyEngineFloat = null;
            _spinorEngine = null;
            _yangMillsEngine = null;
            _relationalTimeEngine = null;
            _relationalTimeEngineDouble = null;
            _kleinGordonEngineDouble = null;
            _hawkingEngine = null;
        }

        public void Dispose()
        {
            DisposeEngines();
            GC.SuppressFinalize(this);
        }
    }
}
