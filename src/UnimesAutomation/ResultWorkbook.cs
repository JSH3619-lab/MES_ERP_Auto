using ClosedXML.Excel;

namespace UnimesAutomation;

// 실행 결과를 단일 xlsx(시트 2개: 품목정보관리 / BIN 정보관리)로 쓴다. 비어 있는 시트는 만들지 않는다.
public static class ResultWorkbook
{
    public static string Write(
        string outputDirectory,
        string timestamp,
        IReadOnlyList<PartResult> itemResults,
        IReadOnlyList<BinResult> binResults)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"result_{timestamp}.xlsx");

        using var workbook = new XLWorkbook();

        if (itemResults.Count > 0)
        {
            var ws = workbook.Worksheets.Add("품목정보관리");
            string[] headers = ["품목", "분류", "BIN 관리", "Turn Key", "조립입고", "불량창고", "Marking", "저장", "상태", "메시지", "처리일시"];
            for (var c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
            }

            var row = 2;
            foreach (var r in itemResults)
            {
                ws.Cell(row, 1).Value = r.PartNo;
                ws.Cell(row, 2).Value = r.Classification;
                ws.Cell(row, 3).Value = r.BinManage;
                ws.Cell(row, 4).Value = r.TurnKey;
                ws.Cell(row, 5).Value = r.AssemblyIn;
                ws.Cell(row, 6).Value = r.DefectWarehouse;
                ws.Cell(row, 7).Value = r.Marking;
                ws.Cell(row, 8).Value = r.Saved;
                ws.Cell(row, 9).Value = r.Status;
                ws.Cell(row, 10).Value = r.Message;
                ws.Cell(row, 11).Value = r.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss");
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        if (binResults.Count > 0)
        {
            var ws = workbook.Worksheets.Add("BIN 정보관리");
            string[] headers = ["품목", "분류", "공정명", "BIN Type", "Retest No", "BIN 완료여부", "Retest TH", "BIN ID", "저장", "상태", "메시지", "처리일시"];
            for (var c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
            }

            var row = 2;
            foreach (var r in binResults)
            {
                ws.Cell(row, 1).Value = r.PartNo;
                ws.Cell(row, 2).Value = r.Classification;
                ws.Cell(row, 3).Value = r.ProcessName;
                ws.Cell(row, 4).Value = r.BinType;
                ws.Cell(row, 5).Value = r.RetestNo;
                ws.Cell(row, 6).Value = r.BinComplete;
                ws.Cell(row, 7).Value = r.RetestTh;
                ws.Cell(row, 8).Value = r.BinId;
                ws.Cell(row, 9).Value = r.Saved;
                ws.Cell(row, 10).Value = r.Status;
                ws.Cell(row, 11).Value = r.Message;
                ws.Cell(row, 12).Value = r.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss");
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        if (!workbook.Worksheets.Any())
        {
            workbook.Worksheets.Add("결과 없음");
        }

        workbook.SaveAs(path);
        return path;
    }
}
