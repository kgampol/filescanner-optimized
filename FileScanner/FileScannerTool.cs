using System.Collections.Concurrent;

namespace FileScanner;

/// <summary>
/// Provides functionality to recursively scan directories and return matching files.
/// </summary>
public class FileScannerTool
{
    private readonly string _rootPath;
    private readonly string? _nameContains;
    private readonly HashSet<string> _extensionFilter; // stored in lowercase with leading dot

    public FileScannerTool(string rootPath, string? nameContains = null, IEnumerable<string>? extensionFilter = null)
    {
        _rootPath = rootPath;
        _nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains;
        _extensionFilter = extensionFilter != null
            ? new HashSet<string>(extensionFilter.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()))
            : new HashSet<string>();
    }

    public IEnumerable<FileEntry> Scan()
    {
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException($"Root path '{_rootPath}' does not exist.");
        }

        // Use a queue (BFS) to avoid stack overflow for deep trees.
        var dirs = new Queue<string>();
        dirs.Enqueue(_rootPath);

        while (dirs.Count > 0)
        {
            string currentDir = dirs.Dequeue();
            IEnumerable<string> subDirs = Enumerable.Empty<string>();
            IEnumerable<string> files = Enumerable.Empty<string>();
            try
            {
                subDirs = Directory.EnumerateDirectories(currentDir);
            }
            catch (UnauthorizedAccessException) { /* skip */ }
            catch (PathTooLongException) { /* skip */ }
            catch (IOException) { /* skip */ }

            foreach (var d in subDirs)
            {
                dirs.Enqueue(d);
            }

            try
            {
                files = Directory.EnumerateFiles(currentDir);
            }
            catch (UnauthorizedAccessException) { /* skip */ }
            catch (PathTooLongException) { /* skip */ }
            catch (IOException) { /* skip */ }

            foreach (var file in files)
            {
                if (MatchesFilters(file))
                {
                    FileInfo fi;
                    try
                    {
                        fi = new FileInfo(file);
                    }
                    catch (Exception)
                    {
                        continue; // skip inaccessible files
                    }

                    yield return new FileEntry(fi.FullName, fi.Length, fi.LastWriteTimeUtc);
                }
            }
        }
    }

    private bool MatchesFilters(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        if (_nameContains != null && !fileName.Contains(_nameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_extensionFilter.Count > 0)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return _extensionFilter.Contains(ext);
        }

        return true;
    }
}
