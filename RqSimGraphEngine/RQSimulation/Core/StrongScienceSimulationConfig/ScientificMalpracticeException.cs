// ============================================================
// ScientificMalpracticeException.cs
// Custom exception for scientific integrity violations
// Part of Strong Science Simulation Config architecture
// ============================================================

namespace RQSimulation.Core.StrongScience;

/// <summary>
/// Exception thrown when scientific integrity is compromised.
/// <para><strong>SCENARIOS:</strong></para>
/// <list type="bullet">
///   <item><description>Fitted constants detected in strict science mode</description></item>
///   <item><description>Interactive rewiring attempted in science mode</description></item>
///   <item><description>Physical constants outside valid ranges</description></item>
///   <item><description>Configuration hash mismatch during reproducibility check</description></item>
/// </list>
/// 
/// <para><strong>DESIGN PHILOSOPHY:</strong></para>
/// <para>
/// This exception exists to make scientific integrity violations LOUD and CLEAR.
/// It is better to fail early with a clear error message than to produce
/// incorrect results that might be mistaken for valid physics.
/// </para>
/// </summary>
public sealed class ScientificMalpracticeException : Exception
{
    /// <summary>
    /// Gets the type of malpractice detected.
    /// </summary>
    public ScientificMalpracticeType MalpracticeType { get; }

    /// <summary>
    /// Gets the name of the contaminating constant (if applicable).
    /// </summary>
    public string? ContaminatingConstant { get; }

    /// <summary>
    /// Gets the expected value (if applicable).
    /// </summary>
    public double? ExpectedValue { get; }

    /// <summary>
    /// Gets the actual value (if applicable).
    /// </summary>
    public double? ActualValue { get; }

    /// <summary>
    /// Creates a new ScientificMalpracticeException.
    /// </summary>
    /// <param name="message">Error message describing the violation.</param>
    /// <param name="malpracticeType">Type of malpractice detected.</param>
    public ScientificMalpracticeException(string message, ScientificMalpracticeType malpracticeType)
        : base(FormatMessage(message, malpracticeType))
    {
        MalpracticeType = malpracticeType;
    }

    /// <summary>
    /// Creates a ScientificMalpracticeException with constant details.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="malpracticeType">Type of malpractice.</param>
    /// <param name="contaminatingConstant">Name of the problematic constant.</param>
    /// <param name="expectedValue">Expected physical value.</param>
    /// <param name="actualValue">Actual (possibly fitted) value.</param>
    public ScientificMalpracticeException(
        string message,
        ScientificMalpracticeType malpracticeType,
        string contaminatingConstant,
        double expectedValue,
        double actualValue)
        : base(FormatMessageWithValues(message, malpracticeType, contaminatingConstant, expectedValue, actualValue))
    {
        MalpracticeType = malpracticeType;
        ContaminatingConstant = contaminatingConstant;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
    }

    /// <summary>
    /// Creates a ScientificMalpracticeException with inner exception.
    /// </summary>
    public ScientificMalpracticeException(
        string message,
        ScientificMalpracticeType malpracticeType,
        Exception innerException)
        : base(FormatMessage(message, malpracticeType), innerException)
    {
        MalpracticeType = malpracticeType;
    }

    private static string FormatMessage(string message, ScientificMalpracticeType type)
    {
        string severity = type switch
        {
            ScientificMalpracticeType.FittedConstantsContamination => "?? CRITICAL",
            ScientificMalpracticeType.InvalidPhysicalConstants => "?? CRITICAL",
            ScientificMalpracticeType.ConfigurationHashMismatch => "?? WARNING",
            ScientificMalpracticeType.InteractiveModificationInScienceMode => "?? WARNING",
            ScientificMalpracticeType.DebugModeInProduction => "?? CAUTION",
            _ => "?? ALERT"
        };

        return $"""
            {severity}: Scientific Integrity Violation
            ========================================
            Type: {type}
            
            {message}
            
            RECOMMENDED ACTIONS:
            {GetRecommendedActions(type)}
            """;
    }

    private static string FormatMessageWithValues(
        string message,
        ScientificMalpracticeType type,
        string constantName,
        double expected,
        double actual)
    {
        return $"""
            {FormatMessage(message, type)}
            
            CONSTANT DETAILS:
            -----------------
            Name:     {constantName}
            Expected: {expected:E4} (physical value)
            Actual:   {actual:E4} (detected value)
            Ratio:    {actual / expected:E2} (deviation from physical)
            """;
    }

    private static string GetRecommendedActions(ScientificMalpracticeType type)
    {
        return type switch
        {
            ScientificMalpracticeType.FittedConstantsContamination => """
                1. Use PlanckScaleConstants or LatticeUnitsConstants instead of FittedConstants
                2. Verify no static references to PhysicsConstants.Fitted.* in science code
                3. Run simulation with StrictScienceProfile, not VisualSandboxProfile
                """,

            ScientificMalpracticeType.InvalidPhysicalConstants => """
                1. Check constant values against CODATA 2022 / PDG 2022
                2. Verify no accidental rescaling or unit conversion errors
                3. Use IPhysicalConstants interface for constant injection
                """,

            ScientificMalpracticeType.ConfigurationHashMismatch => """
                1. Verify experiment protocol was not modified after creation
                2. Check for serialization/deserialization issues
                3. Regenerate configuration hash from current state
                """,

            ScientificMalpracticeType.InteractiveModificationInScienceMode => """
                1. Disable UI sliders when using StrictScienceProfile
                2. Lock configuration after simulation start
                3. Use VisualSandboxProfile for interactive exploration
                """,

            ScientificMalpracticeType.DebugModeInProduction => """
                1. Ensure DEBUG preprocessor directive is not defined
                2. Use Release build for scientific simulations
                3. Remove diagnostic logging that modifies state
                """,

            _ => """
                1. Review the error message for specific guidance
                2. Check configuration against documented requirements
                3. Contact support if issue persists
                """
        };
    }

    /// <summary>
    /// Creates a contamination exception for fitted constant detection.
    /// </summary>
    public static ScientificMalpracticeException FittedConstantDetected(
        string constantName,
        double fittedValue,
        double physicalValue)
    {
        return new ScientificMalpracticeException(
            $"Fitted constant '{constantName}' detected in scientific configuration. " +
            $"Fitted value {fittedValue:E2} differs significantly from physical value {physicalValue:E2}.",
            ScientificMalpracticeType.FittedConstantsContamination,
            constantName,
            physicalValue,
            fittedValue);
    }

    /// <summary>
    /// Creates an exception for interactive modification attempt in science mode.
    /// </summary>
    public static ScientificMalpracticeException InteractiveModificationAttempted(string parameterName)
    {
        return new ScientificMalpracticeException(
            $"Attempted to modify '{parameterName}' during scientific simulation. " +
            "StrictScienceProfile does not allow parameter changes after initialization.",
            ScientificMalpracticeType.InteractiveModificationInScienceMode);
    }
}

/// <summary>
/// Types of scientific malpractice that can be detected.
/// </summary>
public enum ScientificMalpracticeType
{
    /// <summary>
    /// Fitted (tuned for visuals) constants used in strict science mode.
    /// </summary>
    FittedConstantsContamination,

    /// <summary>
    /// Physical constants outside valid ranges (wrong values or units).
    /// </summary>
    InvalidPhysicalConstants,

    /// <summary>
    /// Configuration hash does not match expected value (tampering or corruption).
    /// </summary>
    ConfigurationHashMismatch,

    /// <summary>
    /// Attempted to modify parameters during strict science simulation.
    /// </summary>
    InteractiveModificationInScienceMode,

    /// <summary>
    /// Debug mode enabled in production scientific run.
    /// </summary>
    DebugModeInProduction,

    /// <summary>
    /// Numerical precision insufficient for requested calculation.
    /// </summary>
    InsufficientPrecision,

    /// <summary>
    /// Energy conservation violated beyond tolerance.
    /// </summary>
    EnergyConservationViolation,

    /// <summary>
    /// Gauge constraint violation detected.
    /// </summary>
    GaugeConstraintViolation,

    /// <summary>
    /// Hamiltonian invariance broken.
    /// </summary>
    HamiltonianInvarianceViolation
}
