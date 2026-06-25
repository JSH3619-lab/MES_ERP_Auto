using System.IO;

namespace UnimesAutomation;

public sealed class SimpleLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _writer;

    public event Action<string>? LineWritten;

    public SimpleLogger(string logPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _writer = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write("INFO", message);

    // 예상된 UIA 패턴 미지원→폴백 같은 trace성 잡음. 파일엔 남기되 GUI 콘솔엔 표시 안 함.
    public void Debug(string message) => Write("DEBUG", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");
        Write("ERROR", exception.StackTrace ?? "(no stack trace)");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_lock)
        {
            Console.WriteLine(line);
            _writer.WriteLine(line);
        }

        LineWritten?.Invoke(line);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}

public static class DirectoryLayout
{
    public static RuntimePaths Create(string rootDirectory)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var logs = Path.Combine(rootDirectory, "logs");
        var screenshots = Path.Combine(rootDirectory, "screenshots");
        var output = Path.Combine(rootDirectory, "output");

        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(screenshots);
        Directory.CreateDirectory(output);

        return new RuntimePaths
        {
            RootDirectory = rootDirectory,
            LogsDirectory = logs,
            ScreenshotsDirectory = screenshots,
            OutputDirectory = output,
            Timestamp = timestamp,
            RunLogPath = Path.Combine(logs, $"run_{timestamp}.log"),
            UiDumpPath = Path.Combine(logs, $"ui_dump_{timestamp}.txt")
        };
    }
}
