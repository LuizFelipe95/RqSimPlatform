using ComputeSharp;

namespace RQSimulation.GPUOptimized.RelationalTime
{
    /// <summary>
    /// PAGE-WOOTTERS EVOLUTION KERNEL
    /// ==============================
    /// Implements the core Page-Wootters protocol for relational time evolution.
    /// 
    /// RQ-HYPOTHESIS PHYSICS:
    /// Time emerges as correlations (entanglement) between a clock subsystem
    /// and the rest of the universe. Instead of evolving all nodes by dt,
    /// we compute the conditional probability P(System | Clock) and evolve
    /// only nodes that are quantum-correlated with the clock.
    /// 
    /// Algorithm:
    /// 1. Compute entanglement witness = <Psi_node | Psi_clock> (dot product)
    /// 2. If |entanglement| > threshold: node is correlated, evolve it
    /// 3. Evolution: Psi_new = Psi_old * exp(-i * H * deltaClockPhase)
    /// 4. Update LastClockPhase for next step
    /// 
    /// Nodes NOT correlated with the clock are "frozen" - no event occurs
    /// relative to the observer (pure Background Independence).
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct PageWoottersEvolutionKernel : IComputeShader
    {
        /// <summary>Complex wavefunction per node (x=Real, y=Imag)</summary>
        public readonly ReadWriteBuffer<Float2> waveFunction;
        
        /// <summary>Current quantum phase per node</summary>
        public readonly ReadWriteBuffer<float> phase;
        
        /// <summary>Last clock phase seen by each node (for delta computation)</summary>
        public readonly ReadWriteBuffer<float> lastClockPhase;
        
        /// <summary>Local Hamiltonian eigenvalue per node</summary>
        public readonly ReadOnlyBuffer<float> hamiltonianEnergy;
        
        /// <summary>Output: 1 if node evolved, 0 if frozen</summary>
        public readonly ReadWriteBuffer<int> evolutionFlags;
        
        /// <summary>Index of the clock node</summary>
        public readonly int clockNodeIndex;
        
        /// <summary>Minimum entanglement for evolution to occur</summary>
        public readonly float entanglementThreshold;
        
        public PageWoottersEvolutionKernel(
            ReadWriteBuffer<Float2> waveFunction,
            ReadWriteBuffer<float> phase,
            ReadWriteBuffer<float> lastClockPhase,
            ReadOnlyBuffer<float> hamiltonianEnergy,
            ReadWriteBuffer<int> evolutionFlags,
            int clockNodeIndex,
            float entanglementThreshold)
        {
            this.waveFunction = waveFunction;
            this.phase = phase;
            this.lastClockPhase = lastClockPhase;
            this.hamiltonianEnergy = hamiltonianEnergy;
            this.evolutionFlags = evolutionFlags;
            this.clockNodeIndex = clockNodeIndex;
            this.entanglementThreshold = entanglementThreshold;
        }
        
        public void Execute()
        {
            int nodeIndex = ThreadIds.X;
            if (nodeIndex >= waveFunction.Length) return;
            
            // Clock node always "evolves" (it defines the reference frame)
            if (nodeIndex == clockNodeIndex)
            {
                // Update clock's own phase
                float clockEnergy = hamiltonianEnergy[clockNodeIndex];
                float newClockPhase = phase[clockNodeIndex] + clockEnergy * 0.01f; // Small step
                phase[clockNodeIndex] = Hlsl.Fmod(newClockPhase, 2.0f * 3.14159265f);
                evolutionFlags[clockNodeIndex] = 1;
                return;
            }
            
            // ============================================================
            // STEP 1: Compute entanglement witness with clock
            // ============================================================
            Float2 psiNode = waveFunction[nodeIndex];
            Float2 psiClock = waveFunction[clockNodeIndex];
            
            // Entanglement witness: <Psi_node | Psi_clock> = Re(node) * Re(clock) + Im(node) * Im(clock)
            // This is the real part of the complex inner product
            float entanglementReal = psiNode.X * psiClock.X + psiNode.Y * psiClock.Y;
            
            // Also compute imaginary part for full complex dot product magnitude
            float entanglementImag = psiNode.X * psiClock.Y - psiNode.Y * psiClock.X;
            float entanglementMagnitude = Hlsl.Sqrt(entanglementReal * entanglementReal + 
                                                     entanglementImag * entanglementImag);
            
            // ============================================================
            // STEP 2: Check correlation threshold
            // ============================================================
            if (entanglementMagnitude < entanglementThreshold)
            {
                // Node is NOT correlated with clock - it remains "frozen"
                // No quantum event occurs relative to this observer
                evolutionFlags[nodeIndex] = 0;
                return;
            }
            
            // ============================================================
            // STEP 3: Compute phase delta from clock evolution
            // ============================================================
            float clockPhase = phase[clockNodeIndex];
            float previousClockPhase = lastClockPhase[nodeIndex];
            float deltaPhase = clockPhase - previousClockPhase;
            
            // ============================================================
            // STEP 4: Apply unitary evolution exp(-i * H * deltaPhase)
            // ============================================================
            // In Heisenberg picture: Psi_new = Psi_old * exp(-i * E * deltaPhase)
            // exp(-i * theta) = cos(theta) - i * sin(theta)
            
            float localEnergy = hamiltonianEnergy[nodeIndex];
            float theta = localEnergy * deltaPhase;
            
            // Compute exp(-i * theta)
            float cosTheta = Hlsl.Cos(theta);
            float sinTheta = Hlsl.Sin(theta);
            
            // Complex multiplication: (a + bi) * (cos - i*sin) = (a*cos + b*sin) + i*(b*cos - a*sin)
            Float2 unitaryOp = new Float2(cosTheta, -sinTheta);
            Float2 newPsi = ComplexMultiply(psiNode, unitaryOp);
            
            // ============================================================
            // STEP 5: Store new state and update clock phase marker
            // ============================================================
            waveFunction[nodeIndex] = newPsi;
            phase[nodeIndex] = phase[nodeIndex] + theta;
            lastClockPhase[nodeIndex] = clockPhase;
            evolutionFlags[nodeIndex] = 1;
        }
        
        /// <summary>
        /// Complex multiplication: (a + bi) * (c + di) = (ac - bd) + i(ad + bc)
        /// </summary>
        private static Float2 ComplexMultiply(Float2 a, Float2 b)
        {
            return new Float2(
                a.X * b.X - a.Y * b.Y,  // Real part
                a.X * b.Y + a.Y * b.X   // Imaginary part
            );
        }
    }
    
    /// <summary>
    /// Compute lapse from state velocity: N = 1 / (1 + v)
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct LapseFromVelocityKernel : IComputeShader
    {
        public readonly ReadOnlyBuffer<float> velocity;
        public readonly ReadWriteBuffer<float> lapse;
        public readonly float minLapse;
        public readonly float maxLapse;
        
        public LapseFromVelocityKernel(
            ReadOnlyBuffer<float> velocity,
            ReadWriteBuffer<float> lapse,
            float minLapse,
            float maxLapse)
        {
            this.velocity = velocity;
            this.lapse = lapse;
            this.minLapse = minLapse;
            this.maxLapse = maxLapse;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= lapse.Length) return;
            
            float v = velocity[i];
            float n = 1.0f / (1.0f + Hlsl.Max(0, v));
            lapse[i] = Hlsl.Clamp(n, minLapse, maxLapse);
        }
    }
    
    /// <summary>
    /// Compute entanglement entropy from edge weights.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ComputeEntropyKernel : IComputeShader
    {
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrNeighbors;
        public readonly ReadOnlyBuffer<float> csrWeights;
        public readonly ReadOnlyBuffer<float> correlationMass;
        public readonly ReadWriteBuffer<float> entropy;
        public readonly int nodeCount;
        
        public ComputeEntropyKernel(
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            ReadOnlyBuffer<float> csrWeights,
            ReadOnlyBuffer<float> correlationMass,
            ReadWriteBuffer<float> entropy,
            int nodeCount)
        {
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.csrWeights = csrWeights;
            this.correlationMass = correlationMass;
            this.entropy = entropy;
            this.nodeCount = nodeCount;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;
            
            int start = csrOffsets[i];
            int end = csrOffsets[i + 1];
            
            float totalWeight = 0;
            for (int k = start; k < end; k++)
                totalWeight += csrWeights[k];
            
            if (totalWeight < 1e-12f)
            {
                entropy[i] = 0;
                return;
            }
            
            float s = 0;
            for (int k = start; k < end; k++)
            {
                float w = csrWeights[k];
                if (w < 1e-12f) continue;
                float p = w / totalWeight;
                s -= p * Hlsl.Log(p);
            }
            
            float mass = correlationMass[i];
            if (mass > 0)
                s += Hlsl.Log(1.0f + mass);
            
            entropy[i] = Hlsl.Max(0, s);
        }
    }
    
    /// <summary>
    /// Compute lapse from entropy: N = exp(-? ? S)
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct LapseFromEntropyKernel : IComputeShader
    {
        public readonly ReadWriteBuffer<float> entropy;
        public readonly ReadWriteBuffer<float> lapse;
        public readonly float alpha;
        public readonly float minLapse;
        public readonly float maxLapse;
        
        public LapseFromEntropyKernel(
            ReadWriteBuffer<float> entropy,
            ReadWriteBuffer<float> lapse,
            float alpha,
            float minLapse,
            float maxLapse)
        {
            this.entropy = entropy;
            this.lapse = lapse;
            this.alpha = alpha;
            this.minLapse = minLapse;
            this.maxLapse = maxLapse;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= lapse.Length) return;
            
            float s = entropy[i];
            float n = Hlsl.Exp(-alpha * s);
            lapse[i] = Hlsl.Clamp(n, minLapse, maxLapse);
        }
    }
    
    /// <summary>
    /// Combined lapse: use velocity where valid, entropy as fallback.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct LapseCombinedKernel : IComputeShader
    {
        public readonly ReadOnlyBuffer<float> velocity;
        public readonly ReadWriteBuffer<float> entropy;
        public readonly ReadWriteBuffer<float> lapse;
        public readonly float alpha;
        public readonly float minLapse;
        public readonly float maxLapse;
        
        public LapseCombinedKernel(
            ReadOnlyBuffer<float> velocity,
            ReadWriteBuffer<float> entropy,
            ReadWriteBuffer<float> lapse,
            float alpha,
            float minLapse,
            float maxLapse)
        {
            this.velocity = velocity;
            this.entropy = entropy;
            this.lapse = lapse;
            this.alpha = alpha;
            this.minLapse = minLapse;
            this.maxLapse = maxLapse;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= lapse.Length) return;
            
            float v = velocity[i];
            float n;
            
            if (v >= 0)
                n = 1.0f / (1.0f + v);
            else
                n = Hlsl.Exp(-alpha * entropy[i]);
            
            lapse[i] = Hlsl.Clamp(n, minLapse, maxLapse);
        }
    }
    
    /// <summary>
    /// Compute Unruh temperature from lapse gradient.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct UnruhTemperatureKernel : IComputeShader
    {
        public readonly ReadWriteBuffer<float> lapse;
        public readonly ReadWriteBuffer<float> temperature;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrNeighbors;
        public readonly ReadOnlyBuffer<float> csrWeights;
        public readonly int nodeCount;
        
        public UnruhTemperatureKernel(
            ReadWriteBuffer<float> lapse,
            ReadWriteBuffer<float> temperature,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            ReadOnlyBuffer<float> csrWeights,
            int nodeCount)
        {
            this.lapse = lapse;
            this.temperature = temperature;
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.csrWeights = csrWeights;
            this.nodeCount = nodeCount;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;
            
            float gradN = 0;
            float N_i = lapse[i];
            
            int start = csrOffsets[i];
            int end = csrOffsets[i + 1];
            
            for (int k = start; k < end; k++)
            {
                int j = csrNeighbors[k];
                float w = csrWeights[k];
                if (w < 1e-12f) continue;
                
                float N_j = lapse[j];
                float dN = N_i - N_j;
                float dist = -Hlsl.Log(w);
                if (dist < 1e-6f) dist = 1e-6f;
                
                gradN += Hlsl.Abs(dN / dist);
            }
            
            temperature[i] = gradN / (2.0f * 3.14159265f);
        }
    }
}
