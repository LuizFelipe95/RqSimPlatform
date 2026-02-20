namespace RQSimulation.Core.Plugins;

/// <summary>
/// Attribute for marking physics module classes for automatic discovery.
///
/// Apply this attribute to IPhysicsModule implementations to enable:
/// - Automatic registration in DI container
/// - Plugin discovery via assembly scanning
/// - Metadata extraction for UI and tooling
///
/// Example:
/// <code>
/// [PhysicsModule("Spacetime Evolution", Category = "Topology")]
/// public class SpacetimePhysicsModule : IPhysicsModule
/// {
///     // Implementation...
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PhysicsModuleAttribute : Attribute
{
    /// <summary>
    /// Display name of the module for UI and logging.
    /// This should match the Name property of the IPhysicsModule implementation.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Optional category for UI grouping (e.g., "Fields", "Gauge", "Gravity", "Topology").
    /// If not specified, the category from the IPhysicsModule implementation will be used.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional description for UI tooltips and documentation.
    /// If not specified, the description from the IPhysicsModule implementation will be used.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this module should be enabled by default when discovered.
    /// Default is false (module starts disabled).
    /// </summary>
    public bool EnabledByDefault { get; init; }

    /// <summary>
    /// Optional exclusive group identifier.
    /// If specified, overrides the ExclusiveGroup property from the IPhysicsModule implementation.
    /// </summary>
    public string? ExclusiveGroup { get; init; }

    /// <summary>
    /// Creates a new PhysicsModuleAttribute with the specified name.
    /// </summary>
    /// <param name="name">Display name of the module</param>
    public PhysicsModuleAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Module name cannot be null or whitespace.", nameof(name));

        Name = name;
    }
}
