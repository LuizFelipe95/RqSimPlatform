namespace RQSimulation.Gauge;

/// <summary>
/// Generic interface for gauge group operations.
///
/// CHECKLIST ITEM 44 (11.3): Gauge Group Support Extension
/// ==========================================================
/// Provides a unified abstraction for different gauge groups (SU(2), SU(3), SU(N), U(1), etc.)
/// enabling:
/// - Easy extension to new gauge groups without modifying core simulation code
/// - Consistent API for group operations across different implementations
/// - Type-safe gauge field computations with strategy pattern
///
/// GAUGE GROUP THEORY BACKGROUND:
/// ===============================
/// A gauge group G is a Lie group whose elements U represent local symmetry transformations.
/// Gauge fields live in the Lie algebra g (tangent space at identity).
///
/// Key operations:
/// 1. Identity - neutral element: I * U = U * I = U
/// 2. Multiplication - group composition: (U * V) * W = U * (V * W)
/// 3. Inverse/Conjugate - for unitary groups: U† * U = I
/// 4. Lie algebra exponential: exp: g → G
///
/// Common gauge groups in physics:
/// - U(1): Electromagnetism (phase rotations)
/// - SU(2): Weak interaction (isospin)
/// - SU(3): Strong interaction (color charge, QCD)
/// - SU(N): Generalization for theories with N fundamental charges
///
/// IMPLEMENTATION NOTES:
/// =====================
/// - T should be the matrix type (e.g., Complex[,], SU2Matrix, SU3Matrix)
/// - All operations should preserve group properties (closure, unitarity, det=1 for SU(N))
/// - Implementations should be thread-safe for concurrent gauge field updates
/// - For performance, consider caching structure constants and generators
///
/// USAGE EXAMPLE:
/// ==============
/// <code>
/// IGaugeGroup&lt;SU3Matrix&gt; su3 = new SU3Group();
/// var U1 = su3.Identity;
/// var U2 = su3.Exponential(new double[8] { ... }); // 8 generators for SU(3)
/// var plaquette = su3.ComputePlaquette(U1, U2, U3, U4);
/// var flux = su3.TraceDistance(plaquette, su3.Identity);
/// </code>
/// </summary>
/// <typeparam name="T">Matrix type representing group elements</typeparam>
public interface IGaugeGroup<T>
{
    /// <summary>
    /// Identity element of the group.
    /// For SU(N): I = N×N identity matrix.
    /// </summary>
    T Identity { get; }

    /// <summary>
    /// Dimension of the group (N for SU(N), 1 for U(1)).
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// Number of generators (dimension of Lie algebra).
    /// For SU(N): N² - 1 generators.
    /// Examples: SU(2) has 3, SU(3) has 8, SU(4) has 15.
    /// </summary>
    int GeneratorCount { get; }

    /// <summary>
    /// Group multiplication: compose two group elements.
    ///
    /// PROPERTIES:
    /// - Associative: (a * b) * c = a * (b * c)
    /// - Identity: a * I = I * a = a
    /// - Closure: result is also in the group
    ///
    /// For matrix groups, this is standard matrix multiplication.
    /// </summary>
    /// <param name="a">First group element</param>
    /// <param name="b">Second group element</param>
    /// <returns>Product a * b</returns>
    T Multiply(T a, T b);

    /// <summary>
    /// Hermitian conjugate (inverse for unitary groups).
    ///
    /// For unitary groups (U(N), SU(N)):
    ///   U† * U = U * U† = I
    ///
    /// This is the group inverse for compact Lie groups.
    /// For matrix representation: (U†)_ij = conj(U_ji)
    /// </summary>
    /// <param name="element">Group element</param>
    /// <returns>Hermitian conjugate U†</returns>
    T Conjugate(T element);

    /// <summary>
    /// Compute trace of group element.
    ///
    /// For SU(N): Tr(U) is complex-valued with |Tr(U)| ≤ N.
    ///
    /// PHYSICS INTERPRETATION:
    /// - Tr(U) = N means U = I (no gauge flux)
    /// - Re(Tr(U)) measures deviation from identity
    /// - Used in plaquette action: S = Re(Tr(P))
    /// </summary>
    /// <param name="element">Group element</param>
    /// <returns>Trace (sum of diagonal elements)</returns>
    System.Numerics.Complex Trace(T element);

    /// <summary>
    /// Exponential map from Lie algebra to group: exp: g → G.
    ///
    /// THEORY:
    /// Any element U ∈ G near identity can be written as:
    ///   U = exp(i θ_a T_a)
    /// where T_a are generators and θ_a are real coefficients.
    ///
    /// For SU(N): U = exp(i Σ θ_a λ_a/2)
    /// where λ_a are generalized Gell-Mann matrices.
    ///
    /// This map is crucial for:
    /// - Gauge-covariant field updates
    /// - Hamiltonian evolution: U(t+dt) = exp(-iH dt) U(t)
    /// - Monte Carlo updates with correct measure
    /// </summary>
    /// <param name="algebraCoefficients">
    /// Coefficients for each generator. Length must equal GeneratorCount.
    /// For SU(2): 3 coefficients (θ_1, θ_2, θ_3)
    /// For SU(3): 8 coefficients (θ_1, ..., θ_8)
    /// </param>
    /// <returns>Group element U = exp(i Σ θ_a T_a)</returns>
    T Exponential(double[] algebraCoefficients);

    /// <summary>
    /// Compute a plaquette: P = U_ij * U_jk * U†_kl * U†_li.
    ///
    /// LATTICE GAUGE THEORY:
    /// A plaquette is the product of link variables around a minimal loop.
    /// For a square plaquette starting at site i:
    ///   P = U₁ * U₂ * U₃† * U₄†
    ///
    /// PHYSICS SIGNIFICANCE:
    /// - P = I (identity) means no field strength/curvature
    /// - Deviation from I indicates gauge flux through plaquette
    /// - Wilson action: S_W = Σ [1 - Re(Tr(P))/(2N)]
    /// - Field strength tensor: F_μν ~ i(P - P†)
    ///
    /// For relational graphs: plaquettes are minimal cycles.
    /// </summary>
    /// <param name="u1">First link variable</param>
    /// <param name="u2">Second link variable</param>
    /// <param name="u3">Third link variable (will be conjugated)</param>
    /// <param name="u4">Fourth link variable (will be conjugated)</param>
    /// <returns>Plaquette P = u1 * u2 * u3† * u4†</returns>
    T ComputePlaquette(T u1, T u2, T u3, T u4);

    /// <summary>
    /// Measure "distance" between two group elements using trace.
    ///
    /// DEFINITION:
    /// d(U, V) = |1 - Re(Tr(U† * V)) / N|
    ///
    /// PROPERTIES:
    /// - d(U, U) = 0 (zero distance from self)
    /// - d(U, I) measures deviation from identity
    /// - d(U, V) = d(V, U) (symmetric)
    /// - Range: [0, 2] for normalized trace
    ///
    /// USAGE:
    /// - Convergence criterion for iterative algorithms
    /// - Measuring gauge flux magnitude
    /// - Monte Carlo acceptance probability
    /// </summary>
    /// <param name="a">First group element</param>
    /// <param name="b">Second group element</param>
    /// <returns>Trace-based distance measure</returns>
    double TraceDistance(T a, T b);

    /// <summary>
    /// Project arbitrary matrix onto group manifold.
    ///
    /// Due to numerical errors, computed matrices may drift from exact
    /// group constraints (unitarity, det=1). Projection restores:
    /// - Unitarity: U * U† = I
    /// - Determinant: det(U) = 1 for SU(N)
    ///
    /// METHODS:
    /// - Gram-Schmidt orthogonalization
    /// - Polar decomposition: U = (U U†)^(-1/2) * U
    /// - Iterative Newton-Schulz method
    ///
    /// WHEN TO USE:
    /// - After numerical integration of equations of motion
    /// - After loading from files (handle precision loss)
    /// - Periodically during long simulations (every ~1000 steps)
    /// </summary>
    /// <param name="element">Matrix to project</param>
    /// <returns>Nearest group element</returns>
    T ProjectToGroup(T element);

    /// <summary>
    /// Create random group element near identity for Monte Carlo updates.
    ///
    /// METROPOLIS-HASTINGS ALGORITHM:
    /// Generate proposal: U' = V * U where V = exp(i θ_a T_a)
    /// with θ_a ~ Uniform(-ε, ε) for small ε.
    ///
    /// The scale parameter ε controls:
    /// - Small ε: high acceptance rate, slow exploration
    /// - Large ε: low acceptance rate, fast exploration
    /// - Optimal ε: ~50% acceptance rate
    ///
    /// For hybrid Monte Carlo: ε ~ 0.1 - 0.3
    /// </summary>
    /// <param name="random">Random number generator</param>
    /// <param name="scale">Perturbation scale (0 = identity, 1 = random)</param>
    /// <returns>Group element V ≈ I + O(scale)</returns>
    T RandomNearIdentity(Random random, double scale);

    /// <summary>
    /// Get Lie algebra generator by index.
    ///
    /// Generators T_a satisfy:
    /// - Hermitian: T†_a = T_a
    /// - Traceless: Tr(T_a) = 0 for SU(N)
    /// - Normalized: Tr(T_a T_b) = (1/2) δ_ab
    /// - Commutation: [T_a, T_b] = i f_abc T_c
    ///
    /// For SU(2): T_a = σ_a/2 (Pauli matrices)
    /// For SU(3): T_a = λ_a/2 (Gell-Mann matrices)
    /// </summary>
    /// <param name="index">Generator index (0-based, 0 to GeneratorCount-1)</param>
    /// <returns>Generator matrix T_a</returns>
    T GetGenerator(int index);

    /// <summary>
    /// Get structure constant f_abc for the Lie algebra.
    ///
    /// DEFINITION:
    /// [T_a, T_b] = i Σ_c f_abc T_c
    ///
    /// PROPERTIES:
    /// - Antisymmetric: f_abc = -f_bac
    /// - Jacobi identity: Σ_d (f_abe f_ecd + f_bce f_ead + f_cae f_ebd) = 0
    ///
    /// For SU(2): f_abc = ε_abc (Levi-Civita symbol)
    /// For SU(3): 8×8×8 tensor with specific non-zero entries
    /// </summary>
    /// <param name="a">First index</param>
    /// <param name="b">Second index</param>
    /// <param name="c">Third index</param>
    /// <returns>Structure constant f_abc</returns>
    double GetStructureConstant(int a, int b, int c);
}
