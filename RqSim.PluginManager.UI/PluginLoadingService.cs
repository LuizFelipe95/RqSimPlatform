using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI
{
    /// <summary>
    /// Service for discovering and loading physics modules from external DLLs.
    /// 
    /// Features:
    /// - Scan directories for plugin DLLs
    /// - Validate module compatibility
    /// - Load and instantiate modules
    /// - Cache discovered modules
    /// </summary>
    public class PluginLoadingService
    {
        private readonly List<PluginInfo> _discoveredPlugins = [];
        private readonly HashSet<string> _loadedAssemblies = [];

        /// <summary>
        /// Gets all discovered plugins.
        /// </summary>
        public IReadOnlyList<PluginInfo> DiscoveredPlugins => _discoveredPlugins;

        /// <summary>
        /// Event raised when a plugin is loaded.
        /// </summary>
        public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

        /// <summary>
        /// Event raised when a plugin fails to load.
        /// </summary>
        public event EventHandler<PluginErrorEventArgs>? PluginError;

        /// <summary>
        /// Scans a directory for plugin DLLs containing IPhysicsModule implementations.
        /// </summary>
        /// <param name="directory">Directory to scan</param>
        /// <param name="recursive">Whether to scan subdirectories</param>
        /// <returns>Number of modules found</returns>
        public int ScanDirectory(string directory, bool recursive = false)
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            int foundCount = 0;
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var dllPath in Directory.EnumerateFiles(directory, "*.dll", searchOption))
            {
                try
                {
                    var modules = DiscoverModulesInAssembly(dllPath);
                    foundCount += modules.Count;
                }
                catch (Exception ex)
                {
                    PluginError?.Invoke(this, new PluginErrorEventArgs(dllPath, ex));
                }
            }

            return foundCount;
        }

        /// <summary>
        /// Discovers IPhysicsModule implementations in an assembly.
        /// </summary>
        public IReadOnlyList<PluginInfo> DiscoverModulesInAssembly(string assemblyPath)
        {
            var discovered = new List<PluginInfo>();

            if (_loadedAssemblies.Contains(assemblyPath))
            {
                return discovered;
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(assemblyPath);
            }
            catch (BadImageFormatException)
            {
                // Not a valid .NET assembly, skip silently
                return discovered;
            }

            _loadedAssemblies.Add(assemblyPath);

            var moduleTypes = assembly.GetTypes()
                .Where(IsValidModuleType)
                .ToList();

            foreach (var type in moduleTypes)
            {
                var info = CreatePluginInfo(type, assemblyPath);
                if (info is not null)
                {
                    _discoveredPlugins.Add(info);
                    discovered.Add(info);
                    PluginLoaded?.Invoke(this, new PluginLoadedEventArgs(info));
                }
            }

            return discovered;
        }

        /// <summary>
        /// Creates an instance of a module from plugin info.
        /// </summary>
        public IPhysicsModule? CreateModuleInstance(PluginInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);

            try
            {
                return (IPhysicsModule?)Activator.CreateInstance(info.ModuleType);
            }
            catch (Exception ex)
            {
                PluginError?.Invoke(this, new PluginErrorEventArgs(info.AssemblyPath, ex));
                return null;
            }
        }

        /// <summary>
        /// Creates module instances for all modules in a plugin info list.
        /// </summary>
        public IReadOnlyList<IPhysicsModule> CreateModuleInstances(IEnumerable<PluginInfo> infos)
        {
            var modules = new List<IPhysicsModule>();
            foreach (var info in infos)
            {
                var module = CreateModuleInstance(info);
                if (module is not null)
                {
                    modules.Add(module);
                }
            }
            return modules;
        }

        /// <summary>
        /// Loads all modules from a DLL and adds them to a pipeline.
        /// </summary>
        /// <param name="dllPath">Path to the DLL</param>
        /// <param name="pipeline">Pipeline to add modules to</param>
        /// <returns>Number of modules added</returns>
        public int LoadModulesToPipeline(string dllPath, PhysicsPipeline pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);

            var infos = DiscoverModulesInAssembly(dllPath);
            int count = 0;

            foreach (var info in infos)
            {
                var module = CreateModuleInstance(info);
                if (module is not null)
                {
                    pipeline.RegisterModule(module);
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Clears the discovered plugins cache.
        /// </summary>
        public void ClearCache()
        {
            _discoveredPlugins.Clear();
            _loadedAssemblies.Clear();
        }

        /// <summary>
        /// Gets plugins filtered by category.
        /// </summary>
        public IEnumerable<PluginInfo> GetPluginsByCategory(string category)
            => _discoveredPlugins.Where(p =>
                p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Gets plugins filtered by execution type.
        /// </summary>
        public IEnumerable<PluginInfo> GetPluginsByExecutionType(ExecutionType executionType)
            => _discoveredPlugins.Where(p => p.ExecutionType == executionType);

        private static bool IsValidModuleType(Type type)
        {
            return typeof(IPhysicsModule).IsAssignableFrom(type)
                && !type.IsInterface
                && !type.IsAbstract
                && type.GetConstructor(Type.EmptyTypes) is not null;
        }

        private static PluginInfo? CreatePluginInfo(Type moduleType, string assemblyPath)
        {
            try
            {
                // Create a temporary instance to read metadata
                if (Activator.CreateInstance(moduleType) is not IPhysicsModule instance)
                {
                    return null;
                }

                return new PluginInfo
                {
                    ModuleType = moduleType,
                    AssemblyPath = assemblyPath,
                    Name = instance.Name,
                    Description = instance.Description,
                    Category = instance.Category,
                    ExecutionType = instance.ExecutionType,
                    Priority = instance.Priority
                };
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Information about a discovered plugin module.
    /// </summary>
    public class PluginInfo
    {
        public required Type ModuleType { get; init; }
        public required string AssemblyPath { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string Category { get; init; }
        public required ExecutionType ExecutionType { get; init; }
        public required int Priority { get; init; }

        public string AssemblyName => Path.GetFileName(AssemblyPath);

        public override string ToString() => $"{Name} [{Category}] from {AssemblyName}";
    }

    /// <summary>
    /// Event args for successful plugin loading.
    /// </summary>
    public class PluginLoadedEventArgs : EventArgs
    {
        public PluginInfo PluginInfo { get; }

        public PluginLoadedEventArgs(PluginInfo info)
        {
            PluginInfo = info;
        }
    }

    /// <summary>
    /// Event args for plugin loading errors.
    /// </summary>
    public class PluginErrorEventArgs : EventArgs
    {
        public string AssemblyPath { get; }
        public Exception Exception { get; }

        public PluginErrorEventArgs(string assemblyPath, Exception exception)
        {
            AssemblyPath = assemblyPath;
            Exception = exception;
        }
    }
}
