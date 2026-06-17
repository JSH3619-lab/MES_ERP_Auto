namespace UnimesAutomation;

public enum PartClass
{
    Module,
    Comp,
    Unknown
}

/// <summary>
/// Part No. 접두 2글자로 Module/Comp를 분류하고, 2번째 '-' 앞까지를 PID로 추출한다.
/// 순수 함수만 두어 화면 동작과 분리한다.
/// </summary>
public static class PartClassifier
{
    private static readonly string[] ModulePrefixes = ["RM", "TM", "BM", "CM"];
    private static readonly string[] CompPrefixes = ["RC", "TC", "BC", "CC"];

    public static PartClass Classify(string partNo)
    {
        var normalized = (partNo ?? "").Trim().ToUpperInvariant();
        if (normalized.Length < 2)
        {
            return PartClass.Unknown;
        }

        var prefix = normalized[..2];
        if (ModulePrefixes.Contains(prefix))
        {
            return PartClass.Module;
        }

        if (CompPrefixes.Contains(prefix))
        {
            return PartClass.Comp;
        }

        return PartClass.Unknown;
    }

    public static string ExtractPid(string partNo)
    {
        var value = (partNo ?? "").Trim();
        var first = value.IndexOf('-');
        if (first < 0)
        {
            return value;
        }

        var second = value.IndexOf('-', first + 1);
        if (second < 0)
        {
            return value;
        }

        return value[..second];
    }
}
