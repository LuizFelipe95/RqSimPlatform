using ComputeSharp;

namespace RQSimulation.GPUOptimized.CayleyEvolution
{
    /// <summary>
    /// GPU compute shaders for BiCGStab solver.
    /// 
    /// These implement the core linear algebra operations:
    /// - SpMV: Sparse matrix-vector multiply A·x where A = I + i·?·H
    /// - Vector operations: copy, axpy, inner product, norm
    /// 
    /// All operations work with Float2 representing complex numbers.
    /// </summary>
    
    /// <summary>
    /// Compute right-hand side: b = (I - i·?·H)·?
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct ComputeRhsKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> psi;
        private readonly ReadWriteBuffer<Float2> rhs;
        private readonly ReadOnlyBuffer<int> csrOffsets;
        private readonly ReadOnlyBuffer<int> csrColumns;
        private readonly ReadOnlyBuffer<float> csrValues;
        private readonly ReadOnlyBuffer<float> potential;
        private readonly float alpha;
        private readonly int nodeCount;
        private readonly int gaugeDim;
        
        public ComputeRhsKernel(
            ReadWriteBuffer<Float2> psi,
            ReadWriteBuffer<Float2> rhs,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrColumns,
            ReadOnlyBuffer<float> csrValues,
            ReadOnlyBuffer<float> potential,
            float alpha,
            int nodeCount,
            int gaugeDim)
        {
            this.psi = psi;
            this.rhs = rhs;
            this.csrOffsets = csrOffsets;
            this.csrColumns = csrColumns;
            this.csrValues = csrValues;
            this.potential = potential;
            this.alpha = alpha;
            this.nodeCount = nodeCount;
            this.gaugeDim = gaugeDim;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            int dim = nodeCount * gaugeDim;
            if (idx >= dim) return;
            
            int node = idx / gaugeDim;
            int comp = idx % gaugeDim;
            
            // Compute (H·?)_idx = diagonal + off-diagonal contributions
            Float2 Hpsi = new Float2(0, 0);
            
            // Diagonal: potential V_i
            Hpsi.X += potential[node] * psi[idx].X;
            Hpsi.Y += potential[node] * psi[idx].Y;
            
            // Off-diagonal: graph Laplacian
            int rowStart = csrOffsets[node];
            int rowEnd = csrOffsets[node + 1];
            
            float degree = 0;
            for (int k = rowStart; k < rowEnd; k++)
            {
                int neighbor = csrColumns[k];
                float weight = csrValues[k];
                degree += weight;
                
                int neighborIdx = neighbor * gaugeDim + comp;
                
                // Laplacian contribution: -w_ij * ?_j
                Hpsi.X -= weight * psi[neighborIdx].X;
                Hpsi.Y -= weight * psi[neighborIdx].Y;
            }
            
            // Diagonal of Laplacian: +degree * ?_i
            Hpsi.X += degree * psi[idx].X;
            Hpsi.Y += degree * psi[idx].Y;
            
            // b = ? - i·?·H·? = (?_re - (-?)·Hpsi_im, ?_im - ?·Hpsi_re)
            // = (?_re + ?·Hpsi_im, ?_im - ?·Hpsi_re)
            rhs[idx] = new Float2(
                psi[idx].X + alpha * Hpsi.Y,
                psi[idx].Y - alpha * Hpsi.X
            );
        }
    }
    
    /// <summary>
    /// Compute residual: r = b - A·x where A = I + i·?·H
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct ComputeResidualKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> x;
        private readonly ReadWriteBuffer<Float2> b;
        private readonly ReadWriteBuffer<Float2> r;
        private readonly ReadOnlyBuffer<int> csrOffsets;
        private readonly ReadOnlyBuffer<int> csrColumns;
        private readonly ReadOnlyBuffer<float> csrValues;
        private readonly ReadOnlyBuffer<float> potential;
        private readonly float alpha;
        private readonly int nodeCount;
        private readonly int gaugeDim;
        
        public ComputeResidualKernel(
            ReadWriteBuffer<Float2> x,
            ReadWriteBuffer<Float2> b,
            ReadWriteBuffer<Float2> r,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrColumns,
            ReadOnlyBuffer<float> csrValues,
            ReadOnlyBuffer<float> potential,
            float alpha,
            int nodeCount,
            int gaugeDim)
        {
            this.x = x;
            this.b = b;
            this.r = r;
            this.csrOffsets = csrOffsets;
            this.csrColumns = csrColumns;
            this.csrValues = csrValues;
            this.potential = potential;
            this.alpha = alpha;
            this.nodeCount = nodeCount;
            this.gaugeDim = gaugeDim;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            int dim = nodeCount * gaugeDim;
            if (idx >= dim) return;
            
            int node = idx / gaugeDim;
            int comp = idx % gaugeDim;
            
            // Compute (H·x)_idx
            Float2 Hx = new Float2(0, 0);
            
            Hx.X += potential[node] * x[idx].X;
            Hx.Y += potential[node] * x[idx].Y;
            
            int rowStart = csrOffsets[node];
            int rowEnd = csrOffsets[node + 1];
            
            float degree = 0;
            for (int k = rowStart; k < rowEnd; k++)
            {
                int neighbor = csrColumns[k];
                float weight = csrValues[k];
                degree += weight;
                
                int neighborIdx = neighbor * gaugeDim + comp;
                Hx.X -= weight * x[neighborIdx].X;
                Hx.Y -= weight * x[neighborIdx].Y;
            }
            
            Hx.X += degree * x[idx].X;
            Hx.Y += degree * x[idx].Y;
            
            // A·x = (I + i·?·H)·x = x + i·?·H·x
            Float2 Ax = new Float2(
                x[idx].X - alpha * Hx.Y,
                x[idx].Y + alpha * Hx.X
            );
            
            // r = b - A·x
            r[idx] = new Float2(b[idx].X - Ax.X, b[idx].Y - Ax.Y);
        }
    }
    
    /// <summary>
    /// Sparse matrix-vector multiply: y = A·x where A = I + i·?·H
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct SpMVKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> x;
        private readonly ReadWriteBuffer<Float2> y;
        private readonly ReadOnlyBuffer<int> csrOffsets;
        private readonly ReadOnlyBuffer<int> csrColumns;
        private readonly ReadOnlyBuffer<float> csrValues;
        private readonly ReadOnlyBuffer<float> potential;
        private readonly float alpha;
        private readonly int nodeCount;
        private readonly int gaugeDim;
        
        public SpMVKernel(
            ReadWriteBuffer<Float2> x,
            ReadWriteBuffer<Float2> y,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrColumns,
            ReadOnlyBuffer<float> csrValues,
            ReadOnlyBuffer<float> potential,
            float alpha,
            int nodeCount,
            int gaugeDim)
        {
            this.x = x;
            this.y = y;
            this.csrOffsets = csrOffsets;
            this.csrColumns = csrColumns;
            this.csrValues = csrValues;
            this.potential = potential;
            this.alpha = alpha;
            this.nodeCount = nodeCount;
            this.gaugeDim = gaugeDim;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            int dim = nodeCount * gaugeDim;
            if (idx >= dim) return;
            
            int node = idx / gaugeDim;
            int comp = idx % gaugeDim;
            
            Float2 Hx = new Float2(0, 0);
            
            Hx.X += potential[node] * x[idx].X;
            Hx.Y += potential[node] * x[idx].Y;
            
            int rowStart = csrOffsets[node];
            int rowEnd = csrOffsets[node + 1];
            
            float degree = 0;
            for (int k = rowStart; k < rowEnd; k++)
            {
                int neighbor = csrColumns[k];
                float weight = csrValues[k];
                degree += weight;
                
                int neighborIdx = neighbor * gaugeDim + comp;
                Hx.X -= weight * x[neighborIdx].X;
                Hx.Y -= weight * x[neighborIdx].Y;
            }
            
            Hx.X += degree * x[idx].X;
            Hx.Y += degree * x[idx].Y;
            
            // y = (I + i·?·H)·x
            y[idx] = new Float2(
                x[idx].X - alpha * Hx.Y,
                x[idx].Y + alpha * Hx.X
            );
        }
    }
    
    /// <summary>
    /// Vector copy: dst = src
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct CopyKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> src;
        private readonly ReadWriteBuffer<Float2> dst;
        
        public CopyKernel(ReadWriteBuffer<Float2> src, ReadWriteBuffer<Float2> dst)
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
    /// AXPY: z = x + a·y
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct AxpyKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> x;
        private readonly ReadWriteBuffer<Float2> y;
        private readonly ReadWriteBuffer<Float2> z;
        private readonly Float2 a;
        
        public AxpyKernel(ReadWriteBuffer<Float2> x, ReadWriteBuffer<Float2> y, 
                          ReadWriteBuffer<Float2> z, Float2 a)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.a = a;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= z.Length) return;
            
            // Complex multiply: a·y
            Float2 ay = new Float2(
                a.X * y[idx].X - a.Y * y[idx].Y,
                a.X * y[idx].Y + a.Y * y[idx].X
            );
            
            z[idx] = new Float2(x[idx].X + ay.X, x[idx].Y + ay.Y);
        }
    }
    
    /// <summary>
    /// In-place AXPY: x = x + a·y
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct AxpyInPlaceKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> x;
        private readonly ReadWriteBuffer<Float2> y;
        private readonly Float2 a;
        
        public AxpyInPlaceKernel(ReadWriteBuffer<Float2> x, ReadWriteBuffer<Float2> y, Float2 a)
        {
            this.x = x;
            this.y = y;
            this.a = a;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= x.Length) return;
            
            Float2 ay = new Float2(
                a.X * y[idx].X - a.Y * y[idx].Y,
                a.X * y[idx].Y + a.Y * y[idx].X
            );
            
            x[idx] = new Float2(x[idx].X + ay.X, x[idx].Y + ay.Y);
        }
    }
    
    /// <summary>
    /// Update p in BiCGStab: p = r + beta·(p - omega·v)
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct UpdatePKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> r;
        private readonly ReadWriteBuffer<Float2> p;
        private readonly ReadWriteBuffer<Float2> v;
        private readonly Float2 beta;
        private readonly Float2 omega;
        
        public UpdatePKernel(ReadWriteBuffer<Float2> r, ReadWriteBuffer<Float2> p, 
                             ReadWriteBuffer<Float2> v, Float2 beta, Float2 omega)
        {
            this.r = r;
            this.p = p;
            this.v = v;
            this.beta = beta;
            this.omega = omega;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= p.Length) return;
            
            // omega·v
            Float2 ov = new Float2(
                omega.X * v[idx].X - omega.Y * v[idx].Y,
                omega.X * v[idx].Y + omega.Y * v[idx].X
            );
            
            // p - omega·v
            Float2 pmov = new Float2(p[idx].X - ov.X, p[idx].Y - ov.Y);
            
            // beta·(p - omega·v)
            Float2 b_pmov = new Float2(
                beta.X * pmov.X - beta.Y * pmov.Y,
                beta.X * pmov.Y + beta.Y * pmov.X
            );
            
            // p = r + beta·(p - omega·v)
            p[idx] = new Float2(r[idx].X + b_pmov.X, r[idx].Y + b_pmov.Y);
        }
    }
    
    /// <summary>
    /// Update x in BiCGStab: x = x + alpha·p + omega·s
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct UpdateXKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> x;
        private readonly ReadWriteBuffer<Float2> p;
        private readonly ReadWriteBuffer<Float2> s;
        private readonly Float2 alpha;
        private readonly Float2 omega;
        
        public UpdateXKernel(ReadWriteBuffer<Float2> x, ReadWriteBuffer<Float2> p,
                             ReadWriteBuffer<Float2> s, Float2 alpha, Float2 omega)
        {
            this.x = x;
            this.p = p;
            this.s = s;
            this.alpha = alpha;
            this.omega = omega;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= x.Length) return;
            
            // alpha·p
            Float2 ap = new Float2(
                alpha.X * p[idx].X - alpha.Y * p[idx].Y,
                alpha.X * p[idx].Y + alpha.Y * p[idx].X
            );
            
            // omega·s
            Float2 os = new Float2(
                omega.X * s[idx].X - omega.Y * s[idx].Y,
                omega.X * s[idx].Y + omega.Y * s[idx].X
            );
            
            x[idx] = new Float2(x[idx].X + ap.X + os.X, x[idx].Y + ap.Y + os.Y);
        }
    }
    
    /// <summary>
    /// Parallel reduction for squared norm: ||v||^2
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct SquaredNormKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> v;
        private readonly ReadWriteBuffer<float> partialSums;
        private readonly int length;
        
        public SquaredNormKernel(ReadWriteBuffer<Float2> v, ReadWriteBuffer<float> partialSums, int length)
        {
            this.v = v;
            this.partialSums = partialSums;
            this.length = length;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            int blockId = idx / 256;
            int localId = idx % 256;
            
            float localSum = 0;
            int start = blockId * 256;
            int end = start + 256;
            if (end > length) end = length;
            
            for (int i = start + localId; i < end; i += 256)
            {
                localSum += v[i].X * v[i].X + v[i].Y * v[i].Y;
            }
            
            // Simplified: just write per-element (actual reduction on CPU)
            if (localId == 0)
            {
                float blockSum = 0;
                for (int i = start; i < end; i++)
                {
                    blockSum += v[i].X * v[i].X + v[i].Y * v[i].Y;
                }
                partialSums[blockId] = blockSum;
            }
        }
    }
    
    /// <summary>
    /// Parallel reduction for complex inner product: <a, b> = ? conj(a_i)·b_i
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal readonly partial struct InnerProductKernel : IComputeShader
    {
        private readonly ReadWriteBuffer<Float2> a;
        private readonly ReadWriteBuffer<Float2> b;
        private readonly ReadWriteBuffer<Float2> partialSums;
        private readonly int length;
        
        public InnerProductKernel(ReadWriteBuffer<Float2> a, ReadWriteBuffer<Float2> b,
                                   ReadWriteBuffer<Float2> partialSums, int length)
        {
            this.a = a;
            this.b = b;
            this.partialSums = partialSums;
            this.length = length;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            int blockId = idx / 256;
            int localId = idx % 256;
            
            if (localId == 0)
            {
                int start = blockId * 256;
                int end = start + 256;
                if (end > length) end = length;
                
                Float2 blockSum = new Float2(0, 0);
                for (int i = start; i < end; i++)
                {
                    // conj(a)·b = (a_re - i·a_im)(b_re + i·b_im)
                    //           = (a_re·b_re + a_im·b_im) + i·(a_re·b_im - a_im·b_re)
                    blockSum.X += a[i].X * b[i].X + a[i].Y * b[i].Y;
                    blockSum.Y += a[i].X * b[i].Y - a[i].Y * b[i].X;
                }
                partialSums[blockId] = blockSum;
            }
        }
    }
}
