using System.Text;

namespace FileScanner;

/// <summary>
/// Streaming CSV exporter that writes results as they're found, avoiding memory buildup.
/// </summary>
public class StreamingCsvExporter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new object();
    private long _recordCount = 0;
    private bool _headerWritten = false;

    public StreamingCsvExporter(string filePath)
    {
        _writer = new StreamWriter(filePath, false, Encoding.UTF8, bufferSize: 8192);
    }

    public async Task WriteHeaderAsync()
    {
        if (_headerWritten) return;
        
        lock (_lock)
        {
            if (_headerWritten) return;
            
            _writer.WriteLine("FullPath,SizeBytes,LastModified,SizeMB,RelativePath");
            _headerWritten = true;
        }
        
        await _writer.FlushAsync();
    }

    public async Task WriteEntryAsync(FileEntry entry, string rootPath = "")
    {
        await WriteHeaderAsync();
        
        var sizeMB = Math.Round(entry.SizeBytes / (1024.0 * 1024.0), 3);
        var relativePath = string.IsNullOrEmpty(rootPath) ? entry.FullPath : 
            Path.GetRelativePath(rootPath, entry.FullPath);

        var line = $"\"{EscapeCsv(entry.FullPath)}\",{entry.SizeBytes},\"{entry.LastModified:yyyy-MM-dd HH:mm:ss}\",{sizeMB},\"{EscapeCsv(relativePath)}\"";
        
        lock (_lock)
        {
            _writer.WriteLine(line);
            _recordCount++;
            
            // Flush every 100 records to ensure data isn't lost
            if (_recordCount % 100 == 0)
            {
                _writer.Flush();
            }
        }
    }

    public async Task WriteEntriesAsync(IAsyncEnumerable<FileEntry> entries, string rootPath = "", 
        IProgress<long>? progress = null)
    {
        await WriteHeaderAsync();
        
        await foreach (var entry in entries)
        {
            await WriteEntryAsync(entry, rootPath);
            progress?.Report(_recordCount);
            
            // Periodic progress update
            if (_recordCount % 1000 == 0)
            {
                Console.Write($"\rWrote {_recordCount:N0} records to CSV...");
            }
        }
        
        await _writer.FlushAsync();
        Console.WriteLine($"\nCompleted writing {_recordCount:N0} records to CSV.");
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
            
        // Escape quotes by doubling them
        return value.Replace("\"", "\"\"");
    }

    public long RecordCount => _recordCount;

    public void Dispose()
    {
        _writer?.Dispose();
    }
}