using ComputeSharp;

namespace RQSimulation.GPUCompressedSparseRow.Shaders;

/// <summary>
/// BLAS Level 1 vector operations for BiCGStab solver.
/// All operations are element-wise and fully parallelized.
/// </summary>

/// <summary>
/// Vector copy: dst = src
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorCopyKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> src;
    public readonly ReadWriteBuffer<Double2> dst;
    public readonly int length;

    public VectorCopyKernel(ReadWriteBuffer<Double2> src, ReadWriteBuffer<Double2> dst, int length)
    {
        this.src = src;
        this.dst = dst;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx < length)
            dst[idx] = src[idx];
    }
}

/// <summary>
/// Vector addition: z = x + y
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorAddKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> y;
    public readonly ReadWriteBuffer<Double2> z;
    public readonly int length;

    public VectorAddKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y, 
                           ReadWriteBuffer<Double2> z, int length)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx < length)
            z[idx] = new Double2(x[idx].X + y[idx].X, x[idx].Y + y[idx].Y);
    }
}

/// <summary>
/// Vector subtraction: z = x - y
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorSubKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> y;
    public readonly ReadWriteBuffer<Double2> z;
    public readonly int length;

    public VectorSubKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y, 
                           ReadWriteBuffer<Double2> z, int length)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx < length)
            z[idx] = new Double2(x[idx].X - y[idx].X, x[idx].Y - y[idx].Y);
    }
}

/// <summary>
/// Complex AXPY: z = x + a*y where a is complex.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorAxpyKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> y;
    public readonly ReadWriteBuffer<Double2> z;
    public readonly Double2 a;
    public readonly int length;

    public VectorAxpyKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y, 
                            ReadWriteBuffer<Double2> z, Double2 a, int length)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.a = a;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        // Complex multiply: a * y
        Double2 ay = new Double2(
            a.X * y[idx].X - a.Y * y[idx].Y,
            a.X * y[idx].Y + a.Y * y[idx].X
        );

        z[idx] = new Double2(x[idx].X + ay.X, x[idx].Y + ay.Y);
    }
}

/// <summary>
/// In-place complex AXPY: x = x + a*y where a is complex.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorAxpyInPlaceKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> y;
    public readonly Double2 a;
    public readonly int length;

    public VectorAxpyInPlaceKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> y, 
                                    Double2 a, int length)
    {
        this.x = x;
        this.y = y;
        this.a = a;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        Double2 ay = new Double2(
            a.X * y[idx].X - a.Y * y[idx].Y,
            a.X * y[idx].Y + a.Y * y[idx].X
        );

        x[idx] = new Double2(x[idx].X + ay.X, x[idx].Y + ay.Y);
    }
}

/// <summary>
/// Real scalar multiply: z = ? * x where ? is real.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorScaleKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> z;
    public readonly double alpha;
    public readonly int length;

    public VectorScaleKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> z, 
                             double alpha, int length)
    {
        this.x = x;
        this.z = z;
        this.alpha = alpha;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx < length)
            z[idx] = new Double2(alpha * x[idx].X, alpha * x[idx].Y);
    }
}

/// <summary>
/// Complex scalar multiply: z = a * x where a is complex.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct VectorComplexScaleKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> z;
    public readonly Double2 a;
    public readonly int length;

    public VectorComplexScaleKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> z, 
                                     Double2 a, int length)
    {
        this.x = x;
        this.z = z;
        this.a = a;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        z[idx] = new Double2(
            a.X * x[idx].X - a.Y * x[idx].Y,
            a.X * x[idx].Y + a.Y * x[idx].X
        );
    }
}

/// <summary>
/// BiCGStab p update: p = r + ?*(p - ?*v)
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct UpdatePKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> r;
    public readonly ReadWriteBuffer<Double2> p;
    public readonly ReadWriteBuffer<Double2> v;
    public readonly Double2 beta;
    public readonly Double2 omega;
    public readonly int length;

    public UpdatePKernel(ReadWriteBuffer<Double2> r, ReadWriteBuffer<Double2> p,
                         ReadWriteBuffer<Double2> v, Double2 beta, Double2 omega, int length)
    {
        this.r = r;
        this.p = p;
        this.v = v;
        this.beta = beta;
        this.omega = omega;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        // ? * v
        Double2 ov = new Double2(
            omega.X * v[idx].X - omega.Y * v[idx].Y,
            omega.X * v[idx].Y + omega.Y * v[idx].X
        );

        // p - ?*v
        Double2 pmov = new Double2(p[idx].X - ov.X, p[idx].Y - ov.Y);

        // ? * (p - ?*v)
        Double2 b_pmov = new Double2(
            beta.X * pmov.X - beta.Y * pmov.Y,
            beta.X * pmov.Y + beta.Y * pmov.X
        );

        // p = r + ?*(p - ?*v)
        p[idx] = new Double2(r[idx].X + b_pmov.X, r[idx].Y + b_pmov.Y);
    }
}

/// <summary>
/// BiCGStab x update: x = x + ?*p + ?*s
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct UpdateXKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> x;
    public readonly ReadWriteBuffer<Double2> p;
    public readonly ReadWriteBuffer<Double2> s;
    public readonly Double2 alpha;
    public readonly Double2 omega;
    public readonly int length;

    public UpdateXKernel(ReadWriteBuffer<Double2> x, ReadWriteBuffer<Double2> p,
                         ReadWriteBuffer<Double2> s, Double2 alpha, Double2 omega, int length)
    {
        this.x = x;
        this.p = p;
        this.s = s;
        this.alpha = alpha;
        this.omega = omega;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        // ? * p
        Double2 ap = new Double2(
            alpha.X * p[idx].X - alpha.Y * p[idx].Y,
            alpha.X * p[idx].Y + alpha.Y * p[idx].X
        );

        // ? * s
        Double2 os = new Double2(
            omega.X * s[idx].X - omega.Y * s[idx].Y,
            omega.X * s[idx].Y + omega.Y * s[idx].X
        );

        // x = x + ?*p + ?*s
        x[idx] = new Double2(x[idx].X + ap.X + os.X, x[idx].Y + ap.Y + os.Y);
    }
}

/// <summary>
/// BiCGStab r update: r = s - ?*t
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct UpdateRKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> s;
    public readonly ReadWriteBuffer<Double2> t;
    public readonly ReadWriteBuffer<Double2> r;
    public readonly Double2 omega;
    public readonly int length;

    public UpdateRKernel(ReadWriteBuffer<Double2> s, ReadWriteBuffer<Double2> t,
                         ReadWriteBuffer<Double2> r, Double2 omega, int length)
    {
        this.s = s;
        this.t = t;
        this.r = r;
        this.omega = omega;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        // ? * t
        Double2 ot = new Double2(
            omega.X * t[idx].X - omega.Y * t[idx].Y,
            omega.X * t[idx].Y + omega.Y * t[idx].X
        );

        // r = s - ?*t
        r[idx] = new Double2(s[idx].X - ot.X, s[idx].Y - ot.Y);
    }
}

/// <summary>
/// Compute s = r - ?*v for BiCGStab.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
[RequiresDoublePrecisionSupport]
public readonly partial struct ComputeSKernel : IComputeShader
{
    public readonly ReadWriteBuffer<Double2> r;
    public readonly ReadWriteBuffer<Double2> v;
    public readonly ReadWriteBuffer<Double2> s;
    public readonly Double2 alpha;
    public readonly int length;

    public ComputeSKernel(ReadWriteBuffer<Double2> r, ReadWriteBuffer<Double2> v,
                          ReadWriteBuffer<Double2> s, Double2 alpha, int length)
    {
        this.r = r;
        this.v = v;
        this.s = s;
        this.alpha = alpha;
        this.length = length;
    }

    public void Execute()
    {
        int idx = ThreadIds.X;
        if (idx >= length) return;

        // ? * v
        Double2 av = new Double2(
            alpha.X * v[idx].X - alpha.Y * v[idx].Y,
            alpha.X * v[idx].Y + alpha.Y * v[idx].X
        );

        // s = r - ?*v
        s[idx] = new Double2(r[idx].X - av.X, r[idx].Y - av.Y);
    }
}
