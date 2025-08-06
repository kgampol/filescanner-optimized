using System.Text;

namespace FileScanner;

/// <summary>
/// Writes FileEntry records to a CSV file.
/// </summary>
public static class CsvExporter
{
    public static void Export(IEnumerable<FileEntry> entries, string outputPath)
    {
        var sb = new StringBuilder();//StringBuilder is a class that allows you to build a string incrementally
        sb.AppendLine("FullPath,SizeBytes,LastModifiedUtc");//AppendLine is a method that appends a line to the StringBuilder

        foreach (var entry in entries)//foreach is a loop that iterates over a collection
        {
            string safePath = entry.FullPath.Replace("\"", "\"\"");//Replace is a method that replaces a substring with a new substring
            if (safePath.Contains(',') || safePath.Contains('"'))//Contains is a method that checks if a substring is present in a string
            {
                safePath = $"\"{safePath}\"";//$ is a string interpolation
            }

            sb.AppendLine($"{safePath},{entry.SizeBytes},{entry.LastModified:O}");//AppendLine is a method that appends a line to the StringBuilder
        }

        File.WriteAllText(outputPath, sb.ToString());//WriteAllText is a method that writes a string to a file
    }
}
