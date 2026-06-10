using System.Text;

namespace AudioHQ.Core;

/// <summary>Dead-simple file logger: audiohq.log next to the executable.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "audiohq.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
                // logging must never take the app down
            }
        }
    }
}
