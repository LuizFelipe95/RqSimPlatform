using ComputeSharp;

namespace RQSimulation.GPUOptimized.SpinorField
{
    /// <summary>
    /// GPU compute shaders for Dirac spinor evolution.
    /// 
    /// Implements the staggered Dirac equation with Wilson term:
    /// - Gauge-covariant parallel transport via U(1) phases
    /// - Staggered fermion signs based on topological parity
    /// - Wilson term for doubler suppression
    /// - Lapse function for gravitational time dilation
    /// </summary>
    
    /// <summary>
    /// Copy spinor component: dst = src
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct CopySpinorKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> src;
        private readonly ReadWriteBuffer<Float2> dst;
        
        public CopySpinorKernel(ReadWriteBuffer<Float2> src, ReadWriteBuffer<Float2> dst)
        {
            this.src = src;
            this.dst = dst;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx < dst.Length)
                dst[idx] = src[idx];
        }
    }
    
    /// <summary>
    /// Compute Dirac derivative for all spinor components.
    /// 
    /// RQ-HYPOTHESIS PHYSICS:
    /// - Staggered fermions: sign depends on parity difference
    /// - Wilson term: -r/2 ? ? w_ij ? (?_j - ?_i) to lift doublers
    /// - U(1) parallel transport: exp(-i?phase) for gauge covariance
    /// - Lapse function: d?/dt = N ? (-i/?) ? H ? ?
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct DiracDerivativeKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> spinorA;
        private readonly ReadWriteBuffer<Float2> spinorB;
        private readonly ReadWriteBuffer<Float2> spinorC;
        private readonly ReadWriteBuffer<Float2> spinorD;
        private readonly ReadWriteBuffer<Float2> dA;
        private readonly ReadWriteBuffer<Float2> dB;
        private readonly ReadWriteBuffer<Float2> dC;
        private readonly ReadWriteBuffer<Float2> dD;
        private readonly ReadOnlyBuffer<float> masses;
        private readonly ReadOnlyBuffer<float> lapses;
        private readonly ReadOnlyBuffer<int> parities;
        private readonly ReadOnlyBuffer<int> csrOffsets;
        private readonly ReadOnlyBuffer<int> csrNeighbors;
        private readonly ReadOnlyBuffer<float> csrWeights;
        private readonly ReadOnlyBuffer<float> edgePhases;
        private readonly float c;       // Speed of light
        private readonly float hbar;    // Reduced Planck constant
        private readonly float wilsonR; // Wilson parameter
        private readonly float wilsonMassPenalty; // Extra mass for same-parity edges
        private readonly int nodeCount;
        
        public DiracDerivativeKernel(
            ReadWriteBuffer<Float2> spinorA,
            ReadWriteBuffer<Float2> spinorB,
            ReadWriteBuffer<Float2> spinorC,
            ReadWriteBuffer<Float2> spinorD,
            ReadWriteBuffer<Float2> dA,
            ReadWriteBuffer<Float2> dB,
            ReadWriteBuffer<Float2> dC,
            ReadWriteBuffer<Float2> dD,
            ReadOnlyBuffer<float> masses,
            ReadOnlyBuffer<float> lapses,
            ReadOnlyBuffer<int> parities,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            ReadOnlyBuffer<float> csrWeights,
            ReadOnlyBuffer<float> edgePhases,
            float c,
            float hbar,
            float wilsonR,
            float wilsonMassPenalty,
            int nodeCount)
        {
            this.spinorA = spinorA;
            this.spinorB = spinorB;
            this.spinorC = spinorC;
            this.spinorD = spinorD;
            this.dA = dA;
            this.dB = dB;
            this.dC = dC;
            this.dD = dD;
            this.masses = masses;
            this.lapses = lapses;
            this.parities = parities;
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.csrWeights = csrWeights;
            this.edgePhases = edgePhases;
            this.c = c;
            this.hbar = hbar;
            this.wilsonR = wilsonR;
            this.wilsonMassPenalty = wilsonMassPenalty;
            this.nodeCount = nodeCount;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;
            
            float mass = masses[i];
            float lapse = lapses[i];
            int parityI = parities[i];
            bool isEven = (parityI == 0);
            
            float mc = mass * c;
            
            // Accumulate derivative contributions
            Float2 deltaA = new Float2(0, 0);
            Float2 deltaB = new Float2(0, 0);
            Float2 deltaC = new Float2(0, 0);
            Float2 deltaD = new Float2(0, 0);
            
            // Loop over neighbors
            int rowStart = csrOffsets[i];
            int rowEnd = csrOffsets[i + 1];
            
            for (int k = rowStart; k < rowEnd; k++)
            {
                int j = csrNeighbors[k];
                float weight = csrWeights[k];
                float phase = edgePhases[k];
                
                // U(1) parallel transport: exp(-i?phase)
                float cosP = Hlsl.Cos(phase);
                float sinP = Hlsl.Sin(phase);
                
                // Transport spinor from j to i
                Float2 gaugedA_j = ComplexMul(spinorA[j], cosP, -sinP);
                Float2 gaugedB_j = ComplexMul(spinorB[j], cosP, -sinP);
                Float2 gaugedC_j = ComplexMul(spinorC[j], cosP, -sinP);
                Float2 gaugedD_j = ComplexMul(spinorD[j], cosP, -sinP);
                
                // Wilson term: -r/2 ? w_ij ? (?_j - ?_i)
                // Applied to ALL edges to lift doublers
                float wilsonFactor = -0.5f * wilsonR * weight;
                deltaA = AddScaled(deltaA, SubFloat2(gaugedA_j, spinorA[i]), wilsonFactor);
                deltaB = AddScaled(deltaB, SubFloat2(gaugedB_j, spinorB[i]), wilsonFactor);
                deltaC = AddScaled(deltaC, SubFloat2(gaugedC_j, spinorC[i]), wilsonFactor);
                deltaD = AddScaled(deltaD, SubFloat2(gaugedD_j, spinorD[i]), wilsonFactor);
                
                // Parity-dependent staggered fermion terms
                int parityJ = parities[j];
                bool sameParity = (parityI == parityJ);
                float sign = sameParity ? -1.0f : 1.0f;
                
                if (sameParity)
                {
                    // Same-parity edge: extra Wilson mass penalty
                    float extraWilson = wilsonMassPenalty * weight;
                    deltaA = AddScaled(deltaA, SubFloat2(spinorA[i], gaugedA_j), extraWilson);
                    deltaB = AddScaled(deltaB, SubFloat2(spinorB[i], gaugedB_j), extraWilson);
                    deltaC = AddScaled(deltaC, SubFloat2(spinorC[i], gaugedC_j), extraWilson);
                    deltaD = AddScaled(deltaD, SubFloat2(spinorD[i], gaugedD_j), extraWilson);
                }
                else
                {
                    // Different parity: kinetic term
                    // Direction encoding (simplified: use edge index parity)
                    int edgeDir = k % 2;
                    
                    if (edgeDir == 0)
                    {
                        // X-like direction: couples A?B and C?D
                        deltaB = AddScaled(deltaB, SubFloat2(gaugedA_j, spinorA[i]), sign * weight);
                        deltaA = AddScaled(deltaA, SubFloat2(gaugedB_j, spinorB[i]), sign * weight);
                        deltaD = AddScaled(deltaD, SubFloat2(gaugedC_j, spinorC[i]), sign * weight);
                        deltaC = AddScaled(deltaC, SubFloat2(gaugedD_j, spinorD[i]), sign * weight);
                    }
                    else
                    {
                        // Y-like direction: couples with imaginary unit
                        // i ? (gaugedA_j - spinorA[i])
                        Float2 diffA = SubFloat2(gaugedA_j, spinorA[i]);
                        Float2 diffB = SubFloat2(gaugedB_j, spinorB[i]);
                        Float2 diffC = SubFloat2(gaugedC_j, spinorC[i]);
                        Float2 diffD = SubFloat2(gaugedD_j, spinorD[i]);
                        
                        Float2 iDiffA = new Float2(-diffA.Y, diffA.X); // i ? diff
                        Float2 iDiffB = new Float2(-diffB.Y, diffB.X);
                        Float2 iDiffC = new Float2(-diffC.Y, diffC.X);
                        Float2 iDiffD = new Float2(-diffD.Y, diffD.X);
                        
                        deltaB = AddScaled(deltaB, iDiffA, sign * weight);
                        deltaA = AddScaled(deltaA, iDiffB, -sign * weight);
                        deltaD = AddScaled(deltaD, iDiffC, -sign * weight);
                        deltaC = AddScaled(deltaC, iDiffD, sign * weight);
                    }
                }
            }
            
            // Mass term: -i ? mc/? ? ?^0 ? ?
            // ?^0 couples upper (A,B) to lower (C,D)
            float massFactor = mc / hbar;
            Float2 massTermA = ComplexMulI(spinorC[i], -massFactor);
            Float2 massTermB = ComplexMulI(spinorD[i], -massFactor);
            Float2 massTermC = ComplexMulI(spinorA[i], -massFactor);
            Float2 massTermD = ComplexMulI(spinorB[i], -massFactor);
            
            // Total derivative: d?/dt = N ? (-1/?) ? (c ? delta + massTerm)
            float factor = -lapse / hbar;
            
            dA[i] = Scale(AddFloat2(Scale(deltaA, c), massTermA), factor);
            dB[i] = Scale(AddFloat2(Scale(deltaB, c), massTermB), factor);
            dC[i] = Scale(AddFloat2(Scale(deltaC, c), massTermC), factor);
            dD[i] = Scale(AddFloat2(Scale(deltaD, c), massTermD), factor);
        }
        
        // Complex multiply: (a + bi) ? (cos + i?sin)
        private static Float2 ComplexMul(Float2 z, float cosP, float sinP)
            => new Float2(z.X * cosP - z.Y * sinP, z.X * sinP + z.Y * cosP);
        
        // Multiply by imaginary unit scaled: i ? factor ? z
        private static Float2 ComplexMulI(Float2 z, float factor)
            => new Float2(-z.Y * factor, z.X * factor);
        
        // Vector operations
        private static Float2 SubFloat2(Float2 a, Float2 b)
            => new Float2(a.X - b.X, a.Y - b.Y);
        
        private static Float2 AddFloat2(Float2 a, Float2 b)
            => new Float2(a.X + b.X, a.Y + b.Y);
        
        private static Float2 AddScaled(Float2 a, Float2 b, float s)
            => new Float2(a.X + b.X * s, a.Y + b.Y * s);
        
        private static Float2 Scale(Float2 a, float s)
            => new Float2(a.X * s, a.Y * s);
    }
    
    /// <summary>
    /// Euler step: ? += dt ? d?
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct EulerStepKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> spinor;
        private readonly ReadWriteBuffer<Float2> derivative;
        private readonly float dt;
        
        public EulerStepKernel(ReadWriteBuffer<Float2> spinor, ReadWriteBuffer<Float2> derivative, float dt)
        {
            this.spinor = spinor;
            this.derivative = derivative;
            this.dt = dt;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= spinor.Length) return;
            
            spinor[idx] = new Float2(
                spinor[idx].X + dt * derivative[idx].X,
                spinor[idx].Y + dt * derivative[idx].Y
            );
        }
    }
    
    /// <summary>
    /// Compute state velocity: v_i = ||?(t) - ?(t-dt)||
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 6: State velocity for lapse function
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct StateVelocityKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> spinorA;
        private readonly ReadWriteBuffer<Float2> spinorB;
        private readonly ReadWriteBuffer<Float2> spinorC;
        private readonly ReadWriteBuffer<Float2> spinorD;
        private readonly ReadWriteBuffer<Float2> prevA;
        private readonly ReadWriteBuffer<Float2> prevB;
        private readonly ReadWriteBuffer<Float2> prevC;
        private readonly ReadWriteBuffer<Float2> prevD;
        private readonly ReadWriteBuffer<float> velocity;
        
        public StateVelocityKernel(
            ReadWriteBuffer<Float2> spinorA,
            ReadWriteBuffer<Float2> spinorB,
            ReadWriteBuffer<Float2> spinorC,
            ReadWriteBuffer<Float2> spinorD,
            ReadWriteBuffer<Float2> prevA,
            ReadWriteBuffer<Float2> prevB,
            ReadWriteBuffer<Float2> prevC,
            ReadWriteBuffer<Float2> prevD,
            ReadWriteBuffer<float> velocity)
        {
            this.spinorA = spinorA;
            this.spinorB = spinorB;
            this.spinorC = spinorC;
            this.spinorD = spinorD;
            this.prevA = prevA;
            this.prevB = prevB;
            this.prevC = prevC;
            this.prevD = prevD;
            this.velocity = velocity;
        }
        
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= velocity.Length) return;
            
            // ||? - ?_prev||? = ? |component - prev_component|?
            float distSq = 0;
            
            Float2 dA = new Float2(spinorA[i].X - prevA[i].X, spinorA[i].Y - prevA[i].Y);
            Float2 dB = new Float2(spinorB[i].X - prevB[i].X, spinorB[i].Y - prevB[i].Y);
            Float2 dC = new Float2(spinorC[i].X - prevC[i].X, spinorC[i].Y - prevC[i].Y);
            Float2 dD = new Float2(spinorD[i].X - prevD[i].X, spinorD[i].Y - prevD[i].Y);
            
            distSq += dA.X * dA.X + dA.Y * dA.Y;
            distSq += dB.X * dB.X + dB.Y * dB.Y;
            distSq += dC.X * dC.X + dC.Y * dC.Y;
            distSq += dD.X * dD.X + dD.Y * dD.Y;
            
            // Normalization
            float normCurrent = 0;
            float normPrev = 0;
            
            normCurrent += spinorA[i].X * spinorA[i].X + spinorA[i].Y * spinorA[i].Y;
            normCurrent += spinorB[i].X * spinorB[i].X + spinorB[i].Y * spinorB[i].Y;
            normCurrent += spinorC[i].X * spinorC[i].X + spinorC[i].Y * spinorC[i].Y;
            normCurrent += spinorD[i].X * spinorD[i].X + spinorD[i].Y * spinorD[i].Y;
            
            normPrev += prevA[i].X * prevA[i].X + prevA[i].Y * prevA[i].Y;
            normPrev += prevB[i].X * prevB[i].X + prevB[i].Y * prevB[i].Y;
            normPrev += prevC[i].X * prevC[i].X + prevC[i].Y * prevC[i].Y;
            normPrev += prevD[i].X * prevD[i].X + prevD[i].Y * prevD[i].Y;
            
            float normSum = Hlsl.Sqrt(normCurrent) + Hlsl.Sqrt(normPrev);
            
            if (normSum < 1e-12f)
                velocity[i] = 0;
            else
                velocity[i] = Hlsl.Sqrt(distSq) / (normSum * 0.5f);
        }
    }
}
