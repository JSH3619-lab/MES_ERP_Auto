namespace UnimesAutomation;

// 파트(PID)에서 Marking 문자열을 계산한다. 자리수 고정. 화면과 분리된 순수 로직.
public static class SipMarking
{
    private static readonly string[] NoMarkSuffixes = ["0S", "0G", "0J", "0K"];

    // AK(3-4)+B(10)+H(12)+P(13)+A8(15-16)+"YWW"  (1-based)
    public static string Compute(string pid)
    {
        var code = (pid ?? "").Trim().ToUpperInvariant();
        if (code.Length < 16)
        {
            return "";
        }

        return code.Substring(2, 2) + code[9] + code[11] + code[12] + code.Substring(14, 2) + "YWW";
    }

    // 끝 2글자가 0S/0G/0J/0K면 Marking을 생략한다.
    public static bool ShouldMark(string pid)
    {
        var code = (pid ?? "").Trim().ToUpperInvariant();
        return !NoMarkSuffixes.Any(s => code.EndsWith(s, StringComparison.Ordinal));
    }

    // 검색 PID(searchedPid) 기준으로 그리드 한 행(rowProductId)의 Marking을 만든다.
    // base(==PID): Compute(PID). 변형(PID + "-" + MFGID): "{MFGID 3-4자 용량} " + Compute(PID).
    // PID 소속이 아니거나(끝이 '-'가 아닌 00/0J/0S 등) PID가 예외면 "" → 건드리지 않음.
    public static string RowMarking(string searchedPid, string rowProductId)
    {
        var pid = (searchedPid ?? "").Trim().ToUpperInvariant();
        var rowId = (rowProductId ?? "").Trim().ToUpperInvariant();
        if (!ShouldMark(pid))
        {
            return "";
        }

        var baseMarking = Compute(pid);
        if (string.IsNullOrEmpty(baseMarking))
        {
            return "";
        }

        if (rowId == pid)
        {
            return baseMarking;
        }

        var prefix = pid + "-";
        if (rowId.StartsWith(prefix, StringComparison.Ordinal))
        {
            var mfgid = rowId[prefix.Length..];
            if (mfgid.Length >= 4)
            {
                return $"{mfgid.Substring(2, 2)} {baseMarking}";
            }
        }

        return "";
    }
}
