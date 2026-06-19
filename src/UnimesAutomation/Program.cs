namespace UnimesAutomation;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var options = ParseArgs(args);
        var rootDirectory = FindWorkspaceRoot();
        var paths = DirectoryLayout.Create(rootDirectory);

        using var logger = new SimpleLogger(paths.RunLogPath);

        try
        {
            if (options.Help)
            {
                PrintHelp();
                return 0;
            }

            var config = LoadConfig(options.ConfigPath, rootDirectory, logger);
            var screenshots = new ScreenshotService(paths, logger);

            if (options.DumpOnly)
            {
                var safety = new SafetyGuard(config.Safety, logger);
                var app = new UnimesApp(config, paths, logger, screenshots, safety);
                return app.RunAsync(options).GetAwaiter().GetResult();
            }

            var appSettingsPath = Path.Combine(rootDirectory, "appsettings.json");
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new MainForm(config, paths, logger, screenshots, options, appSettingsPath));
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Automation failed");

            try
            {
                var screenshots = new ScreenshotService(paths, logger);
                screenshots.CaptureDesktop("failure");
            }
            catch (Exception screenshotEx)
            {
                logger.Error(screenshotEx, "Failure screenshot capture failed");
            }

            return 1;
        }
    }

    private static RootConfig LoadConfig(string? explicitPath, string rootDirectory, SimpleLogger logger)
    {
        var configPath = explicitPath;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var defaultPath = Path.Combine(rootDirectory, "appsettings.json");
            configPath = File.Exists(defaultPath) ? defaultPath : null;
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            logger.Warn("appsettings.json not found. Built-in defaults will be used.");
            return RootConfig.CreateDefault();
        }

        var fullPath = Path.GetFullPath(configPath);
        logger.Info($"Loading config: {fullPath}");

        return ConfigStore.Load(fullPath);
    }

    private static CommandLineOptions ParseArgs(string[] args)
    {
        var options = new CommandLineOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--config":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--config requires a file path.");
                    }

                    options.ConfigPath = args[++index];
                    break;

                case "--no-launch":
                    options.NoLaunch = true;
                    break;

                case "--dump-only":
                    options.DumpOnly = true;
                    break;

                case "--help":
                case "-h":
                    options.Help = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;
    }

    private static string FindWorkspaceRoot()
    {
        var current = Environment.CurrentDirectory;

        // When launched from dotnet run, CurrentDirectory is normally the command
        // directory. This upward search keeps output folders in the repo even if the
        // executable is started from bin/Debug.
        var directory = new DirectoryInfo(current);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "appsettings.example.json")) ||
                Directory.Exists(Path.Combine(directory.FullName, "src", "UnimesAutomation")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return current;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("UNIMES Automation PoC");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project .\\src\\UnimesAutomation\\UnimesAutomation.csproj -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --config <path>   Use a specific appsettings JSON file.");
        Console.WriteLine("  --no-launch       Attach to an already running UNIMES window.");
        Console.WriteLine("  --dump-only       Dump the current UNIMES UI tree and exit.");
        Console.WriteLine("  --help, -h        Show this help.");
    }
}
