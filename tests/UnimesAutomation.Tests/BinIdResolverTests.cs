using UnimesAutomation;
using Xunit;

public class BinIdResolverTests
{
    private static readonly BinInfoConfig Cfg = new();

    [Theory]
    [InlineData("RMRDAG58A1P-GPWRRWM7", "RAM_Module_Normal_16GB")] // AG=16GB, DDR무관
    [InlineData("RMRD8G58A1P-GPWRRWM7", "RAM_Module_Normal_8GB")]  // 8G=8GB
    [InlineData("RMRDBG58A1P-GPWRRWM7", "RAM_Module_Normal_32GB")] // BG=32GB
    [InlineData("RMRDCG58A1P-GPWRRWM7", "RAM_Module_Normal_64GB")] // CG=64GB(미등록이어도 이름은 계산)
    public void Module_resolves_capacity_only(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, Cfg);
        Assert.NotNull(target);
        Assert.Equal(PartClass.Module, target!.Class);
        Assert.Equal("M050", target.ProcessSearchKey);
        Assert.Equal(expected, target.BinIdName);
    }

    [Theory]
    [InlineData("RCA8G58A1P-XPWRRWM7", "DRAM_Comp_Bin_8Gb")]            // DDR4 8Gb
    [InlineData("RCAAG58A1P-XPWRRWM7", "DRAM_Comp_Bin_16Gb")]          // DDR4 16Gb
    [InlineData("RCRAH58A1P-XPWRRWM7", "DRAM_Comp_D5_XMP72_Bin_16Gb")] // DDR5 16Gb
    public void Comp_resolves_with_ddr(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, Cfg);
        Assert.NotNull(target);
        Assert.Equal(PartClass.Comp, target!.Class);
        Assert.Equal("C010", target.ProcessSearchKey);
        Assert.Equal(expected, target.BinIdName);
    }

    [Theory]
    [InlineData("XXRDAG58A1P-GPWRRWM7")] // 분류 실패
    [InlineData("RMRDZZ58A1P-GPWRRWM7")] // 모듈 용량코드 미지원
    [InlineData("RC")]                    // 길이 부족
    public void Unresolvable_returns_null(string part)
    {
        Assert.Null(BinIdResolver.Resolve(part, Cfg));
    }
}
