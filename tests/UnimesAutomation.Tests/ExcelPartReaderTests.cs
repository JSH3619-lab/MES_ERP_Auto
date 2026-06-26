using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using UnimesAutomation;
using Xunit;

public class ExcelPartReaderTests
{
    private static IReadOnlyList<IReadOnlyList<string?>> Grid(params string?[][] rows)
        => rows.Select(r => (IReadOnlyList<string?>)r.ToList()).ToList();

    [Fact]
    public void Finds_header_at_origin_and_collects_below()
    {
        var grid = Grid(
            ["품목코드", "품목명"],
            ["ZCA8G485WE-5BVRX", "x"],
            ["ZCAAG485WA-5BPRX", "y"]);

        Assert.Equal(
            new[] { "ZCA8G485WE-5BVRX", "ZCAAG485WA-5BPRX" },
            ExcelPartReader.ExtractCodeCells(grid).ToArray());
    }

    [Fact]
    public void Finds_header_when_offset_by_blank_rows_and_columns()
    {
        // DDR5/SSD 양식: 위·왼쪽에 빈 행/열, 헤더가 안쪽에 있음
        var grid = Grid(
            [null, null, null],
            [null, "품 목 코 드", "품목명"],
            [null, "RMRSBG58A2P-GEWRRWM70Y", "z"]);

        Assert.Equal(
            new[] { "RMRSBG58A2P-GEWRRWM70Y" },
            ExcelPartReader.ExtractCodeCells(grid).ToArray());
    }

    [Fact]
    public void Collects_all_nonempty_below_including_noise_rows()
    {
        // SIP 양식: 중간 빈 행/구역 라벨도 그대로 수집(필터는 FromCodes가 담당)
        var grid = Grid(
            ["품목코드"],
            ["SNAKGD8J0B-HZRV32"],
            [null],
            ["MES - Mining"],
            ["SNAKGD8J0B-DZRV32"]);

        Assert.Equal(
            new[] { "SNAKGD8J0B-HZRV32", "MES - Mining", "SNAKGD8J0B-DZRV32" },
            ExcelPartReader.ExtractCodeCells(grid).ToArray());
    }

    [Fact]
    public void Returns_empty_when_no_header()
    {
        var grid = Grid(["foo", "bar"], ["1", "2"]);
        Assert.Empty(ExcelPartReader.ExtractCodeCells(grid));
    }

    [Fact]
    public void ReadCodes_reads_real_xlsx_and_feeds_fromcodes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xlsxtest_{System.Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(3, 2).Value = "품목코드";       // 헤더가 안쪽(3행 2열)
                ws.Cell(4, 2).Value = "DABHGD8J5F-ARRXZ31Z0";   // 진행
                ws.Cell(5, 2).Value = "DABHGD8J5F-ARRXZ3100";   // 더미(끝00)
                ws.Cell(6, 2).Value = "DUMMY";                  // 노이즈
                wb.SaveAs(path);
            }

            var codes = ExcelPartReader.ReadCodes(path);
            Assert.Equal(
                new[] { "DABHGD8J5F-ARRXZ31Z0", "DABHGD8J5F-ARRXZ3100", "DUMMY" },
                codes.ToArray());

            var parts = PartListParser.FromCodes(codes);
            Assert.Equal(new[] { "DABHGD8J5F-ARRXZ31Z0" }, parts.Select(p => p.PartNo).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
