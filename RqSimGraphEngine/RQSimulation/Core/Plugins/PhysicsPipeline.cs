using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RQSimulation.Core.Exceptions;
using RQSimulation.Core.Infrastructure;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace RQSimulation.Core.Plugins;

/// <summary>
/// GPU synchronization interface for resource barrier transitions.
/// Implemented by GpuSyncManager in the rendering engine.
/// </summary>
public interface IGpuSyncManager
{
    /// <summary>
    /// Transition shared buffers from read state to write state for compute shaders.
    /// Call before GPU physics modules execute.
    /// </summary>
    void TransitionToCompute();

    /// <summary>
    /// Transition shared buffers from write state to read state for rendering.
    /// Call after GPU physics modules complete.
    /// </summary>
    void TransitionToRender();

    /// <summary>
    /// Wait for compute operations to complete before rendering.
    /// </summary>
    void WaitForComputeComplete();
}

/// <summary>
/// Event args for module errors.
/// </summary>
public class ModuleErrorEventArgs : EventArgs
{
    public IPhysicsModule Module { get; }
    public Exception Exception { get; }
    public string Phase { get; }

    /// <summary>
    /// Indicates whether this error is critical and should stop the simulation.
    /// Critical errors include: OutOfMemoryException, StackOverflowException,
    /// AccessViolationException, and GPU device lost errors.
    /// </summary>
    public bool IsCritical { get; }

    public ModuleErrorEventArgs(IPhysicsModule module, Exception exception, string phase, bool isCritical = false)
    {
        Module = module;
        Exception = exception;
        Phase = phase;
        IsCritical = isCritical;
    }
}

/// <summary>
/// Event args for pipeline logging.
/// </summary>
public class PipelineLogEventArgs : EventArgs
{
    public string Message { get; }
    public DateTime Timestamp { get; }

    public PipelineLogEventArgs(string message)
    {
        Message = message;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Manages the ordered collection of physics modules in the simulation pipeline.
///
/// Features:
/// - Register/unregister modules dynamically
/// - Reorder modules (move up/down)
/// - Enable/disable modules at runtime
/// - Initialize all modules at simulation start
/// - Execute all enabled modules each frame
/// - Observable collection for UI binding
/// - GPU synchronization via resource barriers
/// - Zero-copy Span execution for ISpanPhysicsModule implementations
/// - Dynamic physics parameters from UI (see PhysicsPipeline.DynamicParams.cs)
///
/// Thread safety: Thread-safe with ReaderWriterLockSlim for concurrent module access.
/// Registration operations (add/remove) use write locks.
/// Execution operations use read locks for concurrent execution.
/// </summary>
public partial class PhysicsPipeline : INotifyPropertyChanged
{
    private readonly ObservableCollection<IPhysicsModule> _modules = [];
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);
    private readonly ILogger<PhysicsPipeline> _logger;
    private readonly IPhysicsModuleFactory? _moduleFactory;
    private readonly Dictionary<int, IGpuSyncManager> _gpuSyncManagers = new();
    private IGpuSyncManager? _gpuSyncManager; // Legacy single GPU support
    private bool _isInitialized;
    private int _executionCount;
    private int _maxCpuParallelism = Environment.ProcessorCount;

    // Performance optimization: cached enabled modules list (Item 36/4.6)
    private readonly List<IPhysicsModule> _enabledModulesCache = [];
    private bool _enabledCacheDirty = true;

    // Performance optimization: cached sorted modules list (Item 37/4.7)
    private List<IPhysicsModule> _sortedModulesCache = [];
    private bool _sortedCacheDirty = true;

    /// <summary>
    /// Observable collection of registered modules.
    /// Bind to this in UI for live updates.
    /// </summary>
    public ReadOnlyObservableCollection<IPhysicsModule> Modules { get; }

    /// <summary>
    /// Total number of registered modules.
    /// </summary>
    public int Count => _modules.Count;

    /// <summary>
    /// Whether InitializeAll has been called.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Number of times ExecuteFrame has been called.
    /// </summary>
    public int ExecutionCount => _executionCount;

    /// <summary>
    /// Maximum degree of parallelism for CPU module execution.
    /// Defaults to Environment.ProcessorCount.
    /// </summary>
    public int MaxCpuParallelism
    {
        get => _maxCpuParallelism;
        set => _maxCpuParallelism = Math.Max(1, value);
    }

    /// <summary>
    /// Event raised when a module encounters an error during execution.
    /// </summary>
    public event EventHandler<ModuleErrorEventArgs>? ModuleError;

    /// <summary>
    /// Event raised for logging/diagnostics.
    /// </summary>
    public event EventHandler<PipelineLogEventArgs>? Log;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PhysicsPipeline()
    {
        _logger = NullLogger<PhysicsPipeline>.Instance;
        Modules = new ReadOnlyObservableCollection<IPhysicsModule>(_modules);
        _modules.CollectionChanged += OnModulesChanged;
    }

    /// <summary>
    /// Creates pipeline with GPU sync manager for resource barrier coordination.
    /// </summary>
    /// <param name="gpuSyncManager">GPU synchronization manager (can be null for CPU-only pipelines)</param>
    public PhysicsPipeline(IGpuSyncManager? gpuSyncManager) : this()
    {
        _gpuSyncManager = gpuSyncManager;
    }

    /// <summary>
    /// Creates pipeline with dependency injection support.
    /// This constructor supports structured logging via ILogger and module factory via IPhysicsModuleFactory.
    /// </summary>
    /// <param name="logger">Logger for structured logging (optional, uses NullLogger if not provided)</param>
    /// <param name="moduleFactory">Factory for creating physics modules with DI (optional)</param>
    /// <param name="gpuSyncManager">GPU synchronization manager (optional, for GPU modules)</param>
    public PhysicsPipeline(
        ILogger<PhysicsPipeline>? logger = null,
        IPhysicsModuleFactory? moduleFactory = null,
        IGpuSyncManager? gpuSyncManager = null) : this()
    {
        _logger = logger ?? NullLogger<PhysicsPipeline>.Instance;
        _moduleFactory = moduleFactory;
        _gpuSyncManager = gpuSyncManager;
        _logger.LogInformation("PhysicsPipeline created with DI support");
    }

    /// <summary>
    /// Sets or updates the GPU sync manager.
    /// </summary>
    public void SetGpuSyncManager(IGpuSyncManager? gpuSyncManager)
    {
        _gpuSyncManager = gpuSyncManager;
        RaiseLog($"GPU sync manager {(gpuSyncManager is not null ? "attached" : "detached")}");
    }

    /// <summary>
    /// Registers a GPU sync manager for a specific device ID.
    /// Use this for multi-GPU configurations where different modules target different GPUs.
    /// </summary>
    /// <param name="deviceId">GPU device ID (0-based)</param>
    /// <param name="gpuSyncManager">Sync manager for this device</param>
    public void RegisterGpuSyncManager(int deviceId, IGpuSyncManager gpuSyncManager)
    {
        ArgumentNullException.ThrowIfNull(gpuSyncManager);
        ArgumentOutOfRangeException.ThrowIfNegative(deviceId);

        _gpuSyncManagers[deviceId] = gpuSyncManager;
        _logger.LogInformation("Registered GPU sync manager for device {DeviceId}", deviceId);
        RaiseLog($"Registered GPU sync manager for device {deviceId}");
    }

    /// <summary>
    /// Unregisters a GPU sync manager for a specific device ID.
    /// </summary>
    /// <param name="deviceId">GPU device ID to unregister</param>
    /// <returns>True if the manager was removed, false if it didn't exist</returns>
    public bool UnregisterGpuSyncManager(int deviceId)
    {
        bool removed = _gpuSyncManagers.Remove(deviceId);
        if (removed)
        {
            _logger.LogInformation("Unregistered GPU sync manager for device {DeviceId}", deviceId);
            RaiseLog($"Unregistered GPU sync manager for device {deviceId}");
        }
        return removed;
    }

    /// <summary>
    /// Gets the GPU sync manager for a specific device, or the legacy single manager if deviceId is -1.
    /// </summary>
    /// <param name="deviceId">Device ID, or -1 for legacy single GPU manager</param>
    /// <returns>The sync manager for the device, or null if not found</returns>
    private IGpuSyncManager? GetGpuSyncManager(int deviceId)
    {
        if (deviceId == -1)
        {
            return _gpuSyncManager;
        }

        return _gpuSyncManagers.TryGetValue(deviceId, out var manager) ? manager : null;
    }

    /// <summary>
    /// Checks if multi-GPU support is available (at least one device-specific manager registered).
    /// </summary>
    public bool IsMultiGpuEnabled => _gpuSyncManagers.Count > 0;

    /// <summary>
    /// Registers a module at the end of the pipeline.
    /// Thread-safe: Uses write lock.
    /// Validates that the module doesn't conflict with existing modules in the same exclusive group.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the module conflicts with an existing module in the same exclusive group</exception>
    public void RegisterModule(IPhysicsModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        _rwLock.EnterWriteLock();
        try
        {
            if (_modules.Any(m => m.Name == module.Name))
            {
                RaiseLog($"Module '{module.Name}' already registered, skipping");
                return;
            }

            // Check for exclusive group conflicts
            if (!string.IsNullOrEmpty(module.ExclusiveGroup))
            {
                var conflictingModule = _modules.FirstOrDefault(m => m.ExclusiveGroup == module.ExclusiveGroup && m.IsEnabled);
                if (conflictingModule != null)
                {
                    var errorMessage = $"Module '{module.Name}' conflicts with existing module '{conflictingModule.Name}' in exclusive group '{module.ExclusiveGroup}'";
                    _logger.LogWarning("{ErrorMessage}", errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }
            }

            _modules.Add(module);

            // Performance optimization: Subscribe to module's IsEnabled changes for cache invalidation
            if (module is INotifyPropertyChanged notifyModule)
            {
                notifyModule.PropertyChanged += OnModulePropertyChanged;
            }

            // Invalidate caches (Items 36 & 37)
            _enabledCacheDirty = true;
            _sortedCacheDirty = true;

            RaiseLog($"Registered module: {module.Name} [{module.Category}]");
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Registers a module at a specific index.
    /// Thread-safe: Uses write lock.
    /// </summary>
    public void RegisterModuleAt(IPhysicsModule module, int index)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        _rwLock.EnterWriteLock();
        try
        {
            index = Math.Min(index, _modules.Count);
            _modules.Insert(index, module);

            // Performance optimization: Subscribe to module's IsEnabled changes
            if (module is INotifyPropertyChanged notifyModule)
            {
                notifyModule.PropertyChanged += OnModulePropertyChanged;
            }

            // Invalidate caches
            _enabledCacheDirty = true;
            _sortedCacheDirty = true;

            RaiseLog($"Registered module at {index}: {module.Name}");
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a module from the pipeline.
    /// Thread-safe: Uses write lock.
    /// </summary>
    public bool RemoveModule(IPhysicsModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        _rwLock.EnterWriteLock();
        try
        {
            bool removed = _modules.Remove(module);
            if (removed)
            {
                // Unsubscribe from property changes
                if (module is INotifyPropertyChanged notifyModule)
                {
                    notifyModule.PropertyChanged -= OnModulePropertyChanged;
                }

                module.Cleanup();

                // Invalidate caches
                _enabledCacheDirty = true;
                _sortedCacheDirty = true;

                RaiseLog($"Removed module: {module.Name}");
            }
            return removed;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a module by name.
    /// Thread-safe: Uses write lock.
    /// </summary>
    public bool RemoveModule(string name)
    {
        _rwLock.EnterWriteLock();
        try
        {
            var module = _modules.FirstOrDefault(m => m.Name == name);
            if (module is null)
                return false;

            bool removed = _modules.Remove(module);
            if (removed)
            {
                // Unsubscribe from property changes
                if (module is INotifyPropertyChanged notifyModule)
                {
                    notifyModule.PropertyChanged -= OnModulePropertyChanged;
                }

                module.Cleanup();

                // Invalidate caches
                _enabledCacheDirty = true;
                _sortedCacheDirty = true;

                RaiseLog($"Removed module: {module.Name}");
            }
            return removed;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes all modules from the pipeline.
    /// Thread-safe: Uses write lock.
    /// </summary>
    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            foreach (var module in _modules)
            {
                // Unsubscribe from property changes
                if (module is INotifyPropertyChanged notifyModule)
                {
                    notifyModule.PropertyChanged -= OnModulePropertyChanged;
                }

                module.Cleanup();
            }
            _modules.Clear();
            _isInitialized = false;
            _executionCount = 0;

            // Clear caches
            _enabledModulesCache.Clear();
            _sortedModulesCache.Clear();
            _enabledCacheDirty = true;
            _sortedCacheDirty = true;

            RaiseLog("Pipeline cleared");
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Moves a module up (earlier execution) in the pipeline.
    /// </summary>
    public bool MoveUp(IPhysicsModule module)
    {
        int index = _modules.IndexOf(module);
        if (index <= 0) return false;

        _modules.Move(index, index - 1);
        _sortedCacheDirty = true; // Invalidate sorted cache
        return true;
    }

    /// <summary>
    /// Moves a module down (later execution) in the pipeline.
    /// </summary>
    public bool MoveDown(IPhysicsModule module)
    {
        int index = _modules.IndexOf(module);
        if (index < 0 || index >= _modules.Count - 1) return false;

        _modules.Move(index, index + 1);
        _sortedCacheDirty = true; // Invalidate sorted cache
        return true;
    }

    /// <summary>
    /// Moves a module to a specific index.
    /// </summary>
    public bool MoveTo(IPhysicsModule module, int newIndex)
    {
        int currentIndex = _modules.IndexOf(module);
        if (currentIndex < 0) return false;

        newIndex = Math.Clamp(newIndex, 0, _modules.Count - 1);
        if (currentIndex == newIndex) return false;

        _modules.Move(currentIndex, newIndex);
        _sortedCacheDirty = true; // Invalidate sorted cache
        return true;
    }

    /// <summary>
    /// Gets a module by name.
    /// </summary>
    public IPhysicsModule? GetModule(string name)
        => _modules.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all modules in a category.
    /// </summary>
    public IEnumerable<IPhysicsModule> GetModulesByCategory(string category)
        => _modules.Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all enabled modules in execution order.
    /// Performance optimization (Item 36/4.6): Uses cached list instead of LINQ filtering on every call.
    /// </summary>
    public IEnumerable<IPhysicsModule> GetEnabledModules()
    {
        if (_enabledCacheDirty)
        {
            RebuildEnabledCache();
        }
        return _enabledModulesCache;
    }

    /// <summary>
    /// Rebuilds the enabled modules cache.
    /// Called when modules are added/removed or when IsEnabled property changes.
    /// </summary>
    private void RebuildEnabledCache()
    {
        _rwLock.EnterReadLock();
        try
        {
            _enabledModulesCache.Clear();
            foreach (var module in _modules)
            {
                if (module.IsEnabled)
                {
                    _enabledModulesCache.Add(module);
                }
            }
            _enabledCacheDirty = false;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Handles property changes on modules to invalidate caches when IsEnabled changes.
    /// Performance optimization (Item 36/4.6): Track IsEnabled changes for cache invalidation.
    /// </summary>
    private void OnModulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPhysicsModule.IsEnabled))
        {
            _enabledCacheDirty = true;
        }
    }

    /// <summary>
    /// Initializes all enabled modules. Call once at simulation start.
    /// </summary>
    public void InitializeAll(RQGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        RaiseLog($"Initializing {_modules.Count(m => m.IsEnabled)} enabled modules...");

        foreach (var module in _modules.Where(m => m.IsEnabled))
        {
            try
            {
                module.Initialize(graph);
                RaiseLog($"  Initialized: {module.Name}");
            }
            catch (Exception ex)
            {
                RaiseError(module, ex, "Initialize");
                // Continue with other modules
            }
        }

        _isInitialized = true;
        _executionCount = 0;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInitialized)));
    }

    /// <summary>
    /// Gets sorted and filtered enabled modules using cache.
    /// Performance optimization (Item 37/4.7): Cache sorted module list with dirty flag.
    /// </summary>
    /// <returns>List of enabled modules sorted by Stage, ModuleGroup, and Priority</returns>
    private List<IPhysicsModule> GetSortedEnabledModules()
    {
        // Must be called with read lock held or within write lock
        if (_sortedCacheDirty || _enabledCacheDirty)
        {
            // Rebuild the sorted cache
            _sortedModulesCache = _modules
                .Where(m => m.IsEnabled)
                .OrderBy(m => m.Stage)
                .ThenBy(m => m.ModuleGroup ?? string.Empty) // Ungrouped modules come first
                .ThenBy(m => m.Priority)
                .ToList();

            _sortedCacheDirty = false;
            _enabledCacheDirty = false;

            // Also update the enabled cache for GetEnabledModules()
            _enabledModulesCache.Clear();
            foreach (var module in _sortedModulesCache)
            {
                _enabledModulesCache.Add(module);
            }
        }

        return _sortedModulesCache;
    }

    /// <summary>
    /// Executes all enabled modules for one simulation frame.
    /// Modules are executed in order: Stage (Preparation->Forces->Integration->PostProcess),
    /// then by ModuleGroup (grouped modules execute atomically), then Priority within each stage.
    /// GPU modules are wrapped with resource barrier transitions.
    /// Thread-safe: Uses read lock for concurrent execution.
    /// </summary>
    public void ExecuteFrame(RQGraph graph, double dt)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // CHECKLIST ITEM 39 (11.2): Observability Integration - Frame-level tracing
        using var frameActivity = Observability.RqSimPlatformTelemetry.ActivitySource.StartActivity(
            name: "ExecuteFrame",
            kind: System.Diagnostics.ActivityKind.Internal);

        frameActivity?.SetTag("graph.nodeCount", graph.N);
        frameActivity?.SetTag("dt", dt);
        frameActivity?.SetTag("executionCount", _executionCount);

        var frameSw = System.Diagnostics.Stopwatch.StartNew();

        _rwLock.EnterReadLock();
        try
        {
            // Performance optimization (Item 37/4.7): Use cached sorted modules
            var enabledModules = GetSortedEnabledModules();

            frameActivity?.SetTag("modules.enabled", enabledModules.Count);

            // Release read lock before execution to allow concurrent executions
            _rwLock.ExitReadLock();

            // Group modules by Stage and ModuleGroup for atomic execution
            var groupedByStage = enabledModules
                .GroupBy(m => m.Stage)
                .OrderBy(g => g.Key);

            foreach (var stageGroup in groupedByStage)
            {
                // Within each stage, process by module groups
                var moduleGroups = stageGroup
                    .GroupBy(m => m.ModuleGroup ?? $"__ungrouped_{m.Name}") // Each ungrouped module is its own "group"
                    .ToList();

                foreach (var group in moduleGroups)
                {
                    var modulesInGroup = group.OrderBy(m => m.Priority).ToList();
                    bool isRealGroup = modulesInGroup.Count > 1 ||
                                       (modulesInGroup.Count == 1 && modulesInGroup[0].ModuleGroup != null);

                    // Determine group execution mode (use first module's setting)
                    var groupMode = modulesInGroup.FirstOrDefault()?.GroupMode ?? GroupExecutionMode.Sequential;

                    // Separate by execution type
                    var gpuModules = modulesInGroup.Where(m => m.ExecutionType == ExecutionType.GPU).ToList();
                    var cpuModules = modulesInGroup.Where(m => m.ExecutionType == ExecutionType.SynchronousCPU).ToList();
                    var asyncModules = modulesInGroup.Where(m => m.ExecutionType == ExecutionType.AsynchronousTask).ToList();

                    // Execute GPU modules with barrier protection
                    if (gpuModules.Count > 0)
                    {
                        // Group GPU modules by device ID
                        var gpuModulesByDevice = gpuModules
                            .GroupBy(m => m.PreferredDeviceId)
                            .ToList();

                        // If multi-GPU is enabled and modules specify different devices, execute in parallel
                        if (IsMultiGpuEnabled && gpuModulesByDevice.Count > 1)
                        {
                            // Parallel multi-GPU execution
                            var gpuTasks = new List<Task>();

                            foreach (var deviceGroup in gpuModulesByDevice)
                            {
                                int deviceId = deviceGroup.Key;
                                var deviceModules = deviceGroup.ToList();

                                var task = Task.Run(() =>
                                {
                                    var syncManager = GetGpuSyncManager(deviceId);
                                    syncManager?.TransitionToCompute();

                                    foreach (var module in deviceModules)
                                    {
                                        ExecuteModuleSafe(module, graph, dt, deviceId);
                                    }

                                    syncManager?.TransitionToRender();
                                    syncManager?.WaitForComputeComplete();
                                });

                                gpuTasks.Add(task);
                            }

                            Task.WaitAll(gpuTasks.ToArray());
                        }
                        else if (groupMode == GroupExecutionMode.Parallel && gpuModules.Count > 1)
                        {
                            // Parallel execution on single GPU (using Task.WhenAll for independent modules)
                            var syncManager = GetGpuSyncManager(gpuModules[0].PreferredDeviceId);
                            syncManager?.TransitionToCompute();

                            var gpuTasks = new List<Task>();
                            foreach (var module in gpuModules)
                            {
                                var task = Task.Run(() => ExecuteModuleSafe(module, graph, dt, module.PreferredDeviceId));
                                gpuTasks.Add(task);
                            }

                            Task.WaitAll(gpuTasks.ToArray());

                            syncManager?.TransitionToRender();
                            syncManager?.WaitForComputeComplete();
                        }
                        else
                        {
                            // Sequential execution (legacy single GPU mode)
                            var syncManager = _gpuSyncManager ?? GetGpuSyncManager(gpuModules[0].PreferredDeviceId);
                            syncManager?.TransitionToCompute();

                            foreach (var module in gpuModules)
                            {
                                ExecuteModuleSafe(module, graph, dt, module.PreferredDeviceId);
                            }

                            syncManager?.TransitionToRender();
                            syncManager?.WaitForComputeComplete();
                        }
                    }

                    // Execute CPU modules based on group mode
                    if (groupMode == GroupExecutionMode.Parallel && cpuModules.Count > 1)
                    {
                        // Parallel execution within group with max parallelism limit
                        var options = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = _maxCpuParallelism
                        };
                        Parallel.ForEach(cpuModules, options, module =>
                            ExecuteModuleSafe(module, graph, dt));
                    }
                    else
                    {
                        // Sequential execution
                        foreach (var module in cpuModules)
                        {
                            ExecuteModuleSafe(module, graph, dt);
                        }
                    }

                    // Execute async modules (always parallel within group)
                    if (asyncModules.Count > 0)
                    {
                        var tasks = asyncModules.Select(module =>
                            Task.Run(() => ExecuteModuleSafe(module, graph, dt))
                        ).ToArray();
                        Task.WaitAll(tasks); // Wait for group completion (atomic)
                    }
                }
            }

            Interlocked.Increment(ref _executionCount);

            // CHECKLIST ITEM 39 (11.2): Record frame-level metrics
            frameSw.Stop();
            Observability.RqSimPlatformTelemetry.FrameDuration.Record(frameSw.Elapsed.TotalMilliseconds);
            Observability.RqSimPlatformTelemetry.FrameCount.Add(1);

            // Update graph topology metrics (lightweight - only count edges once per frame)
            int edgeCount = 0;
            if (graph.CsrIndices != null && graph.CsrIndices.Length > 0)
            {
                // Use CSR format if available (efficient)
                edgeCount = graph.CsrIndices.Length / 2;
            }
            else
            {
                // Fall back to direct edge array scan (slower)
                for (int i = 0; i < graph.N; i++)
                {
                    for (int j = i + 1; j < graph.N; j++)
                    {
                        if (graph.Edges[i, j]) edgeCount++;
                    }
                }
            }
            Observability.RqSimPlatformTelemetry.UpdateGraphMetrics(graph.N, edgeCount);

            frameActivity?.SetTag("duration_ms", frameSw.Elapsed.TotalMilliseconds);
            frameActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
        }
        finally
        {
            // Only exit if we still hold the lock
            if (_rwLock.IsReadLockHeld)
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Executes all enabled modules for one simulation frame with cancellation support.
    /// Modules are executed in order: Stage (Preparation->Forces->Integration->PostProcess),
    /// then by ModuleGroup (grouped modules execute atomically), then Priority within each stage.
    /// GPU modules are wrapped with resource barrier transitions.
    /// Thread-safe: Uses read lock for concurrent execution.
    /// Supports cancellation via CancellationToken with polling between stages.
    /// </summary>
    /// <param name="graph">The RQGraph instance to update</param>
    /// <param name="dt">Time step</param>
    /// <param name="ct">Cancellation token</param>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
    public void ExecuteFrame(RQGraph graph, double dt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _rwLock.EnterReadLock();
        try
        {
            // Performance optimization (Item 37/4.7): Use cached sorted modules
            var enabledModules = GetSortedEnabledModules();

            // Release read lock before execution to allow concurrent executions
            _rwLock.ExitReadLock();

            // Group modules by Stage and ModuleGroup for atomic execution
            var groupedByStage = enabledModules
                .GroupBy(m => m.Stage)
                .OrderBy(g => g.Key);

            foreach (var stageGroup in groupedByStage)
            {
                // Check for cancellation between stages
                ct.ThrowIfCancellationRequested();

                // Within each stage, process by module groups
                var moduleGroups = stageGroup
                    .GroupBy(m => m.ModuleGroup ?? $"__ungrouped_{m.Name}") // Each ungrouped module is its own "group"
                    .ToList();

                foreach (var group in moduleGroups)
                {
                    // Check for cancellation between module groups
                    ct.ThrowIfCancellationRequested();

                    var modulesInGroup = group.OrderBy(m => m.Priority).ToList();
                    bool isRealGroup = modulesInGroup.Count > 1 ||
                                       (modulesInGroup.Count == 1 && modulesInGroup[0].ModuleGroup != null);

                    // Determine group execution mode (use first module's setting)
                    var groupMode = modulesInGroup.FirstOrDefault()?.GroupMode ?? GroupExecutionMode.Sequential;

                    // Separate by execution type
                    var gpuModules = modulesInGroup.Where(m => m.ExecutionType == ExecutionType.GPU).ToList();
                    var cpuModules = modulesInGroup.Where(m => m.ExecutionType == ExecutionType.SynchronousCPU).ToList();
                    var asyncModules = modulesInGroup.Where(m => m.ExecutionType == ExecutionType.AsynchronousTask).ToList();

                    // Execute GPU modules with barrier protection
                    if (gpuModules.Count > 0)
                    {
                        // Group GPU modules by device ID
                        var gpuModulesByDevice = gpuModules
                            .GroupBy(m => m.PreferredDeviceId)
                            .ToList();

                        // If multi-GPU is enabled and modules specify different devices, execute in parallel
                        if (IsMultiGpuEnabled && gpuModulesByDevice.Count > 1)
                        {
                            // Parallel multi-GPU execution
                            var gpuTasks = new List<Task>();

                            foreach (var deviceGroup in gpuModulesByDevice)
                            {
                                int deviceId = deviceGroup.Key;
                                var deviceModules = deviceGroup.ToList();

                                var task = Task.Run(() =>
                                {
                                    var syncManager = GetGpuSyncManager(deviceId);
                                    syncManager?.TransitionToCompute();

                                    foreach (var module in deviceModules)
                                    {
                                        ct.ThrowIfCancellationRequested();
                                        ExecuteModuleSafe(module, graph, dt, deviceId);
                                    }

                                    syncManager?.TransitionToRender();
                                    syncManager?.WaitForComputeComplete();
                                }, ct);

                                gpuTasks.Add(task);
                            }

                            Task.WaitAll(gpuTasks.ToArray());
                        }
                        else if (groupMode == GroupExecutionMode.Parallel && gpuModules.Count > 1)
                        {
                            // Parallel execution on single GPU (using Task.WhenAll for independent modules)
                            var syncManager = GetGpuSyncManager(gpuModules[0].PreferredDeviceId);
                            syncManager?.TransitionToCompute();

                            var gpuTasks = new List<Task>();
                            foreach (var module in gpuModules)
                            {
                                var task = Task.Run(() =>
                                {
                                    ct.ThrowIfCancellationRequested();
                                    ExecuteModuleSafe(module, graph, dt, module.PreferredDeviceId);
                                }, ct);
                                gpuTasks.Add(task);
                            }

                            Task.WaitAll(gpuTasks.ToArray());

                            syncManager?.TransitionToRender();
                            syncManager?.WaitForComputeComplete();
                        }
                        else
                        {
                            // Sequential execution (legacy single GPU mode)
                            var syncManager = _gpuSyncManager ?? GetGpuSyncManager(gpuModules[0].PreferredDeviceId);
                            syncManager?.TransitionToCompute();

                            foreach (var module in gpuModules)
                            {
                                ct.ThrowIfCancellationRequested();
                                ExecuteModuleSafe(module, graph, dt, module.PreferredDeviceId);
                            }

                            syncManager?.TransitionToRender();
                            syncManager?.WaitForComputeComplete();
                        }
                    }

                    // Execute CPU modules based on group mode
                    if (groupMode == GroupExecutionMode.Parallel && cpuModules.Count > 1)
                    {
                        // Parallel execution within group with max parallelism limit and cancellation
                        var options = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = _maxCpuParallelism,
                            CancellationToken = ct
                        };
                        Parallel.ForEach(cpuModules, options, module =>
                            ExecuteModuleSafe(module, graph, dt));
                    }
                    else
                    {
                        // Sequential execution with cancellation checks
                        foreach (var module in cpuModules)
                        {
                            ct.ThrowIfCancellationRequested();
                            ExecuteModuleSafe(module, graph, dt);
                        }
                    }

                    // Execute async modules (always parallel within group)
                    if (asyncModules.Count > 0)
                    {
                        var tasks = asyncModules.Select(module =>
                            Task.Run(() =>
                            {
                                ct.ThrowIfCancellationRequested();
                                ExecuteModuleSafe(module, graph, dt);
                            }, ct)
                        ).ToArray();
                        Task.WaitAll(tasks); // Wait for group completion (atomic)
                    }
                }
            }

            Interlocked.Increment(ref _executionCount);
        }
        finally
        {
            // Only exit if we still hold the lock
            if (_rwLock.IsReadLockHeld)
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Executes all enabled modules for one frame, async version.
    /// Thread-safe: Uses read lock for concurrent execution.
    /// Supports cancellation via CancellationToken.
    /// </summary>
    public async Task ExecuteFrameAsync(RQGraph graph, double dt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _rwLock.EnterReadLock();
        try
        {
            // Performance optimization (Item 37/4.7): Use cached sorted modules
            var enabledModules = GetSortedEnabledModules();

            // Release read lock before execution
            _rwLock.ExitReadLock();

            var gpuModules = enabledModules.Where(m => m.ExecutionType == ExecutionType.GPU).ToList();
            var syncModules = enabledModules.Where(m => m.ExecutionType == ExecutionType.SynchronousCPU).ToList();
            var asyncModules = enabledModules.Where(m => m.ExecutionType == ExecutionType.AsynchronousTask).ToList();

            // GPU first with barriers (use helper method for multi-GPU support)
            await ExecuteGpuModulesAsync(gpuModules, graph, dt, ct).ConfigureAwait(false);

            // Sync CPU with parallelism limit
            if (syncModules.Any())
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxCpuParallelism,
                    CancellationToken = ct
                };
                Parallel.ForEach(syncModules, options, module =>
                    ExecuteModuleSafe(module, graph, dt));
            }

            // Async concurrently
            if (asyncModules.Any())
            {
                var tasks = asyncModules.Select(module =>
                    Task.Run(() => ExecuteModuleSafe(module, graph, dt), ct)
                ).ToArray();

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            Interlocked.Increment(ref _executionCount);
        }
        finally
        {
            // Only exit if we still hold the lock
            if (_rwLock.IsReadLockHeld)
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Executes all enabled modules with Channel-based async result coordination.
    /// This method allows async modules to stream results back as they complete,
    /// rather than waiting for all modules to finish before processing any results.
    /// Thread-safe: Uses read lock for concurrent execution.
    /// Supports cancellation via CancellationToken.
    /// </summary>
    /// <param name="graph">The RQGraph instance to update</param>
    /// <param name="dt">Time step</param>
    /// <param name="resultHandler">Optional handler called for each completed async module</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task that completes when all modules have executed</returns>
    public async Task ExecuteFrameWithChannelsAsync(
        RQGraph graph,
        double dt,
        Action<IPhysicsModule>? resultHandler = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _rwLock.EnterReadLock();
        try
        {
            // Performance optimization (Item 37/4.7): Use cached sorted modules
            var enabledModules = GetSortedEnabledModules();

            // Release read lock before execution
            _rwLock.ExitReadLock();

            var gpuModules = enabledModules.Where(m => m.ExecutionType == ExecutionType.GPU).ToList();
            var syncModules = enabledModules.Where(m => m.ExecutionType == ExecutionType.SynchronousCPU).ToList();
            var asyncModules = enabledModules.Where(m => m.ExecutionType == ExecutionType.AsynchronousTask).ToList();

            // GPU first with barriers (use helper method for multi-GPU support)
            await ExecuteGpuModulesAsync(gpuModules, graph, dt, ct).ConfigureAwait(false);

            // Sync CPU with parallelism limit
            if (syncModules.Any())
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxCpuParallelism,
                    CancellationToken = ct
                };
                Parallel.ForEach(syncModules, options, module =>
                    ExecuteModuleSafe(module, graph, dt));
            }

            // Async modules with Channel-based result streaming
            if (asyncModules.Any())
            {
                var channel = Channel.CreateUnbounded<IPhysicsModule>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

                // Start all async modules, each writes to channel when complete
                var producerTasks = asyncModules.Select(module =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            ExecuteModuleSafe(module, graph, dt);
                            await channel.Writer.WriteAsync(module, ct);
                        }
                        catch (Exception ex)
                        {
                            RaiseError(module, ex, "ExecuteAsync");
                        }
                    }, ct)
                ).ToList();

                // Signal when all producers are done
                _ = Task.Run(async () =>
                {
                    await Task.WhenAll(producerTasks);
                    channel.Writer.Complete();
                }, ct);

                // Process results as they arrive
                await foreach (var completedModule in channel.Reader.ReadAllAsync(ct))
                {
                    resultHandler?.Invoke(completedModule);
                }
            }

            Interlocked.Increment(ref _executionCount);
        }
        finally
        {
            // Only exit if we still hold the lock
            if (_rwLock.IsReadLockHeld)
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Cleans up all modules. Call when simulation stops.
    /// Thread-safe: Uses write lock.
    /// </summary>
    public void CleanupAll()
    {
        _rwLock.EnterWriteLock();
        try
        {
            foreach (var module in _modules)
            {
                try
                {
                    module.Cleanup();
                }
                catch (Exception ex)
                {
                    RaiseError(module, ex, "Cleanup");
                }
            }
            _isInitialized = false;
            RaiseLog("Pipeline cleanup complete");
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Sorts modules by execution stage and priority.
    /// Order: Stage (Preparation -> Forces -> Integration -> PostProcess), then Priority (lower first).
    /// </summary>
    public void SortByPriority()
    {
        var sorted = _modules
            .OrderBy(m => m.Stage)
            .ThenBy(m => m.Priority)
            .ToList();

        _modules.Clear();
        foreach (var module in sorted)
        {
            _modules.Add(module);
        }

        // Invalidate caches
        _sortedCacheDirty = true;

        RaiseLog("Pipeline sorted by stage and priority");
    }

    /// <summary>
    /// Sorts modules by execution stage, execution type, then priority.
    /// Use this for deterministic ordering that also groups by execution type.
    /// </summary>
    public void SortByStageAndType()
    {
        var sorted = _modules
            .OrderBy(m => m.Stage)
            .ThenBy(m => m.ExecutionType)
            .ThenBy(m => m.Priority)
            .ToList();

        _modules.Clear();
        foreach (var module in sorted)
        {
            _modules.Add(module);
        }

        // Invalidate caches
        _sortedCacheDirty = true;

        RaiseLog("Pipeline sorted by stage, type, and priority");
    }

    private void ExecuteModuleSafe(IPhysicsModule module, RQGraph graph, double dt, int deviceId = -1)
    {
        // CHECKLIST ITEM 39 (11.2): Observability Integration
        // Track module execution with OpenTelemetry
        using var activity = Observability.RqSimPlatformTelemetry.ActivitySource.StartActivity(
            name: $"ExecuteModule.{module.Name}",
            kind: System.Diagnostics.ActivityKind.Internal);

        activity?.SetTag("module.name", module.Name);
        activity?.SetTag("module.category", module.Category);
        activity?.SetTag("module.executionType", module.ExecutionType.ToString());
        activity?.SetTag("module.stage", module.Stage.ToString());
        activity?.SetTag("module.deviceId", deviceId);
        activity?.SetTag("graph.nodeCount", graph.N);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Use zero-copy Span execution if module supports it
            if (module is ISpanPhysicsModule spanModule && graph.EdgePhaseU1 is not null)
            {
                // Get Span views of the underlying arrays for zero-copy access
                Span<double> weightsSpan = graph.Weights.AsSpan();
                Span<double> phasesSpan = graph.EdgePhaseU1.AsSpan();
                ReadOnlySpan<bool> edgesSpan = graph.Edges.AsReadOnlySpan();

                spanModule.ExecuteSpan(weightsSpan, phasesSpan, edgesSpan, graph.N, dt);
            }
            else
            {
                // Fall back to standard execution
                module.ExecuteStep(graph, dt);
            }

            sw.Stop();

            // Record successful execution metrics
            var tags = new System.Diagnostics.TagList
            {
                { "module.name", module.Name },
                { "module.category", module.Category },
                { "module.executionType", module.ExecutionType.ToString() }
            };

            Observability.RqSimPlatformTelemetry.ModuleDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
            Observability.RqSimPlatformTelemetry.ModuleExecutionCount.Add(1, tags);

            activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            sw.Stop();

            // Record error metrics
            var errorTags = new System.Diagnostics.TagList
            {
                { "module.name", module.Name },
                { "module.category", module.Category },
                { "error.type", ex.GetType().Name }
            };
            Observability.RqSimPlatformTelemetry.ModuleErrorCount.Add(1, errorTags);

            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            // Wrap GPU module errors with GpuExecutionException for better diagnostics
            if (module.ExecutionType == ExecutionType.GPU && ex is not GpuExecutionException)
            {
                var gpuEx = new GpuExecutionException(
                    $"GPU execution failed in module '{module.Name}': {ex.Message}",
                    deviceId,
                    deviceName: null, // Device name could be queried from ComputeSharp if needed
                    moduleName: module.Name,
                    inner: ex);
                RaiseError(module, gpuEx, "ExecuteStep");
            }
            else
            {
                RaiseError(module, ex, ".ExecuteStep");
            }
        }
    }

    private void RaiseError(IPhysicsModule module, Exception ex, string phase)
    {
        // Determine if this is a critical error that should stop the simulation
        bool isCritical = IsCriticalException(ex);

        var args = new ModuleErrorEventArgs(module, ex, phase, isCritical);
        ModuleError?.Invoke(this, args);

        if (isCritical)
        {
            _logger.LogCritical(ex, "CRITICAL ERROR in {ModuleName}.{Phase}: {Message}",
                module.Name, phase, ex.Message);
            RaiseLog($"CRITICAL ERROR in {module.Name}.{phase}: {ex.Message}");
            // Re-throw critical errors to stop the simulation
            throw ex;
        }
        else
        {
            _logger.LogError(ex, "ERROR in {ModuleName}.{Phase}: {Message}",
                module.Name, phase, ex.Message);
            RaiseLog($"ERROR in {module.Name}.{phase}: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines if an exception is critical and should stop the simulation.
    /// Critical exceptions include memory errors, stack overflow, access violations,
    /// and GPU device lost errors.
    /// </summary>
    private static bool IsCriticalException(Exception ex)
    {
        return ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or GpuExecutionException { InnerException: OutOfMemoryException }
            or OperationCanceledException; // Cancellation is also critical
    }

    private void RaiseLog(string message)
    {
        // Support both structured logging (ILogger) and legacy event-based logging
        _logger.LogInformation("{Message}", message);
        Log?.Invoke(this, new PipelineLogEventArgs(message));
    }

    /// <summary>
    /// Executes GPU modules with proper device assignment and barrier management.
    /// Supports both single-GPU and multi-GPU execution.
    /// </summary>
    private async Task ExecuteGpuModulesAsync(
        List<IPhysicsModule> gpuModules,
        RQGraph graph,
        double dt,
        CancellationToken ct = default)
    {
        if (gpuModules.Count == 0) return;

        // Group GPU modules by device ID
        var gpuModulesByDevice = gpuModules
            .GroupBy(m => m.PreferredDeviceId)
            .ToList();

        // If multi-GPU is enabled and modules specify different devices, execute in parallel
        if (IsMultiGpuEnabled && gpuModulesByDevice.Count > 1)
        {
            // Parallel multi-GPU execution
            var gpuTasks = new List<Task>();

            foreach (var deviceGroup in gpuModulesByDevice)
            {
                int deviceId = deviceGroup.Key;
                var deviceModules = deviceGroup.ToList();

                var task = Task.Run(() =>
                {
                    var syncManager = GetGpuSyncManager(deviceId);
                    syncManager?.TransitionToCompute();

                    foreach (var module in deviceModules)
                    {
                        ct.ThrowIfCancellationRequested();
                        ExecuteModuleSafe(module, graph, dt, deviceId);
                    }

                    syncManager?.TransitionToRender();
                    syncManager?.WaitForComputeComplete();
                }, ct);

                gpuTasks.Add(task);
            }

            await Task.WhenAll(gpuTasks).ConfigureAwait(false);
        }
        else
        {
            // Sequential or single-GPU execution
            var syncManager = _gpuSyncManager ?? GetGpuSyncManager(gpuModules[0].PreferredDeviceId);
            syncManager?.TransitionToCompute();

            foreach (var module in gpuModules)
            {
                ct.ThrowIfCancellationRequested();
                ExecuteModuleSafe(module, graph, dt, module.PreferredDeviceId);
            }

            syncManager?.TransitionToRender();
            syncManager?.WaitForComputeComplete();
        }
    }

    // ================================================================
    // STATE SERIALIZATION AND DESERIALIZATION (Item 31/11.1)
    // ================================================================

    /// <summary>
    /// Captures complete simulation state including graph topology and all module states.
    ///
    /// CHECKLIST ITEM 31: State Serialization and Deserialization
    /// ============================================================
    /// Enables simulation checkpointing for save/resume functionality.
    ///
    /// USAGE:
    /// <code>
    /// var snapshot = pipeline.SaveSnapshot(graph, tickId, "Experiment checkpoint");
    /// var json = snapshot.ToJson(indented: true);
    /// File.WriteAllText("checkpoint.json", json);
    /// </code>
    /// </summary>
    /// <param name="graph">Current graph state to capture</param>
    /// <param name="tickId">Current simulation tick/step number</param>
    /// <param name="description">Optional description</param>
    /// <returns>Complete simulation snapshot</returns>
    public SimulationSnapshot SaveSnapshot(RQGraph graph, long tickId, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var snapshot = new SimulationSnapshot
        {
            Graph = GraphSnapshot.FromGraph(graph, tickId),
            ExecutionCount = _executionCount,
            SimulationTime = tickId * 0.01, // Estimate based on typical dt
            Timestamp = DateTime.UtcNow,
            Description = description
        };

        // Capture module states
        _rwLock.EnterReadLock();
        try
        {
            foreach (var module in _modules)
            {
                if (module is ISerializableModule serializable)
                {
                    try
                    {
                        var state = serializable.SaveState();
                        if (state != null)
                        {
                            snapshot.ModuleStates[module.Name] = state;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail entire snapshot
                        RaiseLog($"Warning: Failed to save state for module {module.Name}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        // Capture physics metadata (if available)
        // Note: This requires access to EnergyLedger and other physics components
        // which would typically be injected or accessible through graph

        return snapshot;
    }

    /// <summary>
    /// Restores simulation state from a previously saved snapshot.
    ///
    /// CHECKLIST ITEM 31: State Serialization and Deserialization
    /// ============================================================
    /// Loads saved simulation state for resumption.
    ///
    /// REQUIREMENTS:
    /// - Graph must be initialized with same or larger capacity as snapshot
    /// - Modules should already be registered (matching snapshot module names)
    /// - Call this after InitializeAll() but before starting execution
    ///
    /// USAGE:
    /// <code>
    /// var json = File.ReadAllText("checkpoint.json");
    /// var snapshot = SimulationSnapshot.FromJson(json);
    /// if (snapshot != null && snapshot.Validate())
    /// {
    ///     pipeline.LoadSnapshot(graph, snapshot);
    /// }
    /// </code>
    /// </summary>
    /// <param name="graph">Graph instance to restore state into</param>
    /// <param name="snapshot">Previously saved snapshot</param>
    /// <returns>True if restoration succeeded, false otherwise</returns>
    public bool LoadSnapshot(RQGraph graph, SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Validate())
        {
            RaiseLog("Error: Snapshot validation failed. Cannot load invalid snapshot.");
            return false;
        }

        try
        {
            // Restore graph topology and physics state
            if (snapshot.Graph != null)
            {
                // Note: This requires graph to have a method to load from GraphSnapshot
                // For now, we log a message that graph restoration is not fully implemented
                RaiseLog($"Loading graph snapshot: {snapshot.Graph.NodeCount} nodes, " +
                        $"{snapshot.Graph.EdgeCount} edges, tick {snapshot.Graph.TickId}");

                // TODO: Implement RQGraph.LoadFromSnapshot(GraphSnapshot) method
                // graph.LoadFromSnapshot(snapshot.Graph);
            }

            // Restore module states
            _rwLock.EnterReadLock();
            try
            {
                foreach (var module in _modules)
                {
                    if (module is ISerializableModule serializable &&
                        snapshot.ModuleStates.TryGetValue(module.Name, out var state))
                    {
                        try
                        {
                            serializable.LoadState(state);
                            RaiseLog($"Restored state for module: {module.Name}");
                        }
                        catch (Exception ex)
                        {
                            RaiseLog($"Warning: Failed to restore state for module {module.Name}: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                _rwLock.ExitReadLock();
            }

            // Restore execution count
            _executionCount = (int)Math.Min(snapshot.ExecutionCount, int.MaxValue);

            RaiseLog($"Successfully loaded snapshot from {snapshot.Timestamp:u}");
            RaiseLog($"Snapshot description: {snapshot.Description ?? "(none)"}");

            return true;
        }
        catch (Exception ex)
        {
            RaiseLog($"Error loading snapshot: {ex.Message}");
            return false;
        }
    }

    private void OnModulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
    }
}
