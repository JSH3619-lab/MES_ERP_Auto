using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class ScreenshotService
{
    private readonly RuntimePaths _paths;
    private readonly SimpleLogger _logger;

    public ScreenshotService(RuntimePaths paths, SimpleLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string CaptureDesktop(string label)
    {
        var safeLabel = MakeSafeFileName(label);
        var path = Path.Combine(_paths.ScreenshotsDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safeLabel}.png");

        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);

        _logger.Info($"Screenshot saved: {path}");
        return path;
    }

    public string CaptureElement(AutomationElement? element, string label)
    {
        if (element is null)
        {
            return CaptureDesktop(label);
        }

        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1)
            {
                _logger.Warn($"Element rectangle is empty. Falling back to desktop screenshot. label='{label}'");
                return CaptureDesktop(label);
            }

            var width = Math.Max(1, (int)Math.Ceiling(rect.Width));
            var height = Math.Max(1, (int)Math.Ceiling(rect.Height));
            var path = Path.Combine(_paths.ScreenshotsDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{MakeSafeFileName(label)}.png");

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen((int)rect.Left, (int)rect.Top, 0, 0, new Size(width, height));
            bitmap.Save(path, ImageFormat.Png);

            _logger.Info($"Element screenshot saved: {path}");
            return path;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Element screenshot failed. Falling back to desktop screenshot. reason={ex.Message}");
            return CaptureDesktop(label);
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }
}

