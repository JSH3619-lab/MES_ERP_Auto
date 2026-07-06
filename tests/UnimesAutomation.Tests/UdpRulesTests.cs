using UnimesAutomation;
using Xunit;

public class UdpRulesTests
{
    [Theory]
    [InlineData("ULAHGD8J0D-HBRAB2", PartClass.Udp2)] // UDP2.0
    [InlineData("USAHGD8J0D-HBRAB1", PartClass.Udp2)] // uUDP2.0(규칙 동일 → 같은 분류)
    [InlineData("NLAHGD8J0D-H6RF51", PartClass.Udp3)] // UDP3.0
    public void Udp_prefixes_classify(string part, PartClass expected)
    {
        Assert.Equal(expected, PartClassifier.Classify(part));
    }

    // Turn Key: 대시 제외 12번째(조립 업체) == 13번째(Test 업체)면 Y.
    [Theory]
    [InlineData("ULAHGD8J0D-HBRAB2", "N")] // B != R
    [InlineData("ULAHGD8J0D-HBBAB2", "Y")] // B == B
    [InlineData("NLAHGD8J0D-H6RF51", "N")] // 6 != R
    [InlineData("UL", "N")]                // 길이 부족 → N
    public void ComputeTurnKey_compares_positions_12_and_13(string pid, string expected)
    {
        Assert.Equal(expected, UdpRules.ComputeTurnKey(pid));
    }

    // 품목특별속성: 설정 매핑(기본 0M/0R/0Y) 기준으로 PID 끝 2글자 매칭, 그 외 미선택.
    [Theory]
    [InlineData("ULAHGD8J0D-HBRAB20M", "RMA(자산)")]
    [InlineData("ULAHGD8J0D-HBRAB20R", "RMA(비자산)")]
    [InlineData("ULAHGD8J0D-HBRAB20Y", "재고 RETEST")]
    [InlineData("ULAHGD8J0D-HBRAB2", "")]   // Normal
    [InlineData("ULAHGD8J0D-HBRAB20J", "")] // 0J는 특별속성 아님
    public void SpecialAttribute_maps_suffix(string pid, string expected)
    {
        var map = RootConfig.CreateDefault().Global.UdpSpecialAttributes;
        Assert.Equal(expected, UdpRules.SpecialAttribute(pid, map));
    }
}
