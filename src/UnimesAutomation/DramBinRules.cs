namespace UnimesAutomation;

public static class DramBinRules
{
    // 모듈 용량코드 -> GB (DDR 무관)
    private static readonly Dictionary<string, int> ModuleDensityGb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1G"] = 1, ["2G"] = 2, ["4G"] = 4, ["8G"] = 8, ["AG"] = 16, ["BG"] = 32, ["CG"] = 64
    };

    // Comp 용량코드 -> (Gb, DDR5 여부)
    private static readonly Dictionary<string, (int Gb, bool Ddr5)> CompDensity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4G"] = (4, false), ["8G"] = (8, false), ["AG"] = (16, false),
        ["AH"] = (16, true), ["HE"] = (24, true), ["BH"] = (32, true)
    };

    public static BinInfoTarget? Resolve(string partNo, BinInfoValues moduleBinInfo, BinInfoValues compBinInfo)
    {
        var code = (partNo ?? "").Trim();
        var cls = PartClassifier.Classify(code);

        if (cls == PartClass.Module)
        {
            // [소싱2][DRAM1][DIMM1][용량2] -> 용량 = index 4..5
            if (code.Length < 6 || !ModuleDensityGb.TryGetValue(code.Substring(4, 2), out var gb))
            {
                return null;
            }

            var row = FirstRowOrDefault(moduleBinInfo, moduleBinInfo.ProcessSearchKey);
            var processKey = ProcessKey(row, moduleBinInfo.ProcessSearchKey);
            return new BinInfoTarget(cls, [new BinInfoRowTarget(processKey, $"RAM_Module_Normal_{gb}GB", row)]);
        }

        if (cls == PartClass.Comp)
        {
            // [소싱2][DRAM1][용량2] -> 용량 = index 3..4
            if (code.Length < 5 || !CompDensity.TryGetValue(code.Substring(3, 2), out var info))
            {
                return null;
            }

            var name = info.Ddr5
                ? $"DRAM_Comp_D5_XMP72_Bin_{info.Gb}Gb"
                : $"DRAM_Comp_Bin_{info.Gb}Gb";
            var row = FirstRowOrDefault(compBinInfo, compBinInfo.ProcessSearchKey);
            var processKey = ProcessKey(row, compBinInfo.ProcessSearchKey);
            return new BinInfoTarget(cls, [new BinInfoRowTarget(processKey, name, row)]);
        }

        return null;
    }

    private static BinRowConfig FirstRowOrDefault(BinInfoValues binInfo, string processKey)
    {
        var source = binInfo.Rows.FirstOrDefault() ?? BinRowConfig.Default(processKey);
        return Clone(source);
    }

    private static string ProcessKey(BinRowConfig row, string fallback)
        => string.IsNullOrWhiteSpace(row.ProcessName) ? fallback : row.ProcessName;

    private static BinRowConfig Clone(BinRowConfig source) => new()
    {
        ProcessName = source.ProcessName,
        BinType = source.BinType,
        RetestNo = source.RetestNo,
        BinComplete = source.BinComplete,
        RetestTh = source.RetestTh
    };
}
