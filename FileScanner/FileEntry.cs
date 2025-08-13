using System;
using System.IO;

namespace FileScanner;

/// <summary>
/// Enhanced FileEntry class optimized for performance and comprehensive metadata tracking
/// </summary>
public class FileEntry
{
    // Core properties (original)
    public string FullPath { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    
    // Enhanced metadata
    public string FileName { get; set; }
    public string Extension { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? LastAccessedUtc { get; set; }
    public bool IsDirectory { get; set; }
    public FileAttributes? Attributes { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSystem { get; set; }
    public int Depth { get; set; }
    public string ParentDirectory { get; set; }
    
    // Duplicate tracking
    public string ContentHash { get; set; }
    public string DuplicateGroupId { get; set; }
    public int DuplicateCount { get; set; }
    
    // Backwards compatibility constructor
    public FileEntry(string fullPath, long sizeBytes, DateTime lastModified)
    {
        FullPath = fullPath;
        SizeBytes = sizeBytes;
        LastModified = lastModified;
        FileName = Path.GetFileName(fullPath);
        Extension = Path.GetExtension(fullPath);
        ParentDirectory = Path.GetDirectoryName(fullPath);
        IsDirectory = false;
    }
    
    // Default constructor for advanced scenarios
    public FileEntry() { }
    
    /// <summary>
    /// Optimized factory method for FileInfo
    /// </summary>
    public static FileEntry FromFileInfo(FileInfo fileInfo, int depth = 0)
    {
        var attributes = fileInfo.Attributes;
        return new FileEntry
        {
            FullPath = fileInfo.FullName,
            FileName = fileInfo.Name,
            Extension = fileInfo.Extension,
            SizeBytes = fileInfo.Length,
            CreatedUtc = fileInfo.CreationTimeUtc,
            LastModified = fileInfo.LastWriteTimeUtc,
            LastAccessedUtc = fileInfo.LastAccessTimeUtc,
            IsDirectory = false,
            Attributes = attributes,
            IsReadOnly = (attributes & FileAttributes.ReadOnly) != 0,
            IsHidden = (attributes & FileAttributes.Hidden) != 0,
            IsSystem = (attributes & FileAttributes.System) != 0,
            Depth = depth,
            ParentDirectory = fileInfo.DirectoryName
        };
    }
    
    /// <summary>
    /// Optimized factory method for DirectoryInfo
    /// </summary>
    public static FileEntry FromDirectoryInfo(DirectoryInfo dirInfo, int depth = 0)
    {
        var attributes = dirInfo.Attributes;
        return new FileEntry
        {
            FullPath = dirInfo.FullName,
            FileName = dirInfo.Name,
            Extension = string.Empty,
            SizeBytes = 0,
            CreatedUtc = dirInfo.CreationTimeUtc,
            LastModified = dirInfo.LastWriteTimeUtc,
            LastAccessedUtc = dirInfo.LastAccessTimeUtc,
            IsDirectory = true,
            Attributes = attributes,
            IsReadOnly = (attributes & FileAttributes.ReadOnly) != 0,
            IsHidden = (attributes & FileAttributes.Hidden) != 0,
            IsSystem = (attributes & FileAttributes.System) != 0,
            Depth = depth,
            ParentDirectory = dirInfo.Parent?.FullName
        };
    }
}
