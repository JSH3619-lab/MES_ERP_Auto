namespace UnimesAutomation;

// UDP(UL/US/NL) 품목정보관리 파생값. 화면 동작과 분리된 순수 로직.
public static class UdpRules
{
    // Turn Key: 대시 제외 12번째(조립 업체)·13번째(Test 업체) 글자가 같으면 Y(한 업체가 조립+테스트).
    // 근거: Ramos SIP/UDP Ordering Information 자리 정의.
    public static string ComputeTurnKey(string pid)
    {
        var nodash = (pid ?? "").Trim().ToUpperInvariant().Replace("-", "");
        if (nodash.Length < 13)
        {
            return "N";
        }

        return nodash[11] == nodash[12] ? "Y" : "N";
    }

    // 품목특별속성: PID 끝 2글자 → 선택값 매핑(global.udpSpecialAttributes, 기본 0M/0R/0Y).
    // 매핑에 없으면 미선택("") → 셀을 건드리지 않는다.
    public static string SpecialAttribute(string pid, IReadOnlyDictionary<string, string> suffixMap)
    {
        var code = (pid ?? "").Trim().ToUpperInvariant();
        foreach (var (suffix, value) in suffixMap ?? new Dictionary<string, string>())
        {
            if (code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return "";
    }
}
