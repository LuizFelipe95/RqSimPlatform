using ComputeSharp;

namespace RQSimulation.GPUOptimized.YangMills
{
    /// <summary>
    /// Double-precision GPU shaders for Yang-Mills gauge field evolution.
    /// 
    /// NOTE: HLSL transcendental functions only support float.
    /// We cast to float for sin/cos/sqrt, but use double for accumulation.
    /// </summary>

    /// <summary>
    /// Double-precision SU(2) exponential update: U_new = exp(-i·dt·E) × U_old
    /// 
    /// Quaternion representation with Double4 for exact unitarity.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct SU2ExponentialUpdateKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double4> links;
        public readonly ReadWriteBuffer<Double4> electric;
        public readonly double dt;

        public SU2ExponentialUpdateKernelDouble(
            ReadWriteBuffer<Double4> links,
            ReadWriteBuffer<Double4> electric,
            double dt)
        {
            this.links = links;
            this.electric = electric;
            this.dt = dt;
        }

        public void Execute()
        {
            int e = ThreadIds.X;
            if (e >= links.Length) return;

            Double4 U = links[e];
            Double4 E = electric[e];

            double E1 = E.X;
            double E2 = E.Y;
            double E3 = E.Z;

            double Enorm2 = E1 * E1 + E2 * E2 + E3 * E3;
            double Enorm = Hlsl.Sqrt((float)Enorm2);
            double theta = dt * Enorm;

            Double4 expE;
            if (Enorm < 1e-15)
            {
                expE = new Double4(1, 0, 0, 0);
            }
            else
            {
                float thetaF = (float)theta;
                double c = Hlsl.Cos(thetaF);
                double s = Hlsl.Sin(thetaF);
                double invNorm = 1.0 / Enorm;

                expE = new Double4(
                    c,
                    -s * E1 * invNorm,
                    -s * E2 * invNorm,
                    -s * E3 * invNorm
                );
            }

            // Quaternion multiplication with full double precision
            links[e] = QuaternionMul(expE, U);
        }

        private static Double4 QuaternionMul(Double4 a, Double4 b)
        {
            return new Double4(
                a.X * b.X - a.Y * b.Y - a.Z * b.Z - a.W * b.W,
                a.X * b.Y + a.Y * b.X + a.Z * b.W - a.W * b.Z,
                a.X * b.Z - a.Y * b.W + a.Z * b.X + a.W * b.Y,
                a.X * b.W + a.Y * b.Z - a.Z * b.Y + a.W * b.X
            );
        }
    }

    /// <summary>
    /// Double-precision SU(2) staple computation for Wilson action.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct SU2StapleKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double4> links;
        public readonly ReadWriteBuffer<Double4> electric;
        public readonly ReadOnlyBuffer<int> edgeFrom;
        public readonly ReadOnlyBuffer<int> edgeTo;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrNeighbors;
        public readonly int edgeCount;

        public SU2StapleKernelDouble(
            ReadWriteBuffer<Double4> links,
            ReadWriteBuffer<Double4> electric,
            ReadOnlyBuffer<int> edgeFrom,
            ReadOnlyBuffer<int> edgeTo,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            int edgeCount)
        {
            this.links = links;
            this.electric = electric;
            this.edgeFrom = edgeFrom;
            this.edgeTo = edgeTo;
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.edgeCount = edgeCount;
        }

        public void Execute()
        {
            int e = ThreadIds.X;
            if (e >= edgeCount) return;

            int i = edgeFrom[e];
            int j = edgeTo[e];

            Double4 staple = new Double4(0, 0, 0, 0);
            int stapleCount = 0;

            int startI = csrOffsets[i];
            int endI = csrOffsets[i + 1];

            for (int idxI = startI; idxI < endI; idxI++)
            {
                int k = csrNeighbors[idxI];
                if (k == j) continue;

                int startK = csrOffsets[k];
                int endK = csrOffsets[k + 1];

                for (int idxK = startK; idxK < endK; idxK++)
                {
                    if (csrNeighbors[idxK] == j)
                    {
                        Double4 U_ik = links[idxI];
                        Double4 U_kj = links[idxK];

                        Double4 contrib = QuaternionMul(U_ik, U_kj);
                        staple = Add(staple, contrib);
                        stapleCount++;
                        break;
                    }
                }
            }

            if (stapleCount > 0)
            {
                Double4 U = links[e];
                Double4 Udag = QuaternionConj(U);
                Double4 SU = QuaternionMul(staple, Udag);

                double scale = 1.0 / stapleCount;
                electric[e] = new Double4(0, SU.Y * scale, SU.Z * scale, SU.W * scale);
            }
            else
            {
                electric[e] = new Double4(0, 0, 0, 0);
            }
        }

        private static Double4 QuaternionMul(Double4 a, Double4 b)
        {
            return new Double4(
                a.X * b.X - a.Y * b.Y - a.Z * b.Z - a.W * b.W,
                a.X * b.Y + a.Y * b.X + a.Z * b.W - a.W * b.Z,
                a.X * b.Z - a.Y * b.W + a.Z * b.X + a.W * b.Y,
                a.X * b.W + a.Y * b.Z - a.Z * b.Y + a.W * b.X
            );
        }

        private static Double4 QuaternionConj(Double4 q)
            => new Double4(q.X, -q.Y, -q.Z, -q.W);

        private static Double4 Add(Double4 a, Double4 b)
            => new Double4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    }

    /// <summary>
    /// Double-precision Metropolis update for SU(2) thermalization.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct SU2MetropolisKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double4> links;
        public readonly ReadWriteBuffer<Double4> electric;
        public readonly ReadWriteBuffer<uint> rngState;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrNeighbors;
        public readonly double beta;
        public readonly int edgeCount;

        public SU2MetropolisKernelDouble(
            ReadWriteBuffer<Double4> links,
            ReadWriteBuffer<Double4> electric,
            ReadWriteBuffer<uint> rngState,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            double beta,
            int edgeCount)
        {
            this.links = links;
            this.electric = electric;
            this.rngState = rngState;
            this.csrOffsets = csrOffsets;
            this.csrNeighbors = csrNeighbors;
            this.beta = beta;
            this.edgeCount = edgeCount;
        }

        public void Execute()
        {
            int e = ThreadIds.X;
            if (e >= edgeCount) return;

            uint state = rngState[e];
            Double4 U = links[e];
            Double4 E = electric[e];

            double r1 = NextRandom(ref state) * 0.2 - 0.1;
            double r2 = NextRandom(ref state) * 0.2 - 0.1;
            double r3 = NextRandom(ref state) * 0.2 - 0.1;
            double r0sq = 1.0 - r1 * r1 - r2 * r2 - r3 * r3;
            double r0 = r0sq > 0 ? Hlsl.Sqrt((float)r0sq) : 1.0;

            Double4 delta = Normalize(new Double4(r0, r1, r2, r3));
            Double4 U_new = QuaternionMul(delta, U);

            double S_old = ComputeLocalAction(U, E);
            double S_new = ComputeLocalAction(U_new, E);
            double dS = S_new - S_old;

            double r = NextRandom(ref state);
            double expVal = Hlsl.Exp((float)(-beta * dS));
            if (dS <= 0 || r < expVal)
            {
                links[e] = U_new;
            }

            rngState[e] = state;
        }

        private static double ComputeLocalAction(Double4 U, Double4 staple)
        {
            return -(U.X * staple.X + U.Y * staple.Y + U.Z * staple.Z + U.W * staple.W);
        }

        private static Double4 QuaternionMul(Double4 a, Double4 b)
        {
            return new Double4(
                a.X * b.X - a.Y * b.Y - a.Z * b.Z - a.W * b.W,
                a.X * b.Y + a.Y * b.X + a.Z * b.W - a.W * b.Z,
                a.X * b.Z - a.Y * b.W + a.Z * b.X + a.W * b.Y,
                a.X * b.W + a.Y * b.Z - a.Z * b.Y + a.W * b.X
            );
        }

        private static Double4 Normalize(Double4 q)
        {
            double norm2 = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            double norm = Hlsl.Sqrt((float)norm2);
            if (norm < 1e-15) return new Double4(1, 0, 0, 0);
            double inv = 1.0 / norm;
            return new Double4(q.X * inv, q.Y * inv, q.Z * inv, q.W * inv);
        }

        private static double NextRandom(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFF) / 16777216.0;
        }
    }
}
