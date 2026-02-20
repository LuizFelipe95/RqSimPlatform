using System.Text.Json;
using RqSimForms.Forms.Interfaces;
using RQSimulation;

namespace RqSimConsole.ConsoleUI;

public static class ConsoleDiagnosticsScreens
{
    public static void ShowCurrentJsonSettings(string configFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            Console.WriteLine("Config file path is empty.");
            Pause();
            return;
        }

        if (!File.Exists(configFilePath))
        {
            Console.WriteLine($"Config file not found: {configFilePath}");
            Pause();
            return;
        }

        Console.Clear();
        Console.WriteLine("=== Current JSON settings ===");
        Console.WriteLine(configFilePath);
        Console.WriteLine();

        try
        {
            var json = File.ReadAllText(configFilePath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            string pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(pretty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read JSON: {ex.Message}");
        }

        Pause();
    }

    public static void ShowDeviceExecutionStatus(ConsoleConfig? config, string? gpuDeviceName)
    {
        Console.Clear();
        Console.WriteLine("=== Device execution status ===");
        Console.WriteLine();

        if (config is null)
        {
            Console.WriteLine("No configuration loaded.");
            Pause();
            return;
        }

        Console.WriteLine($"CPU threads: {(config.RunSettings.CpuThreads > 0 ? config.RunSettings.CpuThreads : Environment.ProcessorCount)}");
        Console.WriteLine($"GPU enabled (requested): {config.RunSettings.EnableGPU}");
        Console.WriteLine($"GPU index: {config.RunSettings.GpuDeviceIndex}");
        Console.WriteLine($"GPU detected: {(string.IsNullOrWhiteSpace(gpuDeviceName) ? "No" : "Yes")}");
        if (!string.IsNullOrWhiteSpace(gpuDeviceName))
        {
            Console.WriteLine($"GPU device: {gpuDeviceName}");
        }

        Console.WriteLine();
        Console.WriteLine("Workloads:");
        Console.WriteLine($"- Spectral walkers: {(config.RunSettings.MultiGpu.Enabled && config.RunSettings.EnableGPU ? "GPU workers (if 2+ GPUs)" : "single device (CPU/GPU depending on engine)")}");
        Console.WriteLine($"- MCMC: {(config.RunSettings.MultiGpu.Enabled && config.RunSettings.EnableGPU ? "GPU workers (if 2+ GPUs)" : "single device (CPU/GPU depending on engine)")}");
        Console.WriteLine("- Quantum waveform evolution: simulation engine (CPU, optional GPU acceleration if enabled)");

        Pause();
    }

    public static void ShowEngineStatistics(RqSimForms.Forms.Interfaces.RqSimEngineApi api, ConsoleConfig? config, string? gpuDeviceName)
    {
        ArgumentNullException.ThrowIfNull(api);

        Console.Clear();
        Console.WriteLine("=== Engine statistics (live) ===");
        Console.WriteLine();

        Console.WriteLine($"Step: {api.Dispatcher.LiveStep} / {api.Dispatcher.LiveTotalSteps}");
        Console.WriteLine($"Excited: {api.Dispatcher.LiveExcited}");
        Console.WriteLine($"Strong edges: {api.Dispatcher.LiveStrongEdges}");
        Console.WriteLine($"Largest cluster: {api.Dispatcher.LiveLargestCluster}");
        Console.WriteLine($"Heavy mass: {api.Dispatcher.LiveHeavyMass:F4}");
        Console.WriteLine($"Avg degree: {api.Dispatcher.LiveAvgDegree:F3}");
        Console.WriteLine($"Spectral dim: {api.Dispatcher.LiveSpectralDim:F4}");

        Console.WriteLine();
        Console.WriteLine($"GPU active (detected): {(!string.IsNullOrWhiteSpace(gpuDeviceName))}");
        if (!string.IsNullOrWhiteSpace(gpuDeviceName))
        {
            Console.WriteLine($"GPU device: {gpuDeviceName}");
        }

        Console.WriteLine();
        Console.WriteLine($"Multi-GPU active: {api.IsMultiGpuActive}");
        if (api.IsMultiGpuActive)
        {
            Console.WriteLine($"Cluster size: {api.GpuClusterSize}");
            var status = api.GetMultiGpuStatus();
            if (status != null)
            {
                Console.WriteLine($"Spectral workers: {status.BusySpectralWorkers}/{status.SpectralWorkerCount} busy");
                Console.WriteLine($"MCMC workers: {status.BusyMcmcWorkers}/{status.McmcWorkerCount} busy");
                Console.WriteLine($"Pending snapshots: {status.PendingSnapshots}");
                Console.WriteLine($"Total spectral results: {status.TotalSpectralResults}");
                Console.WriteLine($"Total MCMC results: {status.TotalMcmcResults}");
                Console.WriteLine($"Latest spectral (workers): {api.GetMultiGpuSpectralDimension()?.ToString("F4") ?? "n/a"}");
            }
        }

        if (config != null)
        {
            Console.WriteLine();
            Console.WriteLine("(Configured) Multi-GPU snapshot interval: " + config.RunSettings.MultiGpu.SnapshotInterval);
        }

        Pause();
    }

    public static void ShowPipeline(ConsoleConfig? config)
    {
        Console.Clear();
        Console.WriteLine("=== Current pipeline (physics / visualization / shaders) ===");
        Console.WriteLine();

        if (config is null)
        {
            Console.WriteLine("No configuration loaded.");
            Pause();
            return;
        }

        Console.WriteLine("Physics modules:");
        foreach (var (name, enabled) in EnumeratePhysicsModules(config.PhysicsModules))
        {
            Console.WriteLine($"- {(enabled ? "[X]" : "[ ]")} {name}");
        }

        Console.WriteLine();
        Console.WriteLine("Visualization / shaders:");
        Console.WriteLine("- Console mode does not compile DX12 shaders.");
        Console.WriteLine("- Active renderer/shader variants are owned by `RqSimRenderingEngine` / `Dx12WinForm`.");

        Console.WriteLine();
        Console.WriteLine("Plugins:");
        Console.WriteLine("- Physics plugins: PluginManager UI project (not currently exposed in console runtime).");
        Console.WriteLine("- Visualization plugins: not currently exposed in console runtime.");

        Pause();
    }

    /// <summary>
    /// Displays all physics constants from PhysicsConstants class.
    /// </summary>
    public static void ShowPhysicsConstants()
    {
        Console.Clear();
        Console.WriteLine("????????????????????????????????????????????????????????????");
        Console.WriteLine("           RQ-SIMULATOR PHYSICS CONSTANTS                   ");
        Console.WriteLine("????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Fundamental Constants
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("???? FUNDAMENTAL CONSTANTS (Planck Units) ????????????????????");
        Console.ResetColor();
        Console.WriteLine($"? c = {PhysicsConstants.C}, ? = {PhysicsConstants.HBar}, G = {PhysicsConstants.G}, k_B = {PhysicsConstants.KBoltzmann}");
        Console.WriteLine($"? l_P = {PhysicsConstants.PlanckLength}, t_P = {PhysicsConstants.PlanckTime}, m_P = {PhysicsConstants.PlanckMass}");
        Console.WriteLine("????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Gauge Couplings
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("???? GAUGE COUPLINGS (dimensionless) ?????????????????????????");
        Console.ResetColor();
        Console.WriteLine($"? ? = 1/{1.0 / PhysicsConstants.FineStructureConstant:F2} ? {PhysicsConstants.FineStructureConstant:E4}");
        Console.WriteLine($"? ?_s(M_Z) = {PhysicsConstants.StrongCouplingConstant:F4}");
        Console.WriteLine($"? sin??_W = {PhysicsConstants.WeakMixingAngle:F5}");
        Console.WriteLine($"? g_W = {PhysicsConstants.WeakCouplingConstant:F4}, g' = {PhysicsConstants.HyperchargeCoupling:F4}");
        Console.WriteLine("????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // RQ-Hypothesis Flags
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("???? RQ-HYPOTHESIS FLAGS ??????????????????????????????????????");
        Console.ResetColor();
        WriteFlag("Hamiltonian Gravity", PhysicsConstants.UseHamiltonianGravity);
        WriteFlag("Natural Dimension Emergence", PhysicsConstants.EnableNaturalDimensionEmergence);
        WriteFlag("Lapse-Synchronized", PhysicsConstants.EnableLapseSynchronizedGeometry);
        WriteFlag("Topological Parity", PhysicsConstants.EnableTopologicalParity);
        WriteFlag("Topology Energy Compensation", PhysicsConstants.EnableTopologyEnergyCompensation);
        WriteFlag("Plaquette Yang-Mills", PhysicsConstants.EnablePlaquetteYangMills);
        WriteFlag("Symplectic Gauge Evolution", PhysicsConstants.EnableSymplecticGaugeEvolution);
        WriteFlag("Wilson Loop Protection", PhysicsConstants.EnableWilsonLoopProtection);
        WriteFlag("Vacuum Energy Reservoir", PhysicsConstants.EnableVacuumEnergyReservoir);
        WriteFlag("Ollivier-Ricci Curvature", PhysicsConstants.PreferOllivierRicciCurvature);
        Console.WriteLine("????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Spectral Action
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("???? SPECTRAL ACTION (NCG) ????????????????????????????????????");
        Console.ResetColor();
        WriteFlag("Spectral Action Mode", PhysicsConstants.SpectralActionConstants.EnableSpectralActionMode);
        Console.WriteLine($"? ?_cutoff = {PhysicsConstants.SpectralActionConstants.LambdaCutoff}");
        Console.WriteLine($"? Target d_S = {PhysicsConstants.SpectralActionConstants.TargetSpectralDimension}");
        Console.WriteLine($"? f? = {PhysicsConstants.SpectralActionConstants.F0_Cosmological}, f? = {PhysicsConstants.SpectralActionConstants.F2_EinsteinHilbert}, f? = {PhysicsConstants.SpectralActionConstants.F4_Weyl}");
        Console.WriteLine("????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Wheeler-DeWitt
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("???? WHEELER-DEWITT CONSTRAINT ????????????????????????????????");
        Console.ResetColor();
        WriteFlag("Strict Mode", PhysicsConstants.WheelerDeWittConstants.EnableStrictMode);
        Console.WriteLine($"? ? = {PhysicsConstants.WheelerDeWittConstants.GravitationalCoupling}");
        Console.WriteLine($"? Tolerance = {PhysicsConstants.WheelerDeWittConstants.ConstraintTolerance}");
        Console.WriteLine($"? ?_Lagrange = {PhysicsConstants.WheelerDeWittConstants.ConstraintLagrangeMultiplier}");
        Console.WriteLine("????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Simulation Parameters
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("???? SIMULATION PARAMETERS ????????????????????????????????????");
        Console.ResetColor();
        Console.WriteLine($"? Base Timestep = {PhysicsConstants.BaseTimestep}");
        Console.WriteLine($"? Gravitational Coupling = {PhysicsConstants.GravitationalCoupling}");
        Console.WriteLine($"? Warmup Duration = {PhysicsConstants.WarmupDuration} steps");
        Console.WriteLine($"? Topology Update Interval = {PhysicsConstants.TopologyUpdateInterval} steps");
        Console.WriteLine($"? Edge Weight Quantum = {PhysicsConstants.EdgeWeightQuantum}");
        Console.WriteLine($"? Geometry Inertia Mass = {PhysicsConstants.GeometryInertiaMass}"); // HamiltonianMomentumTerm
        Console.WriteLine($"? Wilson Parameter (r) = {PhysicsConstants.WilsonParameter}");
        Console.WriteLine("????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        Console.WriteLine("????????????????????????????????????????????????????????????????");

        Pause();
    }

    private static void WriteFlag(string name, bool enabled)
    {
        Console.Write("? ");
        if (enabled)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[?] ");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[?] ");
        }
        Console.ResetColor();
        Console.WriteLine(name);
    }

    private static IEnumerable<(string name, bool enabled)> EnumeratePhysicsModules(PhysicsModulesConfig modules)
    {
        yield return ("Quantum Driven States", modules.QuantumDrivenStates);
        yield return ("Relational Time", modules.RelationalTime);
        yield return ("Topological Censorship", modules.TopologicalCensorship);
        yield return ("Spacetime Physics", modules.SpacetimePhysics);
        yield return ("Relational Yang-Mills", modules.RelationalYangMills);
        yield return ("Spinor Field", modules.SpinorField);
        yield return ("Network Gravity", modules.NetworkGravity);
        yield return ("Vacuum Fluctuations", modules.VacuumFluctuations);
        yield return ("Unified Physics Step", modules.UnifiedPhysicsStep);
        yield return ("Black Hole Physics", modules.BlackHolePhysics);
        yield return ("Enforce Gauge Constraints", modules.EnforceGaugeConstraints);
        yield return ("Yang-Mills Gauge", modules.YangMillsGauge);
        yield return ("Causal Rewiring", modules.CausalRewiring);
        yield return ("Enhanced Klein-Gordon", modules.EnhancedKleinGordon);
        yield return ("Topological Protection", modules.TopologicalProtection);
        yield return ("Internal Time", modules.InternalTime);
        yield return ("Validate Energy Conservation", modules.ValidateEnergyConservation);
        yield return ("Spectral Geometry", modules.SpectralGeometry);
        yield return ("Mexican Hat Potential", modules.MexicanHatPotential);
        yield return ("Quantum Graphity", modules.QuantumGraphity);
        yield return ("Geometry Momenta", modules.GeometryMomenta);
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
    }
}
