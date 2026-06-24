namespace UnimesAutomation;

public sealed record BinInfoTarget(PartClass Class, IReadOnlyList<BinInfoRowTarget> Rows)
{
    public string ProcessSearchKey => Rows.FirstOrDefault()?.ProcessSearchKey ?? "";
    public string BinIdName => Rows.FirstOrDefault()?.BinIdName ?? "";
}

public sealed record BinInfoRowTarget(string ProcessSearchKey, string BinIdName, BinRowConfig Row);

// 파트번호에서 BIN 정보 처리에 필요한 파생값을 계산한다. 화면 동작과 분리된 순수 로직.
public static class BinIdResolver
{
    public static BinInfoTarget? Resolve(string partNo, RootConfig config)
    {
        var code = (partNo ?? "").Trim();
        var cls = PartClassifier.Classify(code);

        return cls switch
        {
            PartClass.Ssd => SsdBinRules.Resolve(code, config.Categories.Ssd),
            PartClass.Module or PartClass.Comp => DramBinRules.Resolve(
                code,
                config.Categories.DramModule.BinInfo,
                config.Categories.DramComp.BinInfo),
            _ => null
        };
    }

    public static BinInfoTarget? Resolve(string partNo, string moduleProcessKey, string compProcessKey)
    {
        return DramBinRules.Resolve(
            partNo,
            new BinInfoValues { ProcessSearchKey = moduleProcessKey, Rows = [BinRowConfig.Default(moduleProcessKey)] },
            new BinInfoValues { ProcessSearchKey = compProcessKey, Rows = [BinRowConfig.Default(compProcessKey)] });
    }
}

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
            var row = (i < configured.Rows.Count ? configured.Rows[i] : defaults.Rows[i]).Clone();
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
}

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
        return source.Clone();
    }

    private static string ProcessKey(BinRowConfig row, string fallback)
        => string.IsNullOrWhiteSpace(row.ProcessName) ? fallback : row.ProcessName;
}
