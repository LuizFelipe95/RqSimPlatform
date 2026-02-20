using ComputeSharp;

namespace RQSimulation.GPUOptimized.SpinorField
{
    /// <summary>
    /// Double-precision GPU shaders for Dirac spinor evolution.
    /// 
    /// NOTE: HLSL intrinsics (Hlsl.Sin, Hlsl.Cos, etc.) only support float.
    /// For double precision, we use Taylor series or cast to float for transcendentals.
    /// The main benefit of double is in accumulation and inner products.
    /// </summary>

    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct CopySpinorKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> src;
        public readonly ReadWriteBuffer<Double2> dst;

        public CopySpinorKernelDouble(ReadWriteBuffer<Double2> src, ReadWriteBuffer<Double2> dst)
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

    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct DiracDerivativeKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> spinorA;
        public readonly ReadWriteBuffer<Double2> spinorB;
        public readonly ReadWriteBuffer<Double2> spinorC;
        public readonly ReadWriteBuffer<Double2> spinorD;
        public readonly ReadWriteBuffer<Double2> dA;
        public readonly ReadWriteBuffer<Double2> dB;
        public readonly ReadWriteBuffer<Double2> dC;
        public readonly ReadWriteBuffer<Double2> dD;
        public readonly ReadOnlyBuffer<double> masses;
        public readonly ReadOnlyBuffer<double> lapses;
        public readonly ReadOnlyBuffer<int> parities;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrNeighbors;
        public readonly ReadOnlyBuffer<double> csrWeights;
        public readonly ReadOnlyBuffer<double> edgePhases;
        public readonly double c;
        public readonly double hbar;
        public readonly double wilsonR;
        public readonly double wilsonMassPenalty;
        public readonly int nodeCount;

        public DiracDerivativeKernelDouble(
            ReadWriteBuffer<Double2> spinorA,
            ReadWriteBuffer<Double2> spinorB,
            ReadWriteBuffer<Double2> spinorC,
            ReadWriteBuffer<Double2> spinorD,
            ReadWriteBuffer<Double2> dA,
            ReadWriteBuffer<Double2> dB,
            ReadWriteBuffer<Double2> dC,
            ReadWriteBuffer<Double2> dD,
            ReadOnlyBuffer<double> masses,
            ReadOnlyBuffer<double> lapses,
            ReadOnlyBuffer<int> parities,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            ReadOnlyBuffer<double> csrWeights,
            ReadOnlyBuffer<double> edgePhases,
            double c,
            double hbar,
            double wilsonR,
            double wilsonMassPenalty,
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

            double mass = masses[i];
            double lapse = lapses[i];
            int parityI = parities[i];

            double mc = mass * c;

            Double2 deltaA = new Double2(0, 0);
            Double2 deltaB = new Double2(0, 0);
            Double2 deltaC = new Double2(0, 0);
            Double2 deltaD = new Double2(0, 0);

            int rowStart = csrOffsets[i];
            int rowEnd = csrOffsets[i + 1];
            
            // RQG-HYPOTHESIS: Degree normalization 1/N
            // Makes derivative independent of node degree
            int neighborCount = rowEnd - rowStart;
            double degreeNorm = neighborCount > 0 ? 1.0 / neighborCount : 1.0;

            for (int k = rowStart; k < rowEnd; k++)
            {
                int j = csrNeighbors[k];
                double weight = csrWeights[k];
                float phase = (float)edgePhases[k];

                // Use float for trig (HLSL limitation), double for accumulation
                float cosP = Hlsl.Cos(phase);
                float sinP = Hlsl.Sin(phase);

                Double2 gaugedA_j = ComplexMul(spinorA[j], cosP, -sinP);
                Double2 gaugedB_j = ComplexMul(spinorB[j], cosP, -sinP);
                Double2 gaugedC_j = ComplexMul(spinorC[j], cosP, -sinP);
                Double2 gaugedD_j = ComplexMul(spinorD[j], cosP, -sinP);

                double wilsonFactor = -0.5 * wilsonR * weight * degreeNorm;
                deltaA = AddScaled(deltaA, Sub(gaugedA_j, spinorA[i]), wilsonFactor);
                deltaB = AddScaled(deltaB, Sub(gaugedB_j, spinorB[i]), wilsonFactor);
                deltaC = AddScaled(deltaC, Sub(gaugedC_j, spinorC[i]), wilsonFactor);
                deltaD = AddScaled(deltaD, Sub(gaugedD_j, spinorD[i]), wilsonFactor);

                int parityJ = parities[j];
                bool sameParity = (parityI == parityJ);
                double sign = sameParity ? -1.0 : 1.0;

                if (sameParity)
                {
                    double extraWilson = wilsonMassPenalty * weight * degreeNorm;
                    deltaA = AddScaled(deltaA, Sub(spinorA[i], gaugedA_j), extraWilson);
                    deltaB = AddScaled(deltaB, Sub(spinorB[i], gaugedB_j), extraWilson);
                    deltaC = AddScaled(deltaC, Sub(spinorC[i], gaugedC_j), extraWilson);
                    deltaD = AddScaled(deltaD, Sub(spinorD[i], gaugedD_j), extraWilson);
                }
                else
                {
                    int edgeDir = k % 2;
                    double normalizedWeight = sign * weight * degreeNorm;

                    if (edgeDir == 0)
                    {
                        deltaB = AddScaled(deltaB, Sub(gaugedA_j, spinorA[i]), normalizedWeight);
                        deltaA = AddScaled(deltaA, Sub(gaugedB_j, spinorB[i]), normalizedWeight);
                        deltaD = AddScaled(deltaD, Sub(gaugedC_j, spinorC[i]), normalizedWeight);
                        deltaC = AddScaled(deltaC, Sub(gaugedD_j, spinorD[i]), normalizedWeight);
                    }
                    else
                    {
                        Double2 diffA = Sub(gaugedA_j, spinorA[i]);
                        Double2 diffB = Sub(gaugedB_j, spinorB[i]);
                        Double2 diffC = Sub(gaugedC_j, spinorC[i]);
                        Double2 diffD = Sub(gaugedD_j, spinorD[i]);

                        Double2 iDiffA = new Double2(-diffA.Y, diffA.X);
                        Double2 iDiffB = new Double2(-diffB.Y, diffB.X);
                        Double2 iDiffC = new Double2(-diffC.Y, diffC.X);
                        Double2 iDiffD = new Double2(-diffD.Y, diffD.X);

                        deltaB = AddScaled(deltaB, iDiffA, normalizedWeight);
                        deltaA = AddScaled(deltaA, iDiffB, -normalizedWeight);
                        deltaD = AddScaled(deltaD, iDiffC, -normalizedWeight);
                        deltaC = AddScaled(deltaC, iDiffD, normalizedWeight);
                    }
                }
            }

            double massFactor = mc / hbar;
            Double2 massTermA = ComplexMulI(spinorC[i], -massFactor);
            Double2 massTermB = ComplexMulI(spinorD[i], -massFactor);
            Double2 massTermC = ComplexMulI(spinorA[i], -massFactor);
            Double2 massTermD = ComplexMulI(spinorB[i], -massFactor);

            double factor = -lapse / hbar;

            dA[i] = Scale(Add(Scale(deltaA, c), massTermA), factor);
            dB[i] = Scale(Add(Scale(deltaB, c), massTermB), factor);
            dC[i] = Scale(Add(Scale(deltaC, c), massTermC), factor);
            dD[i] = Scale(Add(Scale(deltaD, c), massTermD), factor);
        }

        private static Double2 ComplexMul(Double2 z, float cosP, float sinP)
            => new Double2(z.X * cosP - z.Y * sinP, z.X * sinP + z.Y * cosP);

        private static Double2 ComplexMulI(Double2 z, double factor)
            => new Double2(-z.Y * factor, z.X * factor);

        private static Double2 Sub(Double2 a, Double2 b)
            => new Double2(a.X - b.X, a.Y - b.Y);

        private static Double2 Add(Double2 a, Double2 b)
            => new Double2(a.X + b.X, a.Y + b.Y);

        private static Double2 AddScaled(Double2 a, Double2 b, double s)
            => new Double2(a.X + b.X * s, a.Y + b.Y * s);

        private static Double2 Scale(Double2 a, double s)
            => new Double2(a.X * s, a.Y * s);
    }

    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct EulerStepKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> spinor;
        public readonly ReadWriteBuffer<Double2> derivative;
        public readonly double dt;

        public EulerStepKernelDouble(ReadWriteBuffer<Double2> spinor, ReadWriteBuffer<Double2> derivative, double dt)
        {
            this.spinor = spinor;
            this.derivative = derivative;
            this.dt = dt;
        }

        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= spinor.Length) return;

            spinor[idx] = new Double2(
                spinor[idx].X + dt * derivative[idx].X,
                spinor[idx].Y + dt * derivative[idx].Y
            );
        }
    }

    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct StateVelocityKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> spinorA;
        public readonly ReadWriteBuffer<Double2> spinorB;
        public readonly ReadWriteBuffer<Double2> spinorC;
        public readonly ReadWriteBuffer<Double2> spinorD;
        public readonly ReadWriteBuffer<Double2> prevA;
        public readonly ReadWriteBuffer<Double2> prevB;
        public readonly ReadWriteBuffer<Double2> prevC;
        public readonly ReadWriteBuffer<Double2> prevD;
        public readonly ReadWriteBuffer<double> velocity;

        public StateVelocityKernelDouble(
            ReadWriteBuffer<Double2> spinorA,
            ReadWriteBuffer<Double2> spinorB,
            ReadWriteBuffer<Double2> spinorC,
            ReadWriteBuffer<Double2> spinorD,
            ReadWriteBuffer<Double2> prevA,
            ReadWriteBuffer<Double2> prevB,
            ReadWriteBuffer<Double2> prevC,
            ReadWriteBuffer<Double2> prevD,
            ReadWriteBuffer<double> velocity)
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

            double distSq = 0;

            Double2 dA = new Double2(spinorA[i].X - prevA[i].X, spinorA[i].Y - prevA[i].Y);
            Double2 dB = new Double2(spinorB[i].X - prevB[i].X, spinorB[i].Y - prevB[i].Y);
            Double2 dC = new Double2(spinorC[i].X - prevC[i].X, spinorC[i].Y - prevC[i].Y);
            Double2 dD = new Double2(spinorD[i].X - prevD[i].X, spinorD[i].Y - prevD[i].Y);

            distSq += dA.X * dA.X + dA.Y * dA.Y;
            distSq += dB.X * dB.X + dB.Y * dB.Y;
            distSq += dC.X * dC.X + dC.Y * dC.Y;
            distSq += dD.X * dD.X + dD.Y * dD.Y;

            double normCurrent = 0;
            double normPrev = 0;

            normCurrent += spinorA[i].X * spinorA[i].X + spinorA[i].Y * spinorA[i].Y;
            normCurrent += spinorB[i].X * spinorB[i].X + spinorB[i].Y * spinorB[i].Y;
            normCurrent += spinorC[i].X * spinorC[i].X + spinorC[i].Y * spinorC[i].Y;
            normCurrent += spinorD[i].X * spinorD[i].X + spinorD[i].Y * spinorD[i].Y;

            normPrev += prevA[i].X * prevA[i].X + prevA[i].Y * prevA[i].Y;
            normPrev += prevB[i].X * prevB[i].X + prevB[i].Y * prevB[i].Y;
            normPrev += prevC[i].X * prevC[i].X + prevC[i].Y * prevC[i].Y;
            normPrev += prevD[i].X * prevD[i].X + prevD[i].Y * prevD[i].Y;

            // Use Hlsl.Sqrt with float cast (adequate precision for normalization)
            double normSum = Hlsl.Sqrt((float)normCurrent) + Hlsl.Sqrt((float)normPrev);

            if (normSum < 1e-12)
                velocity[i] = 0;
            else
                velocity[i] = Hlsl.Sqrt((float)distSq) / (normSum * 0.5);
        }
    }
}
