using RQSimulation.Core.Plugins;

namespace RQSimulation.Core.Commands;

/// <summary>
/// Command for initializing a physics module.
/// </summary>
public class InitializeCommand : PhysicsCommandBase
{
    private readonly RQGraph _graph;

    public InitializeCommand(IPhysicsModule module, RQGraph graph)
        : base(module, "Initialize")
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    public override void Execute()
    {
        Module.Initialize(_graph);
    }
}

/// <summary>
/// Command for executing a physics module step.
/// </summary>
public class ExecuteStepCommand : PhysicsCommandBase
{
    private readonly RQGraph _graph;
    private readonly double _dt;
    private readonly Action<IPhysicsModule, RQGraph, double>? _executionStrategy;

    /// <summary>
    /// Creates a command for executing a physics module step.
    /// </summary>
    /// <param name="module">The module to execute</param>
    /// <param name="graph">The graph to update</param>
    /// <param name="dt">Time step</param>
    /// <param name="executionStrategy">Optional custom execution strategy (e.g., for Span-based execution)</param>
    public ExecuteStepCommand(
        IPhysicsModule module,
        RQGraph graph,
        double dt,
        Action<IPhysicsModule, RQGraph, double>? executionStrategy = null)
        : base(module, "ExecuteStep")
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _dt = dt;
        _executionStrategy = executionStrategy;
    }

    public override void Execute()
    {
        if (_executionStrategy != null)
        {
            _executionStrategy(Module, _graph, _dt);
        }
        else
        {
            Module.ExecuteStep(_graph, _dt);
        }
    }
}

/// <summary>
/// Command for cleaning up a physics module.
/// </summary>
public class CleanupCommand : PhysicsCommandBase
{
    public CleanupCommand(IPhysicsModule module)
        : base(module, "Cleanup")
    {
    }

    public override void Execute()
    {
        Module.Cleanup();
    }
}

/// <summary>
/// Composite command that executes multiple commands in sequence.
/// </summary>
public class CompositeCommand : IPhysicsCommand
{
    private readonly List<IPhysicsCommand> _commands = new();
    private readonly IPhysicsModule _primaryModule;

    public CompositeCommand(IPhysicsModule primaryModule, IEnumerable<IPhysicsCommand> commands)
    {
        _primaryModule = primaryModule ?? throw new ArgumentNullException(nameof(primaryModule));
        _commands.AddRange(commands ?? throw new ArgumentNullException(nameof(commands)));
    }

    public IPhysicsModule Module => _primaryModule;

    public string Description => $"Composite[{_commands.Count} commands]";

    public void Execute()
    {
        foreach (var command in _commands)
        {
            command.Execute();
        }
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        foreach (var command in _commands)
        {
            await command.ExecuteAsync(ct).ConfigureAwait(false);
        }
    }

    public void AddCommand(IPhysicsCommand command)
    {
        _commands.Add(command ?? throw new ArgumentNullException(nameof(command)));
    }

    public override string ToString() => $"{Description} [{_primaryModule.Name}]";
}
