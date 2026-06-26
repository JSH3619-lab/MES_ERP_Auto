using ClosedXML.Excel;

namespace UnimesAutomation;

// 부서별 엑셀에서 '품목코드' 열을 찾아 셀 문자열을 뽑는다.
// 헤더 시작 행/열이 파일마다 달라 자동 탐색한다. 분류·더미·중복 필터는 PartListParser.FromCodes가 담당.
public static class ExcelPartReader
{
    public static List<string> ExtractCodeCells(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var (headerRow, col) = FindCodeColumn(rows);
        var result = new List<string>();
        if (headerRow < 0)
        {
            return result;
        }

        for (var r = headerRow + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (col >= row.Count)
            {
                continue;
            }

            var value = (row[col] ?? "").Trim();
            if (value.Length > 0)
            {
                result.Add(value);
            }
        }

        return result;
    }

    // 상단 ~15행에서 '품목코드'(공백 무시) 첫 셀 위치. 못 찾으면 (-1,-1).
    private static (int Row, int Col) FindCodeColumn(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var scan = Math.Min(rows.Count, 15);
        for (var r = 0; r < scan; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var text = row[c];
                if (text != null && text.Replace(" ", "").Contains("품목코드"))
                {
                    return (r, c);
                }
            }
        }

        return (-1, -1);
    }

    // 엑셀 파일의 첫 시트에서 '품목코드' 열 셀을 읽는다. (I/O — 위 순수 로직 + ClosedXML)
    public static List<string> ReadCodes(string path)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.First();
        var used = worksheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        var firstRow = used.RangeAddress.FirstAddress.RowNumber;
        var lastRow = used.RangeAddress.LastAddress.RowNumber;
        var firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
        var lastCol = used.RangeAddress.LastAddress.ColumnNumber;

        var rows = new List<IReadOnlyList<string?>>();
        for (var r = firstRow; r <= lastRow; r++)
        {
            var cells = new List<string?>();
            for (var c = firstCol; c <= lastCol; c++)
            {
                cells.Add(worksheet.Cell(r, c).GetString());
            }

            rows.Add(cells);
        }

        return ExtractCodeCells(rows);
    }
}
