using System.Diagnostics;
using System.Text.Json;
using RqSimUI.FormSimAPI.Interfaces;
using RQSimulation;
using RQSimulation.Core.Infrastructure;
using RQSimulation.Core.Scheduler;

// Use full type names to avoid namespace/class name conflict
// RqSimConsole.RqSimEngineApi is a namespace containing session types
// RqSimForms.Forms.Interfaces.RqSimEngineApi is the class we need

namespace RqSimConsole.ConsoleUI;

/// <summary>
/// Orchestrates the simulation execution from the console.
/// Handles config loading, GPU setup, simulation loop, and result output.
/// </summary>
public sealed class SimulationRunner
{
    private readonly CommandLineOptions _options;
    private readonly ConsoleUIRenderer _renderer;
    private readonly RqSimForms.Forms.Interfaces.RqSimEngineApi _api;
    private ConsoleConfig? _config;
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();
    private string? _gpuDeviceName;

    public RqSimForms.Forms.Interfaces.RqSimEngineApi Api => _api;
    public ConsoleConfig? Config => _config;
    public string? GpuDeviceName => _gpuDeviceName;

    // Multi-GPU tracking
    private bool _multiGpuActive;
    private int _totalGpuCount;

    public SimulationRunner(CommandLineOptions options)
    {
        _options = options;
        _renderer = new ConsoleUIRenderer();
        _api = new RqSimForms.Forms.Interfaces.RqSimEngineApi();
    }

    /// <summary>
    /// Runs the complete simulation workflow.
    /// </summary>
    public async Task<int> RunAsync()
    {
        try
        {
            // 1. Load configuration
            if (!LoadConfiguration())
                return 1;

            // 2. Detect and select GPU
            DetectGpu();

            // 3. Initialize Multi-GPU cluster if enabled
            InitializeMultiGpu();

            // 4. Render initial UI
            _renderer.RenderInitialConfig(_config!, _gpuDeviceName, _multiGpuActive, _totalGpuCount);

            // 5. Wait for start or auto-start
            if (!_options.StartNow)
            {
                Console.WriteLine();
                Console.WriteLine("Press ENTER to start simulation, or Ctrl+C to cancel...");
                Console.ReadLine();
            }

            // 6. Run simulation
            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            int result = await RunSimulationAsync(_cts.Token);

            // 7. Wait for Multi-GPU workers to complete
            if (_multiGpuActive)
            {
                Console.WriteLine("Waiting for Multi-GPU workers to complete...");
                await _api.WaitForMultiGpuWorkersAsync(TimeSpan.FromSeconds(10), _cts.Token);
            }

            // 8. Save results
            string resultPath = SaveResults();

            // 9. Render completion
            if (_cts.Token.IsCancellationRequested)
            {
                _renderer.RenderCancellation(resultPath);
                return 130; // Standard exit code for Ctrl+C
            }

            _renderer.RenderCompletion(resultPath, _stopwatch.Elapsed);
            return result;
        }
        catch (Exception ex)
        {
            _renderer.RenderError(ex.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _api.DisposeMultiGpuCluster();
            _cts?.Dispose();
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        _cts?.Cancel();
        Console.WriteLine();
        Console.WriteLine("Cancellation requested... waiting for current step to complete.");
    }

    private bool LoadConfiguration()
    {
        try
        {
            string json = File.ReadAllText(_options.ConfigFilePath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                TypeInfoResolver = RqSimConsoleJsonContext.Default
            };
            _config = JsonSerializer.Deserialize<ConsoleConfig>(json, jsonOptions);

            if (_config == null)
            {
                Console.Error.WriteLine("Error: Failed to parse configuration file");
                return false;
            }

            // Override settings from command line
            if (_options.UseGpu)
            {
                _config.RunSettings.EnableGPU = true;
                _config.RunSettings.GpuDeviceIndex = _options.GpuIndex;
            }

            if (_options.CpuThreads > 0)
            {
                _config.RunSettings.CpuThreads = _options.CpuThreads;
            }

            if (_options.Steps > 0)
            {
                _config.SimulationParameters.TotalSteps = _options.Steps;
            }

            return true;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Error parsing JSON: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading configuration: {ex.Message}");
            return false;
        }
    }

    private void DetectGpu()
    {
        if (_config == null || !_config.RunSettings.EnableGPU)
        {
            _api.GpuAvailable = false;
            return;
        }

        try
        {
            // Try to get the default GPU device
            var device = ComputeSharp.GraphicsDevice.GetDefault();
            if (device != null)
            {
                _gpuDeviceName = device.Name;
                _api.GpuAvailable = true;
            }
            else
            {
                _gpuDeviceName = null;
                _api.GpuAvailable = false;
            }
        }
        catch (Exception)
        {
            _gpuDeviceName = null;
            _api.GpuAvailable = false;
        }
    }

    private void InitializeMultiGpu()
    {
        _multiGpuActive = false;
        _totalGpuCount = 1;

        if (_config == null || !_config.RunSettings.EnableGPU || !_config.RunSettings.MultiGpu.Enabled)
            return;

        if (!_api.GpuAvailable)
            return;

        try
        {
            // Configure Multi-GPU settings
            _api.MultiGpuSettings.Enabled = _config.RunSettings.MultiGpu.Enabled;
            _api.MultiGpuSettings.SpectralWorkerCount = _config.RunSettings.MultiGpu.SpectralWorkerCount;
            _api.MultiGpuSettings.McmcWorkerCount = _config.RunSettings.MultiGpu.McmcWorkerCount;
            _api.MultiGpuSettings.MaxGraphSize = _config.RunSettings.MultiGpu.MaxGraphSize;
            _api.MultiGpuSettings.SnapshotInterval = _config.RunSettings.MultiGpu.SnapshotInterval;
            _api.MultiGpuSettings.SpectralSteps = _config.RunSettings.MultiGpu.SpectralSteps;
            _api.MultiGpuSettings.SpectralWalkers = _config.RunSettings.MultiGpu.SpectralWalkers;
            _api.MultiGpuSettings.McmcSamples = _config.RunSettings.MultiGpu.McmcSamples;
            _api.MultiGpuSettings.McmcThinning = _config.RunSettings.MultiGpu.McmcThinning;
            _api.MultiGpuSnapshotInterval = _config.RunSettings.MultiGpu.SnapshotInterval;

            // Initialize the Multi-GPU cluster
            _multiGpuActive = _api.InitializeMultiGpuCluster();
            _totalGpuCount = _api.GpuClusterSize;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MultiGPU] Initialization warning: {ex.Message}");
            _multiGpuActive = false;
            _totalGpuCount = 1;
        }
    }

    private async Task<int> RunSimulationAsync(CancellationToken ct)
    {
        if (_config == null)
            return 1;

        // Convert config
        SimulationConfig simConfig = _config.ToSimulationConfig();

        // Initialize simulation
        _api.OnConsoleLog = message => { /* Capture logs if needed */ };
        _api.CpuThreadCount = _config.RunSettings.CpuThreads > 0
            ? _config.RunSettings.CpuThreads
            : Environment.ProcessorCount;
        _api.AutoTuningEnabled = _config.RunSettings.AutoTuningParams;

        // Apply RQ flags
        _api.RQFlags.EnableNaturalDimensionEmergence = _config.RQFlags.NaturalDimensionEmergence;
        _api.RQFlags.EnableTopologicalParity = _config.RQFlags.TopologicalParity;
        _api.RQFlags.EnableLapseSynchronizedGeometry = _config.RQFlags.LapseSynchronizedGeometry;
        _api.RQFlags.EnableTopologyEnergyCompensation = _config.RQFlags.TopologyEnergyCompensation;
        _api.RQFlags.EnablePlaquetteYangMills = _config.RQFlags.PlaquetteYangMills;

        // Initialize live config
        _api.LiveConfig.GravitationalCoupling = _config.PhysicsConstants.GravitationalCoupling;
        _api.LiveConfig.VacuumEnergyScale = _config.PhysicsConstants.VacuumEnergyScale;
        _api.LiveConfig.DecoherenceRate = _config.PhysicsConstants.DecoherenceRate;
        _api.LiveConfig.HotStartTemperature = _config.PhysicsConstants.HotStartTemperature;
        _api.LiveConfig.AdaptiveThresholdSigma = _config.PhysicsConstants.AdaptiveThresholdAlpha;
        _api.LiveConfig.WarmupDuration = _config.PhysicsConstants.WarmupDuration;
        _api.LiveConfig.GravityTransitionDuration = _config.PhysicsConstants.GravityTransition;
        _api.LiveConfig.InitialEdgeProb = _config.PhysicsConstants.InitialEdgeProb;

        // Initialize simulation engine
        _api.InitializeSimulation(simConfig);

        // Initialize GPU if enabled
        bool gpuActive = false;
        if (_config.RunSettings.EnableGPU && _api.GpuAvailable)
        {
            gpuActive = _api.InitializeGpuEngines();
        }

        // Create session
        _api.CurrentSession = _api.CreateSession(
            gpuActive,
            _gpuDeviceName,
            new DisplayFilters { HeavyOnly = _config.RunSettings.HeavyClustersOnly }
        );

        _stopwatch.Start();

        // Run simulation with periodic UI updates
        int totalSteps = simConfig.TotalSteps;
        int nodeCount = simConfig.NodeCount;
        int totalEvents = totalSteps * nodeCount;

        var updateInterval = TimeSpan.FromMilliseconds(1000 / Math.Max(1, _config.RunSettings.MaxFPS));
        DateTime lastUpdate = DateTime.MinValue;
        List<int> excitedHistory = [];

        // Periodic UI update task
        var uiUpdateTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !_api.SimulationComplete)
            {
                await Task.Delay(updateInterval, CancellationToken.None);

                if (DateTime.Now - lastUpdate < updateInterval)
                    continue;

                lastUpdate = DateTime.Now;
                UpdateUIMetrics(excitedHistory);
            }
        }, CancellationToken.None);

        // Non-blocking diagnostics hotkeys
        var hotkeysTask = Task.Run(() =>
        {
            while (!ct.IsCancellationRequested && !_api.SimulationComplete)
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.F1:
                        ConsoleDiagnosticsScreens.ShowDeviceExecutionStatus(_config, _gpuDeviceName);
                        _renderer.RenderInitialConfig(_config, _gpuDeviceName, _multiGpuActive, _totalGpuCount);
                        break;
                    case ConsoleKey.F2:
                        if (!string.IsNullOrWhiteSpace(_options.ConfigFilePath))
                        {
                            ConsoleDiagnosticsScreens.ShowCurrentJsonSettings(_options.ConfigFilePath);
                            _renderer.RenderInitialConfig(_config, _gpuDeviceName, _multiGpuActive, _totalGpuCount);
                        }
                        break;
                    case ConsoleKey.F3:
                        ConsoleDiagnosticsScreens.ShowEngineStatistics(_api, _config, _gpuDeviceName);
                        _renderer.RenderInitialConfig(_config, _gpuDeviceName, _multiGpuActive, _totalGpuCount);
                        break;
                    case ConsoleKey.F4:
                        ConsoleDiagnosticsScreens.ShowPipeline(_config);
                        _renderer.RenderInitialConfig(_config, _gpuDeviceName, _multiGpuActive, _totalGpuCount);
                        break;
                }
            }
        }, CancellationToken.None);

        try
        {
            // Run the simulation loop
            await Task.Run(() =>
            {
                _api.RunParallelEventBasedLoop(ct, totalEvents, useParallel: true, useGpu: gpuActive);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (GraphFragmentationException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Simulation stopped: {ex.Message}");
        }
        catch (EnergyConservationException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Energy conservation violation: {ex.Message}");
        }

        _api.SimulationComplete = true;
        _stopwatch.Stop();

        try { await uiUpdateTask; } catch { }
        try { await hotkeysTask; } catch { }

        return 0;
    }

    private void UpdateUIMetrics(List<int> excitedHistory)
    {
        var dispatcher = _api.Dispatcher;

        double avgExcited = excitedHistory.Count > 0 ? excitedHistory.Average() : 0;
        int maxExcited = excitedHistory.Count > 0 ? excitedHistory.Max() : 0;

        var metrics = new LiveMetrics
        {
            CurrentStep = dispatcher.LiveStep,
            TotalSteps = dispatcher.LiveTotalSteps,
            Excited = dispatcher.LiveExcited,
            AvgExcited = avgExcited,
            MaxExcited = maxExcited,
            Avalanches = 0, // Not tracked in console mode
            MeasurementStatus = "NOT CONFIGURED",
            GlobalNbr = 0, // Would need additional tracking
            GlobalSpont = 0,
            StrongEdges = dispatcher.LiveStrongEdges,
            LargestCluster = dispatcher.LiveLargestCluster,
            HeavyMass = dispatcher.LiveHeavyMass,
            SpectrumLogs = _api.SpectrumLoggingEnabled,
            CEffective = 0,
            GiantPercent = dispatcher.LiveTotalSteps > 0 && _config != null
                ? (double)dispatcher.LiveLargestCluster / _config.SimulationParameters.NodeCount
                : 0,
            AvgDegree = dispatcher.LiveAvgDegree,
            SpectralDimension = dispatcher.LiveSpectralDim
        };

        _renderer.UpdateProgress(metrics);
    }

    private string SaveResults()
    {
        // Create result filename with timestamp
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"result_{timestamp}.json";
        string filePath = Path.Combine(_options.OutputDirectory, fileName);

        // Ensure output directory exists
        Directory.CreateDirectory(_options.OutputDirectory);

        // Archive session if available
        if (_api.CurrentSession != null)
        {
            _api.ArchiveSession(
                _cts?.Token.IsCancellationRequested == true
                    ? SessionEndReason.CancelledByUser
                    : SessionEndReason.Completed,
                string.Empty,
                string.Empty,
                []
            );
        }

        // Create result object
        var result = new ConsoleResult
        {
            SessionId = _api.CurrentSession?.SessionId ?? Guid.NewGuid(),
            StartedAt = _api.CurrentSession?.StartedAt ?? DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            ElapsedSeconds = _stopwatch.Elapsed.TotalSeconds,
            Config = _config,
            GpuDeviceName = _gpuDeviceName,
            FinalStep = _api.Dispatcher.LiveStep,
            TotalStepsPlanned = _api.Dispatcher.LiveTotalSteps,
            FinalSpectralDimension = _api.FinalSpectralDimension,
            FinalNetworkTemperature = _api.FinalNetworkTemperature,
            AverageExcited = _api.LastResult?.AverageExcited ?? 0,
            MaxExcited = _api.LastResult?.MaxExcited ?? 0,
            SeriesSteps = [.. _api.SeriesSteps],
            SeriesExcited = [.. _api.SeriesExcited],
            SeriesSpectralDimension = [.. _api.SeriesSpectralDimension],
            SeriesNetworkTemperature = [.. _api.SeriesNetworkTemperature],
            SeriesHeavyMass = [.. _api.SeriesHeavyMass],
            SeriesStrongEdges = [.. _api.SeriesStrongEdges],
            WasCancelled = _cts?.Token.IsCancellationRequested == true
        };

        // Serialize to JSON
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = RqSimConsoleJsonContext.Default
        };
        string json = JsonSerializer.Serialize(result, jsonOptions);
        File.WriteAllText(filePath, json);

        return filePath;
    }
}

/// <summary>
/// Result object serialized to JSON at simulation end.
/// </summary>
public sealed class ConsoleResult
{
    public Guid SessionId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public double ElapsedSeconds { get; set; }
    public ConsoleConfig? Config { get; set; }
    public string? GpuDeviceName { get; set; }
    public int FinalStep { get; set; }
    public int TotalStepsPlanned { get; set; }
    public double FinalSpectralDimension { get; set; }
    public double FinalNetworkTemperature { get; set; }
    public double AverageExcited { get; set; }
    public int MaxExcited { get; set; }
    public List<int> SeriesSteps { get; set; } = [];
    public List<int> SeriesExcited { get; set; } = [];
    public List<double> SeriesSpectralDimension { get; set; } = [];
    public List<double> SeriesNetworkTemperature { get; set; } = [];
    public List<double> SeriesHeavyMass { get; set; } = [];
    public List<int> SeriesStrongEdges { get; set; } = [];
    public bool WasCancelled { get; set; }
}
