using ComputeSharp;

namespace RQSimulation.GPUOptimized.CayleyEvolution
{
    /// <summary>
    /// Double-precision GPU compute shaders for BiCGStab solver.
    /// 
    /// RQ-HYPOTHESIS PHYSICS: CRITICAL PRECISION REQUIREMENT
    /// GPU REQUIREMENT: Shader Model 6.0+ (NVIDIA Pascal, AMD GCN 4+)
    /// </summary>
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct ComputeRhsKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> psi;
        public readonly ReadWriteBuffer<Double2> rhs;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrColumns;
        public readonly ReadOnlyBuffer<double> csrValues;
        public readonly ReadOnlyBuffer<double> potential;
        public readonly double alpha;
        public readonly int nodeCount;
        public readonly int gaugeDim;
        
        public ComputeRhsKernelDouble(
            ReadWriteBuffer<Double2> psi,
            ReadWriteBuffer<Double2> rhs,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrColumns,
            ReadOnlyBuffer<double> csrValues,
            ReadOnlyBuffer<double> potential,
            double alpha,
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
            
            Double2 Hpsi = new Double2(0, 0);
            
            Hpsi.X += potential[node] * psi[idx].X;
            Hpsi.Y += potential[node] * psi[idx].Y;
            
            int rowStart = csrOffsets[node];
            int rowEnd = csrOffsets[node + 1];
            
            double degree = 0;
            for (int k = rowStart; k < rowEnd; k++)
            {
                int neighbor = csrColumns[k];
                double weight = csrValues[k];
                degree += weight;
                
                int neighborIdx = neighbor * gaugeDim + comp;
                Hpsi.X -= weight * psi[neighborIdx].X;
                Hpsi.Y -= weight * psi[neighborIdx].Y;
            }
            
            Hpsi.X += degree * psi[idx].X;
            Hpsi.Y += degree * psi[idx].Y;
            
            rhs[idx] = new Double2(
                psi[idx].X + alpha * Hpsi.Y,
                psi[idx].Y - alpha * Hpsi.X
            );
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct ComputeResidualKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> x;
        public readonly ReadWriteBuffer<Double2> b;
        public readonly ReadWriteBuffer<Double2> r;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrColumns;
        public readonly ReadOnlyBuffer<double> csrValues;
        public readonly ReadOnlyBuffer<double> potential;
        public readonly double alpha;
        public readonly int nodeCount;
        public readonly int gaugeDim;
        
        public ComputeResidualKernelDouble(
            ReadWriteBuffer<Double2> x,
            ReadWriteBuffer<Double2> b,
            ReadWriteBuffer<Double2> r,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrColumns,
            ReadOnlyBuffer<double> csrValues,
            ReadOnlyBuffer<double> potential,
            double alpha,
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
            
            Double2 Hx = new Double2(0, 0);
            
            Hx.X += potential[node] * x[idx].X;
            Hx.Y += potential[node] * x[idx].Y;
            
            int rowStart = csrOffsets[node];
            int rowEnd = csrOffsets[node + 1];
            
            double degree = 0;
            for (int k = rowStart; k < rowEnd; k++)
            {
                int neighbor = csrColumns[k];
                double weight = csrValues[k];
                degree += weight;
                
                int neighborIdx = neighbor * gaugeDim + comp;
                Hx.X -= weight * x[neighborIdx].X;
                Hx.Y -= weight * x[neighborIdx].Y;
            }
            
            Hx.X += degree * x[idx].X;
            Hx.Y += degree * x[idx].Y;
            
            Double2 Ax = new Double2(
                x[idx].X - alpha * Hx.Y,
                x[idx].Y + alpha * Hx.X
            );
            
            r[idx] = new Double2(b[idx].X - Ax.X, b[idx].Y - Ax.Y);
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct SpMVKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> x;
        public readonly ReadWriteBuffer<Double2> y;
        public readonly ReadOnlyBuffer<int> csrOffsets;
        public readonly ReadOnlyBuffer<int> csrColumns;
        public readonly ReadOnlyBuffer<double> csrValues;
        public readonly ReadOnlyBuffer<double> potential;
        public readonly double alpha;
        public readonly int nodeCount;
        public readonly int gaugeDim;
        
        public SpMVKernelDouble(
            ReadWriteBuffer<Double2> x,
            ReadWriteBuffer<Double2> y,
            ReadOnlyBuffer<int> csrOffsets,
            ReadOnlyBuffer<int> csrColumns,
            ReadOnlyBuffer<double> csrValues,
            ReadOnlyBuffer<double> potential,
            double alpha,
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
            
            Double2 Hx = new Double2(0, 0);
            
            Hx.X += potential[node] * x[idx].X;
            Hx.Y += potential[node] * x[idx].Y;
            
            int rowStart = csrOffsets[node];
            int rowEnd = csrOffsets[node + 1];
            
            double degree = 0;
            for (int k = rowStart; k < rowEnd; k++)
            {
                int neighbor = csrColumns[k];
                double weight = csrValues[k];
                degree += weight;
                
                int neighborIdx = neighbor * gaugeDim + comp;
                Hx.X -= weight * x[neighborIdx].X;
                Hx.Y -= weight * x[neighborIdx].Y;
            }
            
            Hx.X += degree * x[idx].X;
            Hx.Y += degree * x[idx].Y;
            
            y[idx] = new Double2(
                x[idx].X - alpha * Hx.Y,
                x[idx].Y + alpha * Hx.X
            );
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct CopyKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> src;
        public readonly ReadWriteBuffer<Double2> dst;
        
        public CopyKernelDouble(ReadWriteBuffer<Double2> src, ReadWriteBuffer<Double2> dst)
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
    public readonly partial struct AxpyKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> x;
        public readonly ReadWriteBuffer<Double2> y;
        public readonly ReadWriteBuffer<Double2> z;
        public readonly Double2 a;
        
        public AxpyKernelDouble(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y,
                                ReadWriteBuffer<Double2> z, Double2 a)
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
            
            Double2 ay = new Double2(
                a.X * y[idx].X - a.Y * y[idx].Y,
                a.X * y[idx].Y + a.Y * y[idx].X
            );
            
            z[idx] = new Double2(x[idx].X + ay.X, x[idx].Y + ay.Y);
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct AxpyInPlaceKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> x;
        public readonly ReadWriteBuffer<Double2> y;
        public readonly Double2 a;
        
        public AxpyInPlaceKernelDouble(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y, Double2 a)
        {
            this.x = x;
            this.y = y;
            this.a = a;
        }
        
        public void Execute()
        {
            int idx = ThreadIds.X;
            if (idx >= x.Length) return;
            
            Double2 ay = new Double2(
                a.X * y[idx].X - a.Y * y[idx].Y,
                a.X * y[idx].Y + a.Y * y[idx].X
            );
            
            x[idx] = new Double2(x[idx].X + ay.X, x[idx].Y + ay.Y);
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct UpdatePKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> r;
        public readonly ReadWriteBuffer<Double2> p;
        public readonly ReadWriteBuffer<Double2> v;
        public readonly Double2 beta;
        public readonly Double2 omega;
        
        public UpdatePKernelDouble(ReadWriteBuffer<Double2> r, ReadWriteBuffer<Double2> p,
                                    ReadWriteBuffer<Double2> v, Double2 beta, Double2 omega)
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
            
            Double2 ov = new Double2(
                omega.X * v[idx].X - omega.Y * v[idx].Y,
                omega.X * v[idx].Y + omega.Y * v[idx].X
            );
            
            Double2 pmov = new Double2(p[idx].X - ov.X, p[idx].Y - ov.Y);
            
            Double2 b_pmov = new Double2(
                beta.X * pmov.X - beta.Y * pmov.Y,
                beta.X * pmov.Y + beta.Y * pmov.X
            );
            
            p[idx] = new Double2(r[idx].X + b_pmov.X, r[idx].Y + b_pmov.Y);
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct UpdateXKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> x;
        public readonly ReadWriteBuffer<Double2> p;
        public readonly ReadWriteBuffer<Double2> s;
        public readonly Double2 alpha;
        public readonly Double2 omega;
        
        public UpdateXKernelDouble(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> p,
                                    ReadWriteBuffer<Double2> s, Double2 alpha, Double2 omega)
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
            
            Double2 ap = new Double2(
                alpha.X * p[idx].X - alpha.Y * p[idx].Y,
                alpha.X * p[idx].Y + alpha.Y * p[idx].X
            );
            
            Double2 os = new Double2(
                omega.X * s[idx].X - omega.Y * s[idx].Y,
                omega.X * s[idx].Y + omega.Y * s[idx].X
            );
            
            x[idx] = new Double2(x[idx].X + ap.X + os.X, x[idx].Y + ap.Y + os.Y);
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct SquaredNormKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> v;
        public readonly ReadWriteBuffer<double> partialSums;
        public readonly int length;
        
        public SquaredNormKernelDouble(ReadWriteBuffer<Double2> v, ReadWriteBuffer<double> partialSums, int length)
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
            
            if (localId == 0)
            {
                int start = blockId * 256;
                int end = start + 256;
                if (end > length) end = length;
                
                double blockSum = 0;
                for (int i = start; i < end; i++)
                {
                    blockSum += v[i].X * v[i].X + v[i].Y * v[i].Y;
                }
                partialSums[blockId] = blockSum;
            }
        }
    }
    
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct InnerProductKernelDouble : IComputeShader
    {
        public readonly ReadWriteBuffer<Double2> a;
        public readonly ReadWriteBuffer<Double2> b;
        public readonly ReadWriteBuffer<Double2> partialSums;
        public readonly int length;
        
        public InnerProductKernelDouble(ReadWriteBuffer<Double2> a, ReadWriteBuffer<Double2> b,
                                         ReadWriteBuffer<Double2> partialSums, int length)
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
                
                Double2 blockSum = new Double2(0, 0);
                for (int i = start; i < end; i++)
                {
                    blockSum.X += a[i].X * b[i].X + a[i].Y * b[i].Y;
                    blockSum.Y += a[i].X * b[i].Y - a[i].Y * b[i].X;
                }
                partialSums[blockId] = blockSum;
            }
        }
    }
}
