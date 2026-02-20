// ============================================================
// StrictScienceProfile.cs
// Implementation of ISimulationProfile for scientific simulations
// Part of Strong Science Simulation Config architecture
// ============================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Profile for scientifically rigorous simulations (Lattice QCD, HPC, publications).
/// <para><strong>SCIENTIFIC INTEGRITY GUARANTEES:</strong></para>
/// <list type="bullet">
///   <item><description>NO access to fitted/tuned constants</description></item>
///   <item><description>Strict validation of all physics calculations</description></item>
///   <item><description>Immutable configuration after initialization</description></item>
///   <item><description>Protocol hash for experiment reproducibility</description></item>
///   <item><description>No user intervention during simulation</description></item>
/// </list>
/// 
/// <para><strong>PRINCIPLE: "Clean Room"</strong></para>
/// <para>
/// Scientific simulations must be isolated from any parameters that were
/// tuned for visual appeal, UI responsiveness, or numerical convenience.
/// This profile physically blocks access to such parameters.
/// </para>
/// 
/// <para><strong>USE CASES:</strong></para>
/// <list type="bullet">
///   <item><description>Lattice QCD simulations</description></item>
///   <item><description>Graviton mass hypothesis testing</description></item>
///   <item><description>Publishable numerical experiments</description></item>
///   <item><description>Hamiltonian invariance verification</description></item>
/// </list>
/// </summary>
public sealed class StrictScienceProfile : ISimulationProfile
{
    private readonly IPhysicalConstants _constants;
    private readonly string _configHash;
    private readonly DateTime _createdAt;
    private readonly string _experimentId;

    /// <summary>
    /// Creates a new strict science profile.
    /// </summary>
    /// <param name="constants">Physical constants provider. Must NOT be FittedConstants.</param>
    /// <param name="experimentId">Unique identifier for the experiment.</param>
    /// <param name="precision">Numerical precision mode.</param>
    /// <exception cref="ScientificMalpracticeException">
    /// Thrown if fitted constants are detected or configuration is inconsistent.
    /// </exception>
    public StrictScienceProfile(
        IPhysicalConstants constants,
        string experimentId,
        NumericalPrecision precision = NumericalPrecision.Double)
    {
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentException.ThrowIfNullOrWhiteSpace(experimentId);

        _constants = constants;
        _experimentId = experimentId;
        Precision = precision;
        _createdAt = DateTime.UtcNow;

        // Validate configuration before computing hash
        ValidateConfiguration();

        // Compute hash AFTER validation to ensure it captures a valid config
        _configHash = ComputeConfigurationHash();
    }

    /// <summary>
    /// Creates a strict science profile with Planck-scale constants.
    /// </summary>
    /// <param name="experimentId">Unique identifier for the experiment.</param>
    public static StrictScienceProfile CreatePlanckScale(string experimentId)
        => new(new PlanckScaleConstants(), experimentId);

    /// <summary>
    /// Creates a strict science profile with lattice units.
    /// </summary>
    /// <param name="experimentId">Unique identifier for the experiment.</param>
    /// <param name="graphSize">Number of nodes for finite-size scaling.</param>
    public static StrictScienceProfile CreateLatticeUnits(string experimentId, int graphSize)
        => new(new LatticeUnitsConstants(1.0, graphSize), experimentId);

    /// <inheritdoc/>
    public string ProfileName => $"StrictScience[{_experimentId}]";

    /// <inheritdoc/>
    public bool IsStrictValidationEnabled => true;

    /// <inheritdoc/>
    public IPhysicalConstants Constants => _constants;

    /// <inheritdoc/>
    public bool AllowInteractiveRewiring => false;

    /// <inheritdoc/>
    public bool UseSoftWalls => false;

    /// <inheritdoc/>
    public bool UseArtificialViscosity => false;

    /// <inheritdoc/>
    public NumericalPrecision Precision { get; }

    /// <summary>
    /// Gets the experiment ID.
    /// </summary>
    public string ExperimentId => _experimentId;

    /// <summary>
    /// Gets the creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt => _createdAt;

    /// <inheritdoc/>
    public string GetConfigurationHash() => _configHash;

    /// <inheritdoc/>
    public void Validate() => ValidateConfiguration();

    /// <summary>
    /// Validates configuration for scientific integrity.
    /// </summary>
    /// <exception cref="ScientificMalpracticeException">
    /// Thrown if any contamination is detected.
    /// </exception>
    private void ValidateConfiguration()
    {
        // Check 1: Ensure we're not using fitted constants
        if (_constants is FittedConstants)
        {
            throw new ScientificMalpracticeException(
                "StrictScienceProfile cannot use FittedConstants. " +
                "Use PlanckScaleConstants or LatticeUnitsConstants instead.",
                ScientificMalpracticeType.FittedConstantsContamination);
        }

        // Check 2: Validate cosmological constant is not artificially large
        if (_constants.CosmologicalConstant > 1e-10)
        {
            throw new ScientificMalpracticeException(
                $"Detected suspiciously large ? = {_constants.CosmologicalConstant:E2}. " +
                "Physical value is ~10????. This looks like a fitted value.",
                ScientificMalpracticeType.FittedConstantsContamination);
        }

        // Check 3: Validate vacuum energy is not artificially large  
        if (_constants.VacuumEnergyDensity > 1.0 && _constants is not LatticeUnitsConstants)
        {
            throw new ScientificMalpracticeException(
                $"Detected suspiciously large ?_vac = {_constants.VacuumEnergyDensity:E2}. " +
                "Physical value is ~10????. This looks like a fitted value.",
                ScientificMalpracticeType.FittedConstantsContamination);
        }

        // Check 4: Validate coupling constants are within physical bounds
        double alpha = _constants.FineStructureConstant;
        if (alpha < 0.007 || alpha > 0.008)
        {
            throw new ScientificMalpracticeException(
                $"Fine structure constant ? = {alpha:E4} is outside physical range [0.007, 0.008]. " +
                "CODATA 2022 value: ? = 1/137.036 ? 0.00730.",
                ScientificMalpracticeType.InvalidPhysicalConstants);
        }
    }

    /// <summary>
    /// Computes SHA256 hash of the configuration for reproducibility.
    /// </summary>
    private string ComputeConfigurationHash()
    {
        var configData = new
        {
            Profile = nameof(StrictScienceProfile),
            ExperimentId = _experimentId,
            Precision = Precision.ToString(),
            Constants = _constants.Name,
            ConstantsHash = ComputeConstantsHash()
        };

        string json = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = false });
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Computes hash of the constants values.
    /// </summary>
    private string ComputeConstantsHash()
    {
        var constants = _constants.GetAllConstants();
        var sb = new StringBuilder();
        foreach (var kvp in constants.OrderBy(k => k.Key))
        {
            sb.Append($"{kvp.Key}={kvp.Value.Value:R};");
        }
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).Substring(0, 16); // First 16 hex chars
    }

    /// <summary>
    /// Gets the experiment protocol for logging/archiving.
    /// </summary>
    public ExperimentProtocol GetProtocol()
    {
        return new ExperimentProtocol
        {
            ExperimentId = _experimentId,
            ProfileName = ProfileName,
            ConfigurationHash = _configHash,
            CreatedAtUtc = _createdAt,
            ConstantsProvider = _constants.Name,
            Precision = Precision,
            RescalingDocumentation = _constants.RescalingDocumentation,
            AllConstants = _constants.GetAllConstants()
                .ToDictionary(k => k.Key, v => v.Value.Value)
        };
    }
}

/// <summary>
/// Experiment protocol data for archival and reproducibility.
/// </summary>
public sealed record ExperimentProtocol
{
    public required string ExperimentId { get; init; }
    public required string ProfileName { get; init; }
    public required string ConfigurationHash { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required string ConstantsProvider { get; init; }
    public required NumericalPrecision Precision { get; init; }
    public required string RescalingDocumentation { get; init; }
    public required Dictionary<string, double> AllConstants { get; init; }

    /// <summary>
    /// Serializes the protocol to JSON for archival.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }

    /// <summary>
    /// Saves the protocol to a file.
    /// </summary>
    public void SaveToFile(string filePath)
    {
        File.WriteAllText(filePath, ToJson());
    }
}
