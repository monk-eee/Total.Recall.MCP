using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Total.Recall.Reporting;

/// <summary>
/// Renders the JSON output of the report tools as fixed-width text tables.
/// Used by <see cref="ReportRunner"/> when <c>--format table</c> is set.
///
/// Strategy: parse as JSON. If parsing fails, return the raw text (covers the
/// tools' "No data yet" / "ERROR …" plain-text responses). If the root is an
/// array, render that array. If the root is an object, find the primary array
/// property (longest array) and render that; otherwise render the object as a
/// key=value list. Scalar columns are formatted; nested objects/arrays are
/// rendered as compact JSON.
/// </summary>
internal static class TableRenderer
{
    public static string Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return output;
        var trimmed = output.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return output; // not JSON — return as-is
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            return output;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return RenderArray("(root)", root);
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                // Find the longest array property — that's the table data.
                string? primaryName = null;
                int primaryLength = -1;
                var scalars = new List<(string Key, JsonElement Value)>();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var len = prop.Value.GetArrayLength();
                        if (len > primaryLength) { primaryLength = len; primaryName = prop.Name; }
                    }
                    else
                    {
                        scalars.Add((prop.Name, prop.Value));
                    }
                }

                var sb = new StringBuilder();
                if (scalars.Count > 0)
                {
                    sb.AppendLine(RenderKeyValue(scalars));
                }
                if (primaryName is not null)
                {
                    if (scalars.Count > 0) sb.AppendLine();
                    sb.Append(RenderArray(primaryName, root.GetProperty(primaryName)));
                }
                return sb.ToString().TrimEnd();
            }

            return output;
        }
    }

    private static string RenderKeyValue(List<(string Key, JsonElement Value)> entries)
    {
        int keyWidth = entries.Max(e => e.Key.Length);
        var sb = new StringBuilder();
        foreach (var (k, v) in entries)
        {
            sb.Append(k.PadRight(keyWidth));
            sb.Append("  ");
            sb.AppendLine(FormatScalar(v));
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderArray(string label, JsonElement array)
    {
        if (array.GetArrayLength() == 0)
        {
            return $"{label}: (empty)";
        }

        // Collect column names from the union of all element properties (object rows).
        // For arrays of scalars, render a single "value" column.
        var firstKind = array[0].ValueKind;
        if (firstKind != JsonValueKind.Object)
        {
            var lines = new List<string> { "value" };
            foreach (var el in array.EnumerateArray()) lines.Add(FormatScalar(el));
            return RenderColumns(label, [["value"], .. lines.Skip(1).Select(l => new[] { l })]);
        }

        var columns = new List<string>();
        var seen = new HashSet<string>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in el.EnumerateObject())
            {
                if (seen.Add(prop.Name)) columns.Add(prop.Name);
            }
        }

        var rows = new List<string[]> { columns.ToArray() };
        foreach (var el in array.EnumerateArray())
        {
            var row = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                row[i] = el.TryGetProperty(columns[i], out var v) ? FormatScalar(v) : "";
            }
            rows.Add(row);
        }
        return RenderColumns(label, rows);
    }

    private static string RenderColumns(string label, IReadOnlyList<string[]> rows)
    {
        int cols = rows[0].Length;
        var widths = new int[cols];
        for (int c = 0; c < cols; c++)
        {
            int w = 0;
            foreach (var row in rows)
            {
                if (c < row.Length && row[c].Length > w) w = row[c].Length;
            }
            widths[c] = w;
        }

        var sb = new StringBuilder();
        sb.Append('[').Append(label).Append(']').AppendLine();
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var cell = rows[r][c];
                sb.Append(cell.PadRight(widths[c]));
                if (c < cols - 1) sb.Append("  ");
            }
            sb.AppendLine();
            if (r == 0)
            {
                for (int c = 0; c < cols; c++)
                {
                    sb.Append(new string('-', widths[c]));
                    if (c < cols - 1) sb.Append("  ");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatScalar(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        JsonValueKind.Object => v.GetRawText(),
        JsonValueKind.Array => $"[{v.GetArrayLength()} items]",
        _ => v.GetRawText(),
    };
}
