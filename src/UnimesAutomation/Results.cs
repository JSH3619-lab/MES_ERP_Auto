using System.Text.Json.Serialization;

namespace UnimesAutomation;

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

    public BinRowConfig Clone() => new()
    {
        ProcessName = ProcessName,
        BinType = BinType,
        RetestNo = RetestNo,
        BinComplete = BinComplete,
        RetestTh = RetestTh
    };
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
    public string Marking { get; set; } = "";
    public string Saved { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.Now;
}

public sealed class BinResult
{
    public required string PartNo { get; init; }
    public string Classification { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string BinType { get; set; } = "";
    public string RetestNo { get; set; } = "";
    public string BinComplete { get; set; } = "";
    public string RetestTh { get; set; } = "";
    public string BinId { get; set; } = "";
    public string Saved { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.Now;
}
