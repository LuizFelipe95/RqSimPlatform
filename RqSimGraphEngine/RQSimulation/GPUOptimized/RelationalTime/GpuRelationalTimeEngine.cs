using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.RelationalTime
{
    /// <summary>
    /// GPU-accelerated relational time computation engine.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Pure Relational Time (Page-Wootters)
    /// ======================================================================
    /// Implements the Page-Wootters mechanism where time emerges from quantum
    /// correlations between a "clock" subsystem and the rest of the universe.
    /// 
    /// KEY PRINCIPLE: Time does NOT flow via external dt parameter.
    /// Instead, we compute conditional probabilities P(System | Clock) where
    /// evolution occurs only for nodes entangled with the clock.
    /// 
    /// Lapse function N_i = 1 / (1 + ||d?/dt||_FubiniStudy)
    /// </summary>
    public class GpuRelationalTimeEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        
        // Existing lapse computation buffers
        private ReadWriteBuffer<float>? _lapseBuffer;
        private ReadOnlyBuffer<float>? _stateVelocityBuffer;
        private ReadOnlyBuffer<float>? _entropyBuffer;
        private ReadOnlyBuffer<int>? _csrOffsetsBuffer;
        private ReadOnlyBuffer<int>? _csrNeighborsBuffer;
        private ReadOnlyBuffer<float>? _csrWeightsBuffer;
        private ReadOnlyBuffer<float>? _correlationMassBuffer;
        private ReadWriteBuffer<float>? _entropyWorkBuffer;
        
        // Page-Wootters quantum state buffers
        private ReadWriteBuffer<Float2>? _waveFunctionBuffer;  // Complex wavefunction per node
        private ReadWriteBuffer<float>? _phaseBuffer;           // Quantum phase per node
        private ReadWriteBuffer<float>? _lastClockPhaseBuffer;  // Last clock phase seen by node
        private ReadOnlyBuffer<float>? _hamiltonianEnergyBuffer; // Local Hamiltonian eigenvalue
        private ReadWriteBuffer<int>? _evolutionFlagsBuffer;    // Which nodes evolved this step
        
        private int _nodeCount;
        private bool _initialized;
        
        // Lapse parameters
        private float _timeDilationAlpha = 0.5f;
        private float _minLapse = 0.1f;
        private float _maxLapse = 2.0f;
        
        // Page-Wootters parameters
        private int _clockNodeIndex = 0;
        private float _entanglementThreshold = 0.001f;
        
        public GpuRelationalTimeEngine()
        {
            _device = GraphicsDevice.GetDefault();
        }
        
        /// <summary>
        /// Gets or sets the index of the node serving as the "clock" subsystem.
        /// The clock node provides the reference phase for relational evolution.
        /// </summary>
        public int ClockNodeIndex 
        { 
            get => _clockNodeIndex;
            set => _clockNodeIndex = Math.Clamp(value, 0, Math.Max(0, _nodeCount - 1));
        }
        
        /// <summary>
        /// Gets or sets the minimum entanglement required for a node to evolve.
        /// Nodes with correlation below this threshold remain "frozen" in relational time.
        /// </summary>
        public float EntanglementThreshold
        {
            get => _entanglementThreshold;
            set => _entanglementThreshold = Math.Max(0f, value);
        }
        
        public void Initialize(int nodeCount, int totalEdges)
        {
            _nodeCount = nodeCount;
            
            DisposeBuffers();
            
            // Existing lapse buffers
            _lapseBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _stateVelocityBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            _entropyBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            _entropyWorkBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            
            _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
            _csrNeighborsBuffer = _device.AllocateReadOnlyBuffer<int>(totalEdges);
            _csrWeightsBuffer = _device.AllocateReadOnlyBuffer<float>(totalEdges);
            _correlationMassBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            
            // Page-Wootters quantum state buffers
            _waveFunctionBuffer = _device.AllocateReadWriteBuffer<Float2>(nodeCount);
            _phaseBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _lastClockPhaseBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _hamiltonianEnergyBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            _evolutionFlagsBuffer = _device.AllocateReadWriteBuffer<int>(nodeCount);
            
            _initialized = true;
            
            // Initialize lapse to 1.0
            var ones = new float[nodeCount];
            for (int i = 0; i < nodeCount; i++) ones[i] = 1.0f;
            _lapseBuffer.CopyFrom(ones);
            
            // Initialize phases to 0
            var zeros = new float[nodeCount];
            _phaseBuffer.CopyFrom(zeros);
            _lastClockPhaseBuffer.CopyFrom(zeros);
            
            // Initialize wavefunctions to |1, 0> (real amplitude = 1)
            var initialPsi = new Float2[nodeCount];
            for (int i = 0; i < nodeCount; i++) 
                initialPsi[i] = new Float2(1.0f, 0.0f);
            _waveFunctionBuffer.CopyFrom(initialPsi);
        }
        
        public void SetParameters(float alpha, float minLapse, float maxLapse)
        {
            _timeDilationAlpha = alpha;
            _minLapse = minLapse;
            _maxLapse = maxLapse;
        }
        
        public void UploadTopology(int[] csrOffsets, int[] csrNeighbors, float[] csrWeights)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _csrOffsetsBuffer!.CopyFrom(csrOffsets);
            _csrNeighborsBuffer!.CopyFrom(csrNeighbors);
            _csrWeightsBuffer!.CopyFrom(csrWeights);
        }
        
        public void UploadCorrelationMass(float[] mass)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _correlationMassBuffer!.CopyFrom(mass);
        }
        
        /// <summary>
        /// Upload quantum state data for Page-Wootters evolution.
        /// </summary>
        /// <param name="waveFunctions">Complex wavefunction per node (Float2 = real, imag)</param>
        /// <param name="hamiltonianEnergies">Local Hamiltonian eigenvalue per node</param>
        public void UploadQuantumState(Float2[] waveFunctions, float[] hamiltonianEnergies)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            ArgumentNullException.ThrowIfNull(waveFunctions);
            ArgumentNullException.ThrowIfNull(hamiltonianEnergies);
            
            if (waveFunctions.Length != _nodeCount)
                throw new ArgumentException($"WaveFunctions length {waveFunctions.Length} != nodeCount {_nodeCount}");
            if (hamiltonianEnergies.Length != _nodeCount)
                throw new ArgumentException($"HamiltonianEnergies length {hamiltonianEnergies.Length} != nodeCount {_nodeCount}");
            
            _waveFunctionBuffer!.CopyFrom(waveFunctions);
            _hamiltonianEnergyBuffer!.CopyFrom(hamiltonianEnergies);
        }
        
        /// <summary>
        /// PAGE-WOOTTERS RELATIONAL STEP
        /// =============================
        /// Performs a single relational time step based on entanglement with clock node.
        /// 
        /// Instead of moving time forward (dt), we find states correlated with the "clock"
        /// and evolve them by the unitary operator exp(-i * H * ?clockPhase).
        /// 
        /// Nodes NOT entangled with the clock remain "frozen" relative to the observer.
        /// This is the physical basis of Background Independence.
        /// </summary>
        /// <param name="clockNodeIndex">Index of the node serving as the clock subsystem</param>
        public void PerformRelationalStep(int clockNodeIndex)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _clockNodeIndex = Math.Clamp(clockNodeIndex, 0, _nodeCount - 1);
            
            // Clear evolution flags
            var zeroFlags = new int[_nodeCount];
            _evolutionFlagsBuffer!.CopyFrom(zeroFlags);
            
            // Dispatch Page-Wootters correlation kernel
            _device.For(_nodeCount, new PageWoottersEvolutionKernel(
                _waveFunctionBuffer!,
                _phaseBuffer!,
                _lastClockPhaseBuffer!,
                _hamiltonianEnergyBuffer!,
                _evolutionFlagsBuffer,
                _clockNodeIndex,
                _entanglementThreshold
            ));
        }
        
        /// <summary>
        /// Download which nodes evolved during the last relational step.
        /// Nodes with flag = 1 were correlated with clock and evolved.
        /// Nodes with flag = 0 were "frozen" (no quantum event occurred).
        /// </summary>
        public void DownloadEvolutionFlags(int[] flags)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _evolutionFlagsBuffer!.CopyTo(flags);
        }
        
        /// <summary>
        /// Download the updated quantum states after relational evolution.
        /// </summary>
        public void DownloadQuantumState(Float2[] waveFunctions, float[] phases)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _waveFunctionBuffer!.CopyTo(waveFunctions);
            _phaseBuffer!.CopyTo(phases);
        }
        
        // ================================================================
        // EXISTING LAPSE COMPUTATION METHODS (preserved)
        // ================================================================
        
        public void ComputeLapseFromVelocity(float[] stateVelocities)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _stateVelocityBuffer!.CopyFrom(stateVelocities);
            
            _device.For(_nodeCount, new LapseFromVelocityKernel(
                _stateVelocityBuffer,
                _lapseBuffer!,
                _minLapse,
                _maxLapse
            ));
        }
        
        public void ComputeLapseFromEntropy()
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _device.For(_nodeCount, new ComputeEntropyKernel(
                _csrOffsetsBuffer!,
                _csrNeighborsBuffer!,
                _csrWeightsBuffer!,
                _correlationMassBuffer!,
                _entropyWorkBuffer!,
                _nodeCount
            ));
            
            _device.For(_nodeCount, new LapseFromEntropyKernel(
                _entropyWorkBuffer!,
                _lapseBuffer!,
                _timeDilationAlpha,
                _minLapse,
                _maxLapse
            ));
        }
        
        public void ComputeLapseCombined(float[] stateVelocities)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _device.For(_nodeCount, new ComputeEntropyKernel(
                _csrOffsetsBuffer!,
                _csrNeighborsBuffer!,
                _csrWeightsBuffer!,
                _correlationMassBuffer!,
                _entropyWorkBuffer!,
                _nodeCount
            ));
            
            _stateVelocityBuffer!.CopyFrom(stateVelocities);
            
            _device.For(_nodeCount, new LapseCombinedKernel(
                _stateVelocityBuffer,
                _entropyWorkBuffer!,
                _lapseBuffer!,
                _timeDilationAlpha,
                _minLapse,
                _maxLapse
            ));
        }
        
        public void DownloadLapse(float[] lapse)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _lapseBuffer!.CopyTo(lapse);
        }
        
        public void ComputeUnruhTemperature(float[] temperature)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            var tempBuffer = _device.AllocateReadWriteBuffer<float>(_nodeCount);
            
            _device.For(_nodeCount, new UnruhTemperatureKernel(
                _lapseBuffer!,
                tempBuffer,
                _csrOffsetsBuffer!,
                _csrNeighborsBuffer!,
                _csrWeightsBuffer!,
                _nodeCount
            ));
            
            tempBuffer.CopyTo(temperature);
            tempBuffer.Dispose();
        }
        
        private void DisposeBuffers()
        {
            _lapseBuffer?.Dispose();
            _stateVelocityBuffer?.Dispose();
            _entropyBuffer?.Dispose();
            _entropyWorkBuffer?.Dispose();
            _csrOffsetsBuffer?.Dispose();
            _csrNeighborsBuffer?.Dispose();
            _csrWeightsBuffer?.Dispose();
            _correlationMassBuffer?.Dispose();
            
            // Page-Wootters buffers
            _waveFunctionBuffer?.Dispose();
            _phaseBuffer?.Dispose();
            _lastClockPhaseBuffer?.Dispose();
            _hamiltonianEnergyBuffer?.Dispose();
            _evolutionFlagsBuffer?.Dispose();
        }
        
        public void Dispose()
        {
            DisposeBuffers();
            GC.SuppressFinalize(this);
        }
    }
}
