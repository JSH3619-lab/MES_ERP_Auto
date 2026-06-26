namespace UnimesAutomation;

// 파트 입력 텍스트(줄/쉼표/세미콜론/탭/공백 구분)를 중복 없는 PartRequest 목록으로 만든다.
public static class PartListParser
{
    public static List<PartRequest> Parse(string text)
    {
        return (text ?? "")
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(partNo => new PartRequest { PartNo = partNo })
            .ToList();
    }

    // 엑셀 '품목코드' 열 셀들을 받아 작업 대상 PID 목록으로 만든다.
    // 분류 실패(입고 Z4·노이즈) 제외 → 더미(PID 끝 00) 제외 → PID 단위 중복 제거.
    // 타이핑 입력 경로(Parse)는 건드리지 않는다.
    public static List<PartRequest> FromCodes(IEnumerable<string> codes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PartRequest>();
        foreach (var raw in codes ?? [])
        {
            var code = (raw ?? "").Trim();
            if (code.Length == 0
                || PartClassifier.Classify(code) == PartClass.Unknown // 입고(Z4)·노이즈 제외
                || PartClassifier.IsDummy(code))                       // 더미(끝 00) 제외
            {
                continue;
            }

            var pid = PartClassifier.ExtractPid(code);
            if (seen.Add(pid))
            {
                result.Add(new PartRequest { PartNo = pid });
            }
        }

        return result;
    }
}
