using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnimesAutomation;

// appsettings.json 단일 파일을 읽고 쓴다. 구버전(플랫 itemInfo/binInfo)은 분류 구조로 마이그레이션한다.
public static class ConfigStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static RootConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Normalize(RootConfig.CreateDefault());
        }

        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (node is null)
        {
            return Normalize(RootConfig.CreateDefault());
        }

        if (node["categories"] is not null)
        {
            return Normalize(node.Deserialize<RootConfig>(ReadOptions) ?? RootConfig.CreateDefault());
        }

        return Normalize(MigrateLegacy(node));
    }

    public static void Save(string path, RootConfig config)
    {
        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static RootConfig MigrateLegacy(JsonNode node)
    {
        var cfg = RootConfig.CreateDefault();

        if (node["login"] is JsonNode login)
        {
            cfg.Login = login.Deserialize<LoginConfig>(ReadOptions) ?? cfg.Login;
        }
        if (node["safety"] is JsonNode safety)
        {
            cfg.Safety = safety.Deserialize<SafetyConfig>(ReadOptions) ?? cfg.Safety;
        }
        if (node["app"] is JsonNode app)
        {
            cfg.App = app.Deserialize<AppConfig>(ReadOptions) ?? cfg.App;
        }
        if (node["workflow"] is JsonNode workflow)
        {
            cfg.Workflow = workflow.Deserialize<WorkflowConfig>(ReadOptions) ?? cfg.Workflow;
        }

        var ii = node["itemInfo"];
        var bi = node["binInfo"];

        static string S(JsonNode? n, string key, string fallback)
            => n?[key]?.GetValue<string>() ?? fallback;

        cfg.Global.ItemInfoMenuName = S(ii, "menuName", cfg.Global.ItemInfoMenuName);
        cfg.Global.BinInfoMenuName = S(bi, "menuName", cfg.Global.BinInfoMenuName);
        cfg.Global.RecoveryPart = S(ii, "recoveryPart", cfg.Global.RecoveryPart);

        var binManage = S(ii, "binManage", "Y");
        var turnKey = S(ii, "turnKey", "N");
        var assemblyIn = S(ii, "assemblyIn", "Y");
        var moduleWh = S(ii, "moduleDefectWarehouse", "제품 폐기창고");
        var compWh = S(ii, "compDefectWarehouse", "COMPONENT 폐기창고");

        var moduleKey = S(bi, "moduleProcessKey", "M050");
        var compKey = S(bi, "compProcessKey", "C010");
        var binType = S(bi, "binType", "Normal-1");
        var retestNo = S(bi, "retestNo", "0");
        var binComplete = S(bi, "binComplete", "Y");
        var retestTh = S(bi, "retestTh", "H");

        cfg.Categories.DramModule = new CategoryConfig
        {
            ItemInfo = new ItemInfoValues
            {
                BinManage = binManage, TurnKey = turnKey, AssemblyIn = assemblyIn,
                DefectWarehouse = moduleWh
            },
            BinInfo = new BinInfoValues
            {
                ProcessSearchKey = moduleKey,
                Rows = [new BinRowConfig
                {
                    ProcessName = moduleKey, BinType = binType, RetestNo = retestNo,
                    BinComplete = binComplete, RetestTh = retestTh
                }]
            }
        };

        cfg.Categories.DramComp = new CategoryConfig
        {
            ItemInfo = new ItemInfoValues
            {
                BinManage = binManage, TurnKey = turnKey, AssemblyIn = assemblyIn,
                DefectWarehouse = compWh
            },
            BinInfo = new BinInfoValues
            {
                ProcessSearchKey = compKey,
                Rows = [new BinRowConfig
                {
                    ProcessName = compKey, BinType = binType, RetestNo = retestNo,
                    BinComplete = binComplete, RetestTh = retestTh
                }]
            }
        };

        return cfg;
    }

    private static RootConfig Normalize(RootConfig cfg)
    {
        AddMissing(cfg.Options.DefectWarehouses, ["제품 폐기창고", "COMPONENT 폐기창고"]);
        AddMissing(cfg.Options.BinTypes, ["Normal-1", "Normal-2", "Special-1"]);
        AddMissing(cfg.Options.RetestThs, ["H", "Normal", "L"]);
        AddMissing(cfg.Options.BinCompletes, ["Y", "N"]);
        cfg.Categories.Ssd.ItemInfo.AssemblyIn = "";
        return cfg;
    }

    private static void AddMissing(List<string> values, IEnumerable<string> required)
    {
        foreach (var value in required)
        {
            if (!values.Contains(value))
            {
                values.Add(value);
            }
        }
    }
}
