using System.Text.Json.Serialization;

namespace UnimesAutomation;

public sealed class RootConfig
{
    [JsonPropertyName("app")]
    public AppConfig App { get; set; } = new();

    [JsonPropertyName("login")]
    public LoginConfig Login { get; set; } = new();

    [JsonPropertyName("safety")]
    public SafetyConfig Safety { get; set; } = new();

    [JsonPropertyName("workflow")]
    public WorkflowConfig Workflow { get; set; } = new();

    [JsonPropertyName("itemInfo")]
    public ItemInfoConfig ItemInfo { get; set; } = new();

    public static RootConfig CreateDefault()
    {
        return new RootConfig
        {
            App = new AppConfig
            {
                LaunchPath = @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Bizentro\UNIMES - 1 .appref-ms",
                WindowTitleContains = ["UNIMES"],
                WindowTitleExcludes = ["UNIERP"],
                ProcessNameHints = ["UNIMES", "SetupMES", "Bizentro.App.MAIN.ClientAgent", "Bizentro.App.MAIN.Shell"],
                LaunchTimeoutSeconds = 90,
                LoginTimeoutSeconds = 180,
                PopupTimeoutSeconds = 20,
                UiDumpMaxDepth = 12
            },
            Login = new LoginConfig
            {
                UserId = "22402002",
                PasswordMode = "manual",
                Password = "",
                Language = "한국어",
                System = "UNIMES"
            },
            Safety = new SafetyConfig
            {
                DryRun = true,
                SaveEnabled = false
            },
            Workflow = new WorkflowConfig
            {
                Enabled = true,
                InputPartsPath = "input_parts.csv",
                SearchDelayMilliseconds = 1200,
                StopOnFirstFailure = false
            },
            ItemInfo = new ItemInfoConfig()
        };
    }
}

public sealed class AppConfig
{
    [JsonPropertyName("launchPath")]
    public string LaunchPath { get; set; } = "";

    [JsonPropertyName("windowTitleContains")]
    public List<string> WindowTitleContains { get; set; } = ["UNIMES"];

    // 제목에 이 토큰이 포함된 창은 대상에서 제외(예: 같은 플랫폼의 ERP 창).
    [JsonPropertyName("windowTitleExcludes")]
    public List<string> WindowTitleExcludes { get; set; } = [];

    [JsonPropertyName("processNameHints")]
    public List<string> ProcessNameHints { get; set; } = [];

    [JsonPropertyName("launchTimeoutSeconds")]
    public int LaunchTimeoutSeconds { get; set; } = 90;

    [JsonPropertyName("loginTimeoutSeconds")]
    public int LoginTimeoutSeconds { get; set; } = 180;

    [JsonPropertyName("popupTimeoutSeconds")]
    public int PopupTimeoutSeconds { get; set; } = 20;

    [JsonPropertyName("uiDumpMaxDepth")]
    public int UiDumpMaxDepth { get; set; } = 12;

    // "attachOrLaunch"(기본): 로그인된 UNIMES 있으면 붙고 없으면 실행
    // "attach": 로그인된 창에만 붙음(없으면 에러). "launch": 항상 새로 실행.
    [JsonPropertyName("launchMode")]
    public string LaunchMode { get; set; } = "attachOrLaunch";
}

public sealed class LoginConfig
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("passwordMode")]
    public string PasswordMode { get; set; } = "manual";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "한국어";

    [JsonPropertyName("system")]
    public string System { get; set; } = "UNIMES";

    [JsonIgnore]
    public bool UseConfigPassword => string.Equals(PasswordMode, "config", StringComparison.OrdinalIgnoreCase);
}

public sealed class SafetyConfig
{
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; } = true;

    [JsonPropertyName("saveEnabled")]
    public bool SaveEnabled { get; set; }
}

public sealed class WorkflowConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("inputPartsPath")]
    public string InputPartsPath { get; set; } = "input_parts.csv";

    [JsonPropertyName("searchDelayMilliseconds")]
    public int SearchDelayMilliseconds { get; set; } = 1200;

    [JsonPropertyName("stopOnFirstFailure")]
    public bool StopOnFirstFailure { get; set; } = false;

    [JsonPropertyName("showPartInputDialog")]
    public bool ShowPartInputDialog { get; set; } = true;

    [JsonPropertyName("showCompletionDialog")]
    public bool ShowCompletionDialog { get; set; } = true;

    [JsonIgnore]
    public List<PartRequest> RuntimePartRequests { get; set; } = [];
}

public sealed class ItemInfoConfig
{
    [JsonPropertyName("menuName")]
    public string MenuName { get; set; } = "품목정보관리";

    [JsonPropertyName("binManage")]
    public string BinManage { get; set; } = "Y";

    [JsonPropertyName("turnKey")]
    public string TurnKey { get; set; } = "N";

    [JsonPropertyName("assemblyIn")]
    public string AssemblyIn { get; set; } = "Y";

    [JsonPropertyName("moduleDefectWarehouse")]
    public string ModuleDefectWarehouse { get; set; } = "제품 폐기창고";

    [JsonPropertyName("compDefectWarehouse")]
    public string CompDefectWarehouse { get; set; } = "COMPONENT 폐기창고";

    // 미존재 Part 경고 후 열린 고객사PartID 팝업에서 키보드 복구에 사용할 기등록 Part.
    [JsonPropertyName("recoveryPart")]
    public string RecoveryPart { get; set; } = "RMRDAG58A1B-GPWRRWM7";
}

public sealed class PartRequest
{
    public required string PartNo { get; init; }
    public string ItemAccount { get; init; } = "";
    public string ItemId { get; init; } = "";
    public string ItemName { get; init; } = "";
}

public sealed class PartResult
{
    public required string PartNo { get; init; }
    public string Classification { get; set; } = "";
    public string BinManage { get; set; } = "";
    public string TurnKey { get; set; } = "";
    public string AssemblyIn { get; set; } = "";
    public string DefectWarehouse { get; set; } = "";
    public string Saved { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

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
