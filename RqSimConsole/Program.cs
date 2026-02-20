using RqSimConsole.ConsoleUI;
using RqSimConsole.ServerMode;

namespace RqSimConsole;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--server-mode", StringComparison.OrdinalIgnoreCase))
            || args.Any(a => string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase)))
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var host = new ServerModeHost();
            return await host.RunAsync(cts.Token).ConfigureAwait(false);
        }

        CommandLineOptions? options;

        if (args.Length == 0)
        {
            // Interactive mode
            options = InteractiveMenu.Show();
            if (options == null)
            {
                return 0; // User exited menu
            }
        }
        else
        {
            // Parse command-line arguments
            options = CommandLineParser.Parse(args);
            if (options == null)
            {
                return 1;
            }
        }

        // Create and run simulation
        var runner = new SimulationRunner(options);
        return await runner.RunAsync();
    }
}