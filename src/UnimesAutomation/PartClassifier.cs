namespace UnimesAutomation;

public enum PartClass
{
    Module,
    Comp,
    CompMdl,
    Ssd,
    Sip,
    Udp2,
    Udp3,
    Unknown
}

/// <summary>
/// Part No. 접두 2글자로 Module/Comp를 분류하고, 2번째 '-' 앞까지를 PID로 추출한다.
/// 순수 함수만 두어 화면 동작과 분리한다.
/// </summary>
public static class PartClassifier
{
    private static readonly string[] ModulePrefixes = ["RM", "TM", "BM", "CM", "ZM"];
    private static readonly string[] CompPrefixes = ["RC", "TC", "BC", "CC", "ZC"];
    private static readonly string[] SsdPrefixes = ["DA", "DE"];
    private static readonly string[] SipPrefixes = ["SN"];

    // UDP: UL=UDP2.0, US=uUDP2.0(규칙 동일), NL=UDP3.0. 파트 체계는 SIP와 공용(Ordering Info 문서).
    private static readonly string[] Udp2Prefixes = ["UL", "US"];
    private static readonly string[] Udp3Prefixes = ["NL"];

    // MDL 접두(ModulePrefixes) 뒤 2글자가 RC/4C면 Module 모양의 Comp 파트(Comp_MDL)다.
    private static readonly string[] CompMdlInfixes = ["RC", "4C"];

    public static PartClass Classify(string partNo)
    {
        var normalized = (partNo ?? "").Trim().ToUpperInvariant();
        if (normalized.Length < 2)
        {
            return PartClass.Unknown;
        }

        var prefix = normalized[..2];
        if (SsdPrefixes.Contains(prefix))
        {
            return PartClass.Ssd;
        }

        if (ModulePrefixes.Contains(prefix))
        {
            if (normalized.Length >= 4 && CompMdlInfixes.Contains(normalized.Substring(2, 2)))
            {
                return PartClass.CompMdl;
            }

            return PartClass.Module;
        }

        if (CompPrefixes.Contains(prefix))
        {
            return PartClass.Comp;
        }

        if (SipPrefixes.Contains(prefix))
        {
            return PartClass.Sip;
        }

        if (Udp2Prefixes.Contains(prefix))
        {
            return PartClass.Udp2;
        }

        if (Udp3Prefixes.Contains(prefix))
        {
            return PartClass.Udp3;
        }

        return PartClass.Unknown;
    }

    // 더미 Part: PID(2번째 '-' 전) 리터럴 끝 2글자가 "00".
    // 끝쪽 append가 가변이라 위치가 아닌 리터럴 끝으로 판정한다.
    public static bool IsDummy(string partNo)
    {
        return ExtractPid((partNo ?? "").Trim()).EndsWith("00", StringComparison.Ordinal);
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
