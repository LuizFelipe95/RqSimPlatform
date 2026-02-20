using System;
using ComputeSharp;

namespace RQSimulation.GPUOptimized.HawkingRadiation
{
    /// <summary>
    /// GPU-accelerated emergent Hawking radiation engine.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 4: Emergent Black Holes
    /// =====================================================
    /// Implements spontaneous pair creation from vacuum fluctuations
    /// near regions of high lapse gradient (effective horizons).
    /// 
    /// The probability of pair creation follows the Unruh-Hawking formula:
    ///   P_pair = exp(-2? ? m_eff / T)
    /// </summary>
    public class GpuHawkingEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        
        private ReadOnlyBuffer<float>? _temperatureBuffer;
        private ReadWriteBuffer<float>? _pairProbabilityBuffer;
        private ReadWriteBuffer<int>? _pairCreatedBuffer;
        private ReadWriteBuffer<float>? _energyExtractedBuffer;
        private ReadWriteBuffer<float>? _edgeWeightsBuffer;
        private ReadWriteBuffer<uint>? _rngStateBuffer;
        private ReadOnlyBuffer<int>? _csrOffsetsBuffer;
        private ReadOnlyBuffer<int>? _csrNeighborsBuffer;
        
        private int _nodeCount;
        private int _edgeCount;
        private bool _initialized;
        
        private float _massThreshold = 0.1f;
        private float _pairCreationEnergy = 0.01f;
        private float _planckWeightThreshold = 0.01f;
        
        public GpuHawkingEngine()
        {
            _device = GraphicsDevice.GetDefault();
        }
        
        public void Initialize(int nodeCount, int edgeCount)
        {
            _nodeCount = nodeCount;
            _edgeCount = edgeCount;
            
            DisposeBuffers();
            
            _temperatureBuffer = _device.AllocateReadOnlyBuffer<float>(nodeCount);
            _pairProbabilityBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _pairCreatedBuffer = _device.AllocateReadWriteBuffer<int>(nodeCount);
            _energyExtractedBuffer = _device.AllocateReadWriteBuffer<float>(nodeCount);
            _edgeWeightsBuffer = _device.AllocateReadWriteBuffer<float>(edgeCount);
            _rngStateBuffer = _device.AllocateReadWriteBuffer<uint>(nodeCount);
            
            _csrOffsetsBuffer = _device.AllocateReadOnlyBuffer<int>(nodeCount + 1);
            _csrNeighborsBuffer = _device.AllocateReadOnlyBuffer<int>(edgeCount);
            
            _initialized = true;
            
            InitializeRNG();
        }
        
        public void SetParameters(float massThreshold, float pairCreationEnergy, float planckThreshold)
        {
            _massThreshold = massThreshold;
            _pairCreationEnergy = pairCreationEnergy;
            _planckWeightThreshold = planckThreshold;
        }
        
        private void InitializeRNG()
        {
            var seeds = new uint[_nodeCount];
            var rng = new Random();
            for (int i = 0; i < _nodeCount; i++)
                seeds[i] = (uint)rng.Next();
            _rngStateBuffer!.CopyFrom(seeds);
        }
        
        public void UploadTopology(int[] csrOffsets, int[] csrNeighbors)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _csrOffsetsBuffer!.CopyFrom(csrOffsets);
            _csrNeighborsBuffer!.CopyFrom(csrNeighbors);
        }
        
        public void UploadEdgeWeights(float[] weights)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _edgeWeightsBuffer!.CopyFrom(weights);
        }
        
        public int ProcessRadiation(float[] temperature)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
            
            _temperatureBuffer!.CopyFrom(temperature);
            
            _device.For(_nodeCount, new PairProbabilityKernel(
                _temperatureBuffer,
                _pairProbabilityBuffer!,
                _massThreshold
            ));
            
            _device.For(_nodeCount, new StochasticPairCreationKernel(
                _pairProbabilityBuffer!,
                _pairCreatedBuffer!,
                _rngStateBuffer!
            ));
            
            _device.For(_nodeCount, new BackreactionKernel(
                _pairCreatedBuffer!,
                _energyExtractedBuffer!,
                _edgeWeightsBuffer!,
                _csrOffsetsBuffer!,
                _csrNeighborsBuffer!,
                _pairCreationEnergy,
                _planckWeightThreshold,
                _nodeCount
            ));
            
            var pairsCreated = new int[_nodeCount];
            _pairCreatedBuffer!.CopyTo(pairsCreated);
            
            int totalPairs = 0;
            for (int i = 0; i < _nodeCount; i++)
                totalPairs += pairsCreated[i];
            
            return totalPairs;
        }
        
        public void DownloadEdgeWeights(float[] weights)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _edgeWeightsBuffer!.CopyTo(weights);
        }
        
        public void DownloadEnergyExtracted(float[] energy)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _energyExtractedBuffer!.CopyTo(energy);
        }
        
        public void DownloadPairEvents(int[] events)
        {
            if (!_initialized)
                throw new InvalidOperationException("Engine not initialized");
                
            _pairCreatedBuffer!.CopyTo(events);
        }
        
        private void DisposeBuffers()
        {
            _temperatureBuffer?.Dispose();
            _pairProbabilityBuffer?.Dispose();
            _pairCreatedBuffer?.Dispose();
            _energyExtractedBuffer?.Dispose();
            _edgeWeightsBuffer?.Dispose();
            _rngStateBuffer?.Dispose();
            _csrOffsetsBuffer?.Dispose();
            _csrNeighborsBuffer?.Dispose();
        }
        
        public void Dispose()
        {
            DisposeBuffers();
            GC.SuppressFinalize(this);
        }
    }
}
