using System.Linq;
using UnimesAutomation;
using Xunit;

public class BinIdResolverTests
{
    [Theory]
    [InlineData("RMRDAG58A1P-GPWRRWM7", "RAM_Module_Normal_16GB")] // AG=16GB, DDR무관
    [InlineData("RMRD8G58A1P-GPWRRWM7", "RAM_Module_Normal_8GB")]  // 8G=8GB
    [InlineData("RMRDBG58A1P-GPWRRWM7", "RAM_Module_Normal_32GB")] // BG=32GB
    [InlineData("RMRDCG58A1P-GPWRRWM7", "RAM_Module_Normal_64GB")] // CG=64GB(미등록이어도 이름은 계산)
    [InlineData("ZMRDAG58A1P-GPWRRWM7", "RAM_Module_Normal_16GB")] // ZM=Module, 이후 규칙 동일
    public void Module_resolves_capacity_only(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, "M050", "C010");
        Assert.NotNull(target);
        Assert.Equal(PartClass.Module, target!.Class);
        Assert.Equal("M050", target.ProcessSearchKey);
        Assert.Equal(expected, target.BinIdName);
    }

    [Theory]
    [InlineData("RCA8G58A1P-XPWRRWM7", "DRAM_Comp_Bin_8Gb")]            // DDR4 8Gb
    [InlineData("RCAAG58A1P-XPWRRWM7", "DRAM_Comp_Bin_16Gb")]          // DDR4 16Gb
    [InlineData("RCRAH58A1P-XPWRRWM7", "DRAM_Comp_D5_XMP72_Bin_16Gb")] // DDR5 16Gb
    [InlineData("ZCA8G485WD-5BGRX", "DRAM_Comp_Bin_8Gb")]              // ZC=Comp, 이후 규칙 동일
    [InlineData("ZCAAG485WC-5BGRX", "DRAM_Comp_Bin_16Gb")]             // ZC=Comp, 이후 규칙 동일
    public void Comp_resolves_with_ddr(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, "M050", "C010");
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
        Assert.Null(BinIdResolver.Resolve(part, "M050", "C010"));
    }

    [Fact]
    public void Ssd_b0_resolves_two_rows()
    {
        var cfg = RootConfig.CreateDefault();

        var target = BinIdResolver.Resolve("DABHGD8J5F-HRRXZ21B0", cfg);

        Assert.NotNull(target);
        Assert.Equal(PartClass.Ssd, target!.Class);
        Assert.Equal(2, target.Rows.Count);
        Assert.All(target.Rows, r => Assert.Equal("M020", r.ProcessSearchKey));
        Assert.Equal(["SSD_RDT_512GB", "SSD_RDT_512GB"], target.Rows.Select(r => r.BinIdName).ToArray());
        Assert.Equal(["Normal-1", "Normal-1"], target.Rows.Select(r => r.Row.BinType).ToArray());
        Assert.Equal(["0", "1"], target.Rows.Select(r => r.Row.RetestNo).ToArray());
        Assert.Equal(["N", "Y"], target.Rows.Select(r => r.Row.BinComplete).ToArray());
        Assert.Equal(["Normal", "Normal"], target.Rows.Select(r => r.Row.RetestTh).ToArray());
    }

    [Fact]
    public void Ssd_r0_resolves_three_rows()
    {
        var cfg = RootConfig.CreateDefault();

        var target = BinIdResolver.Resolve("DABHGD8J5F-HRRXZ21R0", cfg);

        Assert.NotNull(target);
        Assert.Equal(PartClass.Ssd, target!.Class);
        Assert.Equal(3, target.Rows.Count);
        Assert.Equal(
            ["SSD_RDT_512GB", "SSD_RDT_512GB_R", "SSD_ValueTrime_480GB"],
            target.Rows.Select(r => r.BinIdName).ToArray());
        Assert.Equal(["Normal-1", "Normal-2", "Special-1"], target.Rows.Select(r => r.Row.BinType).ToArray());
        Assert.Equal(["0", "1", "2"], target.Rows.Select(r => r.Row.RetestNo).ToArray());
        Assert.Equal(["N", "N", "Y"], target.Rows.Select(r => r.Row.BinComplete).ToArray());
        Assert.Equal(["H", "Normal", "Normal"], target.Rows.Select(r => r.Row.RetestTh).ToArray());
    }

    [Theory]
    [InlineData("DAXBGD8J5F-HRRXZ21B0", "SSD_RDT_32GB")]
    [InlineData("DEXKGD8J5F-HRRXZ21B0", "SSD_RDT_1TB")]
    public void Ssd_resolves_capacity_code_from_fourth_and_fifth_characters(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, RootConfig.CreateDefault());

        Assert.NotNull(target);
        Assert.Equal(expected, target!.Rows[0].BinIdName);
    }

    [Theory]
    [InlineData("DABHGD8J5F-HRRXZ21X0")]
    [InlineData("DAXXGD8J5F-HRRXZ21B0")]
    public void Ssd_unresolvable_returns_null(string part)
    {
        Assert.Null(BinIdResolver.Resolve(part, RootConfig.CreateDefault()));
    }

    [Fact]
    public void Sip_resolves_two_rows_with_same_binid()
    {
        var cfg = RootConfig.CreateDefault();

        var target = BinIdResolver.Resolve("SNAKGD8J0B-HBRV310J", cfg); // KG=1Tb, 끝 0J(Marking 예외라도 BIN은 동일)

        Assert.NotNull(target);
        Assert.Equal(PartClass.Sip, target!.Class);
        Assert.Equal(2, target.Rows.Count);
        Assert.All(target.Rows, r => Assert.Equal("M030", r.ProcessSearchKey));
        Assert.Equal(["SIP_Normal_1Tb_AIO", "SIP_Normal_1Tb_AIO"], target.Rows.Select(r => r.BinIdName).ToArray());
        Assert.Equal(["Normal-1", "Normal-2"], target.Rows.Select(r => r.Row.BinType).ToArray());
        Assert.Equal(["0", "1"], target.Rows.Select(r => r.Row.RetestNo).ToArray());
        Assert.Equal(["", "Y"], target.Rows.Select(r => r.Row.BinComplete).ToArray()); // 1행 Blank(미설정), 2행 Y
        Assert.Equal(["Normal", "Y"], target.Rows.Select(r => r.Row.RetestTh).ToArray());
    }

    [Theory]
    [InlineData("SNA8GD8J0B-HBRV310J", "SIP_Normal_8Gb_AIO")]  // 4-5='8G'
    [InlineData("SNAAGD8J0B-HBRV310J", "SIP_Normal_16Gb_AIO")] // 4-5='AG'
    [InlineData("SNAVGD8J0B-HBRV310J", "SIP_Normal_8Tb_AIO")]  // 4-5='VG'
    public void Sip_resolves_capacity_from_fourth_and_fifth(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, RootConfig.CreateDefault());

        Assert.NotNull(target);
        Assert.Equal(expected, target!.Rows[0].BinIdName);
    }

    [Theory]
    [InlineData("SNAZZD8J0B-HBRV310J")] // 미지원 용량코드 ZZ
    public void Sip_unresolvable_returns_null(string part)
    {
        Assert.Null(BinIdResolver.Resolve(part, RootConfig.CreateDefault()));
    }
}
