using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace RQSimulation.Core.Plugins;

/// <summary>
/// Default implementation of IPhysicsModuleFactory that supports both:
/// - Dependency injection via IServiceProvider (for modules with dependencies)
/// - Direct instantiation via Activator.CreateInstance (for simple modules, backward compatibility)
///
/// This factory discovers modules marked with [PhysicsModule] attribute and maintains
/// a registry of available module types for dynamic creation.
/// </summary>
public class PhysicsModuleFactory : IPhysicsModuleFactory
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly Dictionary<string, Type> _moduleRegistry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a factory without DI support (uses Activator.CreateInstance only).
    /// For backward compatibility with existing code.
    /// </summary>
    public PhysicsModuleFactory()
    {
        _serviceProvider = null;
        DiscoverModules();
    }

    /// <summary>
    /// Creates a factory with DI support via IServiceProvider.
    /// Modules will be created using the DI container when possible.
    /// </summary>
    /// <param name="serviceProvider">DI service provider for creating modules with dependencies</param>
    public PhysicsModuleFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        DiscoverModules();
    }

    /// <summary>
    /// Discovers all physics modules in loaded assemblies.
    /// Scans for types implementing IPhysicsModule and marked with [PhysicsModule] attribute.
    /// </summary>
    private void DiscoverModules()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            try
            {
                var moduleTypes = assembly.GetTypes()
                    .Where(t => typeof(IPhysicsModule).IsAssignableFrom(t)
                             && !t.IsInterface
                             && !t.IsAbstract);

                foreach (var type in moduleTypes)
                {
                    // Check for PhysicsModuleAttribute
                    var attribute = type.GetCustomAttribute<PhysicsModuleAttribute>();
                    if (attribute != null)
                    {
                        // Use attribute name for registration
                        _moduleRegistry[attribute.Name] = type;
                    }
                    else
                    {
                        // Fallback: try to instantiate temporarily to get the name
                        // This maintains backward compatibility with modules not using the attribute
                        try
                        {
                            var instance = Activator.CreateInstance(type) as IPhysicsModule;
                            if (instance != null)
                            {
                                _moduleRegistry[instance.Name] = type;
                            }
                        }
                        catch
                        {
                            // Skip modules that can't be instantiated without parameters
                            continue;
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Skip assemblies that can't be scanned
                continue;
            }
        }
    }

    /// <summary>
    /// Creates a physics module instance by type using DI or Activator.
    /// </summary>
    public T Create<T>() where T : IPhysicsModule
    {
        return (T)Create(typeof(T));
    }

    /// <summary>
    /// Creates a physics module instance by name.
    /// </summary>
    public IPhysicsModule? Create(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name cannot be null or whitespace.", nameof(moduleName));

        if (!_moduleRegistry.TryGetValue(moduleName, out var moduleType))
            return null;

        return Create(moduleType);
    }

    /// <summary>
    /// Creates a physics module instance by type.
    /// </summary>
    public IPhysicsModule Create(Type moduleType)
    {
        if (moduleType == null)
            throw new ArgumentNullException(nameof(moduleType));

        if (!typeof(IPhysicsModule).IsAssignableFrom(moduleType))
            throw new ArgumentException($"Type {moduleType.Name} does not implement IPhysicsModule.", nameof(moduleType));

        if (moduleType.IsInterface || moduleType.IsAbstract)
            throw new ArgumentException($"Cannot create instance of interface or abstract type {moduleType.Name}.", nameof(moduleType));

        // Try DI container first if available
        if (_serviceProvider != null)
        {
            try
            {
                var instance = ActivatorUtilities.CreateInstance(_serviceProvider, moduleType);
                return (IPhysicsModule)instance;
            }
            catch (InvalidOperationException)
            {
                // Fall through to Activator if DI fails (e.g., module not registered)
            }
        }

        // Fallback to Activator.CreateInstance for backward compatibility
        try
        {
            var instance = Activator.CreateInstance(moduleType);
            if (instance == null)
                throw new InvalidOperationException($"Failed to create instance of {moduleType.Name}.");

            return (IPhysicsModule)instance;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create module of type {moduleType.Name}. " +
                $"Ensure the type has a public parameterless constructor or is registered in the DI container.",
                ex);
        }
    }

    /// <summary>
    /// Gets all available module types discovered by the factory.
    /// </summary>
    public IEnumerable<Type> GetAvailableModuleTypes()
    {
        return _moduleRegistry.Values.Distinct();
    }

    /// <summary>
    /// Checks if a module with the specified name is available.
    /// </summary>
    public bool IsModuleAvailable(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return false;

        return _moduleRegistry.ContainsKey(moduleName);
    }

    /// <summary>
    /// Gets all registered module names.
    /// </summary>
    public IEnumerable<string> GetAvailableModuleNames()
    {
        return _moduleRegistry.Keys;
    }
}
