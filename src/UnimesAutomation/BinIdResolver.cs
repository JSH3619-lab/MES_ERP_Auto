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
