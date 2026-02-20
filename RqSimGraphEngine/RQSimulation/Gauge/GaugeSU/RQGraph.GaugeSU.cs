using System;
using System.Numerics;
using RQSimulation.Physics;
using RQSimulation.Gauge;

namespace RQSimulation
{
    /// <summary>
    /// SU(2)/SU(3) gauge field support with proper matrix representation.
    /// 
    /// CHECKLIST ITEMS IMPLEMENTED:
    /// - Item 1: Full SU(3) matrix storage via SU3Matrix struct with Gell-Mann generators
    /// - Item 1: PolarProjectToSU for stable SU(N) projection (replaces Gram-Schmidt)
    /// - Item 2: Explicit SU(2) structure constants from PauliMatrices class
    /// - Item 3: Square plaquette contributions in staple calculation
    /// - Item 4: Lie algebra exponential update for large epsilon
    /// </summary>
    public partial class RQGraph
    {
        private Complex[,,] _gaugeSU; // [i,j, element] flattened matrix (dim*dim)
        
        /// <summary>
        /// Full SU(3) matrix storage for strong gauge field.
        /// CHECKLIST ITEM 1: Replaces real-valued _gluonField with proper SU(3) matrices.
        /// Each edge (i,j) carries a unitary 3×3 matrix U_ij with det=1.
        /// </summary>
        private SU3Matrix[,]? _gaugeSU3;
        
        /// <summary>
        /// Full SU(2) matrix storage for weak gauge field.
        /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Proper SU(2) link variables.
        /// Each edge (i,j) carries a unitary 2×2 matrix W_ij with det=1.
        /// </summary>
        private SU2Matrix[,]? _gaugeSU2;
        
        public int GaugeDimension { get; private set; } = 3;

        /// <summary>
        /// Configure gauge dimension and initialize matrices.
        /// 
        /// CHECKLIST ITEM 15: Link GaugeDimension to QuantumComponents.
        /// When gauge dimension changes, QuantumComponents is synchronized
        /// to ensure consistency between gauge and matter sectors.
        /// 
        /// CHECKLIST ITEM 1: For SU(3), uses proper SU3Matrix struct with
        /// Gell-Mann generators instead of flat arrays.
        /// </summary>
        public void ConfigureGaugeDimension(int dim)
        {
            if (dim != 1 && dim != 2 && dim != 3) throw new ArgumentOutOfRangeException(nameof(dim));
            GaugeDimension = dim;
            _gaugeSU = new Complex[N, N, dim * dim];
            
            // CHECKLIST ITEM 15: Synchronize QuantumComponents with gauge dimension
            // This ensures the multi-component wavefunction matches the color DOF
            QuantumComponents = dim;
            _waveMulti = new Complex[N * dim];
            
            // CHECKLIST ITEM 1: Initialize SU(3) matrices if dimension is 3
            if (dim == 3)
            {
                _gaugeSU3 = new SU3Matrix[N, N];
                for (int i = 0; i < N; i++)
                {
                    foreach (int j in Neighbors(i))
                    {
                        _gaugeSU3[i, j] = SU3Matrix.Identity;
                    }
                }
            }
            
            // RQ-HYPOTHESIS CHECKLIST ITEM 3: Initialize SU(2) matrices for weak field
            // Always initialize for electroweak, regardless of gauge dimension
            _gaugeSU2 = new SU2Matrix[N, N];
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    _gaugeSU2[i, j] = SU2Matrix.Identity;
                }
            }
            
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                    InitialiseIdentity(i, j);
        }

        private void InitialiseIdentity(int i, int j)
        {
            int d = GaugeDimension;
            for (int r = 0; r < d; r++)
                for (int c = 0; c < d; c++)
                    _gaugeSU[i, j, r * d + c] = (r == c) ? Complex.One : Complex.Zero;
        }

        private Complex[] MultiplyMatrixVector(int i, int j, Complex[] vec)
        {
            int d = GaugeDimension;
            var res = new Complex[d];
            for (int r = 0; r < d; r++)
            {
                Complex acc = Complex.Zero;
                for (int c = 0; c < d; c++)
                {
                    acc += _gaugeSU[i, j, r * d + c] * vec[c];
                }
                res[r] = acc;
            }
            return res;
        }

        /// <summary>
        /// Project matrix to SU(N) using stable polar decomposition.
        /// 
        /// CHECKLIST ITEM 1: Replaces Gram-Schmidt with polar decomposition
        /// which is numerically more stable for large perturbations.
        /// 
        /// Falls back to Gram-Schmidt only if polar decomposition fails.
        /// </summary>
        private void ProjectToSUN(Complex[] newMatrix)
        {
            int d = GaugeDimension;
            
            // Try polar decomposition first (more stable)
            try
            {
                var projected = GaugeFieldUpdater.PolarProjectToSU(newMatrix, d);
                Array.Copy(projected, newMatrix, d * d);
                return;
            }
            catch
            {
                // Fall back to Gram-Schmidt if polar decomposition fails
            }
            
            // Fallback: Gram-Schmidt on rows
            for (int r = 0; r < d; r++)
            {
                // subtract projections onto previous orthonormal rows
                for (int k = 0; k < r; k++)
                {
                    Complex dot = Complex.Zero;
                    for (int c = 0; c < d; c++) dot += newMatrix[r * d + c] * Complex.Conjugate(newMatrix[k * d + c]);
                    for (int c = 0; c < d; c++) newMatrix[r * d + c] -= dot * newMatrix[k * d + c];
                }
                // normalise row
                double normSq = 0.0;
                for (int c = 0; c < d; c++)
                {
                    var val = newMatrix[r * d + c];
                    normSq += val.Magnitude * val.Magnitude;
                }
                if (normSq <= 0) // fallback to identity row if degenerate
                {
                    for (int c = 0; c < d; c++) newMatrix[r * d + c] = (r == c) ? Complex.One : Complex.Zero;
                    continue;
                }
                double inv = 1.0 / Math.Sqrt(normSq);
                for (int c = 0; c < d; c++) newMatrix[r * d + c] *= inv;
            }
            // adjust determinant phase to +1 (only meaningful for d>1)
            if (d > 1)
            {
                Complex det = DeterminantFlat(newMatrix, d);
                double absDet = det.Magnitude;
                if (absDet > 0)
                {
                    // multiply last row by phase inverse so that det becomes real+ positive
                    Complex phase = det / absDet; // unit phase of determinant
                    for (int c = 0; c < d; c++) newMatrix[(d - 1) * d + c] /= phase;
                }
            }
        }

        // Compute determinant for small d (d=2 or 3) from flat array
        private Complex DeterminantFlat(Complex[] m, int d)
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
            // Fallback slow Laplace (not expected for larger)
            throw new NotSupportedException("Determinant for dimension >3 not supported");
        }

        /// <summary>
        /// Wilson-like staple update with projection onto SU(N).
        /// 
        /// CHECKLIST ITEMS:
        /// - Item 3: Extended to include square plaquettes (4-node loops) in addition to triangles
        /// - Item 4: Uses Lie algebra exponential for large epsilon (stable for big updates)
        /// - Item 1: Uses polar decomposition projection (more stable than Gram-Schmidt)
        /// </summary>
        /// <param name="epsilon">Step size. For epsilon >= 0.1, uses exponential map.</param>
        public void UpdateNonAbelianGauge(double epsilon = 0.01)
        {
            if (GaugeDimension == 1 || _gaugeSU == null) return;
            int d = GaugeDimension;
            var updated = new Complex[N, N, d * d];
            
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    // CHECKLIST ITEM 3: Compute staple with both triangle and square plaquettes
                    var staple = ComputeExtendedStaple(i, j, d);
                    
                    // Get current link
                    var Uold = new Complex[d * d];
                    for (int idx = 0; idx < d * d; idx++)
                        Uold[idx] = _gaugeSU[i, j, idx];
                    
                    Complex[] newMat;
                    
                    // CHECKLIST ITEM 4: Use Lie algebra exponential for large epsilon
                    if (epsilon >= 0.1)
                    {
                        // Large step: use exponential update
                        newMat = GaugeFieldUpdater.UpdateLinkExponential(Uold, staple, epsilon, d);
                    }
                    else
                    {
                        // Small step: Euler update (faster)
                        newMat = new Complex[d * d];
                        for (int idx = 0; idx < d * d; idx++)
                        {
                            var force = staple[idx] - Uold[idx];
                            newMat[idx] = Uold[idx] + epsilon * force;
                        }
                        // Project to SU(N)
                        ProjectToSUN(newMat);
                    }
                    
                    for (int idx = 0; idx < d * d; idx++) 
                        updated[i, j, idx] = newMat[idx];
                }
            }
            _gaugeSU = updated; // replace
        }
        
        /// <summary>
        /// Compute extended staple including both triangle and square plaquettes.
        /// 
        /// CHECKLIST ITEM 3: Only triangular loops (3 nodes) were used before.
        /// This adds search for square plaquettes (4 nodes) i-k-l-j.
        /// </summary>
        private Complex[] ComputeExtendedStaple(int i, int j, int d)
        {
            var staple = new Complex[d * d];
            
            // Triangle contributions: i -> k -> j for each k
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                if (!Edges[k, j]) continue;
                
                // Add U(i->k) * U(k->j)
                for (int r = 0; r < d; r++)
                {
                    for (int c = 0; c < d; c++)
                    {
                        Complex sum = Complex.Zero;
                        for (int m = 0; m < d; m++)
                        {
                            sum += _gaugeSU[i, k, r * d + m] * _gaugeSU[k, j, m * d + c];
                        }
                        staple[r * d + c] += sum;
                    }
                }
            }
            
            // Square plaquette contributions: i -> k -> l -> j for each (k, l) pair
            // CHECKLIST ITEM 3: Search for 4-node minimal cycles
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                
                foreach (int l in Neighbors(i))
                {
                    if (l == j || l == k) continue;
                    if (!Edges[k, l] || !Edges[l, j]) continue;
                    
                    // Square plaquette i -> k -> l -> j
                    // Compute U(i->k) * U(k->l) * U(l->j)
                    for (int r = 0; r < d; r++)
                    {
                        for (int c = 0; c < d; c++)
                        {
                            Complex sum = Complex.Zero;
                            for (int m = 0; m < d; m++)
                            {
                                for (int n = 0; n < d; n++)
                                {
                                    sum += _gaugeSU[i, k, r * d + m] * 
                                           _gaugeSU[k, l, m * d + n] * 
                                           _gaugeSU[l, j, n * d + c];
                                }
                            }
                            // Weight square contributions less than triangles (longer loops = weaker)
                            staple[r * d + c] += sum * 0.5;
                        }
                    }
                }
            }
            
            return staple;
        }

        /// <summary>
        /// Compute local plaquette action contribution for link (i->j): sum over minimal triangles i-k-j.
        /// ReTr(1 - U_plaquette). Simplified: uses available neighbours to form small loops.
        /// </summary>
        private double LocalPlaquetteAction(int i, int j, int d)
        {
            double s = 0.0;
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                if (!Edges[k, j]) continue;
                // plaquette U = U(i,k) * U(k,j) * U(j,i)
                var Uik = GetMatrixFlat(i, k, d);
                var Ukj = GetMatrixFlat(k, j, d);
                var Uji = GetMatrixFlat(j, i, d);
                var prod = MultiplyFlat(Uik, Ukj, d);
                prod = MultiplyFlat(prod, Uji, d);
                // ReTr(1 - U)
                Complex trace = Complex.Zero;
                for (int r = 0; r < d; r++) trace += prod[r * d + r];
                double re = 1.0 * d - trace.Real; // Tr(1) = d
                s += re;
            }
            return s;
        }

        private double WilsonPlaquetteAction(int i, int j)
        {
            int d = GaugeDimension;
            double s = 0.0;

            foreach (int k in Neighbors(i))
            {
                if (k == j || !Edges[k, j]) continue;

                var Uik = GetMatrixFlat(i, k, d);
                var Ukj = GetMatrixFlat(k, j, d);
                var Uji = GetMatrixFlat(j, i, d);

                var prod = MultiplyFlat(Uik, Ukj, d);
                prod = MultiplyFlat(prod, Uji, d);

                System.Numerics.Complex trace = System.Numerics.Complex.Zero;
                for (int r = 0; r < d; r++)
                    trace += prod[r * d + r];

                s += (d - trace.Real); // Re Tr(1 - U)
            }

            return s;
        }

        private Complex[] GetMatrixFlat(int i, int j, int d)
        {
            var m = new Complex[d * d];
            for (int idx = 0; idx < d * d; idx++) m[idx] = _gaugeSU[i, j, idx];
            return m;
        }
        private Complex[] MultiplyFlat(Complex[] A, Complex[] B, int d)
        {
            var C = new Complex[d * d];
            for (int r = 0; r < d; r++)
            {
                for (int c = 0; c < d; c++)
                {
                    Complex acc = Complex.Zero;
                    for (int m = 0; m < d; m++) acc += A[r * d + m] * B[m * d + c];
                    C[r * d + c] = acc;
                }
            }
            return C;
        }
        private Complex RandomSmallComplex(double scale = 0.05)
        {
            double a = (_rng.NextDouble() * 2.0 - 1.0) * scale;
            double b = (_rng.NextDouble() * 2.0 - 1.0) * scale;
            return new Complex(a, b);
        }

        /// <summary>
        /// Metropolis update for SU(3) links using local change in plaquette action.
        /// </summary>
        public void UpdateStrongGaugeMetropolis(double beta)
        {
            int d = 3;
            if (GaugeDimension != d || _gaugeSU == null) return;
            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    double sOld = LocalPlaquetteAction(i, j, d);
                    var trial = new Complex[d * d];
                    for (int idx = 0; idx < d * d; idx++) trial[idx] = _gaugeSU[i, j, idx] + RandomSmallComplex();
                    ProjectToSUN(trial);
                    var backup = new Complex[d * d];
                    for (int idx = 0; idx < d * d; idx++) backup[idx] = _gaugeSU[i, j, idx];
                    for (int idx = 0; idx < d * d; idx++) _gaugeSU[i, j, idx] = trial[idx];
                    double sNew = LocalPlaquetteAction(i, j, d);
                    double dS = sNew - sOld;
                    if (dS > 0 && _rng.NextDouble() > Math.Exp(-beta * dS))
                    {
                        for (int idx = 0; idx < d * d; idx++) _gaugeSU[i, j, idx] = backup[idx];
                    }
                }
            }
        }

        /// <summary>
        /// Couple the non-Abelian gauge field to the multi-component matter wavefunction.
        /// The update nudges each link matrix in the direction of the local colour current
        /// psi_i ⊗ psi_j* and then projects back to SU(N). This implements a minimal coupling
        /// between matter and gauge sectors without introducing external tuning parameters.
        /// </summary>
        public void UpdateGaugeFromMatter(double kappa = 0.01)
        {
            if (GaugeDimension <= 1 || _gaugeSU == null) return;
            if (_waveMulti == null || _waveMulti.Length == 0) return;

            int d = GaugeDimension;
            int comps = QuantumComponents > 0 ? QuantumComponents : d;
            int active = System.Math.Min(d, comps);
            var updated = new System.Numerics.Complex[N, N, d * d];

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    var newMat = new System.Numerics.Complex[d * d];
                    for (int r = 0; r < d; r++)
                    {
                        for (int c = 0; c < d; c++)
                        {
                            var Uold = _gaugeSU[i, j, r * d + c];
                            System.Numerics.Complex desired = System.Numerics.Complex.Zero;
                            if (r < active && c < active)
                            {
                                var psiI = _waveMulti[i * d + r];
                                var psiJ = _waveMulti[j * d + c];
                                desired = psiI * System.Numerics.Complex.Conjugate(psiJ);
                            }
                            newMat[r * d + c] = Uold + kappa * (desired - Uold);
                        }
                    }
                    ProjectToSUN(newMat);
                    for (int idx = 0; idx < d * d; idx++)
                        updated[i, j, idx] = newMat[idx];
                }
            }

            _gaugeSU = updated;
        }


        /// <summary>
        /// Transport multi-component wavefunction covariantly for node i from neighbor j.
        /// </summary>
        private Complex[] Transport(int i, int j)
        {
            if (GaugeDimension == 1) return null; // caller handles phase path
            int d = GaugeDimension;
            var vec = new Complex[d];
            for (int c = 0; c < d; c++) vec[c] = _waveMulti[j * d + c];
            return MultiplyMatrixVector(i, j, vec);
        }

        /// <summary>
        /// Get SU(3) link matrix for edge (i,j).
        /// 
        /// CHECKLIST ITEM 1: Provides access to the full SU(3) gauge matrix
        /// for use in parallel transport and Dirac equation.
        /// </summary>
        /// <param name="i">Source node</param>
        /// <param name="j">Target node</param>
        /// <returns>SU(3) link matrix U_ij</returns>
        public SU3Matrix GetSU3Link(int i, int j)
        {
            if (_gaugeSU3 != null && GaugeDimension == 3)
            {
                return _gaugeSU3[i, j];
            }
            
            // Fallback: construct from flat array
            if (_gaugeSU != null && GaugeDimension == 3)
            {
                var elements = new Complex[9];
                for (int idx = 0; idx < 9; idx++)
                    elements[idx] = _gaugeSU[i, j, idx];
                return new SU3Matrix(elements);
            }
            
            return SU3Matrix.Identity;
        }
        
        /// <summary>
        /// Set SU(3) link matrix for edge (i,j).
        /// 
        /// CHECKLIST ITEM 1: Updates both the SU3Matrix storage and the flat array
        /// for backward compatibility.
        /// </summary>
        public void SetSU3Link(int i, int j, SU3Matrix U)
        {
            if (_gaugeSU3 != null && GaugeDimension == 3)
            {
                _gaugeSU3[i, j] = U;
            }
            
            // Also update flat array for compatibility
            if (_gaugeSU != null && GaugeDimension == 3)
            {
                var flat = U.ToFlatArray();
                for (int idx = 0; idx < 9; idx++)
                    _gaugeSU[i, j, idx] = flat[idx];
            }
        }
        
        /// <summary>
        /// Apply SU(3) parallel transport to a color triplet vector.
        /// 
        /// CHECKLIST ITEM 2: Gauge-covariant transport for fermion fields.
        /// Given a color vector ψ_j at node j, returns U†_ij ψ_j transported to node i.
        /// 
        /// The adjoint U† is used because we're transporting from j to i,
        /// and U_ij transforms vectors at i to vectors at j.
        /// </summary>
        /// <param name="i">Target node</param>
        /// <param name="j">Source node (where the vector lives)</param>
        /// <param name="colorVector">3-component color vector at node j</param>
        /// <returns>Transported color vector at node i</returns>
        public Complex[] ParallelTransportSU3(int i, int j, Complex[] colorVector)
        {
            if (colorVector.Length != 3)
                throw new ArgumentException("Color vector must have 3 components", nameof(colorVector));
                
            if (GaugeDimension != 3)
            {
                // No gauge transformation for non-SU(3)
                return (Complex[])colorVector.Clone();
            }
            
            // Get U†_ij = (U_ij)^†
            SU3Matrix U = GetSU3Link(i, j);
            SU3Matrix Udag = U.Dagger();
            
            // Apply: ψ_transported = U† * ψ
            return Udag.Apply(colorVector);
        }
        
        /// <summary>
        /// Update SU(3) link using Wilson action with staple contribution.
        /// 
        /// CHECKLIST ITEM 1: Uses proper SU(3) matrix operations.
        /// CHECKLIST ITEM 4: Uses Lie algebra exponential for large steps.
        /// </summary>
        public void UpdateSU3Link(int i, int j, double epsilon = 0.01, Random? rng = null)
        {
            if (GaugeDimension != 3 || _gaugeSU3 == null)
                return;
                
            SU3Matrix U = _gaugeSU3[i, j];
            
            // Compute staple (sum of plaquette contributions)
            SU3Matrix staple = ComputeSU3Staple(i, j);
            
            // For small epsilon: U_new = U + epsilon * (staple - U) projected to SU(3)
            // For large epsilon: Use exponential map
            if (epsilon >= 0.1)
            {
                // Lie algebra update: U_new = exp(epsilon * X) * U
                // where X = (staple * U† - h.c.) / 2 (anti-Hermitian force)
                SU3Matrix SUdag = staple.Multiply(U.Dagger());
                
                // Extract anti-Hermitian part and convert to Lie algebra coefficients
                // This is a simplified version - for full implementation, decompose into Gell-Mann basis
                double[] theta = new double[8];
                for (int a = 0; a < 8; a++)
                {
                    theta[a] = epsilon * ExtractGellMannCoefficient(SUdag, a);
                }
                
                SU3Matrix expX = SU3Matrix.Exp(theta);
                _gaugeSU3[i, j] = expX.Multiply(U).ProjectToSU3();
            }
            else
            {
                // Euler step with projection
                var Unew = new Complex[9];
                var Uflat = U.ToFlatArray();
                var Sflat = staple.ToFlatArray();
                
                for (int idx = 0; idx < 9; idx++)
                {
                    Unew[idx] = Uflat[idx] + epsilon * (Sflat[idx] - Uflat[idx]);
                }
                
                _gaugeSU3[i, j] = new SU3Matrix(Unew).ProjectToSU3();
            }
            
            // Sync with flat array
            SetSU3Link(i, j, _gaugeSU3[i, j]);
        }
        
        /// <summary>
        /// Compute staple for SU(3) Wilson action at edge (i,j).
        /// Includes both triangle and square plaquette contributions.
        /// </summary>
        private SU3Matrix ComputeSU3Staple(int i, int j)
        {
            var staple = new Complex[9]; // Accumulator
            
            // Triangle contributions: i -> k -> j
            foreach (int k in Neighbors(i))
            {
                if (k == j || !Edges[k, j]) continue;
                
                SU3Matrix U_ik = GetSU3Link(i, k);
                SU3Matrix U_kj = GetSU3Link(k, j);
                SU3Matrix contribution = U_ik.Multiply(U_kj);
                
                var contrib = contribution.ToFlatArray();
                for (int idx = 0; idx < 9; idx++)
                    staple[idx] += contrib[idx];
            }
            
            // Square plaquette contributions: i -> k -> l -> j
            foreach (int k in Neighbors(i))
            {
                if (k == j) continue;
                
                foreach (int l in Neighbors(k))
                {
                    if (l == i || l == j || !Edges[l, j]) continue;
                    
                    SU3Matrix U_ik = GetSU3Link(i, k);
                    SU3Matrix U_kl = GetSU3Link(k, l);
                    SU3Matrix U_lj = GetSU3Link(l, j);
                    SU3Matrix contribution = U_ik.Multiply(U_kl).Multiply(U_lj);
                    
                    var contrib = contribution.ToFlatArray();
                    for (int idx = 0; idx < 9; idx++)
                        staple[idx] += contrib[idx] * 0.5; // Weight squares less
                }
            }
            
            return new SU3Matrix(staple).ProjectToSU3();
        }
        
        /// <summary>
        /// Extract Gell-Mann coefficient θ_a from matrix M.
        /// θ_a = Tr(λ_a * M) / 2
        /// </summary>
        private static double ExtractGellMannCoefficient(SU3Matrix M, int a)
        {
            var lambda = SU3Matrix.GetGellMann(a + 1);
            Complex trace = Complex.Zero;
            
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    trace += lambda[r, c] * M[r, c];
                }
            }
            
            // Return imaginary part (anti-Hermitian generators give real coefficients from imaginary trace)
            return trace.Imaginary * 0.5;
        }

        // Added per checklist: SetMatrixFlat and UpdateGaugeWilson based on local correlation density
        private void SetMatrixFlat(int i, int j, System.Numerics.Complex[] m, int d)
        {
            for (int idx = 0; idx < d * d; idx++)
                _gaugeSU[i, j, idx] = m[idx];
        }

        /// <summary>
        /// Metropolis-like Wilson update with beta derived from local correlation density.
        /// </summary>
        public void UpdateGaugeWilson()
        {
            if (GaugeDimension == 1 || _gaugeSU == null) return;
            int d = GaugeDimension;

            for (int i = 0; i < N; i++)
            {
                foreach (int j in Neighbors(i))
                {
                    var oldMat = GetMatrixFlat(i, j, d);
                    double oldS = WilsonPlaquetteAction(i, j);

                    // small random SU(N) proposal
                    var proposal = (System.Numerics.Complex[])oldMat.Clone();
                    for (int idx = 0; idx < proposal.Length; idx++)
                    {
                        double angle = (_rng.NextDouble() * 2.0 - 1.0) * 0.1;
                        proposal[idx] *= System.Numerics.Complex.FromPolarCoordinates(1.0, angle);
                    }
                    ProjectToSUN(proposal);
                    SetMatrixFlat(i, j, proposal, d);

                    double newS = WilsonPlaquetteAction(i, j);

                    // beta from local correlation density
                    double beta = GetLocalCorrelationDensity(i);
                    double dS = newS - oldS;

                    if (dS > 0.0)
                    {
                        double accept = Math.Exp(-beta * dS);
                        if (_rng.NextDouble() > accept)
                        {
                            // reject step
                            SetMatrixFlat(i, j, oldMat, d);
                        }
                    }
                }
            }
        }
    }
}
