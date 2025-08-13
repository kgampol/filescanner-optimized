using System.Collections.Concurrent;
using System.Text;

namespace FileScanner;

/// <summary>
/// High-performance streaming CSV exporter with duplicate detection and minimal I/O
/// </summary>
public class StreamingCsvExporter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new object();
    private readonly ConcurrentDictionary<(long size, string name), int> _duplicateTracker;
    private readonly List<string> _batch;
    private long _recordCount = 0;
    private long _duplicateCount = 0;
    private bool _headerWritten = false;
    private const int BatchSize = 1000;

    public StreamingCsvExporter(string filePath)
    {
        _writer = new StreamWriter(filePath, false, Encoding.UTF8, bufferSize: 65536);
        _duplicateTracker = new ConcurrentDictionary<(long, string), int>();
        _batch = new List<string>(BatchSize);
    }

    public void WriteHeader()
    {
        if (_headerWritten) return;
        
        _writer.WriteLine("FullPath,SizeBytes,Created,LastModified,SizeMB,DuplicateCount");
        _headerWritten = true;
    }

    public void WriteEntry(FileEntry entry)
    {
        if (!_headerWritten) WriteHeader();
        
        // Fast duplicate detection using size + filename
        var key = (entry.SizeBytes, Path.GetFileName(entry.FullPath));
        var duplicateCount = _duplicateTracker.AddOrUpdate(key, 1, (k, v) => v + 1);
        
        if (duplicateCount > 1) _duplicateCount++;
        
        var sizeMB = Math.Round(entry.SizeBytes / (1024.0 * 1024.0), 3);
        var created = entry.CreatedUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
        var line = $"\"{EscapeCsv(entry.FullPath)}\",{entry.SizeBytes},\"{created}\",\"{entry.LastModified:yyyy-MM-dd HH:mm:ss}\",{sizeMB},{duplicateCount}";
        
        lock (_lock)
        {
            _batch.Add(line);
            _recordCount++;
            
            // Write batch when full to reduce I/O
            if (_batch.Count >= BatchSize)
            {
                FlushBatch();
            }
        }
    }

    public async Task WriteEntriesAsync(IAsyncEnumerable<FileEntry> entries, IProgress<long>? progress = null)
    {
        WriteHeader();
        
        await foreach (var entry in entries)
        {
            WriteEntry(entry);
            
            // Progress update every 1000 records
            if (_recordCount % 1000 == 0)
            {
                progress?.Report(_recordCount);
                Console.Write($"\rProcessed {_recordCount:N0} files, {_duplicateCount:N0} duplicates...");
            }
        }
        
        // Flush remaining batch
        lock (_lock)
        {
            if (_batch.Count > 0) FlushBatch();
        }
        
        _writer.Flush();
        Console.WriteLine($"\nCompleted: {_recordCount:N0} files, {_duplicateCount:N0} duplicates");
    }

    private void FlushBatch()
    {
        foreach (var line in _batch)
        {
            _writer.WriteLine(line);
        }
        _batch.Clear();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
            
        // Escape quotes by doubling them
        return value.Replace("\"", "\"\"");
    }

    public long RecordCount => _recordCount;
    public long DuplicateCount => _duplicateCount;

    public void Dispose()
    {
        // Flush any remaining batch
        lock (_lock)
        {
            if (_batch.Count > 0) FlushBatch();
        }
        _writer?.Dispose();
    }
}