using System;
using System.IO;
using System.Text;

namespace TraceZero.Services;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechArchive", "TraceZero");
    private static readonly string LogPath = Path.Combine(LogDirectory, "startup.log");

    public static string CurrentLogPath => LogPath;

    public static void Log(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    public static void LogException(string source, Exception exception)
    {
        Log($"{source}: {exception}");
    }
}
