using System.Globalization;
using System.IO;

namespace BringLater;

/// <summary>
/// A small rolling log for bug reports. Window titles are never written here; they can be private.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly Lock Gate = new();
    private static string File => Path.Combine(Paths.Logs, "bringlater.log");

    public static void Info(string message) => Write("info", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("error", exception is null ? message : $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.Logs);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > MaxBytes)
                    System.IO.File.Move(File, File + ".1", overwrite: true);
                var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                System.IO.File.AppendAllText(File, $"{stamp} [{level}] {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
