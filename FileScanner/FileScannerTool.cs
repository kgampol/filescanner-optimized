using System.Collections.Concurrent;
using System.Diagnostics;

namespace FileScanner;

/// <summary>
/// Provides functionality to recursively scan directories and return matching files.
/// </summary>
public class FileScannerTool
{
    private readonly string _rootPath;//readonly is a modifier that makes the field read-only
    private readonly string? _nameContains;//? is a nullable type   
    private readonly HashSet<string> _extensionFilter; // stored in lowercase with leading dot

    public FileScannerTool(string rootPath, string? nameContains = null, IEnumerable<string>? extensionFilter = null)//constructor
    {
        _rootPath = rootPath;//_ is a field
        _nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains;//string.IsNullOrWhiteSpace is a method that checks if a string is null or whitespace
        _extensionFilter = extensionFilter != null
            ? new HashSet<string>(extensionFilter.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()))//Select is a method that projects each element of a sequence into a new form
            : new HashSet<string>();//HashSet is a collection that stores unique values
    }

    public IEnumerable<FileEntry> Scan()//Scan is a method that scans the directory and returns a list of files
    {
        if (!Directory.Exists(_rootPath))//Directory.Exists is a method that checks if a directory exists
        {
            throw new DirectoryNotFoundException($"Root path '{_rootPath}' does not exist.");//DirectoryNotFoundException is a exception that is thrown when a directory is not found
        }

        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine($"Scanning started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        
        // Use a queue (BFS) to avoid stack overflow for deep trees.
        var dirs = new Queue<string>();//Queue is a collection that stores a first-in, first-out (FIFO) collection of objects
        dirs.Enqueue(_rootPath);//Enqueue is a method that adds an object to the end of the Queue

        while (dirs.Count > 0)//while is a loop that runs while a condition is true
        {
            string currentDir = dirs.Dequeue();//Dequeue is a method that removes and returns the object at the beginning of the Queue
            
            // Display current folder and elapsed time
            Console.Write($"\r[{stopwatch.Elapsed:hh\\:mm\\:ss}] Processing: {currentDir}".PadRight(Console.WindowWidth - 1));
            Console.Out.Flush();
            IEnumerable<string> subDirs = Enumerable.Empty<string>();//Enumerable.Empty is a method that returns an empty sequence
            IEnumerable<string> files = Enumerable.Empty<string>();//Enumerable.Empty is a method that returns an empty sequence
            try
            {
                subDirs = Directory.EnumerateDirectories(currentDir);//EnumerateDirectories is a method that enumerates the directories in a specified path and returns an enumerable collection of strings
            }
            catch (UnauthorizedAccessException) { /* skip */ }//UnauthorizedAccessException is a exception that is thrown when a user does not have permission to access a resource
            catch (PathTooLongException) { /* skip */ }//PathTooLongException is a exception that is thrown when a path is too long
            catch (IOException) { /* skip */ }//IOException is a exception that is thrown when an I/O error occurs

            foreach (var d in subDirs)//foreach is a loop that iterates over a collection
            {
                dirs.Enqueue(d);//Enqueue is a method that adds an object to the end of the Queue
            }

            try
            {
                files = Directory.EnumerateFiles(currentDir);//EnumerateFiles is a method that enumerates the files in a specified path and returns an enumerable collection of strings
            }
            catch (UnauthorizedAccessException) { /* skip */ }//UnauthorizedAccessException is a exception that is thrown when a user does not have permission to access a resource
            catch (PathTooLongException) { /* skip */ }//PathTooLongException is a exception that is thrown when a path is too long
            catch (IOException) { /* skip */ }//IOException is a exception that is thrown when an I/O error occurs

            foreach (var file in files)//foreach is a loop that iterates over a collection
            {
                if (MatchesFilters(file))//MatchesFilters is a method that checks if a file matches the filters
                {
                    FileInfo fi;//FileInfo is a class that provides properties and instance methods for the creation, copying, deletion, moving, and opening of files
                    try
                    {
                        fi = new FileInfo(file);//FileInfo is a class that provides properties and instance methods for the creation, copying, deletion, moving, and opening of files
                    }
                    catch (Exception)//Exception is a class that represents errors that occur during application execution
                    {
                        continue; // skip inaccessible files
                    }

                    yield return new FileEntry(fi.FullName, fi.Length, fi.LastWriteTimeUtc);//FileEntry is a class that represents a file on disk
                }
            }
        }
        
        stopwatch.Stop();
        Console.WriteLine($"\nScanning completed in {stopwatch.Elapsed:hh\\:mm\\:ss}");
    }

    private bool MatchesFilters(string filePath)//MatchesFilters is a method that checks if a file matches the filters
    {
        string fileName = Path.GetFileName(filePath);//Path.GetFileName is a method that returns the file name and extension of the specified path string

        if (_nameContains != null && !fileName.Contains(_nameContains, StringComparison.OrdinalIgnoreCase))//StringComparison.OrdinalIgnoreCase is a method that compares strings ignoring case
        {
            return false;//return is a keyword that returns a value from a method
        }

        if (_extensionFilter.Count > 0)//Count is a property that gets the number of elements in the collection
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();//Path.GetExtension is a method that returns the extension of the specified path string
            return _extensionFilter.Contains(ext);//Contains is a method that checks if a collection contains a specific value
        }

        return true;//return is a keyword that returns a value from a method
    }
}
