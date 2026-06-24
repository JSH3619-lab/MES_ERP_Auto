namespace UnimesAutomation;

public sealed class RuntimePaths
{
    public required string RootDirectory { get; init; }
    public required string LogsDirectory { get; init; }
    public required string ScreenshotsDirectory { get; init; }
    public required string OutputDirectory { get; init; }
    public required string Timestamp { get; init; }
    public required string RunLogPath { get; init; }
    public required string UiDumpPath { get; init; }
}

public sealed class CommandLineOptions
{
    public string? ConfigPath { get; set; }
    public bool NoLaunch { get; set; }
    public bool DumpOnly { get; set; }
    public bool Help { get; set; }
}
