using System.Text.Json.Serialization;

namespace RqSimForms.Events;

/// <summary>
/// Immutable record representing a physics verification event.
/// Contains all data needed to track and export simulation events.
/// </summary>
/// <param name="EventType">The type of physics event.</param>
/// <param name="Timestamp">When the event occurred (simulation step).</param>
/// <param name="Description">Human-readable description of the event.</param>
/// <param name="Value">Primary numeric value associated with the event.</param>
/// <param name="SecondaryValue">Optional secondary value (e.g., for comparisons).</param>
/// <param name="Parameters">Optional dictionary of additional parameters.</param>
public sealed record PhysicsVerificationEvent(
    PhysicsEventType EventType,
    long Timestamp,
    string Description,
    double Value,
    double? SecondaryValue = null,
    Dictionary<string, object>? Parameters = null)
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Wall-clock time when the event was recorded.
    /// </summary>
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Severity level of the event (0=info, 1=warning, 2=critical).
    /// </summary>
    public int Severity { get; init; }

    /// <summary>
    /// Creates a MassGap event.
    /// </summary>
    public static PhysicsVerificationEvent MassGap(long step, double gapValue, double? targetGap = null)
        => new(
            PhysicsEventType.MassGap,
            step,
            $"Mass gap ?? = {gapValue:F6}",
            gapValue,
            targetGap,
            new Dictionary<string, object> { ["lambda1"] = gapValue });

    /// <summary>
    /// Creates a SpectralDimension event.
    /// </summary>
    public static PhysicsVerificationEvent SpectralDimension(long step, double dS, double confidence)
        => new(
            PhysicsEventType.SpectralDimension,
            step,
            $"d_S = {dS:F3} (conf: {confidence:P0})",
            dS,
            confidence,
            new Dictionary<string, object> { ["dS"] = dS, ["confidence"] = confidence });

    /// <summary>
    /// Creates a SpeedOfLightIsotropy event.
    /// </summary>
    public static PhysicsVerificationEvent SpeedOfLightIsotropy(long step, double velocity, double variance)
        => new(
            PhysicsEventType.SpeedOfLightIsotropy,
            step,
            $"c_eff = {velocity:F4}, ?? = {variance:E2}",
            velocity,
            variance,
            new Dictionary<string, object> { ["c_eff"] = velocity, ["variance"] = variance });

    /// <summary>
    /// Creates a RicciFlatness event.
    /// </summary>
    public static PhysicsVerificationEvent RicciFlatness(long step, double avgCurvature)
        => new(
            PhysicsEventType.RicciFlatness,
            step,
            $"Avg Ricci = {avgCurvature:F6}",
            avgCurvature,
            null,
            new Dictionary<string, object> { ["avgRicci"] = avgCurvature })
        {
            Severity = Math.Abs(avgCurvature) > 0.1 ? 1 : 0
        };

    /// <summary>
    /// Creates a HolographicAreaLaw event.
    /// </summary>
    public static PhysicsVerificationEvent HolographicAreaLaw(long step, double entropy, double area, double volume)
        => new(
            PhysicsEventType.HolographicAreaLaw,
            step,
            $"S/A = {(area > 0 ? entropy / area : 0):F4}",
            entropy,
            area,
            new Dictionary<string, object>
            {
                ["entropy"] = entropy,
                ["area"] = area,
                ["volume"] = volume,
                ["S_over_A"] = area > 0 ? entropy / area : 0,
                ["S_over_V"] = volume > 0 ? entropy / volume : 0
            });

    /// <summary>
    /// Creates a HausdorffDimension event.
    /// </summary>
    public static PhysicsVerificationEvent HausdorffDimension(long step, double dH)
        => new(
            PhysicsEventType.HausdorffDimension,
            step,
            $"d_H = {dH:F3}",
            dH,
            null,
            new Dictionary<string, object> { ["dH"] = dH });

    /// <summary>
    /// Creates a ClusterTransition event.
    /// </summary>
    public static PhysicsVerificationEvent ClusterTransition(long step, int largestClusterSize, double clusterRatio, string phase)
        => new(
            PhysicsEventType.ClusterTransition,
            step,
            $"Cluster: {largestClusterSize} ({clusterRatio:P1}) - {phase}",
            clusterRatio,
            largestClusterSize,
            new Dictionary<string, object>
            {
                ["size"] = largestClusterSize,
                ["ratio"] = clusterRatio,
                ["phase"] = phase
            })
        {
            Severity = clusterRatio > 0.5 ? 2 : (clusterRatio > 0.3 ? 1 : 0)
        };

    /// <summary>
    /// Creates an AutoTuningAdjustment event.
    /// </summary>
    public static PhysicsVerificationEvent AutoTuningAdjustment(long step, string parameter, double oldValue, double newValue)
        => new(
            PhysicsEventType.AutoTuningAdjustment,
            step,
            $"{parameter}: {oldValue:G4} ? {newValue:G4}",
            newValue,
            oldValue,
            new Dictionary<string, object>
            {
                ["parameter"] = parameter,
                ["oldValue"] = oldValue,
                ["newValue"] = newValue,
                ["delta"] = newValue - oldValue
            });

    /// <summary>
    /// Returns a formatted parameters string for display.
    /// </summary>
    [JsonIgnore]
    public string ParametersDisplay => Parameters is null
        ? string.Empty
        : string.Join(", ", Parameters.Select(p => $"{p.Key}={p.Value}"));
}
