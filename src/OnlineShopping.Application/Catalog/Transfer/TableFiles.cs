using System.Text;

namespace OnlineShopping.Application.Catalog.Transfer;

/// <summary>A sheet of text cells: one header row and the data rows (every cell already as text).</summary>
public sealed record TableData(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>A file format products are exported to and imported from (CSV, Excel).</summary>
public interface ITableFile
{
    /// <summary>"csv" or "xlsx".</summary>
    string Format { get; }

    string ContentType { get; }

    byte[] Write(TableData table);

    /// <summary>The header row and at most <paramref name="maxRows"/> + 1 data rows (one more tells the caller it was too long).</summary>
    TableData Read(Stream stream, int maxRows);
}

/// <summary>
/// CSV as spreadsheet programs write it (RFC 4180: quoted cells, doubled quotes, line breaks inside quotes). Export
/// starts with a UTF-8 byte order mark so Excel shows Persian, Arabic and Armenian correctly; import accepts comma,
/// semicolon or tab, whichever the header uses.
/// </summary>
public sealed class CsvTableFile : ITableFile
{
    private static readonly char[] Separators = [',', ';', '\t'];
    private static readonly char[] NeedsQuotes = [',', '"', '\n', '\r', ';'];

    public string Format => "csv";

    public string ContentType => "text/csv; charset=utf-8";

    public byte[] Write(TableData table)
    {
        var text = new StringBuilder();
        void Line(IEnumerable<string> cells) => text.Append(string.Join(',', cells.Select(Quote))).Append("\r\n");
        Line(table.Headers);
        foreach (var row in table.Rows)
            Line(row);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public TableData Read(Stream stream, int maxRows)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        var firstLine = content.Split('\n', 2)[0];
        var separator = Separators.MaxBy(c => firstLine.Count(x => x == c));
        var rows = Parse(content, separator, maxRows + 1).Where(r => r.Any(c => c.Length > 0)).ToList();
        return rows.Count == 0 ? new TableData([], []) : new TableData(rows[0], rows.Skip(1).ToList());
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny(NeedsQuotes) >= 0 || cell.StartsWith(' ') || cell.EndsWith(' ')
            ? $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : cell;

    private static IEnumerable<List<string>> Parse(string content, char separator, int maxDataRows)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var produced = 0;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
                continue;
            }
            if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else if (c == '\n')
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
                // Blank lines are skipped (and not counted).
                if (row.Any(c => c.Length > 0))
                {
                    yield return row;
                    // Header + maxDataRows rows are enough to tell "too many".
                    if (++produced > maxDataRows)
                        yield break;
                }
                row = [];
            }
            else if (c != '\r')
            {
                cell.Append(c);
            }
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().Trim());
            yield return row;
        }
    }
}

/// <summary>The available formats: by name for export, by content for import (an .xlsx file is a ZIP archive).</summary>
public sealed class TableFiles(IEnumerable<ITableFile> formats)
{
    private readonly List<ITableFile> _formats = formats.ToList();

    public ITableFile? ByFormat(string? format) => _formats.FirstOrDefault(f => string.Equals(f.Format, format?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Excel for a ZIP signature ("PK\x03\x04"), otherwise CSV.</summary>
    public ITableFile Detect(ReadOnlySpan<byte> start) =>
        (start.Length >= 4 && start[0] == 0x50 && start[1] == 0x4B && start[2] == 0x03 && start[3] == 0x04 ? ByFormat("xlsx") : null)
        ?? ByFormat("csv")!;
}
