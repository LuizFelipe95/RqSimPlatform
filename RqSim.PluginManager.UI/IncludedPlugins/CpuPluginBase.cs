using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins;

/// <summary>
/// Base class for CPU-based included plugins.
/// Provides common infrastructure for default pipeline modules.
/// </summary>
public abstract class CpuPluginBase : IPhysicsModule
{
    public abstract string Name { get; }
    public virtual string Description => Name;
    public bool IsEnabled { get; set; } = true;
    public virtual ExecutionType ExecutionType => ExecutionType.SynchronousCPU;
    public virtual int Priority => 100;
    public virtual string Category => "CPU";

    /// <summary>
    /// Indicates this is a default/included plugin.
    /// </summary>
    public bool IsIncludedPlugin => true;

    public abstract void Initialize(RQGraph graph);
    public abstract void ExecuteStep(RQGraph graph, double dt);
    public virtual void Cleanup() { }
}
