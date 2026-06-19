using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using UnimesAutomation;
using Xunit;

public class ResultWorkbookTests
{
    [Fact]
    public void Write_creates_two_sheets_with_headers_and_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unimes_xlsx_{Guid.NewGuid():N}");
        var item = new PartResult
        {
            PartNo = "RMRDAG58A1B-GPWRRWM7", Classification = "Module",
            BinManage = "Y", TurnKey = "N", AssemblyIn = "Y", DefectWarehouse = "제품 폐기창고",
            Saved = "YES", Status = "OK", Message = "ok",
            ProcessedAt = new DateTime(2026, 6, 19, 10, 0, 0)
        };
        var bin = new BinResult
        {
            PartNo = "RCAH18AG-XPWRRWM7", Classification = "Comp", ProcessName = "C010",
            BinType = "Normal-1", RetestNo = "0", BinComplete = "Y", RetestTh = "H",
            BinId = "DRAM_Comp_D5_XMP72_Bin_16Gb", Saved = "YES", Status = "OK", Message = "ok",
            ProcessedAt = new DateTime(2026, 6, 19, 10, 1, 0)
        };
        try
        {
            var path = ResultWorkbook.Write(dir, "20260619_100000", [item], [bin]);
            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToList();
            Assert.Contains("품목정보관리", names);
            Assert.Contains("BIN 정보관리", names);

            var binWs = wb.Worksheet("BIN 정보관리");
            Assert.Equal("공정명", binWs.Cell(1, 3).GetString());
            Assert.Equal("BIN ID", binWs.Cell(1, 8).GetString());
            Assert.Equal("C010", binWs.Cell(2, 3).GetString());
            Assert.Equal("DRAM_Comp_D5_XMP72_Bin_16Gb", binWs.Cell(2, 8).GetString());

            var itemWs = wb.Worksheet("품목정보관리");
            Assert.Equal("불량창고", itemWs.Cell(1, 6).GetString());
            Assert.Equal("제품 폐기창고", itemWs.Cell(2, 6).GetString());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_bin_only_omits_item_sheet()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unimes_xlsx_{Guid.NewGuid():N}");
        var bin = new BinResult { PartNo = "RC...", Classification = "Comp", ProcessName = "C010",
            BinId = "x", Saved = "NO", Status = "DRYRUN", Message = "", ProcessedAt = DateTime.Now };
        try
        {
            var path = ResultWorkbook.Write(dir, "ts2", [], [bin]);
            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToList();
            Assert.DoesNotContain("품목정보관리", names);
            Assert.Contains("BIN 정보관리", names);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
