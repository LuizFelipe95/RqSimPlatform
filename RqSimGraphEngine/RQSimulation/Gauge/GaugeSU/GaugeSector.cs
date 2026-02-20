using System;
using System.Numerics;

namespace RQSimulation.Gauge;

/// <summary>
/// Unified gauge field representation for different gauge groups.
/// 
/// CHECKLIST ITEM 16: Unified GaugeField class for different interactions.
/// 
/// This class encapsulates gauge link variables for:
/// - U(1): Electromagnetic (phase only)
/// - SU(2): Weak isospin (2?2 unitary)
/// - SU(3): Color (3?3 unitary)
/// 
/// Instead of separate _gluonField, _weakField, _hyperchargeField arrays,
/// we use GaugeSector instances with appropriate group sizes.
/// 
/// Physics: In lattice gauge theory, gauge fields live on edges (links)
/// as group elements U_ij ? G. The Wilson action is:
///   S = -?/N_c ?_plaquettes Re Tr(U_plaquette)
/// </summary>
public class GaugeSector : IDisposable
{
    /// <summary>Size of the gauge group (1 for U(1), 2 for SU(2), 3 for SU(3))</summary>
    public int GroupSize { get; }

    /// <summary>Name of the gauge sector (for debugging)</summary>
    public string Name { get; }

    /// <summary>Number of nodes in the graph</summary>
    public int NodeCount { get; }

    /// <summary>Coupling constant for this sector</summary>
    public double Coupling { get; set; }

    // Link matrices storage: [i * N + j] * GroupSize? gives start index
    private Complex[] _linkMatrices;

    // Field strength tensor (computed from links)
    private double[,,]? _fieldStrength;

    // Track which edges exist (for sparse graphs)
    private readonly Func<int, int, bool> _hasEdge;

    /// <summary>
    /// Create a gauge sector for a graph with N nodes.
    /// </summary>
    /// <param name="name">Sector name (e.g., "Strong", "Weak", "Hypercharge")</param>
    /// <param name="nodeCount">Number of nodes</param>
    /// <param name="groupSize">Group dimension (1, 2, or 3)</param>
    /// <param name="hasEdge">Function to check if edge (i,j) exists</param>
    /// <param name="coupling">Gauge coupling constant</param>
    public GaugeSector(
        string name,
        int nodeCount,
        int groupSize,
        Func<int, int, bool> hasEdge,
        double coupling = 1.0)
    {
        if (groupSize < 1 || groupSize > 3)
            throw new ArgumentOutOfRangeException(nameof(groupSize), "Group size must be 1, 2, or 3");

        Name = name;
        NodeCount = nodeCount;
        GroupSize = groupSize;
        Coupling = coupling;
        _hasEdge = hasEdge;

        // Allocate storage for all possible link matrices
        // For sparse graphs, most will stay as identity
        int matrixSize = groupSize * groupSize;
        _linkMatrices = new Complex[nodeCount * nodeCount * matrixSize];

        // Initialize all links to identity
        InitializeToIdentity();
    }

    /// <summary>
    /// Initialize all link matrices to identity.
    /// </summary>
    public void InitializeToIdentity()
    {
        int d = GroupSize;
        int matrixSize = d * d;

        for (int i = 0; i < NodeCount; i++)
        {
            for (int j = 0; j < NodeCount; j++)
            {
                int baseIdx = (i * NodeCount + j) * matrixSize;

                // Set to identity: ?_{rc}
                for (int r = 0; r < d; r++)
                {
                    for (int c = 0; c < d; c++)
                    {
                        _linkMatrices[baseIdx + r * d + c] = (r == c) ? Complex.One : Complex.Zero;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Initialize links with small random perturbations from identity.
    /// Used for thermalization.
    /// </summary>
    public void InitializeRandom(Random rng, double scale = 0.01)
    {
        int d = GroupSize;
        int matrixSize = d * d;

        for (int i = 0; i < NodeCount; i++)
        {
            for (int j = 0; j < NodeCount; j++)
            {
                if (!_hasEdge(i, j)) continue;

                int baseIdx = (i * NodeCount + j) * matrixSize;

                // Add small random perturbation to identity
                for (int idx = 0; idx < matrixSize; idx++)
                {
                    double re = (rng.NextDouble() - 0.5) * scale;
                    double im = (rng.NextDouble() - 0.5) * scale;
                    _linkMatrices[baseIdx + idx] += new Complex(re, im);
                }

                // Project back to group
                ProjectLinkToGroup(i, j);
            }
        }
    }

    /// <summary>
    /// Get the link matrix U_ij as flat array.
    /// </summary>
    public Complex[] GetLinkMatrix(int i, int j)
    {
        int d = GroupSize;
        int matrixSize = d * d;
        int baseIdx = (i * NodeCount + j) * matrixSize;

        var U = new Complex[matrixSize];
        Array.Copy(_linkMatrices, baseIdx, U, 0, matrixSize);
        return U;
    }

    /// <summary>
    /// Set the link matrix U_ij from flat array.
    /// </summary>
    public void SetLinkMatrix(int i, int j, Complex[] U)
    {
        int d = GroupSize;
        int matrixSize = d * d;
        int baseIdx = (i * NodeCount + j) * matrixSize;

        Array.Copy(U, 0, _linkMatrices, baseIdx, matrixSize);
    }

    /// <summary>
    /// Get single matrix element U_ij[r,c].
    /// </summary>
    public Complex GetLinkElement(int i, int j, int row, int col)
    {
        int d = GroupSize;
        int baseIdx = (i * NodeCount + j) * d * d;
        return _linkMatrices[baseIdx + row * d + col];
    }

    /// <summary>
    /// Set single matrix element U_ij[r,c].
    /// </summary>
    public void SetLinkElement(int i, int j, int row, int col, Complex value)
    {
        int d = GroupSize;
        int baseIdx = (i * NodeCount + j) * d * d;
        _linkMatrices[baseIdx + row * d + col] = value;
    }

    /// <summary>
    /// Project link matrix to SU(N) via Gram-Schmidt and phase correction.
    /// For U(1), just normalizes the phase.
    /// </summary>
    public void ProjectLinkToGroup(int i, int j)
    {
        if (GroupSize == 1)
        {
            // U(1): just normalize to unit complex number
            int baseIdx = i * NodeCount + j;
            Complex z = _linkMatrices[baseIdx];
            double mag = z.Magnitude;
            _linkMatrices[baseIdx] = mag > 1e-12 
                ? z / mag 
                : Complex.One;
            return;
        }

        var U = GetLinkMatrix(i, j);
        ProjectToSUN(U, GroupSize);
        SetLinkMatrix(i, j, U);
    }

    /// <summary>
    /// Project matrix to SU(N) using Gram-Schmidt orthonormalization
    /// followed by determinant phase correction.
    /// 
    /// CHECKLIST ITEM 1: For better stability, see PolarProjectToSU.
    /// </summary>
    private static void ProjectToSUN(Complex[] matrix, int d)
    {
        // Gram-Schmidt orthonormalization on rows
        for (int r = 0; r < d; r++)
        {
            // Subtract projections onto previous rows
            for (int k = 0; k < r; k++)
            {
                Complex dot = Complex.Zero;
                for (int c = 0; c < d; c++)
                {
                    dot += matrix[r * d + c] * Complex.Conjugate(matrix[k * d + c]);
                }
                for (int c = 0; c < d; c++)
                {
                    matrix[r * d + c] -= dot * matrix[k * d + c];
                }
            }

            // Normalize row
            double normSq = 0.0;
            for (int c = 0; c < d; c++)
            {
                var val = matrix[r * d + c];
                normSq += val.Real * val.Real + val.Imaginary * val.Imaginary;
            }

            if (normSq <= 1e-24)
            {
                // Degenerate: reset to identity row
                for (int c = 0; c < d; c++)
                {
                    matrix[r * d + c] = (r == c) ? Complex.One : Complex.Zero;
                }
                continue;
            }

            double invNorm = 1.0 / Math.Sqrt(normSq);
            for (int c = 0; c < d; c++)
            {
                matrix[r * d + c] *= invNorm;
            }
        }

        // Fix determinant phase to +1
        if (d > 1)
        {
            Complex det = ComputeDeterminant(matrix, d);
            double absDet = det.Magnitude;
            if (absDet > 1e-12)
            {
                Complex phase = det / absDet;
                // Divide last row by phase
                for (int c = 0; c < d; c++)
                {
                    matrix[(d - 1) * d + c] /= phase;
                }
            }
        }
    }

    /// <summary>
    /// Compute determinant of small matrix (d ? 3).
    /// </summary>
    private static Complex ComputeDeterminant(Complex[] m, int d)
    {
        if (d == 1) return m[0];

        if (d == 2)
        {
            return m[0] * m[3] - m[1] * m[2];
        }

        if (d == 3)
        {
            // Expansion by first row
            Complex a = m[0], b = m[1], c = m[2];
            Complex d1 = m[3], e = m[4], f = m[5];
            Complex g = m[6], h = m[7], i = m[8];
            return a * (e * i - f * h) - b * (d1 * i - f * g) + c * (d1 * h - e * g);
        }

        throw new NotSupportedException("Determinant for d > 3 not implemented");
    }

    /// <summary>
    /// Euler update for link (i,j) toward staple.
    /// U_new = U_old + ? * (staple - U_old), then project.
    /// </summary>
    public void UpdateLinkEuler(int i, int j, Complex[] staple, double epsilon)
    {
        int d = GroupSize;
        int matrixSize = d * d;

        var Uold = GetLinkMatrix(i, j);
        var Unew = new Complex[matrixSize];

        for (int idx = 0; idx < matrixSize; idx++)
        {
            Unew[idx] = Uold[idx] + epsilon * (staple[idx] - Uold[idx]);
        }

        SetLinkMatrix(i, j, Unew);
        ProjectLinkToGroup(i, j);
    }

    /// <summary>
    /// Compute staple for link (i,j) from triangular plaquettes.
    /// Staple = ?_k U_ik * U_kj for triangles i-k-j.
    /// </summary>
    public Complex[] ComputeStaple(int i, int j, Func<int, int[]> getNeighbors)
    {
        int d = GroupSize;
        int matrixSize = d * d;
        var staple = new Complex[matrixSize];

        foreach (int k in getNeighbors(i))
        {
            if (k == j || !_hasEdge(k, j)) continue;

            // Triangle plaquette i -> k -> j
            var Uik = GetLinkMatrix(i, k);
            var Ukj = GetLinkMatrix(k, j);

            // Multiply U_ik * U_kj and add to staple
            for (int r = 0; r < d; r++)
            {
                for (int c = 0; c < d; c++)
                {
                    Complex sum = Complex.Zero;
                    for (int m = 0; m < d; m++)
                    {
                        sum += Uik[r * d + m] * Ukj[m * d + c];
                    }
                    staple[r * d + c] += sum;
                }
            }
        }

        return staple;
    }

    /// <summary>
    /// Compute staple including square plaquettes (CHECKLIST ITEM 3).
    /// Includes both triangles i-k-j and squares i-k-l-j.
    /// </summary>
    public Complex[] ComputeStapleExtended(int i, int j, Func<int, int[]> getNeighbors)
    {
        int d = GroupSize;
        int matrixSize = d * d;
        var staple = new Complex[matrixSize];

        var neighborsI = getNeighbors(i);

        foreach (int k in neighborsI)
        {
            if (k == j) continue;

            // Triangle contribution: i -> k -> j
            if (_hasEdge(k, j))
            {
                var Uik = GetLinkMatrix(i, k);
                var Ukj = GetLinkMatrix(k, j);
                AddMatrixProduct(staple, Uik, Ukj, d);
            }

            // Square contribution: i -> k -> l -> j for all l
            foreach (int l in neighborsI)
            {
                if (l == j || l == k) continue;
                if (!_hasEdge(k, l) || !_hasEdge(l, j)) continue;

                // Square plaquette i -> k -> l -> j
                var Uik = GetLinkMatrix(i, k);
                var Ukl = GetLinkMatrix(k, l);
                var Ulj = GetLinkMatrix(l, j);

                // Triple product: U_ik * U_kl * U_lj
                var temp = new Complex[matrixSize];
                MultiplyMatrices(temp, Uik, Ukl, d);
                AddMatrixProduct(staple, temp, Ulj, d);
            }
        }

        return staple;
    }

    /// <summary>
    /// Multiply two matrices and store in result.
    /// </summary>
    private static void MultiplyMatrices(Complex[] result, Complex[] A, Complex[] B, int d)
    {
        for (int r = 0; r < d; r++)
        {
            for (int c = 0; c < d; c++)
            {
                Complex sum = Complex.Zero;
                for (int m = 0; m < d; m++)
                {
                    sum += A[r * d + m] * B[m * d + c];
                }
                result[r * d + c] = sum;
            }
        }
    }

    /// <summary>
    /// Add matrix product A*B to accumulator.
    /// </summary>
    private static void AddMatrixProduct(Complex[] acc, Complex[] A, Complex[] B, int d)
    {
        for (int r = 0; r < d; r++)
        {
            for (int c = 0; c < d; c++)
            {
                Complex sum = Complex.Zero;
                for (int m = 0; m < d; m++)
                {
                    sum += A[r * d + m] * B[m * d + c];
                }
                acc[r * d + c] += sum;
            }
        }
    }

    /// <summary>
    /// Compute local plaquette action for edge (i,j).
    /// S = ?_plaquettes Re Tr(1 - U_plaquette)
    /// </summary>
    public double ComputeLocalAction(int i, int j, Func<int, int[]> getNeighbors)
    {
        int d = GroupSize;
        double action = 0.0;

        foreach (int k in getNeighbors(i))
        {
            if (k == j || !_hasEdge(k, j)) continue;

            // Plaquette U = U_ik * U_kj * U_ji
            var Uik = GetLinkMatrix(i, k);
            var Ukj = GetLinkMatrix(k, j);
            var Uji = GetLinkMatrix(j, i);

            // Compute product
            var temp = new Complex[d * d];
            var prod = new Complex[d * d];
            MultiplyMatrices(temp, Uik, Ukj, d);
            MultiplyMatrices(prod, temp, Uji, d);

            // Trace
            Complex trace = Complex.Zero;
            for (int r = 0; r < d; r++)
            {
                trace += prod[r * d + r];
            }

            // Re Tr(1 - U) = d - Re Tr(U)
            action += d - trace.Real;
        }

        return action;
    }

    /// <summary>
    /// Metropolis update for a single link.
    /// </summary>
    public bool MetropolisUpdate(int i, int j, Random rng, double beta, Func<int, int[]> getNeighbors)
    {
        if (!_hasEdge(i, j)) return false;

        int d = GroupSize;
        int matrixSize = d * d;

        // Store old matrix
        var oldMatrix = GetLinkMatrix(i, j);
        double oldAction = ComputeLocalAction(i, j, getNeighbors);

        // Generate proposal: small random perturbation
        var proposal = new Complex[matrixSize];
        for (int idx = 0; idx < matrixSize; idx++)
        {
            double re = (rng.NextDouble() - 0.5) * 0.1;
            double im = (rng.NextDouble() - 0.5) * 0.1;
            proposal[idx] = oldMatrix[idx] + new Complex(re, im);
        }

        // Set proposal and project
        SetLinkMatrix(i, j, proposal);
        ProjectLinkToGroup(i, j);

        double newAction = ComputeLocalAction(i, j, getNeighbors);
        double deltaS = newAction - oldAction;

        // Accept/reject
        if (deltaS <= 0 || rng.NextDouble() < Math.Exp(-beta * deltaS))
        {
            return true; // Accepted
        }
        else
        {
            // Restore old matrix
            SetLinkMatrix(i, j, oldMatrix);
            return false; // Rejected
        }
    }

    /// <summary>
    /// Get field strength tensor (if computed).
    /// </summary>
    public double[,,]? FieldStrength => _fieldStrength;

    /// <summary>
    /// Compute field strength tensor for all edges.
    /// For Abelian (U(1)): F_ij = phase around smallest plaquette.
    /// For non-Abelian: F_ij^a ? U_plaquette - U_plaquette^† (anti-Hermitian part).
    /// </summary>
    public void ComputeFieldStrength(Func<int, int[]> getNeighbors)
    {
        int N = NodeCount;
        int numGenerators = GroupSize == 1 ? 1 : (GroupSize * GroupSize - 1);

        if (_fieldStrength == null || _fieldStrength.GetLength(0) != N)
        {
            _fieldStrength = new double[N, N, numGenerators];
        }

        for (int i = 0; i < N; i++)
        {
            foreach (int j in getNeighbors(i))
            {
                if (j <= i) continue;

                // Compute plaquette sum for this edge
                var plaquetteSum = ComputePlaquetteSum(i, j, getNeighbors);

                // Extract field strength from anti-Hermitian part
                for (int a = 0; a < numGenerators; a++)
                {
                    _fieldStrength[i, j, a] = ExtractGeneratorComponent(plaquetteSum, a);
                    _fieldStrength[j, i, a] = -_fieldStrength[i, j, a];
                }
            }
        }
    }

    /// <summary>
    /// Compute sum of plaquettes containing edge (i,j).
    /// </summary>
    private Complex[] ComputePlaquetteSum(int i, int j, Func<int, int[]> getNeighbors)
    {
        int d = GroupSize;
        int matrixSize = d * d;
        var sum = new Complex[matrixSize];

        foreach (int k in getNeighbors(i))
        {
            if (k == j || !_hasEdge(k, j)) continue;

            var Uij = GetLinkMatrix(i, j);
            var Ujk = GetLinkMatrix(j, k);
            var Uki = GetLinkMatrix(k, i);

            var temp = new Complex[matrixSize];
            var plaq = new Complex[matrixSize];
            MultiplyMatrices(temp, Uij, Ujk, d);
            MultiplyMatrices(plaq, temp, Uki, d);

            for (int idx = 0; idx < matrixSize; idx++)
            {
                sum[idx] += plaq[idx];
            }
        }

        return sum;
    }

    /// <summary>
    /// Extract component along Lie algebra generator T^a.
    /// Uses F^a = -i Tr(T^a * (U - U†)) / 2
    /// </summary>
    private double ExtractGeneratorComponent(Complex[] U, int a)
    {
        int d = GroupSize;

        if (d == 1)
        {
            // U(1): Just the phase
            return U[0].Imaginary;
        }

        // For SU(2) and SU(3): Use generator matrices
        // Simplified: just extract imaginary diagonal/off-diagonal parts
        // This is an approximation - full implementation would use explicit generators

        if (d == 2 && a < 3)
        {
            // SU(2): Pauli-like extraction
            return a switch
            {
                0 => (U[1].Real + U[2].Real) * 0.5,     // ?_x component
                1 => (U[1].Imaginary - U[2].Imaginary) * 0.5, // ?_y component
                2 => (U[0].Real - U[3].Real) * 0.5,     // ?_z component
                _ => 0.0
            };
        }

        // SU(3): More complex extraction (simplified)
        return U[a % (d * d)].Imaginary;
    }

    public void Dispose()
    {
        // Clean up managed resources
        _linkMatrices = Array.Empty<Complex>();
        _fieldStrength = null;
    }
}
