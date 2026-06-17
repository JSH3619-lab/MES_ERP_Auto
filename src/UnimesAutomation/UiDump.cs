using System.Text;
using System.Windows.Automation;

namespace UnimesAutomation;

public static class UiDump
{
    public static void DumpToFile(AutomationElement root, string outputPath, int maxDepth, SimpleLogger logger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var builder = new StringBuilder();
        builder.AppendLine($"UI dump created at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"MaxDepth: {maxDepth}");
        builder.AppendLine();

        DumpElement(root, builder, depth: 0, maxDepth, logger);
        File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);

        logger.Info($"UI dump saved: {outputPath}");
    }

    private static void DumpElement(
        AutomationElement element,
        StringBuilder builder,
        int depth,
        int maxDepth,
        SimpleLogger logger)
    {
        if (depth > maxDepth)
        {
            return;
        }

        try
        {
            var indent = new string(' ', depth * 2);
            builder.Append(indent);
            builder.Append("- ");
            builder.Append(DescribeElement(element));
            builder.AppendLine();

            if (depth == maxDepth)
            {
                return;
            }

            AutomationElementCollection children;
            try
            {
                children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            }
            catch (Exception ex)
            {
                builder.Append(indent).Append("  ").AppendLine($"children_unavailable=true error='{Escape(ex.Message)}'");
                return;
            }

            foreach (AutomationElement child in children)
            {
                DumpElement(child, builder, depth + 1, maxDepth, logger);
            }
        }
        catch (ElementNotAvailableException)
        {
            builder.AppendLine($"{new string(' ', depth * 2)}- element_not_available=true");
        }
        catch (Exception ex)
        {
            logger.Warn($"UI dump failed for one element: {ex.Message}");
            builder.AppendLine($"{new string(' ', depth * 2)}- dump_error='{Escape(ex.Message)}'");
        }
    }

    private static string DescribeElement(AutomationElement element)
    {
        var controlType = SafeRead(() => element.Current.ControlType.ProgrammaticName) ?? "";
        var name = SafeRead(() => element.Current.Name) ?? "";
        var automationId = SafeRead(() => element.Current.AutomationId) ?? "";
        var className = SafeRead(() => element.Current.ClassName) ?? "";
        var rect = SafeReadRect(() => element.Current.BoundingRectangle);
        var enabled = SafeReadBool(() => element.Current.IsEnabled);
        var offscreen = SafeReadBool(() => element.Current.IsOffscreen);
        var visible = offscreen.HasValue ? !offscreen.Value : (bool?)null;

        var valueInfo = GetValueInfo(element);

        return string.Join(" | ", new[]
        {
            $"control_type='{Escape(controlType)}'",
            $"name='{Escape(name)}'",
            $"automation_id='{Escape(automationId)}'",
            $"class_name='{Escape(className)}'",
            $"rectangle='{FormatRect(rect)}'",
            $"enabled='{FormatNullable(enabled)}'",
            $"visible='{FormatNullable(visible)}'",
            $"text_value='{Escape(valueInfo)}'"
        });
    }

    private static string GetValueInfo(AutomationElement element)
    {
        var parts = new List<string>();

        if (TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            var value = SafeRead(() => valuePattern.Current.Value) ?? "";
            parts.Add($"ValuePattern:{value}");
        }

        if (TryGetPattern<TextPattern>(element, TextPattern.Pattern, out var textPattern))
        {
            string text;
            try
            {
                text = textPattern.DocumentRange.GetText(200);
            }
            catch (Exception ex)
            {
                text = $"unreadable:{ex.Message}";
            }

            parts.Add($"TextPattern:{text}");
        }

        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    private static bool TryGetPattern<T>(AutomationElement element, AutomationPattern pattern, out T typedPattern)
        where T : class
    {
        typedPattern = null!;
        try
        {
            if (element.TryGetCurrentPattern(pattern, out var rawPattern) && rawPattern is T cast)
            {
                typedPattern = cast;
                return true;
            }
        }
        catch
        {
            // Pattern lookup can fail if the element disappears during dump.
        }

        return false;
    }

    private static T? SafeRead<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }

    private static bool? SafeReadBool(Func<bool> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static System.Windows.Rect? SafeReadRect(Func<System.Windows.Rect> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatRect(System.Windows.Rect? rect)
    {
        if (!rect.HasValue || rect.Value.IsEmpty)
        {
            return "";
        }

        var r = rect.Value;
        return $"L={r.Left:0},T={r.Top:0},R={r.Right:0},B={r.Bottom:0},W={r.Width:0},H={r.Height:0}";
    }

    private static string FormatNullable(bool? value) => value.HasValue ? value.Value.ToString() : "";

    private static string Escape(string value)
    {
        return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("'", "\\'");
    }
}
