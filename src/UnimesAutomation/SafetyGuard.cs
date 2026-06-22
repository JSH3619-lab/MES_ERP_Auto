using System.Windows.Automation;

namespace UnimesAutomation;

public sealed class SafetyGuard
{
    private static readonly string[] DangerousButtonKeywords =
    [
        "저장",
        "등록",
        "삭제",
        "확정",
        "승인",
        "적용",
        "Save",
        "Register",
        "Delete",
        "Confirm",
        "Apply"
    ];

    private static readonly string[] SaveButtonKeywords =
    [
        "저장",
        "Save"
    ];

    private readonly SafetyConfig _config;
    private readonly SimpleLogger _logger;

    public SafetyGuard(SafetyConfig config, SimpleLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool CanClick(AutomationElement element, string reason)
    {
        var name = SafeRead(() => element.Current.Name) ?? "";
        var controlType = SafeRead(() => element.Current.ControlType.ProgrammaticName) ?? "";

        if (!string.Equals(controlType, "ControlType.Button", StringComparison.Ordinal))
        {
            return true;
        }

        var matched = DangerousButtonKeywords.FirstOrDefault(keyword =>
            name.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            return true;
        }

        var realSaveMode = _config.SaveEnabled && !_config.DryRun;
        var saveButton = SaveButtonKeywords.Any(keyword =>
            name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (realSaveMode && saveButton)
        {
            _logger.Warn($"Save button allowed by real save mode. button='{name}', keyword='{matched}', reason='{reason}'");
            return true;
        }

        _logger.Error($"Blocked dangerous button click. button='{name}', keyword='{matched}', dryRun={_config.DryRun}, saveEnabled={_config.SaveEnabled}, reason='{reason}'");
        return false;
    }

    public void EnsureCanClick(AutomationElement element, string reason)
    {
        if (!CanClick(element, reason))
        {
            throw new InvalidOperationException("Safety guard blocked a dangerous button click.");
        }
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
}
