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
}
