namespace FileScanner;

/// <summary>
/// Represents metadata about a single file on disk.
/// </summary>
public record FileEntry(string FullPath, long SizeBytes, DateTime LastModified);
