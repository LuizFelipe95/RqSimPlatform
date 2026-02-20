using ComputeSharp;

namespace RQSimulation.GPUOptimized.YangMills
{
    /// <summary>
    /// GPU compute shaders for Yang-Mills gauge field evolution.
    /// 
    /// SU(2) representation: Quaternion form a0 + i(a1?1 + a2?2 + a3?3)
    /// with constraint a0? + a1? + a2? + a3? = 1
    /// 
    /// Exponential map: exp(i?·n?·?) = cos(?) + i·sin(?)·(n?·?)
    /// </summary>
    
    /// <summary>
    /// SU(2) exponential update: U_new = exp(-i·dt·E) ? U_old
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Lie algebra exponential map
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct SU2ExponentialUpdateKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float4> links;     // Quaternion (a0, a1, a2, a3)
        private readonly ReadWriteBuffer<Float4> electric;  // E = (E1, E2, E3, 0) in su(2) basis
        private readonly float dt;
        
        public SU2ExponentialUpdateKernel(
            ReadWriteBuffer<Float4> links,
            ReadWriteBuffer<Float4> electric,
            float dt)
        {
            this.links = links;
            this.electric = electric;
            this.dt = dt;
        }
        
        public void Execute()
        {
            int e = ThreadIds.X;
            if (e >= links.Length) return;
            
            Float4 U = links[e];      // Current link
            Float4 E = electric[e];   // Electric field in Lie algebra
            
            // Compute exp(-i·dt·E)
            // E = E1·?1 + E2·?2 + E3·?3
            // exp(i?·n?·?) = cos(?)·I + i·sin(?)·(n?·?)
            
            float E1 = E.X;
            float E2 = E.Y;
            float E3 = E.Z;
            
            // ? = dt ? |E|
            float Enorm = Hlsl.Sqrt(E1 * E1 + E2 * E2 + E3 * E3);
            float theta = dt * Enorm;
            
            Float4 expE;
            if (Enorm < 1e-10f)
            {
                // Small angle: exp ? I
                expE = new Float4(1, 0, 0, 0);
            }
            else
            {
                // exp(-i?n?·?) = cos(?) - i·sin(?)·(n?·?)
                // In quaternion: (cos(?), -sin(?)·n?)
                float c = Hlsl.Cos(theta);
                float s = Hlsl.Sin(theta);
                float invNorm = 1.0f / Enorm;
                
                expE = new Float4(
                    c,
                    -s * E1 * invNorm,
                    -s * E2 * invNorm,
                    -s * E3 * invNorm
                );
            }
            
            // Quaternion multiplication: U_new = expE ? U
            links[e] = QuaternionMul(expE, U);
        }
        
        // Quaternion multiplication
        private static Float4 QuaternionMul(Float4 a, Float4 b)
        {
            return new Float4(
                a.X * b.X - a.Y * b.Y - a.Z * b.Z - a.W * b.W,
                a.X * b.Y + a.Y * b.X + a.Z * b.W - a.W * b.Z,
                a.X * b.Z - a.Y * b.W + a.Z * b.X + a.W * b.Y,
                a.X * b.W + a.Y * b.Z - a.Z * b.Y + a.W * b.X
            );
        }
    }
    
    /// <summary>
    /// Compute staple contribution for SU(2) Wilson action.
    /// 
    /// The staple is the sum of products around plaquettes that share edge e:
    /// Staple(e) = ?_plaq U(plaq \ e)
    /// 
    /// Electric field is derived from the Wilson action gradient:
    /// E = -?S/?U ? (Staple - Staple†)/2i
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct SU2StapleKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float4> links;
        private readonly ReadWriteBuffer<Float4> electric;
        private readonly ReadOnlyBuffer<int> edgeFrom;
        private readonly ReadOnlyBuffer<int> edgeTo;
        private readonly ReadOnlyBuffer<int> csrOffsets;
        private readonly ReadOnlyBuffer<int> csrNeighbors;
        private readonly int edgeCount;
        
        public SU2StapleKernel(
            ReadWriteBuffer<Float4> links,
            ReadWriteBuffer<Float4> electric,
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
            
            // Accumulate staple contributions from triangular plaquettes
            Float4 staple = new Float4(0, 0, 0, 0);
            int stapleCount = 0;
            
            // Find common neighbors k: i-k and k-j edges exist
            int startI = csrOffsets[i];
            int endI = csrOffsets[i + 1];
            
            for (int idxI = startI; idxI < endI; idxI++)
            {
                int k = csrNeighbors[idxI];
                if (k == j) continue;
                
                // Check if k-j edge exists
                int startK = csrOffsets[k];
                int endK = csrOffsets[k + 1];
                
                for (int idxK = startK; idxK < endK; idxK++)
                {
                    if (csrNeighbors[idxK] == j)
                    {
                        // Found triangle i-k-j
                        // Get edge indices (simplified: use neighbor indices directly)
                        Float4 U_ik = links[idxI];
                        Float4 U_kj = links[idxK];
                        
                        // Staple contribution: U(i?k) ? U(k?j)
                        Float4 contrib = QuaternionMul(U_ik, U_kj);
                        staple = AddFloat4(staple, contrib);
                        stapleCount++;
                        break;
                    }
                }
            }
            
            // Electric field from Wilson action gradient
            // E ~ Im(Staple ? U†) projected to su(2)
            if (stapleCount > 0)
            {
                Float4 U = links[e];
                Float4 Udag = QuaternionConj(U);
                
                // Staple ? U†
                Float4 SU = QuaternionMul(staple, Udag);
                
                // Anti-Hermitian projection: (SU - SU†)/2
                // For quaternion: imaginary part is already anti-Hermitian
                float scale = 1.0f / stapleCount;
                electric[e] = new Float4(0, SU.Y * scale, SU.Z * scale, SU.W * scale);
            }
            else
            {
                electric[e] = new Float4(0, 0, 0, 0);
            }
        }
        
        private static Float4 QuaternionMul(Float4 a, Float4 b)
        {
            return new Float4(
                a.X * b.X - a.Y * b.Y - a.Z * b.Z - a.W * b.W,
                a.X * b.Y + a.Y * b.X + a.Z * b.W - a.W * b.Z,
                a.X * b.Z - a.Y * b.W + a.Z * b.X + a.W * b.Y,
                a.X * b.W + a.Y * b.Z - a.Z * b.Y + a.W * b.X
            );
        }
        
        private static Float4 QuaternionConj(Float4 q)
            => new Float4(q.X, -q.Y, -q.Z, -q.W);
        
        private static Float4 AddFloat4(Float4 a, Float4 b)
            => new Float4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    }
    
    /// <summary>
    /// Metropolis update for SU(2) thermalization.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct SU2MetropolisKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float4> links;
        private readonly ReadWriteBuffer<Float4> electric;
        private readonly ReadWriteBuffer<uint> rngState;
        private readonly ReadOnlyBuffer<int> csrOffsets;
        private readonly ReadOnlyBuffer<int> csrNeighbors;
        private readonly float beta;
        private readonly int edgeCount;
        
        public SU2MetropolisKernel(
            ReadWriteBuffer<Float4> links,
            ReadWriteBuffer<Float4> electric,
            ReadWriteBuffer<uint> rngState,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrNeighbors,
            float beta,
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
            
            // Get current state
            uint state = rngState[e];
            Float4 U = links[e];
            Float4 E = electric[e];
            
            // Generate random SU(2) perturbation
            // Small random quaternion near identity
            float r1 = NextRandom(ref state) * 0.2f - 0.1f;
            float r2 = NextRandom(ref state) * 0.2f - 0.1f;
            float r3 = NextRandom(ref state) * 0.2f - 0.1f;
            float r0 = Hlsl.Sqrt(1.0f - r1 * r1 - r2 * r2 - r3 * r3);
            
            if (r0 * r0 < 0) r0 = 1; // Numerical safety
            
            Float4 delta = new Float4(r0, r1, r2, r3);
            delta = Normalize(delta);
            
            // Proposed new link
            Float4 U_new = QuaternionMul(delta, U);
            
            // Compute change in action using staple
            // ?S = -? ? Re Tr(Staple ? (U_new† - U†))
            // For SU(2), Tr = 2 ? Re(quaternion)
            
            float S_old = ComputeLocalAction(U, E);
            float S_new = ComputeLocalAction(U_new, E);
            float dS = S_new - S_old;
            
            // Metropolis acceptance
            float r = NextRandom(ref state);
            if (dS <= 0 || r < Hlsl.Exp(-beta * dS))
            {
                links[e] = U_new;
            }
            
            rngState[e] = state;
        }
        
        private static float ComputeLocalAction(Float4 U, Float4 staple)
        {
            // S = -? ? Re Tr(U ? Staple†)
            // For quaternions: Re Tr = 2 ? (U · Staple)
            return -(U.X * staple.X + U.Y * staple.Y + U.Z * staple.Z + U.W * staple.W);
        }
        
        private static Float4 QuaternionMul(Float4 a, Float4 b)
        {
            return new Float4(
                a.X * b.X - a.Y * b.Y - a.Z * b.Z - a.W * b.W,
                a.X * b.Y + a.Y * b.X + a.Z * b.W - a.W * b.Z,
                a.X * b.Z - a.Y * b.W + a.Z * b.X + a.W * b.Y,
                a.X * b.W + a.Y * b.Z - a.Z * b.Y + a.W * b.X
            );
        }
        
        private static Float4 Normalize(Float4 q)
        {
            float norm = Hlsl.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (norm < 1e-10f) return new Float4(1, 0, 0, 0);
            float inv = 1.0f / norm;
            return new Float4(q.X * inv, q.Y * inv, q.Z * inv, q.W * inv);
        }
        
        // Xorshift RNG
        private static float NextRandom(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFF) / 16777216.0f;
        }
    }
}
