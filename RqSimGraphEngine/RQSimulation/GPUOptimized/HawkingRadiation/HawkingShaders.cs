using ComputeSharp;

namespace RQSimulation.GPUOptimized.HawkingRadiation
{
    /// <summary>
    /// Compute pair creation probability: P = exp(-2? ? m / T)
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct PairProbabilityKernel : IComputeShader
    {
        public readonly ReadOnlyBuffer<float> temperature;
        public readonly ReadWriteBuffer<float> probability;
        public readonly float massThreshold;
        
        public PairProbabilityKernel(
            ReadOnlyBuffer<float> temperature,
            ReadWriteBuffer<float> probability,
            float massThreshold)
        {
            this.temperature = temperature;
            this.probability = probability;
            this.massThreshold = massThreshold;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= probability.Length) return;
            
            float T = temperature[i];
            
            if (T < 1e-10f)
            {
                probability[i] = 0;
                return;
            }
            
            float exponent = -2.0f * 3.14159265f * massThreshold / T;
            
            if (exponent < -50.0f)
                probability[i] = 0;
            else
                probability[i] = Hlsl.Exp(exponent);
        }
    }
    
    /// <summary>
    /// Stochastic pair creation based on probability.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct StochasticPairCreationKernel : IComputeShader
    {
        public readonly ReadWriteBuffer<float> probability;
        public readonly ReadWriteBuffer<int> pairCreated;
        public readonly ReadWriteBuffer<uint> rngState;
        
        public StochasticPairCreationKernel(
            ReadWriteBuffer<float> probability,
            ReadWriteBuffer<int> pairCreated,
            ReadWriteBuffer<uint> rngState)
        {
            this.probability = probability;
            this.pairCreated = pairCreated;
            this.rngState = rngState;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= pairCreated.Length) return;
            
            float p = probability[i];
            
            uint state = rngState[i];
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            rngState[i] = state;
            
            float r = (state & 0x00FFFFFF) / 16777216.0f;
            pairCreated[i] = (r < p) ? 1 : 0;
        }
    }
    
    /// <summary>
    /// Backreaction: reduce edge weights when pair is created.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct BackreactionKernel : IComputeShader
    {
        public readonly ReadWriteBuffer<int> pairCreated;
        public readonly ReadWriteBuffer<float> energyExtracted;
        public readonly ReadWriteBuffer<float> edgeWeights;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrNeighbors;
        public readonly float pairEnergy;
        public readonly float planckThreshold;
        public readonly int nodeCount;
        
        public BackreactionKernel(
            ReadWriteBuffer<int> pairCreated,
            ReadWriteBuffer<float> energyExtracted,
            ReadWriteBuffer<float> edgeWeights,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            float pairEnergy,
            float planckThreshold,
            int nodeCount)
        {
            this.pairCreated = pairCreated;
            this.energyExtracted = energyExtracted;
            this.edgeWeights = edgeWeights;
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.pairEnergy = pairEnergy;
            this.planckThreshold = planckThreshold;
            this.nodeCount = nodeCount;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;
            
            if (pairCreated[i] == 0)
            {
                energyExtracted[i] = 0;
                return;
            }
            
            int start = csrOffsets[i];
            int end = csrOffsets[i + 1];
            
            float totalWeight = 0;
            for (int k = start; k < end; k++)
                totalWeight += edgeWeights[k];
            
            if (totalWeight < 1e-10f)
            {
                energyExtracted[i] = 0;
                return;
            }
            
            float fractionToRemove = pairEnergy / totalWeight;
            if (fractionToRemove > 0.1f) fractionToRemove = 0.1f;
            
            float extracted = 0;
            for (int k = start; k < end; k++)
            {
                float oldWeight = edgeWeights[k];
                float reduction = oldWeight * fractionToRemove;
                float newWeight = oldWeight - reduction;
                
                if (newWeight < planckThreshold)
                    newWeight = planckThreshold;
                
                edgeWeights[k] = newWeight;
                extracted += oldWeight - newWeight;
            }
            
            energyExtracted[i] = extracted;
        }
    }
}
