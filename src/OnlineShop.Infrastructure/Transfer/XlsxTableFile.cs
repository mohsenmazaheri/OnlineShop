using System.Globalization;
using MiniExcelLibs;
using OnlineShop.Application.Catalog.Transfer;

namespace OnlineShop.Infrastructure.Transfer;

/// <summary>Excel (.xlsx) through MiniExcel (Apache-2.0, streaming, no other dependencies).</summary>
public sealed class XlsxTableFile : ITableFile
{
    public string Format => "xlsx";

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public byte[] Write(TableData table)
    {
        // Every cell is written as text, so SKUs and barcodes keep their leading zeros.
        var rows = table.Rows.Select(row =>
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < table.Headers.Count; i++)
                values[table.Headers[i]] = i < row.Count ? row[i] : "";
            return values;
        }).ToList();
        if (rows.Count == 0)
            rows.Add(table.Headers.ToDictionary(h => h, _ => (object?)"", StringComparer.Ordinal));

        using var stream = new MemoryStream();
        stream.SaveAs(rows, printHeader: true, sheetName: "products", excelType: ExcelType.XLSX);
        return stream.ToArray();
    }

    public TableData Read(Stream stream, int maxRows)
    {
        // MiniExcel needs a seekable stream.
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;

        var rows = new List<IReadOnlyList<string>>();
        foreach (IDictionary<string, object?> row in copy.Query(useHeaderRow: false, excelType: ExcelType.XLSX))
        {
            var cells = row.Values.Select(Text).ToList();
            while (cells.Count > 0 && cells[^1].Length == 0)
                cells.RemoveAt(cells.Count - 1);
            if (cells.Count == 0)
                continue;
            rows.Add(cells);
            if (rows.Count > maxRows + 1)
                break;
        }
        return rows.Count == 0 ? new TableData([], []) : new TableData(rows[0], rows.Skip(1).ToList());
    }

    /// <summary>A cell as text: numbers without exponent or grouping, dates as ISO, the rest as typed.</summary>
    private static string Text(object? value) => value switch
    {
        null => "",
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        DateTime t => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "",
    };
}
