using System.Text;
using System.IO;

namespace PasswordManager.Services;

public static class LogService
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;
    private static bool _loggingAvailable = true;

    static LogService()
    {
        string appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PasswordManager");

        LogDirectory = Path.Combine(appDirectory, "logs");
    }

    public static string LogDirectory { get; }

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogDirectory);
                WriteEntry("INFO", "Logger", "Logging initialized.");
                WriteEntry("INFO", "Environment", $".NET {Environment.Version}; OS {Environment.OSVersion}; Machine {Environment.MachineName}");
            }
            catch
            {
                _loggingAvailable = false;
            }

            _initialized = true;
        }
    }

    public static void Info(string context, string message)
    {
        EnsureInitialized();
        WriteEntry("INFO", context, message);
    }

    public static void Warning(string context, string message)
    {
        EnsureInitialized();
        WriteEntry("WARN", context, message);
    }

    public static void Error(string context, Exception exception)
    {
        EnsureInitialized();
        WriteEntry("ERROR", context, BuildExceptionText(exception));
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    private static void WriteEntry(string level, string context, string message)
    {
        if (!_loggingAvailable)
        {
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        StringBuilder builder = new();
        builder.Append('[').Append(timestamp).Append("] ");
        builder.Append(level).Append(" [").Append(context).Append("] ");
        builder.AppendLine(message);

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(CurrentLogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            _loggingAvailable = false;
        }
    }

    private static string BuildExceptionText(Exception exception)
    {
        StringBuilder builder = new();
        int depth = 0;
        Exception? current = exception;

        while (current is not null)
        {
            if (depth > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"Inner exception {depth}:");
            }

            builder.AppendLine($"{current.GetType().FullName}: {current.Message}");
            builder.AppendLine(current.StackTrace ?? "(no stack trace)");

            current = current.InnerException;
            depth++;
        }

        return builder.ToString().TrimEnd();
    }
}
