using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RQSimulation.Core.Configuration;
using RQSimulation.Core.Plugins;

namespace RQSimulation.DependencyInjection;

/// <summary>
/// Extension methods for registering RQSimulation services in the DI container.
///
/// Usage Example:
/// <code>
/// var services = new ServiceCollection();
/// services.AddRQSimulation(configuration);
///
/// // Or with custom configuration
/// services.AddRQSimulation(settings =>
/// {
///     settings.InitialVacuumEnergy = 1.0e6;
///     settings.StrictConservation = true;
/// });
/// </code>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core RQSimulation services to the DI container with default configuration.
    /// Registers: PhysicsPipeline, IPhysicsModuleFactory, EnergyLedger, and SimulationSettings.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddRQSimulation(this IServiceCollection services)
    {
        return services.AddRQSimulation(SimulationSettings.Default);
    }

    /// <summary>
    /// Adds core RQSimulation services to the DI container with configuration from IConfiguration.
    /// Loads settings from the "Simulation" section of the configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration source (e.g., from appsettings.json)</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddRQSimulation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Bind configuration section to settings and register using Options pattern
        var section = configuration.GetSection(SimulationSettings.SectionName);
        services.Configure<SimulationSettings>(section);

        return AddRQSimulationCore(services);
    }

    /// <summary>
    /// Adds core RQSimulation services to the DI container with programmatic configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureSettings">Action to configure simulation settings</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddRQSimulation(
        this IServiceCollection services,
        Action<SimulationSettings> configureSettings)
    {
        if (configureSettings == null)
            throw new ArgumentNullException(nameof(configureSettings));

        // For record type, we need to use the "with" pattern or create a new instance
        // Since SimulationSettings is immutable, we'll bind via Configure
        services.Configure(configureSettings);

        return AddRQSimulationCore(services);
    }

    /// <summary>
    /// Adds core RQSimulation services to the DI container with a specific settings instance.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="settings">Simulation settings instance</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddRQSimulation(
        this IServiceCollection services,
        SimulationSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        settings.Validate();

        // Register settings as singleton
        services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(settings));

        return AddRQSimulationCore(services);
    }

    /// <summary>
    /// Internal method to register core RQSimulation services.
    /// </summary>
    private static IServiceCollection AddRQSimulationCore(IServiceCollection services)
    {
        // Register module factory as singleton
        services.TryAddSingleton<IPhysicsModuleFactory, PhysicsModuleFactory>();

        // Register PhysicsPipeline as singleton (one pipeline per application)
        // Note: Can be changed to Scoped or Transient based on application needs
        services.TryAddSingleton<PhysicsPipeline>();

        // Register EnergyLedger as transient (one per simulation)
        // Each simulation should have its own energy accounting
        services.TryAddTransient<EnergyLedger>();

        return services;
    }

    /// <summary>
    /// Registers a physics module in the DI container for use with IPhysicsModuleFactory.
    /// The module will be created with constructor injection when requested.
    /// </summary>
    /// <typeparam name="TModule">The physics module type to register</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="lifetime">Service lifetime (default: Transient)</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddPhysicsModule<TModule>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TModule : class, IPhysicsModule
    {
        services.Add(new ServiceDescriptor(typeof(TModule), typeof(TModule), lifetime));
        services.Add(new ServiceDescriptor(typeof(IPhysicsModule), sp => sp.GetRequiredService<TModule>(), lifetime));

        return services;
    }

    /// <summary>
    /// Registers multiple physics modules from the specified assembly.
    /// Scans for types marked with [PhysicsModule] attribute.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="assembly">Assembly to scan for modules</param>
    /// <param name="lifetime">Service lifetime for all discovered modules (default: Transient)</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddPhysicsModulesFromAssembly(
        this IServiceCollection services,
        System.Reflection.Assembly assembly,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(IPhysicsModule).IsAssignableFrom(t)
                     && !t.IsInterface
                     && !t.IsAbstract
                     && t.GetCustomAttributes(typeof(PhysicsModuleAttribute), false).Any());

        foreach (var moduleType in moduleTypes)
        {
            services.Add(new ServiceDescriptor(moduleType, moduleType, lifetime));
            services.Add(new ServiceDescriptor(typeof(IPhysicsModule), sp => sp.GetRequiredService(moduleType), lifetime));
        }

        return services;
    }

    /// <summary>
    /// Configures the maximum CPU parallelism for the PhysicsPipeline.
    /// Call this after AddRQSimulation().
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="maxParallelism">Maximum degree of parallelism (default: processor count)</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection ConfigurePhysicsPipeline(
        this IServiceCollection services,
        int maxParallelism)
    {
        if (maxParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(maxParallelism), "Must be at least 1.");

        services.AddSingleton<Action<PhysicsPipeline>>(pipeline =>
        {
            pipeline.MaxCpuParallelism = maxParallelism;
        });

        return services;
    }
}
