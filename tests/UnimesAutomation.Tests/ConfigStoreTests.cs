using System;
using System.IO;
using UnimesAutomation;
using Xunit;

public class ConfigStoreTests
{
    [Fact]
    public void Save_then_Load_roundtrips_categories()
    {
        var cfg = RootConfig.CreateDefault();
        cfg.Categories.DramModule.ItemInfo.TurnKey = "Y";
        cfg.Categories.DramComp.BinInfo.ProcessSearchKey = "C999";

        var path = Path.Combine(Path.GetTempPath(), $"unimes_cfg_{Guid.NewGuid():N}.json");
        try
        {
            ConfigStore.Save(path, cfg);
            var loaded = ConfigStore.Load(path);

            Assert.Equal("Y", loaded.Categories.DramModule.ItemInfo.TurnKey);
            Assert.Equal("C999", loaded.Categories.DramComp.BinInfo.ProcessSearchKey);
            Assert.Equal("제품 폐기창고", loaded.Categories.DramModule.ItemInfo.DefectWarehouse);
            Assert.Single(loaded.Categories.DramModule.BinInfo.Rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_legacy_flat_config_builds_categories()
    {
        var legacy = """
        {
          "login": { "userId": "22402002", "passwordMode": "env" },
          "safety": { "dryRun": true, "saveEnabled": false },
          "itemInfo": {
            "menuName": "품목정보관리", "binManage": "Y", "turnKey": "N", "assemblyIn": "Y",
            "moduleDefectWarehouse": "제품 폐기창고", "compDefectWarehouse": "COMPONENT 폐기창고",
            "recoveryPart": "RMRDAG58A1B-GPWRRWM7"
          },
          "binInfo": {
            "menuName": "품목별 BIN 정보 관리", "moduleProcessKey": "M050", "compProcessKey": "C010",
            "binType": "Normal-1", "retestNo": "0", "binComplete": "Y", "retestTh": "H"
          }
        }
        """;
        var path = Path.Combine(Path.GetTempPath(), $"unimes_legacy_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, legacy);
        try
        {
            var loaded = ConfigStore.Load(path);

            Assert.Equal("제품 폐기창고", loaded.Categories.DramModule.ItemInfo.DefectWarehouse);
            Assert.Equal("COMPONENT 폐기창고", loaded.Categories.DramComp.ItemInfo.DefectWarehouse);
            Assert.Equal("M050", loaded.Categories.DramModule.BinInfo.ProcessSearchKey);
            Assert.Equal("C010", loaded.Categories.DramComp.BinInfo.ProcessSearchKey);
            Assert.Equal("H", loaded.Categories.DramModule.BinInfo.Rows[0].RetestTh);
            Assert.Equal("품목별 BIN 정보 관리", loaded.Global.BinInfoMenuName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var loaded = ConfigStore.Load(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json"));
        Assert.Equal("M050", loaded.Categories.DramModule.BinInfo.ProcessSearchKey);
    }
}
