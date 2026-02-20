using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.Configuration;

/// <summary>
/// Serializer for plugin pipeline configurations.
/// Handles saving/loading configurations to/from JSON files.
/// </summary>
public static class PluginConfigSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Default configuration file path in user's AppData folder.
    /// </summary>
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RqSimPlatform",
        "plugins.json");

    /// <summary>
    /// Saves the pipeline configuration to a JSON file.
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="filePath">Target file path (uses default if null)</param>
    public static void Save(PluginPipelineConfig config, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        filePath ??= DefaultConfigPath;
        
        // Ensure directory exists
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        config.LastModified = DateTime.UtcNow;
        
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a pipeline configuration from a JSON file.
    /// </summary>
    /// <param name="filePath">Source file path (uses default if null)</param>
    /// <returns>Loaded configuration, or null if file doesn't exist</returns>
    public static PluginPipelineConfig? Load(string? filePath = null)
    {
        filePath ??= DefaultConfigPath;
        
        if (!File.Exists(filePath))
        {
            return null;
        }
        
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<PluginPipelineConfig>(json, JsonOptions);
    }

    /// <summary>
    /// Creates a configuration snapshot from the current pipeline state.
    /// </summary>
    /// <param name="pipeline">Pipeline to capture</param>
    /// <param name="presetName">Optional preset name</param>
    /// <returns>Configuration representing current pipeline state</returns>
    public static PluginPipelineConfig CaptureFromPipeline(PhysicsPipeline pipeline, string? presetName = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        
        var config = new PluginPipelineConfig(presetName ?? "Custom");
        
        int orderIndex = 0;
        foreach (var module in pipeline.Modules)
        {
            var moduleConfig = new PluginModuleConfig
            {
                TypeName = module.GetType().FullName ?? module.GetType().Name,
                AssemblyPath = GetAssemblyPath(module.GetType()),
                IsEnabled = module.IsEnabled,
                OrderIndex = orderIndex++,
                Parameters = CaptureModuleProperties(module)
            };
            
            config.Modules.Add(moduleConfig);
        }
        
        return config;
    }

    /// <summary>
    /// Restores pipeline state from a configuration.
    /// </summary>
    /// <param name="pipeline">Pipeline to restore to</param>
    /// <param name="config">Configuration to restore from</param>
    /// <param name="clearExisting">Whether to clear existing modules first</param>
    /// <returns>Number of modules successfully restored</returns>
    public static int RestoreToPipeline(PhysicsPipeline pipeline, PluginPipelineConfig config, bool clearExisting = true)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(config);
        
        if (clearExisting)
        {
            pipeline.Clear();
        }
        
        int restoredCount = 0;
        
        // Sort modules by order index
        var orderedModules = config.Modules.OrderBy(m => m.OrderIndex).ToList();
        
        foreach (var moduleConfig in orderedModules)
        {
            try
            {
                var module = CreateModuleFromConfig(moduleConfig);
                if (module is not null)
                {
                    module.IsEnabled = moduleConfig.IsEnabled;
                    pipeline.RegisterModule(module);
                    restoredCount++;
                }
            }
            catch
            {
                // Skip modules that fail to create
            }
        }
        
        return restoredCount;
    }

    /// <summary>
    /// Creates a module instance from configuration.
    /// </summary>
    private static IPhysicsModule? CreateModuleFromConfig(PluginModuleConfig config)
    {
        Type? moduleType = null;
        
        // Try to load from external assembly first
        if (!string.IsNullOrEmpty(config.AssemblyPath) && File.Exists(config.AssemblyPath))
        {
            try
            {
                var assembly = Assembly.LoadFrom(config.AssemblyPath);
                moduleType = assembly.GetType(config.TypeName);
            }
            catch
            {
                // Fall back to searching loaded assemblies
            }
        }
        
        // Search in already loaded assemblies
        if (moduleType is null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                moduleType = assembly.GetType(config.TypeName);
                if (moduleType is not null)
                    break;
            }
        }
        
        // Try partial name match (for built-in modules)
        if (moduleType is null)
        {
            string shortName = config.TypeName.Split('.').Last();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    moduleType = assembly.GetTypes()
                        .FirstOrDefault(t => t.Name == shortName && 
                                            typeof(IPhysicsModule).IsAssignableFrom(t) &&
                                            !t.IsAbstract && !t.IsInterface);
                    if (moduleType is not null)
                        break;
                }
                catch
                {
                    // Skip assemblies that fail to enumerate types
                }
            }
        }
        
        if (moduleType is null)
            return null;

        // 1. Try parameterless constructor first
        var parameterlessCtor = moduleType.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor is not null)
        {
            var instance = parameterlessCtor.Invoke(null) as IPhysicsModule;
            if (instance is not null)
            {
                ApplyStoredProperties(instance, config.Parameters);
            }
            return instance;
        }

        // 2. Try constructors with parameters from config.Parameters
        foreach (var ctor in moduleType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length))
        {
            try
            {
                var ctorParams = ctor.GetParameters();
                var args = new object?[ctorParams.Length];
                bool allResolved = true;

                for (int i = 0; i < ctorParams.Length; i++)
                {
                    var p = ctorParams[i];
                    var match = config.Parameters
                        .FirstOrDefault(kv =>
                            string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase));

                    if (match.Key is not null && match.Value is not null)
                    {
                        args[i] = match.Value is JsonElement je
                            ? ConvertJsonElement(je, p.ParameterType)
                            : Convert.ChangeType(match.Value, p.ParameterType);
                    }
                    else if (p.HasDefaultValue)
                    {
                        args[i] = p.DefaultValue;
                    }
                    else
                    {
                        allResolved = false;
                        break;
                    }
                }

                if (allResolved)
                {
                    var instance = ctor.Invoke(args) as IPhysicsModule;
                    if (instance is not null)
                    {
                        ApplyStoredProperties(instance, config.Parameters);
                    }
                    return instance;
                }
            }
            catch
            {
                // Try next constructor
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the assembly path for external modules, null for built-in.
    /// </summary>
    private static string? GetAssemblyPath(Type moduleType)
    {
        var assembly = moduleType.Assembly;
        
        // Check if it's a built-in module (from RqSimGraphEngine)
        if (assembly.GetName().Name?.StartsWith("RqSimPlatform") == true ||
            assembly.GetName().Name?.StartsWith("RQSimulation") == true)
        {
            return null; // Built-in, no path needed
        }
        
        return assembly.Location;
    }

    /// <summary>
    /// Captures configurable public properties from a module for serialization.
    /// </summary>
    private static Dictionary<string, object?> CaptureModuleProperties(IPhysicsModule module)
    {
        var props = new Dictionary<string, object?>();
        var type = module.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip interface-defined properties
            if (prop.Name is "Name" or "Description" or "IsEnabled" or "Category"
                    or "Priority" or "ExecutionType" or "IsIncludedPlugin")
                continue;

            // Only capture primitive/string types that can be serialized to JSON
            if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string)
                || prop.PropertyType == typeof(decimal))
            {
                try
                {
                    props[prop.Name] = prop.GetValue(module);
                }
                catch
                {
                    // Skip unreadable properties
                }
            }
        }

        return props;
    }

    /// <summary>
    /// Applies stored property values from configuration to a module instance.
    /// </summary>
    private static void ApplyStoredProperties(
        IPhysicsModule module, Dictionary<string, object?> parameters)
    {
        if (parameters.Count == 0) return;

        var type = module.GetType();
        foreach (var (key, value) in parameters)
        {
            if (value is null) continue;

            var prop = type.GetProperty(key,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop is { CanWrite: true })
            {
                try
                {
                    var converted = value is JsonElement je
                        ? ConvertJsonElement(je, prop.PropertyType)
                        : Convert.ChangeType(value, prop.PropertyType);
                    prop.SetValue(module, converted);
                }
                catch
                {
                    // Skip incompatible properties
                }
            }
        }
    }

    /// <summary>
    /// Converts a JsonElement to the specified target type.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        return targetType switch
        {
            _ when targetType == typeof(double) => element.GetDouble(),
            _ when targetType == typeof(float) => element.GetSingle(),
            _ when targetType == typeof(int) => element.GetInt32(),
            _ when targetType == typeof(long) => element.GetInt64(),
            _ when targetType == typeof(bool) => element.GetBoolean(),
            _ when targetType == typeof(string) => element.GetString(),
            _ => Convert.ChangeType(element.ToString(), targetType)
        };
    }

    /// <summary>
    /// Checks if a configuration file exists at the specified path.
    /// </summary>
    public static bool ConfigExists(string? filePath = null)
    {
        filePath ??= DefaultConfigPath;
        return File.Exists(filePath);
    }

    /// <summary>
    /// Deletes the configuration file at the specified path.
    /// </summary>
    public static bool DeleteConfig(string? filePath = null)
    {
        filePath ??= DefaultConfigPath;
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }
        
        return false;
    }
}
