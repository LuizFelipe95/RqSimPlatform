using System;

namespace RqSimConsole.ServerMode;

internal static class ConsoleExceptionLogger
{
    public static void Log(string prefix, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(ex);

        Console.WriteLine($"{prefix} {ex.GetType().FullName}: {ex.Message}");
        Console.WriteLine("[Exception.ToString()]");
        Console.WriteLine(ex.ToString());

        var inner = ex.InnerException;
        int depth = 0;
        while (inner is not null && depth++ < 10)
        {
            Console.WriteLine($"[InnerException:{depth}] {inner.GetType().FullName}: {inner.Message}");
            if (!string.IsNullOrWhiteSpace(inner.StackTrace))
            {
                Console.WriteLine("[InnerStackTrace]");
                Console.WriteLine(inner.StackTrace);
            }

            inner = inner.InnerException;
        }
    }
}
