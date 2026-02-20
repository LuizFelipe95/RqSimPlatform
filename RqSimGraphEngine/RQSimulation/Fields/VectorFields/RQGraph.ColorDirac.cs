using System;
using System.Numerics;
using System.Threading.Tasks;
using RQSimulation.Fields;
using RQSimulation.Gauge;

namespace RQSimulation;

/// <summary>
/// Color spinor field evolution for SU(3) gauge theory (QCD).
/// 
/// CHECKLIST ITEM 2.3: Extend spinors to color triplets for SU(3).
/// 
/// This partial class adds full color-spinor support to RQGraph:
/// - ColorSpinor[N] array storing 12 complex DOF per node
/// - SU(3) gauge-covariant Dirac equation
/// - Color current and charge density computations
/// 
/// The color spinor evolution is SEPARATE from the regular spinor evolution.
/// When GaugeDimension == 3, use UpdateColorDiracField() instead of UpdateDiracFieldRelational().
/// </summary>
public partial class RQGraph
{
    // ================================================================
    // COLOR SPINOR FIELD STORAGE
    // ================================================================

    /// <summary>
    /// Color spinor field: 4 Dirac ? 3 color = 12 complex DOF per node.
    /// Used when GaugeDimension == 3 for full QCD simulation.
    /// </summary>
    private ColorSpinor[]? _colorSpinorField;

    /// <summary>
    /// Previous color spinor state for symplectic integrator.
    /// </summary>
    private ColorSpinor[]? _colorSpinorFieldPrev;

    /// <summary>
    /// Color charge density per node (8-component Gell-Mann basis).
    /// T_a = ?� (?_a/2) ? summed over Dirac indices.
    /// </summary>
    private double[,]? _colorChargeDensity;

    /// <summary>
    /// Check if color spinor field is initialized.
    /// </summary>
    public bool HasColorSpinorField => _colorSpinorField != null && _colorSpinorField.Length == N;

    // ================================================================
    // INITIALIZATION
    // ================================================================

    /// <summary>
    /// Initialize color spinor field with random fluctuations.
    /// 
    /// Each node gets a ColorSpinor with 12 random complex components,
    /// normalized so total field norm = sqrt(N).
    /// </summary>
    /// <param name="amplitude">Initial amplitude for fluctuations</param>
    public void InitColorSpinorField(double amplitude = 0.1)
    {
        _colorSpinorField = ColorSpinorField.InitializeRandom(N, _rng, amplitude);
        _colorSpinorFieldPrev = new ColorSpinor[N];
        Array.Copy(_colorSpinorField, _colorSpinorFieldPrev, N);

        _colorChargeDensity = new double[N, 8];

        // RQ-HYPOTHESIS v2.0: Fermion mass is dynamic, no separate field needed.
        // Mass is computed dynamically from Higgs + Topology (see ComputeDynamicalFermionMass).
    }

    /// <summary>
    /// Place a localized quark at a node with specific color and spin.
    /// </summary>
    /// <param name="node">Node index</param>
    /// <param name="color">Color index: 0=red, 1=green, 2=blue</param>
    /// <param name="spinUp">True for spin up, false for spin down</param>
    /// <param name="leftHanded">True for left-handed, false for right-handed</param>
    /// <param name="amplitude">Amplitude of the spinor</param>
    public void PlaceColorSpinor(int node, int color, bool spinUp, bool leftHanded = true, double amplitude = 1.0)
    {
        if (_colorSpinorField == null)
            InitColorSpinorField();

        if (node < 0 || node >= N || color < 0 || color > 2)
            return;

        // Clear existing spinor at this node
        _colorSpinorField![node] = ColorSpinor.Zero;

        // Create color triplet with only one color component
        var colorVec = ColorTriplet.Zero;
        colorVec[color] = new Complex(amplitude, 0);

        // Set appropriate Dirac component
        if (leftHanded)
        {
            if (spinUp)
                _colorSpinorField[node].A = colorVec;
            else
                _colorSpinorField[node].B = colorVec;
        }
        else
        {
            if (spinUp)
                _colorSpinorField[node].C = colorVec;
            else
                _colorSpinorField[node].D = colorVec;
        }

        // Update physics properties
        if (PhysicsProperties != null && PhysicsProperties.Length == N)
        {
            PhysicsProperties[node].Type = ParticleType.Fermion;
            PhysicsProperties[node].Spin = spinUp ? 0.5 : -0.5;
        }
    }

    // ================================================================
    // DIRAC EVOLUTION WITH FULL SU(3) GAUGE COUPLING
    // ================================================================

    /// <summary>
    /// Evolve color spinor field using gauge-covariant Dirac equation.
    /// 
    /// CHECKLIST ITEM 2.3: Full SU(3) parallel transport for color triplets.
    /// 
    /// The Dirac equation on a graph with SU(3) gauge field:
    ///   i??/?t = H_D ?
    /// 
    /// where H_D includes:
    /// - Kinetic term: ?_j w_ij (U�_ij ?_j - ?_i) (gauge-covariant hopping)
    /// - Mass term: m_i ?_i (from Higgs mechanism)
    /// 
    /// Uses Leapfrog (midpoint) symplectic integrator.
    /// </summary>
    /// <param name="dt">Time step</param>
    public void UpdateColorDiracField(double dt)
    {
        if (_colorSpinorField == null)
            InitColorSpinorField();

        // Check if SU(3) gauge field is available
        if (GaugeDimension != 3 || _gaugeSU3 == null)
        {
            //Console.WriteLine("[WARNING] UpdateColorDiracField requires GaugeDimension=3 and SU(3) gauge field");
            return;
        }

        double hbar = VectorMath.HBar;
        double c = VectorMath.SpeedOfLight;

        // ===== STEP 1: Compute derivatives k1 at current state =====
        var k1 = new ColorSpinor[N];
        ComputeColorDiracDerivatives(_colorSpinorField!, k1, c, hbar);

        // ===== STEP 2: Compute midpoint state =====
        var midField = new ColorSpinor[N];
        double halfDt = dt * 0.5;
        for (int i = 0; i < N; i++)
            midField[i] = _colorSpinorField![i] + halfDt * k1[i];

        // ===== STEP 3: Compute derivatives k2 at midpoint =====
        var k2 = new ColorSpinor[N];
        ComputeColorDiracDerivatives(midField, k2, c, hbar);

        // ===== STEP 4: Full step using midpoint derivatives =====
        for (int i = 0; i < N; i++)
            _colorSpinorField![i] = _colorSpinorField[i] + dt * k2[i];

        // Minimal normalization for long-term stability
        AdaptiveNormalizeColorSpinorField();

        // Update color charge densities
        UpdateColorChargeDensities();
    }

    /// <summary>
    /// Compute Dirac operator derivatives for color spinor field.
    /// 
    /// For each node i:
    ///   d?_i/dt = -i/? [?_j w_ij c (U�_ij ?_j - ?_i) + m_i c? ?_i]
    /// 
    /// where U_ij is the SU(3) gauge link (parallel transport operator).
    /// </summary>
    private void ComputeColorDiracDerivatives(ColorSpinor[] field, ColorSpinor[] derivatives, double c, double hbar)
    {
        Parallel.For(0, N, i =>
        {
            // RQ-HYPOTHESIS v2.0: Dynamic mass from Higgs + Topology
            double higgsVal = ScalarField != null && i < ScalarField.Length ? ScalarField[i] : 0.0;
            double topoMass = _correlationMass != null && i < _correlationMass.Length ? _correlationMass[i] : 1.0;
            double m = PhysicsConstants.YukawaCoupling * Math.Abs(higgsVal) + PhysicsConstants.TopoMassCoupling * topoMass;
            
            double mc = m * c;
            bool isEvenSite = (i % 2 == 0);

            // Accumulate hopping terms for each Dirac component
            var deltaA = ColorTriplet.Zero;
            var deltaB = ColorTriplet.Zero;
            var deltaC = ColorTriplet.Zero;
            var deltaD = ColorTriplet.Zero;

            foreach (int j in Neighbors(i))
            {
                double weight = Weights[i, j];
                if (weight < 1e-10) continue;

                // Staggered fermion sign
                bool isNeighborEven = (j % 2 == 0);
                double sign = (isEvenSite != isNeighborEven) ? 1.0 : -1.0;

                // CHECKLIST ITEM 2.3: Full SU(3) parallel transport
                // Get gauge link and transport neighbor spinor
                SU3Matrix U_ij = GetSU3Link(i, j);
                SU3Matrix Udag = U_ij.Dagger();

                // Transport each color component of each Dirac component
                ColorSpinor psiJ = field[j];
                ColorSpinor transported = psiJ.ParallelTransport(U_ij);

                // Also apply U(1) electromagnetic phase if present
                if (_edgePhaseU1 != null)
                {
                    Complex u1Phase = Complex.FromPolarCoordinates(1.0, -_edgePhaseU1[i, j]);
                    transported = u1Phase * transported;
                }

                // REFACTORING PLAN FIX #3: SPECTRAL-BASED EDGE ORIENTATION
                // Use spectral coordinates instead of node indices for background independence
                // Reference: Refactoring plan section 3, RQGraph.SpectralGeometry.cs
                int edgeDirection = GetSpectralEdgeOrientation(i, j, 2);

                // Staggered Dirac hopping (alternating X-like and Y-like directions)

                if (edgeDirection == 0)
                {
                    // X-like direction
                    deltaB = deltaB + sign * weight * (transported.A - field[i].A);
                    deltaA = deltaA + sign * weight * (transported.B - field[i].B);
                    deltaD = deltaD + sign * weight * (transported.C - field[i].C);
                    deltaC = deltaC + sign * weight * (transported.D - field[i].D);
                }
                else
                {
                    // Y-like direction (with i factors)
                    Complex iSign = Complex.ImaginaryOne * sign;
                    deltaB = deltaB + iSign * weight * (transported.A - field[i].A);
                    deltaA = deltaA - iSign * weight * (transported.B - field[i].B);
                    deltaD = deltaD - iSign * weight * (transported.C - field[i].C);
                    deltaC = deltaC + iSign * weight * (transported.D - field[i].D);
                }
            }

            // Mass term: couples left and right components
            Complex massFactor = -Complex.ImaginaryOne * mc / hbar;
            var massA = massFactor * field[i].C;
            var massB = massFactor * field[i].D;
            var massC = massFactor * field[i].A;
            var massD = massFactor * field[i].B;

            // Full derivative: d?/dt = -i/? * H * ?
            // FIX: Get local time dilation factor (lapse function) like in DiracRelational.cs
            double lapse = GetLocalLapse(i);
            double factor = -lapse / hbar;

            derivatives[i] = new ColorSpinor
            {
                A = factor * (c * deltaA + massA),
                B = factor * (c * deltaB + massB),
                C = factor * (c * deltaC + massC),
                D = factor * (c * deltaD + massD)
            };
        });
    }

    /// <summary>
    /// Adaptive normalization for color spinor field (safety net).
    /// </summary>
    private void AdaptiveNormalizeColorSpinorField()
    {
        if (_colorSpinorField == null) return;

        // Compute total norm
        double totalNorm = 0;
        for (int i = 0; i < N; i++)
            totalNorm += _colorSpinorField[i].NormSquared;

        double targetNorm = N; // Target: average norm per node ~ 1
        double relativeDeviation = Math.Abs(totalNorm - targetNorm) / targetNorm;

        // Only correct if deviation is significant
        if (relativeDeviation > PhysicsConstants.SymplecticNormSafetyThreshold && totalNorm > 1e-10)
        {
            double currentScale = Math.Sqrt(targetNorm / totalNorm);
            double scale = 1.0 + PhysicsConstants.SymplecticNormCorrectionRate * (currentScale - 1.0);

            for (int i = 0; i < N; i++)
                _colorSpinorField[i] = scale * _colorSpinorField[i];
        }
    }

    // ================================================================
    // COLOR CHARGE AND CURRENT COMPUTATIONS
    // ================================================================

    /// <summary>
    /// Update color charge densities for all nodes.
    /// Computes T_a = ?� (?_a/2) ? for each Gell-Mann generator.
    /// </summary>
    private void UpdateColorChargeDensities()
    {
        if (_colorSpinorField == null || _colorChargeDensity == null) return;

        Parallel.For(0, N, i =>
        {
            var spinor = _colorSpinorField![i];

            // Compute color charge for each generator
            // Simplified: compute diagonal charges T_3 and T_8

            // T_3 ? |r|? - |g|? (isospin-like)
            double t3 = 0.0;
            t3 += spinor.A.R.Magnitude * spinor.A.R.Magnitude - spinor.A.G.Magnitude * spinor.A.G.Magnitude;
            t3 += spinor.B.R.Magnitude * spinor.B.R.Magnitude - spinor.B.G.Magnitude * spinor.B.G.Magnitude;
            t3 += spinor.C.R.Magnitude * spinor.C.R.Magnitude - spinor.C.G.Magnitude * spinor.C.G.Magnitude;
            t3 += spinor.D.R.Magnitude * spinor.D.R.Magnitude - spinor.D.G.Magnitude * spinor.D.G.Magnitude;
            _colorChargeDensity![i, 2] = t3 * 0.5;

            // T_8 ? (|r|? + |g|? - 2|b|?) / ?3 (hypercharge-like)
            double t8 = 0.0;
            double bSq = spinor.A.B.Magnitude * spinor.A.B.Magnitude
                       + spinor.B.B.Magnitude * spinor.B.B.Magnitude
                       + spinor.C.B.Magnitude * spinor.C.B.Magnitude
                       + spinor.D.B.Magnitude * spinor.D.B.Magnitude;
            double rgSq = spinor.A.R.Magnitude * spinor.A.R.Magnitude + spinor.A.G.Magnitude * spinor.A.G.Magnitude
                        + spinor.B.R.Magnitude * spinor.B.R.Magnitude + spinor.B.G.Magnitude * spinor.B.G.Magnitude
                        + spinor.C.R.Magnitude * spinor.C.R.Magnitude + spinor.C.G.Magnitude * spinor.C.G.Magnitude
                        + spinor.D.R.Magnitude * spinor.D.R.Magnitude + spinor.D.G.Magnitude * spinor.D.G.Magnitude;
            t8 = (rgSq - 2.0 * bSq) / Math.Sqrt(3.0);
            _colorChargeDensity![i, 7] = t8 * 0.5;

            // Off-diagonal generators (1,2,4,5,6,7) are more complex and involve phases
            // For now, set to zero (simplified model)
            _colorChargeDensity![i, 0] = 0.0;
            _colorChargeDensity![i, 1] = 0.0;
            _colorChargeDensity![i, 3] = 0.0;
            _colorChargeDensity![i, 4] = 0.0;
            _colorChargeDensity![i, 5] = 0.0;
            _colorChargeDensity![i, 6] = 0.0;
        });
    }

    /// <summary>
    /// Get color charge density for node i and generator a.
    /// </summary>
    public double GetColorCharge(int i, int generatorIndex)
    {
        if (_colorChargeDensity == null || i < 0 || i >= N || generatorIndex < 0 || generatorIndex > 7)
            return 0.0;
        return _colorChargeDensity[i, generatorIndex];
    }

    /// <summary>
    /// Compute total color singlet density at node (all colors summed).
    /// </summary>
    public double GetColorSpinorDensity(int i)
    {
        if (_colorSpinorField == null || i < 0 || i >= N)
            return 0.0;
        return _colorSpinorField[i].FermionDensity;
    }

    /// <summary>
    /// Compute color current between two connected nodes.
    /// J^a_ij = ??_i (?^a/2) ?^? U_ij ?_j (simplified to scalar for graph)
    /// </summary>
    public double ComputeColorCurrent(int i, int j, int generatorIndex)
    {
        if (_colorSpinorField == null || !Edges[i, j])
            return 0.0;

        if (generatorIndex < 0 || generatorIndex > 7)
            return 0.0;

        // Simplified: return difference of color charges weighted by edge
        double qi = GetColorCharge(i, generatorIndex);
        double qj = GetColorCharge(j, generatorIndex);

        return (qj - qi) * Weights[i, j];
    }

    /// <summary>
    /// Check if quarks are confined (no net color charge in any region).
    /// Returns true if total color charge is below threshold.
    /// </summary>
    public bool IsColorConfined(double threshold = 0.1)
    {
        if (_colorChargeDensity == null) return true;

        // Sum absolute color charges
        double totalCharge = 0.0;
        for (int i = 0; i < N; i++)
        {
            for (int a = 0; a < 8; a++)
            {
                totalCharge += Math.Abs(_colorChargeDensity[i, a]);
            }
        }

        return totalCharge / N < threshold;
    }
}
