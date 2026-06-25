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
}
