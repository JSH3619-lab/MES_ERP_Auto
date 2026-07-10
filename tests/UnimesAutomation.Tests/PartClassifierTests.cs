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
}
