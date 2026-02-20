namespace RQSimulation.Core.Plugins;

/// <summary>
/// Factory interface for creating physics module instances with dependency injection support.
///
/// This abstraction enables:
/// - Constructor injection of dependencies into modules
/// - Centralized module creation logic
/// - Easy testing with mock implementations
/// - Lazy module instantiation
///
/// Implementation:
/// The factory can use IServiceProvider (DI container) or Activator.CreateInstance (legacy)
/// based on configuration and available dependencies.
/// </summary>
public interface IPhysicsModuleFactory
{
    /// <summary>
    /// Creates a physics module instance by type.
    /// </summary>
    /// <typeparam name="T">The module type to create</typeparam>
    /// <returns>A new instance of the specified module type</returns>
    /// <exception cref="InvalidOperationException">If the module cannot be created</exception>
    T Create<T>() where T : IPhysicsModule;

    /// <summary>
    /// Creates a physics module instance by module name.
    /// The name should match the Name property or PhysicsModuleAttribute.Name.
    /// </summary>
    /// <param name="moduleName">The name of the module to create</param>
    /// <returns>A new instance of the module, or null if not found</returns>
    IPhysicsModule? Create(string moduleName);

    /// <summary>
    /// Creates a physics module instance by type.
    /// </summary>
    /// <param name="moduleType">The type of module to create</param>
    /// <returns>A new instance of the module</returns>
    /// <exception cref="ArgumentException">If the type does not implement IPhysicsModule</exception>
    /// <exception cref="InvalidOperationException">If the module cannot be created</exception>
    IPhysicsModule Create(Type moduleType);

    /// <summary>
    /// Gets all available module types that can be created by this factory.
    /// </summary>
    /// <returns>Collection of module types</returns>
    IEnumerable<Type> GetAvailableModuleTypes();

    /// <summary>
    /// Checks if a module with the specified name is available.
    /// </summary>
    /// <param name="moduleName">The name to check</param>
    /// <returns>True if a module with this name can be created, false otherwise</returns>
    bool IsModuleAvailable(string moduleName);
}
