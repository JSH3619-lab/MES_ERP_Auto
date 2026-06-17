namespace UnimesAutomation;

public sealed record BinInfoTarget(PartClass Class, string ProcessSearchKey, string BinIdName);

// 파트번호에서 BIN 정보 처리에 필요한 파생값을 계산한다. 화면 동작과 분리된 순수 로직.
public static class BinIdResolver
{
    // 모듈 용량코드 → GB (DDR 무관)
    private static readonly Dictionary<string, int> ModuleDensityGb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1G"] = 1, ["2G"] = 2, ["4G"] = 4, ["8G"] = 8, ["AG"] = 16, ["BG"] = 32, ["CG"] = 64
    };

    // Comp 용량코드 → (Gb, DDR5 여부)
    private static readonly Dictionary<string, (int Gb, bool Ddr5)> CompDensity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4G"] = (4, false), ["8G"] = (8, false), ["AG"] = (16, false),
        ["AH"] = (16, true), ["HE"] = (24, true), ["BH"] = (32, true)
    };

    public static BinInfoTarget? Resolve(string partNo, BinInfoConfig config)
    {
        var code = (partNo ?? "").Trim();
        var cls = PartClassifier.Classify(code);

        if (cls == PartClass.Module)
        {
            // [소싱2][DRAM1][DIMM1][용량2] → 용량 = index 4..5
            if (code.Length < 6 || !ModuleDensityGb.TryGetValue(code.Substring(4, 2), out var gb))
            {
                return null;
            }

            return new BinInfoTarget(cls, config.ModuleProcessKey, $"RAM_Module_Normal_{gb}GB");
        }

        if (cls == PartClass.Comp)
        {
            // [소싱2][DRAM1][용량2] → 용량 = index 3..4
            if (code.Length < 5 || !CompDensity.TryGetValue(code.Substring(3, 2), out var info))
            {
                return null;
            }

            var name = info.Ddr5
                ? $"DRAM_Comp_D5_XMP72_Bin_{info.Gb}Gb"
                : $"DRAM_Comp_Bin_{info.Gb}Gb";
            return new BinInfoTarget(cls, config.CompProcessKey, name);
        }

        return null;
    }
}
