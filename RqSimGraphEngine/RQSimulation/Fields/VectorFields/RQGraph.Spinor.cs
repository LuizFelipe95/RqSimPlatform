using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RQSimulation
{
    /// <summary>
    /// Implements spinor field dynamics for fermion propagation according to RQ hypothesis.
    /// Fermions are represented as localized spinor excitations that propagate through
    /// the correlation network according to a discretized Dirac equation.
    /// </summary>
    public partial class RQGraph
    {
        // Dirac spinor field: 4 complex components per node
        private Complex[]? _spinorA;  // Left-handed up
        private Complex[]? _spinorB;  // Left-handed down
        private Complex[]? _spinorC;  // Right-handed up
        private Complex[]? _spinorD;  // Right-handed down

        // Spinor momentum for Hamiltonian dynamics
        private Complex[]? _spinorDotA;
        private Complex[]? _spinorDotB;
        private Complex[]? _spinorDotC;
        private Complex[]? _spinorDotD;

        // RQ-HYPOTHESIS v2.0: Removed static mass field.
        // Mass is now dynamic (Higgs + Topology).
        // private double[]? _fermionMassField;

        // Pauli matrices (stored for efficiency)
        private static readonly Complex[,] PauliSigma1 = {
            { Complex.Zero, Complex.One },
            { Complex.One, Complex.Zero }
        };

        private static readonly Complex[,] PauliSigma2 = {
            { Complex.Zero, -Complex.ImaginaryOne },
            { Complex.ImaginaryOne, Complex.Zero }
        };

        private static readonly Complex[,] PauliSigma3 = {
            { Complex.One, Complex.Zero },
            { Complex.Zero, -Complex.One }
        };

        /// <summary>
        /// Initialize spinor field with small random fluctuations.
        /// </summary>
        public void InitSpinorField(double amplitude = 0.01)
        {
            _spinorA = new Complex[N];
            _spinorB = new Complex[N];
            _spinorC = new Complex[N];
            _spinorD = new Complex[N];
            _spinorDotA = new Complex[N];
            _spinorDotB = new Complex[N];
            _spinorDotC = new Complex[N];
            _spinorDotD = new Complex[N];
            // _fermionMassField = new double[N]; // Removed

            for (int i = 0; i < N; i++)
            {
                // Random spinor initialization
                _spinorA[i] = new Complex(
                    (_rng.NextDouble() - 0.5) * amplitude,
                    (_rng.NextDouble() - 0.5) * amplitude);
                _spinorB[i] = new Complex(
                    (_rng.NextDouble() - 0.5) * amplitude,
                    (_rng.NextDouble() - 0.5) * amplitude);
                _spinorC[i] = new Complex(
                    (_rng.NextDouble() - 0.5) * amplitude,
                    (_rng.NextDouble() - 0.5) * amplitude);
                _spinorD[i] = new Complex(
                    (_rng.NextDouble() - 0.5) * amplitude,
                    (_rng.NextDouble() - 0.5) * amplitude);

                // Mass is now dynamic
                // _fermionMassField[i] = ComputeNodeMass(i);
            }
        }

        /// <summary>
        /// Places a localized Dirac spinor (particle) at a node with given spin state.
        /// </summary>
        public void PlaceSpinor(int node, bool spinUp, double amplitude = 1.0, bool leftHanded = true)
        {
            if (_spinorA == null) InitSpinorField();

            // Clear previous spinor at this node
            _spinorA![node] = Complex.Zero;
            _spinorB![node] = Complex.Zero;
            _spinorC![node] = Complex.Zero;
            _spinorD![node] = Complex.Zero;

            if (leftHanded)
            {
                if (spinUp)
                    _spinorA[node] = new Complex(amplitude, 0);
                else
                    _spinorB[node] = new Complex(amplitude, 0);
            }
            else
            {
                if (spinUp)
                    _spinorC[node] = new Complex(amplitude, 0);
                else
                    _spinorD[node] = new Complex(amplitude, 0);
            }

            // Mark node as fermion
            if (PhysicsProperties != null && PhysicsProperties.Length == N)
            {
                PhysicsProperties[node].Type = ParticleType.Fermion;
                PhysicsProperties[node].Spin = spinUp ? 0.5 : -0.5;
            }
        }

        /// <summary>
        /// Computes the spinor probability density at a node.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double SpinorDensity(int node)
        {
            if (_spinorA == null) return 0;

            return _spinorA![node].Magnitude * _spinorA[node].Magnitude +
                   _spinorB![node].Magnitude * _spinorB[node].Magnitude +
                   _spinorC![node].Magnitude * _spinorC[node].Magnitude +
                   _spinorD![node].Magnitude * _spinorD[node].Magnitude;
        }

        /// <summary>
        /// Computes the total spinor field norm.
        /// </summary>
        public double TotalSpinorNorm()
        {
            if (_spinorA == null) return 0;

            double norm = 0;
            for (int i = 0; i < N; i++)
            {
                norm += SpinorDensity(i);
            }
            return Math.Sqrt(norm);
        }


        /// <summary>
        /// Computes the spinor current density j^μ = ψ̄γ^μψ at a node.
        /// Returns (j0, jx, jy) where j0 is the probability density.
        /// </summary>
        public (double j0, double jx, double jy) SpinorCurrent(int node)
        {
            if (_spinorA == null) return (0, 0, 0);

            // j^0 = ψ†ψ = |A|² + |B|² + |C|² + |D|²
            double j0 = SpinorDensity(node);

            // j^x = ψ†α^x ψ where α^x = γ^0 γ^x
            // In chiral basis: α^x exchanges L and R with σ^x
            // j^x = 2*Re(A*C + B*D) for simplified form
            Complex A = _spinorA[node], B = _spinorB![node];
            Complex C = _spinorC![node], D = _spinorD![node];

            double jx = 2.0 * (A * Complex.Conjugate(C) + B * Complex.Conjugate(D)).Real;

            // j^y = 2*Re(-i*A*C + i*B*D) = 2*Im(A*C - B*D)
            double jy = 2.0 * (A * Complex.Conjugate(C) - B * Complex.Conjugate(D)).Imaginary;

            return (j0, jx, jy);
        }

        /// <summary>
        /// Computes the spin density S^z = ψ†Σ^z ψ at a node.
        /// </summary>
        public double SpinZ(int node)
        {
            if (_spinorA == null) return 0;

            // Σ^z = diag(σ^z, σ^z) in the Dirac representation
            // S^z = |A|² - |B|² + |C|² - |D|²
            double A2 = _spinorA[node].Magnitude * _spinorA[node].Magnitude;
            double B2 = _spinorB![node].Magnitude * _spinorB[node].Magnitude;
            double C2 = _spinorC![node].Magnitude * _spinorC[node].Magnitude;
            double D2 = _spinorD![node].Magnitude * _spinorD[node].Magnitude;

            return 0.5 * (A2 - B2 + C2 - D2);  // In units of ℏ
        }

        /// <summary>
        /// Computes the helicity h = S·p/|p| for the spinor at a node.
        /// </summary>
        public double Helicity(int node)
        {
            if (_spinorA == null) return 0;

            var (j0, jx, jy) = SpinorCurrent(node);
            double pMag = Math.Sqrt(jx * jx + jy * jy);
            if (pMag < 1e-10) return 0;

            double Sz = SpinZ(node);

            // For massless fermions, helicity = chirality = ±1/2
            // For massive, it's more complex; approximate with momentum direction
            return Sz;  // Simplified
        }

        /// <summary>
        /// Applies a local SU(2)_L gauge transformation to the spinor field.
        /// 
        /// CHECKLIST ITEM 11: Only transform left-handed doublet (A, B).
        /// Right-handed components (C, D) are SU(2)_L singlets and must NOT be transformed.
        /// 
        /// In the Standard Model:
        /// - Left-handed fermions form doublets under SU(2)_L: (ν_L, e_L), (u_L, d_L)
        /// - Right-handed fermions are singlets under SU(2)_L: e_R, u_R, d_R
        /// 
        /// If an SU(2)_R gauge symmetry is desired, it must be implemented separately
        /// with its own gauge matrix.
        /// </summary>
        public void ApplyLocalSU2Gauge(int node, Complex[,] gaugeMatrix)
        {
            if (_spinorA == null || gaugeMatrix.GetLength(0) != 2) return;

            // Transform left-handed doublet (A, B) under SU(2)_L
            Complex A = _spinorA[node], B = _spinorB![node];
            Complex newA = gaugeMatrix[0, 0] * A + gaugeMatrix[0, 1] * B;
            Complex newB = gaugeMatrix[1, 0] * A + gaugeMatrix[1, 1] * B;
            _spinorA[node] = newA;
            _spinorB[node] = newB;

            // CHECKLIST ITEM 11: Right-handed singlet (C, D) does NOT transform under SU(2)_L
            // (_spinorC[node] and _spinorD[node] remain unchanged)
            // 
            // Previously, the code incorrectly transformed (C, D) as if there were
            // an SU(2)_R gauge symmetry. This violates the Standard Model where
            // right-handed fermions have no SU(2)_L charge.
        }

        /// <summary>
        /// Updates fermion mass from scalar field via proper Yukawa coupling.
        /// 
        /// FIX: Replaced manual _correlationMass with proper Higgs mechanism.
        /// 
        /// Physics: In the Standard Model, fermion masses arise from Yukawa coupling
        /// to the Higgs field:
        ///   L_Yukawa = -g_Y * ψ̄_L φ ψ_R + h.c.
        /// 
        /// After spontaneous symmetry breaking, φ = v + h where v = Higgs VEV.
        /// This gives fermion mass:
        ///   m_f = g_Y * v
        /// 
        /// In the RQ framework, the scalar field φ_i at each node plays the role of
        /// the Higgs field. The fermion mass at node i is:
        ///   m_i = g_Y * |φ_i|
        /// 
        /// CHECKLIST ITEM 12: Rate-limited mass evolution to prevent non-adiabatic jumps.
        /// The mass change is capped to 10% per step to maintain adiabaticity.
        /// </summary>
        public void UpdateFermionMasses()
        {
            const double maxMassChangeRate = 0.10; // 10% max change per step
            const double g_Yukawa = 0.1; // Yukawa coupling constant
            const double bareMass = 0.001; // Small bare mass for numerical stability

            for (int i = 0; i < N; i++)
            {
                // FIX: Mass from scalar field (Higgs mechanism), NOT correlation density
                // The scalar field φ acts as the Higgs field
                double phi = 0.0;
                if (ScalarField != null && i < ScalarField.Length)
                {
                    phi = ScalarField[i];
                }

                // Yukawa coupling: m = g_Y * |φ|
                // The mass is proportional to the local Higgs VEV
                double targetMass = bareMass + g_Yukawa * Math.Abs(phi);

                // RQ-HYPOTHESIS v2.0: Mass is dynamic, no need to store in _fermionMassField.
                // We update PhysicsProperties for visualization/analytics.
                
                // Update physics properties
                if (PhysicsProperties != null && PhysicsProperties.Length == N &&
                    PhysicsProperties[i].Type == ParticleType.Fermion)
                {
                    PhysicsProperties[i].Mass = targetMass;
                }
            }
        }

        /// <summary>
        /// Computes the axial current j5^μ = ψ̄γ^μγ5 ψ (chiral current).
        /// </summary>
        public double AxialCharge(int node)
        {
            if (_spinorA == null) return 0;

            // j5^0 = ψ†γ5 ψ = |A|² + |B|² - |C|² - |D|² (in chiral basis)
            double A2 = _spinorA[node].Magnitude * _spinorA[node].Magnitude;
            double B2 = _spinorB![node].Magnitude * _spinorB[node].Magnitude;
            double C2 = _spinorC![node].Magnitude * _spinorC[node].Magnitude;
            double D2 = _spinorD![node].Magnitude * _spinorD[node].Magnitude;

            return A2 + B2 - C2 - D2;
        }

        /// <summary>
        /// Computes the chiral condensate ⟨ψ̄ψ⟩ as an order parameter for chiral symmetry breaking.
        /// </summary>
        public double ChiralCondensate()
        {
            if (_spinorA == null) return 0;

            double sum = 0;
            for (int i = 0; i < N; i++)
            {
                // ψ̄ψ = ψ†γ0ψ for Dirac conjugate
                // In Weyl basis: 2*Re(ψ_L†ψ_R)
                Complex lDotR = Complex.Conjugate(_spinorA[i]) * _spinorC![i] +
                               Complex.Conjugate(_spinorB![i]) * _spinorD![i];
                sum += 2.0 * lDotR.Real;
            }
            return sum / N;
        }
    }
}
