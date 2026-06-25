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
}
