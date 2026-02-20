
using System.Diagnostics;
using RqSimForms.ProcessesDispatcher.Managers;

namespace RqSimForms;

static class Program
{
    private static LifeCycleManager? _lifeCycleManager;

    [STAThread]
    static void Main()
    {
        // Регистрация глобальных обработчиков исключений
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Application.ThreadException += OnThreadException;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();

        try
        {
            // Создаём LifeCycleManager - он будет инжектиться в Form_Main
            _lifeCycleManager = new LifeCycleManager();

            Application.Run(new Form_Main_RqSim(_lifeCycleManager));
        }
        finally
        {
            // Гарантированная очистка при любом завершении
            SafeCleanup();
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Trace.WriteLine($"[FATAL] Unhandled exception: {ex?.Message}");

        // Аварийная очистка
        SafeCleanup();

        // Логгирование в файл (опционально)
        LogCrash(ex);

        // Выход с ненулевым кодом
        Environment.Exit(1);
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        Trace.WriteLine($"[ERROR] Thread exception: {e.Exception.Message}");

        // Для UI-потока показываем диалог с возможностью продолжить
        var result = MessageBox.Show(
            $"Произошла ошибка:\n{e.Exception.Message}\n\nПродолжить работу?",
            "Ошибка",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);

        if (result == DialogResult.No)
        {
            SafeCleanup();
            Application.Exit();
        }
    }

    private static void SafeCleanup()
    {
        try
        {
            _lifeCycleManager?.Cleanup();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ERROR] Cleanup failed: {ex.Message}");
        }
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RqSimulator",
                $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);

            File.WriteAllText(crashLogPath, $"""
                RqSimulator Crash Report
                Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                Exception: {ex?.GetType().Name}
                Message: {ex?.Message}
                StackTrace:
                {ex?.StackTrace}
                """);
        }
        catch
        {
            // Игнорируем ошибки логгирования
        }
    }
}
