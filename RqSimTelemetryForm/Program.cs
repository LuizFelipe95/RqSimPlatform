namespace RqSimTelemetryForm
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Force classic (light) theme — no dark mode
            Application.SetColorMode(SystemColorMode.Classic);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, ex) =>
            {
                MessageBox.Show(
                    $"Unhandled error: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                    "RqSim Telemetry — Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            ApplicationConfiguration.Initialize();
            Application.Run(new TelemetryForm());
        }
    }
}