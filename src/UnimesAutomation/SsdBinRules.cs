namespace UnimesAutomation;

public static class SsdBinRules
{
    private const string ValueTrimBinId = "SSD_ValueTrime_480GB";

    private static readonly Dictionary<string, string> Density = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BG"] = "32GB",
        ["CG"] = "64GB",
        ["12"] = "120GB",
        ["DG"] = "128GB",
        ["24"] = "240GB",
        ["25"] = "250GB",
        ["FG"] = "256GB",
        ["48"] = "480GB",
        ["HG"] = "512GB",
        ["50"] = "500GB",
        ["KG"] = "1TB",
        ["96"] = "960GB",
        ["MG"] = "2TB"
    };

    public static BinInfoTarget? Resolve(string partNo, SsdCategoryConfig config)
    {
        var code = (partNo ?? "").Trim();
        if (PartClassifier.Classify(code) != PartClass.Ssd)
        {
            return null;
        }

        if (code.Length < 5 || !Density.TryGetValue(code.Substring(3, 2), out var capacity))
        {
            return null;
        }

        if (code.EndsWith("B0", StringComparison.OrdinalIgnoreCase))
        {
            var binIds = new[] { $"SSD_RDT_{capacity}", $"SSD_RDT_{capacity}" };
            return BuildTarget(config.B0BinInfo, SsdCategoryConfig.Default().B0BinInfo, binIds);
        }

        if (code.EndsWith("R0", StringComparison.OrdinalIgnoreCase))
        {
            var binIds = new[] { $"SSD_RDT_{capacity}", $"SSD_RDT_{capacity}_R", ValueTrimBinId };
            return BuildTarget(config.R0BinInfo, SsdCategoryConfig.Default().R0BinInfo, binIds);
        }

        return null;
    }

    private static BinInfoTarget BuildTarget(BinInfoValues configured, BinInfoValues defaults, IReadOnlyList<string> binIds)
    {
        var rows = new List<BinInfoRowTarget>();
        for (var i = 0; i < binIds.Count; i++)
        {
            var row = Clone(i < configured.Rows.Count ? configured.Rows[i] : defaults.Rows[i]);
            var processKey = string.IsNullOrWhiteSpace(row.ProcessName)
                ? configured.ProcessSearchKey
                : row.ProcessName;
            if (string.IsNullOrWhiteSpace(processKey))
            {
                processKey = defaults.ProcessSearchKey;
            }

            rows.Add(new BinInfoRowTarget(processKey, binIds[i], row));
        }

        return new BinInfoTarget(PartClass.Ssd, rows);
    }

    private static BinRowConfig Clone(BinRowConfig source) => new()
    {
        ProcessName = source.ProcessName,
        BinType = source.BinType,
        RetestNo = source.RetestNo,
        BinComplete = source.BinComplete,
        RetestTh = source.RetestTh
    };
}
