using UnimesAutomation;
using Xunit;

public class SipMarkingTests
{
    [Fact]
    public void Compute_builds_marking_from_fixed_pid_positions()
    {
        // AK(3-4)+B(10)+H(12)+P(13)+A8(15-16)+"YWW"
        Assert.Equal("AKBHPA8YWW", SipMarking.Compute("SNAKGD8J0B-HPRA81"));
    }

    [Theory]
    [InlineData("SNAKGD8J0B-HPRA81", true)]   // 정상 → Marking 함
    [InlineData("SNAKGD8J0B-HPRA0S", false)]  // 예외 0S
    [InlineData("SNAKGD8J0B-HPRA0G", false)]  // 예외 0G
    [InlineData("SNAKGD8J0B-HPRA0J", false)]  // 예외 0J
    [InlineData("SNAKGD8J0B-HPRA0K", false)]  // 예외 0K
    public void ShouldMark_skips_exception_suffixes(string pid, bool expected)
    {
        Assert.Equal(expected, SipMarking.ShouldMark(pid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SN")]
    [InlineData("SNAKGD8J0B")] // A8 자리(15-16) 추출 불가
    public void Compute_returns_empty_for_too_short(string pid)
    {
        Assert.Equal("", SipMarking.Compute(pid));
    }

    [Fact]
    public void Sn_prefix_classifies_as_sip()
    {
        Assert.Equal(PartClass.Sip, PartClassifier.Classify("SNAKGD8J0B-HPRA81"));
    }

    // 실물 조회결과(검색 PID = SNAKGD8J0B-HZRA81) 기준. base=Z 자리라 AKBHZA8YWW.
    [Theory]
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA81", "AKBHZA8YWW")]            // base(==PID)
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA81-AP4GF0T", "4G AKBHZA8YWW")] // AP 변형
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA81-APDGA00", "DG AKBHZA8YWW")] // AP 변형
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA81-TPDGA0T", "DG AKBHZA8YWW")] // TP 변형
    public void RowMarking_marks_base_and_variants(string pid, string rowId, string expected)
    {
        Assert.Equal(expected, SipMarking.RowMarking(pid, rowId));
    }

    // P 뒤가 '-'가 아니면(00/0J/0S) 다른 파트라 절대 건드리지 않음.
    [Theory]
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA8100")]          // 더미(00)
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA810J")]          // 다른 PID(0J)
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA810J-TPDGA0T")]  // 810J 변형
    [InlineData("SNAKGD8J0B-HZRA81", "SNAKGD8J0B-HZRA810S-TP4GF0T")]  // 810S 변형
    public void RowMarking_excludes_other_pids_and_dummy(string pid, string rowId)
    {
        Assert.Equal("", SipMarking.RowMarking(pid, rowId));
    }

    [Fact]
    public void RowMarking_returns_empty_when_pid_is_exception()
    {
        Assert.Equal("", SipMarking.RowMarking("SNAKGD8J0B-HZRA0J", "SNAKGD8J0B-HZRA0J"));
    }
}
