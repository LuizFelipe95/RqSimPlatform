using System.Text;

namespace RqSimConsole.ConsoleUI;

/// <summary>
/// Renders the console UI with checkbox displays, progress bars, and live metrics.
/// Designed to show all parameters from the WinForms UI in a text-based format.
/// </summary>
public sealed class ConsoleUIRenderer
{
    private int _lastProgressLine = -1;
    private int _metricsStartLine = -1;
    private readonly object _renderLock = new();

    /// <summary>
    /// Renders the initial configuration display showing all parameters.
    /// </summary>
    public void RenderInitialConfig(ConsoleConfig config, string? gpuDeviceName, bool multiGpuActive = false, int totalGpuCount = 1)
    {
        Console.Clear();
        Console.CursorVisible = false;

        RenderHeader();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("Hotkeys: F1=Device status  F2=Show JSON settings  F3=Engine stats  F4=Pipeline/plugins/shaders");
        Console.ResetColor();
        Console.WriteLine();

        RenderPhysicsModules(config.PhysicsModules);
        Console.WriteLine();
        RenderRQFlags(config.RQFlags);
        Console.WriteLine();
        RenderSimulationParameters(config.SimulationParameters);
        Console.WriteLine();
        RenderPhysicsConstants(config.PhysicsConstants);
        Console.WriteLine();
        RenderRunSettings(config.RunSettings, gpuDeviceName, multiGpuActive, totalGpuCount);
        Console.WriteLine();

        _metricsStartLine = Console.CursorTop;
        RenderLiveMetricsPlaceholder();
        Console.WriteLine();

        _lastProgressLine = Console.CursorTop;
    }

    private void RenderHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("??????????????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?                           RQ-Sim Console                                   ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????????????????????");
        Console.ResetColor();
    }

    private void RenderPhysicsModules(PhysicsModulesConfig modules)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("?? Physics Modules ?????????????????????????????????????????????????????????");
        Console.ResetColor();

        // Render in columns matching the UI layout
        var col1 = new (string name, bool enabled)[]
        {
            ("Quantum Driven States", modules.QuantumDrivenStates),
            ("Spacetime Physics", modules.SpacetimePhysics),
            ("Spinor Field", modules.SpinorField),
            ("Vacuum Fluctuations", modules.VacuumFluctuations),
            ("Black Hole Physics", modules.BlackHolePhysics),
            ("Yang-Mills Gauge", modules.YangMillsGauge),
            ("Enhanced Klein-Gordon", modules.EnhancedKleinGordon),
            ("Internal Time", modules.InternalTime),
            ("Spectral Geometry", modules.SpectralGeometry),
            ("Quantum Graphity", modules.QuantumGraphity),
        };

        var col2 = new (string name, bool enabled)[]
        {
            ("Relational Time", modules.RelationalTime),
            ("Relational Yang-Mills", modules.RelationalYangMills),
            ("Network Gravity", modules.NetworkGravity),
            ("Unified Physics Step", modules.UnifiedPhysicsStep),
            ("Enforce Gauge Constraints", modules.EnforceGaugeConstraints),
            ("Causal Rewiring", modules.CausalRewiring),
            ("Topological Protection", modules.TopologicalProtection),
            ("Validate Energy Conserv.", modules.ValidateEnergyConservation),
            ("Mexican Hat Potential", modules.MexicanHatPotential),
            ("Geometry Momenta", modules.GeometryMomenta),
        };

        var col3 = new (string name, bool enabled)[]
        {
            ("Topological Censorship", modules.TopologicalCensorship),
        };

        int maxRows = Math.Max(col1.Length, Math.Max(col2.Length, col3.Length));

        for (int i = 0; i < maxRows; i++)
        {
            Console.Write("? ");
            if (i < col1.Length)
                WriteCheckbox(col1[i].name, col1[i].enabled, 24);
            else
                Console.Write(new string(' ', 28));

            if (i < col2.Length)
                WriteCheckbox(col2[i].name, col2[i].enabled, 24);
            else
                Console.Write(new string(' ', 28));

            if (i < col3.Length)
                WriteCheckbox(col3[i].name, col3[i].enabled, 20);
            else
                Console.Write(new string(' ', 24));

            Console.WriteLine("?");
        }

        Console.WriteLine("????????????????????????????????????????????????????????????????????????????");
    }

    private void RenderRQFlags(RQExperimentalFlagsConfig flags)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("?? RQ Experimental Flags ???????????????????????????????????????????????????");
        Console.ResetColor();

        Console.Write("? ");
        WriteCheckbox("Natural Dim. Emergence", flags.NaturalDimensionEmergence, 24);
        WriteCheckbox("Topological Parity", flags.TopologicalParity, 22);
        WriteCheckbox("Lapse-Sync. Geometry", flags.LapseSynchronizedGeometry, 20);
        Console.WriteLine("?");

        Console.Write("? ");
        WriteCheckbox("Topology Energy Comp.", flags.TopologyEnergyCompensation, 24);
        WriteCheckbox("Plaquette Yang-Mills", flags.PlaquetteYangMills, 22);
        Console.Write(new string(' ', 24));
        Console.WriteLine("?");

        Console.WriteLine("????????????????????????????????????????????????????????????????????????????");
    }

    private void RenderSimulationParameters(SimulationParametersConfig simParams)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("?? Simulation Parameters ???????????????????????????????????????????????????");
        Console.ResetColor();

        Console.WriteLine($"? Node Count:            {simParams.NodeCount,10}   ? Target Degree:         {simParams.TargetDegree,10}   ?");
        Console.WriteLine($"? Initial Excited Prob:  {simParams.InitialExcitedProb,10:F2}   ? Lambda State:          {simParams.LambdaState,10:F2}   ?");
        Console.WriteLine($"? Temperature:           {simParams.Temperature,10:F2}   ? Edge Trial Prob:       {simParams.EdgeTrialProb,10:F3}   ?");
        Console.WriteLine($"? Measurement Threshold: {simParams.MeasurementThreshold,10:F3}   ? Total Steps:           {simParams.TotalSteps,10}   ?");
        Console.WriteLine($"? Fractal Levels:        {simParams.FractalLevels,10}   ? Fractal Branch Factor: {simParams.FractalBranchFactor,10}   ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????????????????");
    }

    private void RenderPhysicsConstants(PhysicsConstantsConfig constants)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("?? Physics Constants ???????????????????????????????????????????????????????");
        Console.ResetColor();

        Console.WriteLine($"? Initial Edge Prob:     {constants.InitialEdgeProb,10:F4}   ? Gravitational Coupling:{constants.GravitationalCoupling,10:F4}   ?");
        Console.WriteLine($"? Vacuum Energy Scale:   {constants.VacuumEnergyScale,10:F4}   ? Gravity Transition:    {constants.GravityTransition,10:F1}   ?");
        Console.WriteLine($"? Decoherence Rate:      {constants.DecoherenceRate,10:F4}   ? Hot Start Temperature: {constants.HotStartTemperature,10:F1}   ?");
        Console.WriteLine($"? Adaptive Threshold ?:  {constants.AdaptiveThresholdAlpha,10:F2}   ? Warmup Duration:       {constants.WarmupDuration,10:F0}   ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????????????????");
    }

    private void RenderRunSettings(RunSettingsConfig settings, string? gpuDeviceName, bool multiGpuActive = false, int totalGpuCount = 1)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("?? Run Settings ????????????????????????????????????????????????????????????");
        Console.ResetColor();

        Console.Write("? ");
        WriteCheckbox("Auto-tuning params", settings.AutoTuningParams, 22);
        WriteCheckbox("Heavy clusters only", settings.HeavyClustersOnly, 22);
        Console.Write(new string(' ', 26));
        Console.WriteLine("?");

        Console.Write("? ");
        WriteCheckbox("Auto-scroll console", settings.AutoScrollConsole, 22);
        Console.Write($" CPU Threads: {settings.CpuThreads,-4}");

        Console.Write(new string(' ', 22));
        Console.WriteLine("?");

        Console.Write("? ");
        WriteCheckbox("Enable GPU", settings.EnableGPU, 14);

        if (settings.EnableGPU && !string.IsNullOrEmpty(gpuDeviceName))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($" GPU {settings.GpuDeviceIndex}: {gpuDeviceName}".PadRight(54));
            Console.ResetColor();
        }
        else if (settings.EnableGPU)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($" GPU {settings.GpuDeviceIndex}: (searching...)".PadRight(54));
            Console.ResetColor();
        }
        else
        {
            Console.Write(new string(' ', 54));
        }
        Console.WriteLine("?");

        // Multi-GPU cluster settings
        if (multiGpuActive && totalGpuCount > 1)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("? ");
            Console.Write("[MULTI-GPU CLUSTER] ");
            Console.ResetColor();
            Console.Write($"{totalGpuCount} GPUs  |  ");
            Console.Write($"Spectral: {settings.MultiGpu.SpectralWorkerCount switch { 0 => "auto", var n => n.ToString() }}  |  ");
            Console.Write($"MCMC: {settings.MultiGpu.McmcWorkerCount switch { 0 => "auto", var n => n.ToString() }}  |  ");
            Console.Write($"Interval: {settings.MultiGpu.SnapshotInterval}");
            Console.WriteLine("".PadRight(8) + "?");
        }
        else if (settings.MultiGpu.Enabled && settings.EnableGPU)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("? ");
            Console.Write("[MULTI-GPU] ");
            Console.ResetColor();
            Console.Write("Enabled but only 1 GPU detected - running in single-GPU mode");
            Console.WriteLine("".PadRight(10) + "?");
        }

        Console.WriteLine("????????????????????????????????????????????????????????????????????????????");
    }

    private void RenderLiveMetricsPlaceholder()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("?? Live Metrics ?????????????????????????????????????????????????????????????");
        Console.ResetColor();
        Console.WriteLine("? Current step:     0          ? Total steps:    0                          ?");
        Console.WriteLine("? Avg excited:      0.00       ? Max excited:    0                          ?");
        Console.WriteLine("? Avalanches:       0          ? Measurement:    NOT CONFIGURED             ?");
        Console.WriteLine("? Global Nbr:       0.000      ? Global Spont:   0.000                      ?");
        Console.WriteLine("? Strong edges:     0          ? Largest cluster:0                          ?");
        Console.WriteLine("? Heavy mass:       0.00       ? Spectrum logs:  off                        ?");
        Console.WriteLine("? c_eff:            0          ?                                            ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????????????????");
        Console.WriteLine();
        Console.WriteLine("Step: 0/0        Excited: 0 (avg 0.00)  Giant:0%  | E:0 | <k>:0.0 | Comp:0");
    }

    private void WriteCheckbox(string label, bool enabled, int width)
    {
        if (enabled)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[X] ");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[ ] ");
        }
        Console.ResetColor();

        string displayLabel = label.Length > width ? label[..width] : label;
        Console.Write(displayLabel.PadRight(width));
    }

    /// <summary>
    /// Updates the progress bar and live metrics.
    /// </summary>
    public void UpdateProgress(LiveMetrics metrics)
    {
        lock (_renderLock)
        {
            if (_metricsStartLine < 0 || _lastProgressLine < 0) return;

            try
            {
                // Update live metrics section
                int lineOffset = 1; // Skip the header line
                UpdateMetricsLine(lineOffset++, $"? Current step:     {metrics.CurrentStep,-10} ? Total steps:    {metrics.TotalSteps,-26} ?");
                UpdateMetricsLine(lineOffset++, $"? Avg excited:      {metrics.AvgExcited,-10:F2} ? Max excited:    {metrics.MaxExcited,-26} ?");
                UpdateMetricsLine(lineOffset++, $"? Avalanches:       {metrics.Avalanches,-10} ? Measurement:    {metrics.MeasurementStatus,-26} ?");
                UpdateMetricsLine(lineOffset++, $"? Global Nbr:       {metrics.GlobalNbr,-10:F3} ? Global Spont:   {metrics.GlobalSpont,-26:F3} ?");
                UpdateMetricsLine(lineOffset++, $"? Strong edges:     {metrics.StrongEdges,-10} ? Largest cluster:{metrics.LargestCluster,-26} ?");
                UpdateMetricsLine(lineOffset++, $"? Heavy mass:       {metrics.HeavyMass,-10:F2} ? Spectrum logs:  {(metrics.SpectrumLogs ? "on" : "off"),-26} ?");
                UpdateMetricsLine(lineOffset++, $"? c_eff:            {metrics.CEffective,-10} ?                                            ?");

                // Update status bar
                Console.SetCursorPosition(0, _lastProgressLine);
                RenderStatusBar(metrics);
            }
            catch
            {
                // Ignore cursor positioning errors
            }
        }
    }

    private void UpdateMetricsLine(int offset, string content)
    {
        Console.SetCursorPosition(0, _metricsStartLine + offset);
        Console.Write(content);
    }

    private void RenderStatusBar(LiveMetrics metrics)
    {
        double progress = metrics.TotalSteps > 0 ? (double)metrics.CurrentStep / metrics.TotalSteps : 0;
        int barWidth = 30;
        int filled = (int)(progress * barWidth);

        var sb = new StringBuilder();
        sb.Append("Step: ");
        sb.Append($"{metrics.CurrentStep}/{metrics.TotalSteps}".PadRight(15));

        // Progress bar
        sb.Append('[');
        sb.Append(new string('?', filled));
        sb.Append(new string('?', barWidth - filled));
        sb.Append("] ");
        sb.Append($"{progress:P0}".PadRight(6));

        // Key metrics
        sb.Append($" Excited:{metrics.Excited} (avg {metrics.AvgExcited:F2})");
        sb.Append($" Giant:{metrics.GiantPercent:P0}");
        sb.Append($" | E:{metrics.StrongEdges}");
        sb.Append($" | <k>:{metrics.AvgDegree:F1}");
        sb.Append($" | d_S:{metrics.SpectralDimension:F2}");

        Console.Write(sb.ToString().PadRight(Console.WindowWidth - 1));
    }

    /// <summary>
    /// Renders final completion message.
    /// </summary>
    public void RenderCompletion(string resultFilePath, TimeSpan elapsed)
    {
        lock (_renderLock)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("??????????????????????????????????????????????????????????????????????????????");
            Console.WriteLine("?                        SIMULATION COMPLETE                                 ?");
            Console.WriteLine("??????????????????????????????????????????????????????????????????????????????");
            Console.ResetColor();
            Console.WriteLine($"  Elapsed time: {elapsed:hh\\:mm\\:ss\\.fff}");
            Console.WriteLine($"  Results saved to: {resultFilePath}");
            Console.WriteLine();
            Console.CursorVisible = true;
        }
    }

    /// <summary>
    /// Renders error message.
    /// </summary>
    public void RenderError(string message)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
        Console.CursorVisible = true;
    }

    /// <summary>
    /// Renders cancellation message.
    /// </summary>
    public void RenderCancellation(string resultFilePath)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("??????????????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?                       SIMULATION CANCELLED                                 ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????????????????????");
        Console.ResetColor();
        Console.WriteLine($"  Partial results saved to: {resultFilePath}");
        Console.CursorVisible = true;
    }
}

/// <summary>
/// Live metrics for console display updates.
/// </summary>
public sealed class LiveMetrics
{
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public int Excited { get; set; }
    public double AvgExcited { get; set; }
    public int MaxExcited { get; set; }
    public int Avalanches { get; set; }
    public string MeasurementStatus { get; set; } = "NOT CONFIGURED";
    public double GlobalNbr { get; set; }
    public double GlobalSpont { get; set; }
    public int StrongEdges { get; set; }
    public int LargestCluster { get; set; }
    public double HeavyMass { get; set; }
    public bool SpectrumLogs { get; set; }
    public int CEffective { get; set; }
    public double GiantPercent { get; set; }
    public double AvgDegree { get; set; }
    public double SpectralDimension { get; set; }
}
