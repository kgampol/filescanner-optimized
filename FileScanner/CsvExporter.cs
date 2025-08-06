using System.Text;

namespace FileScanner;

/// <summary>
/// Writes FileEntry records to a CSV file.
/// </summary>
public static class CsvExporter
{
    public static void Export(IEnumerable<FileEntry> entries, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FullPath,SizeBytes,LastModifiedUtc");

        foreach (var entry in entries)
        {
            string safePath = entry.FullPath.Replace("\"", "\"\"");
            if (safePath.Contains(',') || safePath.Contains('"'))
            {
                safePath = $"\"{safePath}\"";
            }

            sb.AppendLine($"{safePath},{entry.SizeBytes},{entry.LastModified:O}");
        }

        File.WriteAllText(outputPath, sb.ToString());
    }
}
