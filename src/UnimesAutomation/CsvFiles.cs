using System.Text;

namespace UnimesAutomation;

public static class CsvFiles
{
    public static IReadOnlyList<PartRequest> ReadPartRequests(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return [];
        }

        var header = ParseCsvLine(lines[0]);
        var partIndex = FindColumn(header, "part_no", "partno", "품목명", "품목ID");
        var accountIndex = FindColumn(header, "item_account", "품목계정");
        var itemIdIndex = FindColumn(header, "item_id", "품목ID");
        var itemNameIndex = FindColumn(header, "item_name", "품목명");

        if (partIndex < 0)
        {
            throw new InvalidOperationException("input_parts.csv must contain a part_no column.");
        }

        var requests = new List<PartRequest>();
        foreach (var line in lines.Skip(1))
        {
            var cells = ParseCsvLine(line);
            var partNo = GetCell(cells, partIndex);
            if (string.IsNullOrWhiteSpace(partNo))
            {
                continue;
            }

            requests.Add(new PartRequest
            {
                PartNo = partNo.Trim(),
                ItemAccount = GetCell(cells, accountIndex).Trim(),
                ItemId = GetCell(cells, itemIdIndex).Trim(),
                ItemName = GetCell(cells, itemNameIndex).Trim()
            });
        }

        return requests;
    }

    public static string WriteResults(string outputDirectory, string timestamp, IReadOnlyList<PartResult> results)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"result_{timestamp}.csv");

        var builder = new StringBuilder();
        builder.AppendLine("part_no,classification,bin_manage,turn_key,assembly_in,defect_warehouse,saved,status,message");

        foreach (var result in results)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Escape(result.PartNo),
                Escape(result.Classification),
                Escape(result.BinManage),
                Escape(result.TurnKey),
                Escape(result.AssemblyIn),
                Escape(result.DefectWarehouse),
                Escape(result.Saved),
                Escape(result.Status),
                Escape(result.Message)
            }));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static int FindColumn(IReadOnlyList<string> header, params string[] names)
    {
        for (var index = 0; index < header.Count; index++)
        {
            var normalized = Normalize(header[index]);
            if (names.Any(name => normalized.Equals(Normalize(name), StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetCell(IReadOnlyList<string> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
        {
            return "";
        }

        return cells[index];
    }

    private static string Normalize(string value)
    {
        return value.Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant();
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}
