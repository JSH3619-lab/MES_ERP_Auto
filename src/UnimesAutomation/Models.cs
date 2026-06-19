using System.Text.Json.Serialization;

namespace UnimesAutomation;

public enum WorkScope
{
    ItemInfo,
    BinInfo,
    Both
}

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

    [JsonPropertyName("options")]
    public OptionsConfig Options { get; set; } = new();

    [JsonPropertyName("categories")]
    public CategoriesConfig Categories { get; set; } = new();

    [JsonPropertyName("global")]
    public GlobalConfig Global { get; set; } = new();

    public CategoryConfig? ResolveCategory(PartClass cls) => cls switch
    {
        PartClass.Module => Categories.DramModule,
        PartClass.Comp => Categories.DramComp,
        _ => null
    };

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
                PasswordMode = "env",
                Password = "",
                UserIdEnvironmentVariable = "UNIMES_USER_ID",
                PasswordEnvironmentVariable = "UNIMES_PASSWORD",
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
            Options = new OptionsConfig(),
            Categories = new CategoriesConfig(),
            Global = new GlobalConfig()
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

    [JsonPropertyName("passwordEncrypted")]
    public string PasswordEncrypted { get; set; } = "";

    [JsonIgnore]
    public bool UseDpapiPassword =>
        string.Equals(PasswordMode, "dpapi", StringComparison.OrdinalIgnoreCase);

    [JsonPropertyName("userIdEnvironmentVariable")]
    public string UserIdEnvironmentVariable { get; set; } = "UNIMES_USER_ID";

    [JsonPropertyName("passwordEnvironmentVariable")]
    public string PasswordEnvironmentVariable { get; set; } = "UNIMES_PASSWORD";

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

    [JsonPropertyName("showWorkScopeDialog")]
    public bool ShowWorkScopeDialog { get; set; } = true;

    [JsonIgnore]
    public WorkScope RuntimeWorkScope { get; set; } = WorkScope.ItemInfo;

    [JsonIgnore]
    public List<PartRequest> RuntimePartRequests { get; set; } = [];
}

public sealed class OptionsConfig
{
    [JsonPropertyName("defectWarehouses")]
    public List<string> DefectWarehouses { get; set; } = ["제품 폐기창고", "COMPONENT 폐기창고"];

    [JsonPropertyName("binTypes")]
    public List<string> BinTypes { get; set; } = ["Normal-1"];

    [JsonPropertyName("retestThs")]
    public List<string> RetestThs { get; set; } = ["H", "L"];

    [JsonPropertyName("binCompletes")]
    public List<string> BinCompletes { get; set; } = ["Y", "N"];
}

public sealed class CategoriesConfig
{
    [JsonPropertyName("dramModule")]
    public CategoryConfig DramModule { get; set; } = CategoryConfig.DefaultModule();

    [JsonPropertyName("dramComp")]
    public CategoryConfig DramComp { get; set; } = CategoryConfig.DefaultComp();
}

public sealed class CategoryConfig
{
    [JsonPropertyName("itemInfo")]
    public ItemInfoValues ItemInfo { get; set; } = new();

    [JsonPropertyName("binInfo")]
    public BinInfoValues BinInfo { get; set; } = new();

    public static CategoryConfig DefaultModule() => new()
    {
        ItemInfo = new ItemInfoValues { DefectWarehouse = "제품 폐기창고" },
        BinInfo = new BinInfoValues { ProcessSearchKey = "M050", Rows = [BinRowConfig.Default("M050")] }
    };

    public static CategoryConfig DefaultComp() => new()
    {
        ItemInfo = new ItemInfoValues { DefectWarehouse = "COMPONENT 폐기창고" },
        BinInfo = new BinInfoValues { ProcessSearchKey = "C010", Rows = [BinRowConfig.Default("C010")] }
    };
}

public sealed class ItemInfoValues
{
    [JsonPropertyName("binManage")]
    public string BinManage { get; set; } = "Y";

    [JsonPropertyName("turnKey")]
    public string TurnKey { get; set; } = "N";

    [JsonPropertyName("assemblyIn")]
    public string AssemblyIn { get; set; } = "Y";

    [JsonPropertyName("defectWarehouse")]
    public string DefectWarehouse { get; set; } = "";
}

public sealed class BinInfoValues
{
    [JsonPropertyName("processSearchKey")]
    public string ProcessSearchKey { get; set; } = "";

    [JsonPropertyName("rows")]
    public List<BinRowConfig> Rows { get; set; } = [];
}

public sealed class BinRowConfig
{
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("binType")]
    public string BinType { get; set; } = "Normal-1";

    [JsonPropertyName("retestNo")]
    public string RetestNo { get; set; } = "0";

    [JsonPropertyName("binComplete")]
    public string BinComplete { get; set; } = "Y";

    [JsonPropertyName("retestTh")]
    public string RetestTh { get; set; } = "H";

    public static BinRowConfig Default(string processName) => new() { ProcessName = processName };
}

public sealed class GlobalConfig
{
    [JsonPropertyName("recoveryPart")]
    public string RecoveryPart { get; set; } = "RMRDAG58A1B-GPWRRWM7";

    [JsonPropertyName("itemInfoMenuName")]
    public string ItemInfoMenuName { get; set; } = "품목정보관리";

    [JsonPropertyName("binInfoMenuName")]
    public string BinInfoMenuName { get; set; } = "품목별 BIN 정보 관리";
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
