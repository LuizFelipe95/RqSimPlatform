using RQSimulation.Core.Plugins;
using RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;
using RqSimPlatform.PluginManager.UI.IncludedPlugins.GPUOptimizedCSR;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins;

/// <summary>
/// Registry for all included/default plugins.
/// Provides methods to register default plugins into the PhysX pipeline.
/// 
/// PLUGIN CATEGORIES:
/// ==================
/// - CPU: Basic physics modules running on CPU
/// - GPU: Compute shader-based modules (CSR topology, double precision)
/// - Topology: Wilson loops, link activation, gauge protection
/// - Gauge: Gauss law monitoring, constraint enforcement
/// - Geometry: Curvature, anisotropy calculations
/// - Rendering: Physics-to-vertex mapping
/// </summary>
public static class IncludedPluginsRegistry
{
    /// <summary>
    /// Gets all available CPU plugin types.
    /// </summary>
    public static IReadOnlyList<Type> CpuPluginTypes { get; } =
    [
        typeof(EnergyLedgerCpuModule),
        typeof(HamiltonianCpuModule),
        typeof(ComplexEdgeCpuModule),
        typeof(GaugeAwareTopologyCpuModule),
        typeof(InternalObserverCpuModule),
        typeof(MCMCSamplerCpuModule),
        typeof(OllivierRicciCpuModule),
    ];

    /// <summary>
    /// Gets all available GPU plugin types.
    /// </summary>
    public static IReadOnlyList<Type> GpuPluginTypes { get; } =
    [
        // Physics/Gravity
        typeof(GravityShaderFormanModule),
        typeof(GpuMCMCEngineModule),

        // Curvature
        typeof(SinkhornOllivierRicciGpuModule),

        // Geometry (NEW)
        typeof(EdgeAnisotropyGpuModule),
        
        // Topology
        typeof(TopologicalInvariantsGpuModule),
        typeof(PotentialLinkActivatorGpuModule),
        
        // Gauge
        typeof(GaussLawMonitorGpuModule),
        
        // Rendering
        typeof(RenderDataMapperGpuModule),
    ];

    /// <summary>
    /// Gets all available included plugin types.
    /// </summary>
    public static IReadOnlyList<Type> AllPluginTypes { get; } =
    [
        // CPU plugins (in priority order)
        typeof(EnergyLedgerCpuModule),
        typeof(HamiltonianCpuModule),
        typeof(ComplexEdgeCpuModule),
        typeof(GaugeAwareTopologyCpuModule),
        typeof(InternalObserverCpuModule),
        typeof(MCMCSamplerCpuModule),
        typeof(OllivierRicciCpuModule),

        // GPU plugins - Physics
        typeof(GravityShaderFormanModule),
        typeof(GpuMCMCEngineModule),

        // GPU plugins - Curvature
        typeof(SinkhornOllivierRicciGpuModule),

        // GPU plugins - Geometry (NEW)
        typeof(EdgeAnisotropyGpuModule),
        
        // GPU plugins - Topology
        typeof(TopologicalInvariantsGpuModule),
        typeof(PotentialLinkActivatorGpuModule),
        
        // GPU plugins - Gauge
        typeof(GaussLawMonitorGpuModule),
        
        // GPU plugins - Rendering
        typeof(RenderDataMapperGpuModule),
    ];
    
    /// <summary>
    /// Gets topology-related GPU plugin types for atomic group execution.
    /// </summary>
    public static IReadOnlyList<Type> TopologyPluginTypes { get; } =
    [
        typeof(TopologicalInvariantsGpuModule),
        typeof(PotentialLinkActivatorGpuModule),
    ];
    
    /// <summary>
    /// Gets geometry-related GPU plugin types (curvature, anisotropy).
    /// </summary>
    public static IReadOnlyList<Type> GeometryPluginTypes { get; } =
    [
        typeof(GravityShaderFormanModule),
        typeof(SinkhornOllivierRicciGpuModule),
        typeof(EdgeAnisotropyGpuModule),
    ];
    
    /// <summary>
    /// Gets gauge constraint GPU plugin types.
    /// </summary>
    public static IReadOnlyList<Type> GaugePluginTypes { get; } =
    [
        typeof(GaussLawMonitorGpuModule),
    ];
    
    /// <summary>
    /// Gets rendering/visualization GPU plugin types.
    /// </summary>
    public static IReadOnlyList<Type> RenderingPluginTypes { get; } =
    [
        typeof(RenderDataMapperGpuModule),
    ];

    /// <summary>
    /// Creates instances of all CPU plugins.
    /// </summary>
    public static IReadOnlyList<IPhysicsModule> CreateCpuPlugins()
    {
        return CpuPluginTypes
            .Select(CreatePlugin)
            .Where(p => p is not null)
            .Cast<IPhysicsModule>()
            .ToList();
    }

    /// <summary>
    /// Creates instances of all GPU plugins.
    /// </summary>
    public static IReadOnlyList<IPhysicsModule> CreateGpuPlugins()
    {
        return GpuPluginTypes
            .Select(CreatePlugin)
            .Where(p => p is not null)
            .Cast<IPhysicsModule>()
            .ToList();
    }

    /// <summary>
    /// Creates instances of all included plugins.
    /// </summary>
    public static IReadOnlyList<IPhysicsModule> CreateAllPlugins()
    {
        return AllPluginTypes
            .Select(CreatePlugin)
            .Where(p => p is not null)
            .Cast<IPhysicsModule>()
            .ToList();
    }

    /// <summary>
    /// Registers all default CPU plugins into the pipeline.
    /// </summary>
    public static void RegisterDefaultCpuPlugins(PhysicsPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        foreach (IPhysicsModule plugin in CreateCpuPlugins())
        {
            pipeline.RegisterModule(plugin);
        }
    }

    /// <summary>
    /// Registers all default GPU plugins into the pipeline.
    /// </summary>
    public static void RegisterDefaultGpuPlugins(PhysicsPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        foreach (IPhysicsModule plugin in CreateGpuPlugins())
        {
            pipeline.RegisterModule(plugin);
        }
    }

    /// <summary>
    /// Registers all default plugins into the pipeline.
    /// </summary>
    public static void RegisterAllDefaultPlugins(PhysicsPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        foreach (IPhysicsModule plugin in CreateAllPlugins())
        {
            pipeline.RegisterModule(plugin);
        }
    }

    /// <summary>
    /// Replaces existing modules of the same category with default plugins.
    /// This allows swapping out loaded external plugins with built-in versions.
    /// </summary>
    public static void ReplaceWithDefaultPlugins(PhysicsPipeline pipeline, bool cpuOnly = false)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var pluginsToAdd = cpuOnly ? CreateCpuPlugins() : CreateAllPlugins();

        foreach (IPhysicsModule plugin in pluginsToAdd)
        {
            // Find and remove existing modules with same name or category
            var toRemove = pipeline.Modules
                .Where(m => m.Name == plugin.Name || 
                           (m.Category == plugin.Category && m.GetType().Name.Contains(plugin.GetType().Name.Replace("Module", ""))))
                .ToList();

            foreach (var existing in toRemove)
            {
                pipeline.RemoveModule(existing);
            }

            // Add the default plugin
            pipeline.RegisterModule(plugin);
        }
    }

    /// <summary>
    /// Gets plugin info for UI display.
    /// </summary>
    public static IReadOnlyList<PluginInfo> GetPluginInfoList()
    {
        var list = new List<PluginInfo>();

        foreach (Type type in AllPluginTypes)
        {
            IPhysicsModule? plugin = CreatePlugin(type);
            if (plugin is not null)
            {
                list.Add(new PluginInfo
                {
                    Type = type,
                    Name = plugin.Name,
                    Description = plugin.Description,
                    Category = plugin.Category,
                    ExecutionType = plugin.ExecutionType,
                    Priority = plugin.Priority,
                    IsGpu = plugin.ExecutionType == ExecutionType.GPU
                });

                // Dispose if disposable
                (plugin as IDisposable)?.Dispose();
            }
        }

        return list;
    }

    private static IPhysicsModule? CreatePlugin(Type type)
    {
        try
        {
            return Activator.CreateInstance(type) as IPhysicsModule;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Information about an included plugin for UI display.
/// </summary>
public record PluginInfo
{
    public required Type Type { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required ExecutionType ExecutionType { get; init; }
    public required int Priority { get; init; }
    public required bool IsGpu { get; init; }
}
