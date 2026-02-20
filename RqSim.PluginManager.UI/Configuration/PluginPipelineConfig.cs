namespace RqSimPlatform.PluginManager.UI.Configuration;

/// <summary>
/// Configuration for a complete physics pipeline, including all modules and their settings.
/// Used for serializing and deserializing pipeline state to/from JSON.
/// </summary>
public sealed class PluginPipelineConfig
{
    /// <summary>
    /// Name of this configuration preset.
    /// </summary>
    public string PresetName { get; set; }
    
    /// <summary>
    /// Configuration version for migration support.
    /// </summary>
    public string Version { get; set; } = "1.0";
    
    /// <summary>
    /// When this configuration was last modified.
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// List of module configurations in execution order.
    /// </summary>
    public List<PluginModuleConfig> Modules { get; set; } = [];
    
    /// <summary>
    /// Creates a new pipeline configuration with the specified preset name.
    /// </summary>
    public PluginPipelineConfig(string presetName)
    {
        PresetName = presetName;
    }
    
    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// </summary>
    public PluginPipelineConfig() : this("Default")
    {
    }
}

/// <summary>
/// Configuration for a single physics module.
/// Stores type information and settings needed to recreate the module instance.
/// </summary>
public sealed class PluginModuleConfig
{
    /// <summary>
    /// Fully qualified type name of the module.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Path to external assembly containing the module, or null for built-in modules.
    /// </summary>
    public string? AssemblyPath { get; set; }
    
    /// <summary>
    /// Whether the module is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Execution order index within the pipeline.
    /// </summary>
    public int OrderIndex { get; set; }
    
    /// <summary>
    /// Custom parameters for configurable modules.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = [];
}
