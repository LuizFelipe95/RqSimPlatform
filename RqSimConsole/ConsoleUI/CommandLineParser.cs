namespace RqSimConsole.ConsoleUI;

/// <summary>
/// Command-line argument parser for RqSimConsole.
/// Supports: -loadparam param.json -usegpu 0 -startnow
/// </summary>
public sealed class CommandLineParser
{
    /// <summary>
    /// Parses command-line arguments into typed options.
    /// </summary>
    /// <param name="args">Command-line arguments array</param>
    /// <returns>Parsed options or null if parsing failed</returns>
    public static CommandLineOptions? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return null;
        }

        var options = new CommandLineOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();

            switch (arg)
            {
                case "-loadparam":
                case "--loadparam":
                case "-p":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: -loadparam requires a file path argument");
                        return null;
                    }
                    options.ConfigFilePath = args[++i];
                    break;

                case "-usegpu":
                case "--usegpu":
                case "-g":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: -usegpu requires a GPU index argument");
                        return null;
                    }
                    if (!int.TryParse(args[++i], out int gpuIndex) || gpuIndex < 0)
                    {
                        Console.Error.WriteLine($"Error: Invalid GPU index '{args[i]}'. Must be a non-negative integer.");
                        return null;
                    }
                    options.GpuIndex = gpuIndex;
                    options.UseGpu = true;
                    break;

                case "-startnow":
                case "--startnow":
                case "-s":
                    options.StartNow = true;
                    break;

                case "-output":
                case "--output":
                case "-o":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: -output requires a directory path argument");
                        return null;
                    }
                    options.OutputDirectory = args[++i];
                    break;

                case "-cputhreads":
                case "--cputhreads":
                case "-t":
                case "-threads": // Added alias
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: -cputhreads requires a thread count argument");
                        return null;
                    }
                    if (!int.TryParse(args[++i], out int threadCount) || threadCount < 1)
                    {
                        Console.Error.WriteLine($"Error: Invalid thread count '{args[i]}'. Must be a positive integer.");
                        return null;
                    }
                    options.CpuThreads = threadCount;
                    break;

                case "-steps":
                case "--steps":
                case "-n":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: -steps requires a step count argument");
                        return null;
                    }
                    if (!int.TryParse(args[++i], out int stepCount) || stepCount < 1)
                    {
                        Console.Error.WriteLine($"Error: Invalid step count '{args[i]}'. Must be a positive integer.");
                        return null;
                    }
                    options.Steps = stepCount;
                    break;

                case "-help":
                case "--help":
                case "-h":
                case "-?":
                    PrintUsage();
                    return null;

                default:
                    // Check if it's an unknown flag
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Warning: Unknown argument '{args[i]}' - ignoring");
                    }
                    break;
            }
        }

        // Validate required arguments
        if (string.IsNullOrEmpty(options.ConfigFilePath))
        {
            Console.Error.WriteLine("Error: -loadparam is required");
            PrintUsage();
            return null;
        }

        if (!File.Exists(options.ConfigFilePath))
        {
            Console.Error.WriteLine($"Error: Configuration file not found: {options.ConfigFilePath}");
            return null;
        }

        return options;
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("RqSimConsole - Console-based RQ Simulation Runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  rqsimconsole.exe -loadparam <config.json> [options]");
        Console.WriteLine();
        Console.WriteLine("Required:");
        Console.WriteLine("  -loadparam, -p <file>   JSON configuration file path");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -usegpu, -g <index>     Use GPU with specified index (0 = first GPU)");
        Console.WriteLine("  -startnow, -s           Start simulation immediately without prompt");
        Console.WriteLine("  -output, -o <dir>       Output directory for results (default: current)");
        Console.WriteLine("  -cputhreads, -t <n>     Number of CPU threads (default: auto)");
        Console.WriteLine("  -steps, -n <n>          Number of simulation steps (overrides config)");
        Console.WriteLine("  -help, -h               Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  rqsimconsole.exe -loadparam config.json -usegpu 0 -startnow");
        Console.WriteLine("  rqsimconsole.exe -p params.json -g 0 -s -o ./results");
        Console.WriteLine();
    }
}

/// <summary>
/// Parsed command-line options.
/// </summary>
public sealed class CommandLineOptions
{
    /// <summary>Path to JSON configuration file</summary>
    public string ConfigFilePath { get; set; } = string.Empty;

    /// <summary>Whether to use GPU acceleration</summary>
    public bool UseGpu { get; set; }

    /// <summary>GPU device index (0 = first GPU)</summary>
    public int GpuIndex { get; set; }

    /// <summary>Start simulation immediately without prompt</summary>
    public bool StartNow { get; set; }

    /// <summary>Output directory for result files</summary>
    public string OutputDirectory { get; set; } = ".";

    /// <summary>Number of CPU threads (0 = auto)</summary>
    public int CpuThreads { get; set; }

    /// <summary>Number of simulation steps (0 = use config)</summary>
    public int Steps { get; set; }
}
