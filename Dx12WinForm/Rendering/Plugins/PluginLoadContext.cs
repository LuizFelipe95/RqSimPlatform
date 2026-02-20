using System.Reflection;
using System.Runtime.Loader;

namespace RqSimUI.Rendering.Plugins;

/// <summary>
/// Custom AssemblyLoadContext for isolated loading of render backend plugins.
/// Each backend (Veldrid, DX12) runs in its own context with isolated dependencies.
/// This allows different versions of the same DLL (e.g., Vortice.DXGI v2 vs v3) to coexist.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve from plugin's dependencies first
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        
        if (assemblyPath is not null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Fall back to default context for shared assemblies (like Abstractions)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        
        if (libraryPath is not null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
