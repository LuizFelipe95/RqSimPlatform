using System;
using System.Linq;
using System.Threading.Tasks;
using ComputeSharp;

namespace RQSimulation.GPUOptimized
{
    /// <summary>
    /// Gauss Law Projection for gauge constraint enforcement
    /// Ensures ∇·E = ρ (divergence of electric field equals charge density)
    /// 
    /// This is critical for maintaining gauge invariance during Yang-Mills evolution.
    /// Without projection, numerical errors accumulate and break gauge symmetry.
    /// </summary>
    public static class GaussLawProjection
    {
        /// <summary>
        /// Compute divergence of electric field at each node
        /// E field is approximated by time derivative of gauge potential
        /// </summary>
        public static double[] ComputeDivergenceOfElectricField(RQGraph graph)
        {
            int N = graph.N;
            double[] divergence = new double[N];

            // For each node, compute div(E) = sum over neighbors of E_ij
            Parallel.For(0, N, i =>
            {
                double div = 0.0;
                
                foreach (int j in graph.Neighbors(i))
                {
                    // Electric field component along edge i->j
                    // E ~ dA/dt, but we approximate with spatial gradient for steady state
                    double E_ij = graph.GetGaugeFieldComponent(i, j);
                    double E_ji = graph.GetGaugeFieldComponent(j, i);
                    
                    // Divergence contribution: outgoing - incoming
                    div += (E_ij - E_ji);
                }
                
                divergence[i] = div;
            });

            return divergence;
        }

        /// <summary>
        /// Compute charge density at each node from fermion fields
        /// ρ = sum over fermion components of |ψ|²
        /// </summary>
        public static double[] ComputeChargeDensity(RQGraph graph)
        {
            int N = graph.N;
            double[] charge = new double[N];

            for (int i = 0; i < N; i++)
            {
                charge[i] = graph.ComputeFermionDensity(i);
            }

            return charge;
        }

        /// <summary>
        /// Solve Poisson equation on graph: ∇²χ = b
        /// Uses Conjugate Gradient method for better convergence than Gauss-Seidel
        /// Returns gauge transformation function χ at each node
        /// </summary>
        public static double[] SolvePoissonOnGraph(RQGraph graph, double[] rhs)
        {
            int N = graph.N;
            double[] chi = new double[N];
            double[] residual = new double[N];
            double[] direction = new double[N];
            double[] Ap = new double[N];

            // Initialize: r = b - A*x (A is graph Laplacian, x = chi = 0 initially)
            Array.Copy(rhs, residual, N);
            Array.Copy(rhs, direction, N);

            double rsold = DotProduct(residual, residual);
            const double tolerance = 1e-8;
            const int maxIterations = 1000;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // Compute A*p (Laplacian applied to direction)
                ApplyLaplacian(graph, direction, Ap);

                double alpha = rsold / Math.Max(DotProduct(direction, Ap), 1e-15);

                // Update solution: chi = chi + alpha * p
                for (int i = 0; i < N; i++)
                {
                    chi[i] += alpha * direction[i];
                    residual[i] -= alpha * Ap[i];
                }

                double rsnew = DotProduct(residual, residual);

                // Check convergence
                if (Math.Sqrt(rsnew) < tolerance)
                {
                    //Console.WriteLine($"[Gauss Projection] CG converged in {iter} iterations");
                    break;
                }

                double beta = rsnew / rsold;

                // Update direction: p = r + beta * p
                for (int i = 0; i < N; i++)
                {
                    direction[i] = residual[i] + beta * direction[i];
                }

                rsold = rsnew;
            }

            return chi;
        }

        /// <summary>
        /// Apply graph Laplacian operator to vector
        /// (L*v)_i = d_i * v_i - sum_j w_ij * v_j
        /// </summary>
        private static void ApplyLaplacian(RQGraph graph, double[] v, double[] result)
        {
            int N = graph.N;

            Parallel.For(0, N, i =>
            {
                double sum = 0.0;
                int degree = 0;

                foreach (int j in graph.Neighbors(i))
                {
                    double w_ij = graph.Weights[i, j];
                    sum += w_ij * v[j];
                    degree++;
                }

                result[i] = degree * v[i] - sum;
            });
        }

        /// <summary>
        /// Dot product of two vectors
        /// </summary>
        private static double DotProduct(double[] a, double[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }
            return sum;
        }

        /// <summary>
        /// Project gauge field to satisfy Gauss law
        /// Corrects phases: φ'[i,j] = φ[i,j] - (χ[i] - χ[j])
        /// </summary>
        public static void ProjectGaugeField(RQGraph graph, double[] chi)
        {
            int N = graph.N;

            // Apply gauge transformation in parallel
            Parallel.For(0, N, i =>
            {
                foreach (int j in graph.Neighbors(i))
                {
                    // Gauge transformation: A'_ij = A_ij - (chi_i - chi_j)
                    graph.ApplyGaugeTransformation(i, j, chi[i] - chi[j]);
                }
            });
        }

        /// <summary>
        /// Complete Gauss law enforcement pipeline
        /// 1. Compute div(E) and ρ
        /// 2. Solve Poisson equation for χ
        /// 3. Apply gauge transformation
        /// </summary>
        public static void EnforceGaussLaw(RQGraph graph)
        {
            // Step 1: Compute divergence and charge density
            double[] divE = ComputeDivergenceOfElectricField(graph);
            double[] rho = ComputeChargeDensity(graph);

            // Step 2: Compute RHS = div(E) - rho
            double[] rhs = new double[graph.N];
            for (int i = 0; i < graph.N; i++)
            {
                rhs[i] = divE[i] - rho[i];
            }

            // Step 3: Solve Poisson equation for gauge transformation
            double[] chi = SolvePoissonOnGraph(graph, rhs);

            // Step 4: Apply gauge transformation to fix constraint
            ProjectGaugeField(graph, chi);

            // Verify constraint is satisfied
            double maxViolation = graph.ComputeGaussLawViolation();
            if (maxViolation > 1e-6)
            {
                //Console.WriteLine($"[WARNING] Gauss law violation after projection: {maxViolation:E3}");
            }
        }
    }

    /// <summary>
    /// GPU-accelerated Gauss Law solver using ComputeSharp
    /// Implements Conjugate Gradient method on GPU for Poisson equation
    /// </summary>
    public class GpuGaussLawEngine : IDisposable
    {
        private readonly GraphicsDevice _device;
        private ReadWriteBuffer<float>? _xBuffer;        // Solution vector
        private ReadWriteBuffer<float>? _rBuffer;        // Residual
        private ReadWriteBuffer<float>? _pBuffer;        // Search direction
        private ReadWriteBuffer<float>? _ApBuffer;       // A*p product
        private ReadOnlyBuffer<float>? _weightsBuffer;   // Graph weights
        private ReadOnlyBuffer<int>? _neighborOffsets;   // CSR offsets
        private ReadOnlyBuffer<int>? _neighborIndices;   // CSR indices
        private ReadWriteBuffer<float>? _scratchBuffer;  // For reduction operations

        public GpuGaussLawEngine()
        {
            _device = GraphicsDevice.GetDefault();
        }

        /// <summary>
        /// Solve Poisson equation using GPU-accelerated Conjugate Gradient
        /// </summary>
        public float[] SolvePoissonGpu(
            float[] rhs,
            float[] flatWeights,
            int[] neighborOffsets,
            int[] neighborIndices,
            int nodeCount,
            float tolerance = 1e-6f,
            int maxIterations = 1000)
        {
            int N = nodeCount;

            // Allocate GPU buffers
            _xBuffer = _device.AllocateReadWriteBuffer<float>(N);
            _rBuffer = _device.AllocateReadWriteBuffer<float>(N);
            _pBuffer = _device.AllocateReadWriteBuffer<float>(N);
            _ApBuffer = _device.AllocateReadWriteBuffer<float>(N);
            _weightsBuffer = _device.AllocateReadOnlyBuffer(flatWeights);
            _neighborOffsets = _device.AllocateReadOnlyBuffer(neighborOffsets);
            _neighborIndices = _device.AllocateReadOnlyBuffer(neighborIndices);
            _scratchBuffer = _device.AllocateReadWriteBuffer<float>(N);

            // Initialize: x = 0, r = rhs, p = rhs
            float[] zeros = new float[N];
            _xBuffer.CopyFrom(zeros);
            _rBuffer.CopyFrom(rhs);
            _pBuffer.CopyFrom(rhs);

            // Compute initial rsold = r · r
            float rsold = ComputeDotProductGpu(_rBuffer, _rBuffer, N);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // Compute Ap = Laplacian * p
                var laplacianShader = new LaplacianShader(
                    _ApBuffer, _pBuffer, _weightsBuffer,
                    _neighborOffsets, _neighborIndices, N);
                _device.For(N, laplacianShader);

                // Compute alpha = rsold / (p · Ap)
                float pAp = ComputeDotProductGpu(_pBuffer, _ApBuffer, N);
                float alpha = rsold / Math.Max(pAp, 1e-15f);

                // Update x and r: x = x + alpha*p, r = r - alpha*Ap
                var updateShader = new CGUpdateShader(_xBuffer, _rBuffer, _pBuffer, _ApBuffer, alpha);
                _device.For(N, updateShader);

                // Compute rsnew = r · r
                float rsnew = ComputeDotProductGpu(_rBuffer, _rBuffer, N);

                // Check convergence
                if (MathF.Sqrt(rsnew) < tolerance)
                {
                    //Console.WriteLine($"[GPU Gauss] CG converged in {iter} iterations");
                    break;
                }

                // Update p: p = r + beta*p
                float beta = rsnew / rsold;
                var directionShader = new CGDirectionShader(_pBuffer, _rBuffer, beta);
                _device.For(N, directionShader);

                rsold = rsnew;
            }

            // Copy result back
            float[] result = new float[N];
            _xBuffer.CopyTo(result);

            return result;
        }

        /// <summary>
        /// Compute dot product on GPU using parallel reduction
        /// </summary>
        private float ComputeDotProductGpu(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, int N)
        {
            // Element-wise multiply into scratch buffer
            var mulShader = new ElementWiseMultiplyShader(_scratchBuffer, a, b);
            _device.For(N, mulShader);

            // Sum reduction on CPU (for simplicity; could be GPU-accelerated)
            float[] scratch = new float[N];
            _scratchBuffer.CopyTo(scratch);

            float sum = 0f;
            for (int i = 0; i < N; i++)
                sum += scratch[i];

            return sum;
        }

        public void Dispose()
        {
            _xBuffer?.Dispose();
            _rBuffer?.Dispose();
            _pBuffer?.Dispose();
            _ApBuffer?.Dispose();
            _weightsBuffer?.Dispose();
            _neighborOffsets?.Dispose();
            _neighborIndices?.Dispose();
            _scratchBuffer?.Dispose();
        }
    }

    /// <summary>
    /// GPU shader for applying graph Laplacian: (L*v)_i = degree_i * v_i - sum_j w_ij * v_j
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct LaplacianShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> result;
        public readonly ReadWriteBuffer<float> v;
        public readonly ReadOnlyBuffer<float> weights;
        public readonly ReadOnlyBuffer<int> neighborOffsets;
        public readonly ReadOnlyBuffer<int> neighborIndices;
        public readonly int nodeCount;

        public LaplacianShader(
            ReadWriteBuffer<float> result,
            ReadWriteBuffer<float> v,
            ReadOnlyBuffer<float> weights,
            ReadOnlyBuffer<int> neighborOffsets,
            ReadOnlyBuffer<int> neighborIndices,
            int nodeCount)
        {
            this.result = result;
            this.v = v;
            this.weights = weights;
            this.neighborOffsets = neighborOffsets;
            this.neighborIndices = neighborIndices;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;

            int start = neighborOffsets[i];
            int end = neighborOffsets[i + 1];
            int degree = end - start;

            float sum = 0.0f;
            for (int idx = start; idx < end; idx++)
            {
                int j = neighborIndices[idx];
                float w_ij = weights[i * nodeCount + j];
                sum += w_ij * v[j];
            }

            result[i] = degree * v[i] - sum;
        }
    }

    /// <summary>
    /// GPU shader for CG update: x += alpha*p, r -= alpha*Ap
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct CGUpdateShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> x;
        public readonly ReadWriteBuffer<float> r;
        public readonly ReadWriteBuffer<float> p;
        public readonly ReadWriteBuffer<float> Ap;
        public readonly float alpha;

        public CGUpdateShader(
            ReadWriteBuffer<float> x,
            ReadWriteBuffer<float> r,
            ReadWriteBuffer<float> p,
            ReadWriteBuffer<float> Ap,
            float alpha)
        {
            this.x = x;
            this.r = r;
            this.p = p;
            this.Ap = Ap;
            this.alpha = alpha;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            x[i] += alpha * p[i];
            r[i] -= alpha * Ap[i];
        }
    }

    /// <summary>
    /// GPU shader for CG direction update: p = r + beta*p
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct CGDirectionShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> p;
        public readonly ReadWriteBuffer<float> r;
        public readonly float beta;

        public CGDirectionShader(
            ReadWriteBuffer<float> p,
            ReadWriteBuffer<float> r,
            float beta)
        {
            this.p = p;
            this.r = r;
            this.beta = beta;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            p[i] = r[i] + beta * p[i];
        }
    }

    /// <summary>
    /// GPU shader for element-wise multiplication: c = a * b
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ElementWiseMultiplyShader : IComputeShader
    {
        public readonly ReadWriteBuffer<float> c;
        public readonly ReadWriteBuffer<float> a;
        public readonly ReadWriteBuffer<float> b;

        public ElementWiseMultiplyShader(
            ReadWriteBuffer<float> c,
            ReadWriteBuffer<float> a,
            ReadWriteBuffer<float> b)
        {
            this.c = c;
            this.a = a;
            this.b = b;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            c[i] = a[i] * b[i];
        }
    }

    /// <summary>
    /// GPU shader for computing per-node Gauss law violation: (∇·E - ρ)² at each node.
    /// This allows monitoring gauge invariance without full CPU readback.
    /// 
    /// USAGE:
    /// 1. Run this shader to compute squared violations per node
    /// 2. Run GaussLawViolationReductionShader for partial sums
    /// 3. Read back single value every ~100 frames for monitoring
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct GaussLawViolationShader : IComputeShader
    {
        /// <summary>Electric field divergence per node</summary>
        public readonly ReadOnlyBuffer<double> divergenceE;

        /// <summary>Charge density per node</summary>
        public readonly ReadOnlyBuffer<double> chargeDensity;

        /// <summary>Output: squared violation (∇·E - ρ)² per node</summary>
        public readonly ReadWriteBuffer<double> squaredViolation;

        /// <summary>Number of nodes</summary>
        public readonly int nodeCount;

        public GaussLawViolationShader(
            ReadOnlyBuffer<double> divergenceE,
            ReadOnlyBuffer<double> chargeDensity,
            ReadWriteBuffer<double> squaredViolation,
            int nodeCount)
        {
            this.divergenceE = divergenceE;
            this.chargeDensity = chargeDensity;
            this.squaredViolation = squaredViolation;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;

            double divE = divergenceE[i];
            double rho = chargeDensity[i];
            double violation = divE - rho;

            squaredViolation[i] = violation * violation;
        }
    }

    /// <summary>
    /// GPU shader for computing partial sums of Gauss law violation.
    /// Uses block-level reduction to minimize CPU readback.
    /// 
    /// OUTPUT: partialSums[blockId] = sum of squaredViolation in that block
    /// CPU sums partial sums for total violation (single readback every N frames).
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct GaussLawViolationReductionShader : IComputeShader
    {
        /// <summary>Per-node squared violation values</summary>
        public readonly ReadOnlyBuffer<double> squaredViolation;

        /// <summary>Output: partial sums per thread block</summary>
        public readonly ReadWriteBuffer<double> partialSums;

        /// <summary>Number of nodes</summary>
        public readonly int nodeCount;

        /// <summary>Block size for reduction</summary>
        public readonly int blockSize;

        public GaussLawViolationReductionShader(
            ReadOnlyBuffer<double> squaredViolation,
            ReadWriteBuffer<double> partialSums,
            int nodeCount,
            int blockSize)
        {
            this.squaredViolation = squaredViolation;
            this.partialSums = partialSums;
            this.nodeCount = nodeCount;
            this.blockSize = blockSize;
        }

        public void Execute()
        {
            int idx = ThreadIds.X;
            int blockId = idx / blockSize;
            int localId = idx % blockSize;

            // Only first thread in each block computes the sum
            if (localId == 0)
            {
                int start = blockId * blockSize;
                int end = start + blockSize;
                if (end > nodeCount) end = nodeCount;

                double blockSum = 0.0;
                for (int i = start; i < end; i++)
                {
                    blockSum += squaredViolation[i];
                }

                partialSums[blockId] = blockSum;
            }
        }
    }

    /// <summary>
    /// Compute electric field divergence per node on GPU using CSR topology.
    /// E_ij ~ dA/dt approximated by spatial gradient of gauge potential.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    [RequiresDoublePrecisionSupport]
    public readonly partial struct DivergenceEShader : IComputeShader
    {
        /// <summary>CSR row offsets</summary>
        public readonly ReadOnlyBuffer<int> rowOffsets;

        /// <summary>CSR column indices</summary>
        public readonly ReadOnlyBuffer<int> colIndices;

        /// <summary>Gauge field component (E_ij approximation)</summary>
        public readonly ReadOnlyBuffer<double> gaugeField;

        /// <summary>Output: divergence at each node</summary>
        public readonly ReadWriteBuffer<double> divergence;

        /// <summary>Number of nodes</summary>
        public readonly int nodeCount;

        public DivergenceEShader(
            ReadOnlyBuffer<int> rowOffsets,
            ReadOnlyBuffer<int> colIndices,
            ReadOnlyBuffer<double> gaugeField,
            ReadWriteBuffer<double> divergence,
            int nodeCount)
        {
            this.rowOffsets = rowOffsets;
            this.colIndices = colIndices;
            this.gaugeField = gaugeField;
            this.divergence = divergence;
            this.nodeCount = nodeCount;
        }

        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= nodeCount) return;

            int start = rowOffsets[i];
            int end = rowOffsets[i + 1];

            double div = 0.0;
            for (int idx = start; idx < end; idx++)
            {
                // Outgoing field contribution (E_ij)
                double E_ij = gaugeField[idx];
                
                // For divergence, we sum outgoing - incoming
                // In CSR format with symmetric edges, we count outgoing
                div += E_ij;
            }

            divergence[i] = div;
        }
    }
}
