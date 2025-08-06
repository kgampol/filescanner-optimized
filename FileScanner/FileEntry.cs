namespace FileScanner;

/// <summary>
/// Represents metadata about a single file on disk.
/// </summary>
public record FileEntry(string FullPath, long SizeBytes, DateTime LastModified);//record is a class that is immutable and has a constructor
