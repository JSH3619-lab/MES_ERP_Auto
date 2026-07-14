using UnimesAutomation;
using Xunit;

public class PartClassifierTests
{
    [Theory]
    [InlineData("RMRCAG58A1P-GPWRRWM7")]
    [InlineData("TMRCAG58A1P-GPWRRWM7")]
    [InlineData("BMRCAG58A1P-GPWRRWM7")]
    [InlineData("CMRCAG58A1P-GPWRRWM7")]
    [InlineData("ZMRCAG58A1P-GPWRRWM7")]
    [InlineData("RM4C1G58A1P-GPWRRWM7")]
    [InlineData("TM4C1G58A1P-GPWRRWM7")]
    [InlineData("BM4C1G58A1P-GPWRRWM7")]
    [InlineData("CM4C1G58A1P-GPWRRWM7")]
    [InlineData("ZM4C1G58A1P-GPWRRWM7")]
    public void CompMdl_item_info_uses_dram_module_warehouse(string part)
    {
        var cfg = RootConfig.CreateDefault();
        var classification = PartClassifier.Classify(part);

        Assert.Equal(PartClass.CompMdl, classification);
        Assert.Same(cfg.Categories.DramModule.ItemInfo, cfg.ResolveItemInfo(classification));
        Assert.Equal("제품 폐기창고", cfg.ResolveItemInfo(classification)!.DefectWarehouse);
    }

    // DRAM Module 한정: 끝 00/B0/R0는 더미. Comp/Comp_MDL/SSD의 B0/R0는 정상.
    [Theory]
    [InlineData("RMRDAG58A1P-GPWRRWM00", true)]  // 끝 00
    [InlineData("RMRDAG58A1P-GPWRRWMB0", true)]  // Module 끝 B0
    [InlineData("RMRDAG58A1P-GPWRRWMR0", true)]  // Module 끝 R0
    [InlineData("ZMRDAG58A1P-GPWRRWMB0", true)]  // ZM도 Module
    [InlineData("RMRDAG58A1P-GPWRRWM7", false)]  // 정상 Module
    [InlineData("ZCA8G485WE-5BVRB0", false)]     // Comp 끝 B0는 정상
    [InlineData("RM4C1G58A1P-GPWRRWMB0", false)] // Comp_MDL 끝 B0는 정상
    [InlineData("DABHGD8H5E-HRRXZ21B0", false)]  // SSD 끝 B0는 정상
    [InlineData("DABHGD8J5F-HRRXZ21R0", false)]  // SSD 끝 R0는 정상
    public void IsDummy_module_b0_r0_but_not_other_classes(string part, bool expected)
    {
        Assert.Equal(expected, PartClassifier.IsDummy(part));
    }
}
