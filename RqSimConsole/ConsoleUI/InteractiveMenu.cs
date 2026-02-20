using System;
using System.IO;
using RQSimulation.GPUOptimized;

namespace RqSimConsole.ConsoleUI;

/// <summary>
/// Interactive menu for configuring simulation parameters when no command-line arguments are provided.
/// </summary>
public static class InteractiveMenu
{
    public static CommandLineOptions? Show()
    {
        var options = new CommandLineOptions();
        bool exit = false;
        bool start = false;

        while (!exit && !start)
        {
            Console.Clear();
            Console.WriteLine("=== RqSimConsole Interactive Mode ===");
            Console.WriteLine();
            Console.WriteLine($"1. Load Configuration File (Current: {(string.IsNullOrEmpty(options.ConfigFilePath) ? "None" : options.ConfigFilePath)})");
            Console.WriteLine($"2. Set CPU Threads (Current: {(options.CpuThreads == 0 ? "Auto" : options.CpuThreads.ToString())})");
            Console.WriteLine($"3. Set Simulation Steps (Current: {(options.Steps == 0 ? "From Config" : options.Steps.ToString())})");
            
            string gpuStatus = options.UseGpu 
                ? $"Enabled (Index {options.GpuIndex}: {OptimizedGpuSimulationEngine.GetGpuName(options.GpuIndex)})" 
                : "Disabled";
            Console.WriteLine($"4. Toggle GPU (Current: {gpuStatus})");
            
            Console.WriteLine($"5. Set Output Directory (Current: {options.OutputDirectory})");
            Console.WriteLine("6. Preview current JSON settings");
            Console.WriteLine("7. Preview pipeline/plugins/shaders (from config)");
            Console.WriteLine("8. Show PhysicsConstants (all constants & flags)");
            Console.WriteLine("9. Start Simulation");
            Console.WriteLine("0. Exit");
            Console.WriteLine();
            Console.Write("Select an option: ");

            string? input = Console.ReadLine();

            switch (input)
            {
                case "1":
                    Console.Write("Enter configuration file path: ");
                    string? path = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (File.Exists(path))
                        {
                            options.ConfigFilePath = path;
                        }
                        else
                        {
                            Console.WriteLine("File not found. Press any key to continue...");
                            Console.ReadKey();
                        }
                    }
                    break;

                case "2":
                    Console.Write("Enter number of CPU threads (0 for auto): ");
                    if (int.TryParse(Console.ReadLine(), out int threads) && threads >= 0)
                    {
                        options.CpuThreads = threads;
                    }
                    else
                    {
                        Console.WriteLine("Invalid number. Press any key to continue...");
                        Console.ReadKey();
                    }
                    break;

                case "3":
                    Console.Write("Enter number of steps (0 for config default): ");
                    if (int.TryParse(Console.ReadLine(), out int steps) && steps >= 0)
                    {
                        options.Steps = steps;
                    }
                    else
                    {
                        Console.WriteLine("Invalid number. Press any key to continue...");
                        Console.ReadKey();
                    }
                    break;

                case "4":
                    if (options.UseGpu)
                    {
                        options.UseGpu = false;
                    }
                    else
                    {
                        options.UseGpu = true;
                        Console.Write("Enter GPU Index (default 0): ");
                        string? gpuInput = Console.ReadLine();
                        if (int.TryParse(gpuInput, out int gpuIndex) && gpuIndex >= 0)
                        {
                            options.GpuIndex = gpuIndex;
                        }
                        else
                        {
                            options.GpuIndex = 0;
                        }

                        string selectedGpuName = OptimizedGpuSimulationEngine.GetGpuName(options.GpuIndex);
                        Console.WriteLine($"Selected GPU: {selectedGpuName}");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                    }
                    break;

                case "5":
                    Console.Write("Enter output directory: ");
                    string? outDir = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(outDir))
                    {
                        options.OutputDirectory = outDir;
                    }
                    break;

                case "6":
                    if (string.IsNullOrWhiteSpace(options.ConfigFilePath))
                    {
                        Console.WriteLine("No config selected. Use option 1 first.");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey(true);
                        break;
                    }

                    ConsoleDiagnosticsScreens.ShowCurrentJsonSettings(options.ConfigFilePath);
                    break;

                case "7":
                    if (string.IsNullOrWhiteSpace(options.ConfigFilePath))
                    {
                        Console.WriteLine("No config selected. Use option 1 first.");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey(true);
                        break;
                    }

                    if (!File.Exists(options.ConfigFilePath))
                    {
                        Console.WriteLine($"File not found: {options.ConfigFilePath}");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey(true);
                        break;
                    }

                    try
                    {
                        var json = File.ReadAllText(options.ConfigFilePath);
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                            AllowTrailingCommas = true,
                            TypeInfoResolver = RqSimConsoleJsonContext.Default
                        };
                        var config = System.Text.Json.JsonSerializer.Deserialize<ConsoleConfig>(json, jsonOptions);
                        if (config == null)
                        {
                            Console.WriteLine("Failed to parse config.");
                            Console.WriteLine("Press any key to continue...");
                            Console.ReadKey(true);
                            break;
                        }

                        ConsoleDiagnosticsScreens.ShowPipeline(config);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load config: {ex.Message}");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey(true);
                    }
                    break;

                case "8":
                    ConsoleDiagnosticsScreens.ShowPhysicsConstants();
                    break;

                case "9":
                    if (string.IsNullOrEmpty(options.ConfigFilePath))
                    {
                        Console.WriteLine("Error: Configuration file is required. Press any key to continue...");
                        Console.ReadKey();
                    }
                    else
                    {
                        start = true;
                        options.StartNow = true; // Auto-start when running from menu
                    }
                    break;

                case "0":
                    exit = true;
                    break;

                default:
                    break;
            }
        }

        return start ? options : null;
    }
}
