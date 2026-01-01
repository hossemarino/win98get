using System.Globalization;

namespace Win98Get.Services;

public static class WingetTableParser
{
    public static IReadOnlyList<Dictionary<string, string>> Parse(string output)
    {
        var lines = SplitLines(output)
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        // Winget table output uses a dashed separator line under the header.
        var headerIndex = lines.FindIndex(IsSeparatorLike);

        if (headerIndex <= 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        var headerLine = lines[headerIndex - 1];
        var separatorLine = lines[headerIndex];

        // Determine columns by scanning the header for word starts.
        var columnStarts = new List<int>();
        for (var i = 0; i < headerLine.Length; i++)
        {
            var isStart = (i == 0 || headerLine[i - 1] == ' ') && headerLine[i] != ' ';
            if (isStart)
            {
                columnStarts.Add(i);
            }
        }

        var columnNames = new List<string>();
        for (var i = 0; i < columnStarts.Count; i++)
        {
            var start = columnStarts[i];
            var end = i == columnStarts.Count - 1 ? headerLine.Length : columnStarts[i + 1];
            var name = SafeSubstring(headerLine, start, end - start).Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                columnNames.Add(name);
            }
        }

        // Build column ranges.
        var ranges = new List<(string Name, int Start, int End)>();
        for (var i = 0; i < columnNames.Count; i++)
        {
            var start = columnStarts[i];
            var end = i == columnNames.Count - 1 ? int.MaxValue : columnStarts[i + 1];
            ranges.Add((columnNames[i], start, end));
        }

        // Data starts after separator.
        var rows = new List<Dictionary<string, string>>();
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsSeparatorLike(line) || IsFootnoteLine(line))
            {
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, start, end) in ranges)
            {
                var value = end == int.MaxValue
                    ? SafeSubstring(line, start, line.Length - start)
                    : SafeSubstring(line, start, Math.Min(end, line.Length) - start);

                row[name] = value.Trim();
            }

            // Skip empty rows
            if (row.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static bool IsSeparatorLike(string line)
        => line.All(c => c == '-' || char.IsWhiteSpace(c)) && line.Count(c => c == '-') >= 10;

    private static bool IsFootnoteLine(string line)
        => line.TrimStart().StartsWith("\u00A9", true, CultureInfo.InvariantCulture);

    private static string SafeSubstring(string s, int start, int length)
    {
        if (start < 0) return string.Empty;
        if (start >= s.Length) return string.Empty;
        if (length <= 0) return string.Empty;
        if (start + length > s.Length) length = s.Length - start;
        return s.Substring(start, length);
    }
}
