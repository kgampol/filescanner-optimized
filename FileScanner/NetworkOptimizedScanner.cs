using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FileScanner;

/// <summary>
/// Network-optimized scanner with BFS traversal to prevent stack overflow.
/// Maintains constant network utilization for large server scanning.
/// </summary>
public class NetworkOptimizedScanner
{
    private readonly string _rootPath;
    private readonly string? _nameContains;
    private readonly HashSet<string> _extensionFilter;
    private readonly int _networkConcurrency;
    private readonly int _bufferSize;
    
    // Network monitoring
    private long _networkRequestsPerSecond;
    private long _totalNetworkRequests;
    private long _totalFilesFound;
    private long _totalDirectoriesProcessed;
    
    public NetworkOptimizedScanner(
        string rootPath, 
        string? nameContains = null, 
        IEnumerable<string>? extensionFilter = null,
        int networkConcurrency = 50,
        int bufferSize = 100000)
    {
        _rootPath = rootPath;
        _nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains;
        _extensionFilter = extensionFilter != null
            ? new HashSet<string>(extensionFilter.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()))
            : new HashSet<string>();
        _networkConcurrency = networkConcurrency;
        _bufferSize = bufferSize;
    }

    public async IAsyncEnumerable<FileEntry> ScanAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException($"Root path '{_rootPath}' does not exist.");
        }

        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine($"Network-optimized BFS scanning started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Config: Concurrency={_networkConcurrency}, Buffer={_bufferSize:N0}");

        // Large network buffer for continuous streaming
        var networkBuffer = Channel.CreateBounded<FileEntry>(new BoundedChannelOptions(_bufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        // BFS directory queue - NO RECURSION, breadth-first only
        var directoryQueue = new ConcurrentQueue<string>();
        var discoveredDirectories = Channel.CreateUnbounded<string>();
        
        // Start with root directory
        directoryQueue.Enqueue(_rootPath);
        
        var activeNetworkTasks = 0;
        var scanningComplete = false;

        // Network statistics monitoring
        var statsTask = StartNetworkMonitoring(stopwatch, cancellationToken);

        // Directory discovery task (BFS level management)
        var discoveryTask = Task.Run(async () =>
        {
            await foreach (var newDir in discoveredDirectories.Reader.ReadAllAsync(cancellationToken))
            {
                directoryQueue.Enqueue(newDir);
            }
        }, cancellationToken);

        // Network worker tasks - maintain constant network activity
        var networkTasks = Enumerable.Range(0, _networkConcurrency)
            .Select(workerId => Task.Run(async () =>
            {
                var worker = $"Worker-{workerId:D2}";
                var processedDirs = 0;
                
                while (!cancellationToken.IsCancellationRequested && !scanningComplete)
                {
                    if (directoryQueue.TryDequeue(out var directory))
                    {
                        Interlocked.Increment(ref activeNetworkTasks);
                        try
                        {
                            processedDirs++;
                            await ProcessDirectoryBFS(directory, networkBuffer.Writer, discoveredDirectories.Writer, cancellationToken);
                            Interlocked.Increment(ref _totalDirectoriesProcessed);
                        }
                        catch (Exception ex)
                        {
                            // Log but don't stop - network resilience
                            Console.WriteLine($"\n{worker} error in {directory}: {ex.Message}");
                        }
                        finally
                        {
                            Interlocked.Decrement(ref activeNetworkTasks);
                        }
                    }
                    else
                    {
                        // No work available - brief pause then check completion
                        await Task.Delay(50, cancellationToken);
                        
                        // BFS completion check: no active work AND no queued directories
                        if (activeNetworkTasks == 0 && directoryQueue.IsEmpty)
                        {
                            // Double-check after brief delay to ensure no race conditions
                            await Task.Delay(100, cancellationToken);
                            if (activeNetworkTasks == 0 && directoryQueue.IsEmpty)
                            {
                                scanningComplete = true;
                                break;
                            }
                        }
                    }
                }
                
                Console.WriteLine($"\n{worker} completed after processing {processedDirs} directories");
            }, cancellationToken))
            .ToArray();

        // Completion management
        var completionTask = Task.Run(async () =>
        {
            await Task.WhenAll(networkTasks);
            discoveredDirectories.Writer.Complete();
            await discoveryTask;
            networkBuffer.Writer.Complete();
            Console.WriteLine("\nAll network tasks completed, buffer sealed");
        }, cancellationToken);

        // Stream results as they arrive - consumer never blocks network
        var resultCount = 0;
        var lastProgressUpdate = DateTime.Now;
        
        await foreach (var result in networkBuffer.Reader.ReadAllAsync(cancellationToken))
        {
            resultCount++;
            Interlocked.Increment(ref _totalFilesFound);
            yield return result;
            
            // Throttled progress updates to avoid console spam
            var now = DateTime.Now;
            if ((now - lastProgressUpdate).TotalMilliseconds > 500)
            {
                var bufferUsage = GetBufferUsagePercent();
                Console.Write($"\r[{stopwatch.Elapsed:hh\\:mm\\:ss}] Files: {_totalFilesFound:N0} | Dirs: {_totalDirectoriesProcessed:N0} | Buffer: {bufferUsage:F1}% | Net Req/s: {_networkRequestsPerSecond:N0} | Active: {activeNetworkTasks}     ");
                lastProgressUpdate = now;
            }
        }

        await completionTask;
        
        stopwatch.Stop();
        Console.WriteLine($"\nBFS Network scanning completed in {stopwatch.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"Files found: {_totalFilesFound:N0} | Directories processed: {_totalDirectoriesProcessed:N0}");
        Console.WriteLine($"Network requests: {_totalNetworkRequests:N0} | Avg req/sec: {_totalNetworkRequests / stopwatch.Elapsed.TotalSeconds:F1}");
    }

    private async Task ProcessDirectoryBFS(
        string directory, 
        ChannelWriter<FileEntry> fileWriter,
        ChannelWriter<string> directoryWriter,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalNetworkRequests);
        
        try
        {
            // BFS: Discover subdirectories first (breadth-first expansion)
            var subdirTask = Task.Run(async () =>
            {
                try
                {
                    var subdirectories = Directory.EnumerateDirectories(directory);
                    foreach (var subdir in subdirectories)
                    {
                        // Add to QUEUE (not recursive call) - this is BFS!
                        await directoryWriter.WriteAsync(subdir, cancellationToken);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (PathTooLongException) { }
                catch (IOException) { }
            }, cancellationToken);

            // Process files in current directory
            var fileTask = Task.Run(async () =>
            {
                try
                {
                    var files = Directory.EnumerateFiles(directory);
                    var fileTasks = new List<Task>();
                    
                    foreach (var file in files)
                    {
                        if (MatchesFilters(file))
                        {
                            fileTasks.Add(ProcessSingleFileAsync(file, fileWriter, cancellationToken));
                            
                            // Batch processing to prevent task explosion
                            if (fileTasks.Count >= 25)
                            {
                                await Task.WhenAll(fileTasks);
                                fileTasks.Clear();
                            }
                        }
                    }
                    
                    if (fileTasks.Count > 0)
                    {
                        await Task.WhenAll(fileTasks);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (PathTooLongException) { }
                catch (IOException) { }
            }, cancellationToken);

            // Wait for both directory discovery and file processing
            await Task.WhenAll(subdirTask, fileTask);
        }
        catch (Exception)
        {
            // Directory-level errors don't stop the BFS traversal
        }
    }

    private async Task ProcessSingleFileAsync(string filePath, ChannelWriter<FileEntry> writer, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var entry = new FileEntry(fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc);
            
            await writer.WriteAsync(entry, cancellationToken);
        }
        catch (Exception) 
        { 
            // Skip individual file errors - maintain network flow
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

    private double GetBufferUsagePercent()
    {
        try
        {
            // Approximate buffer usage for monitoring
            return (_totalFilesFound % _bufferSize) * 100.0 / _bufferSize;
        }
        catch
        {
            return 0.0;
        }
    }

    private Task StartNetworkMonitoring(Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            var lastRequests = 0L;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
                
                var currentRequests = _totalNetworkRequests;
                _networkRequestsPerSecond = currentRequests - lastRequests;
                lastRequests = currentRequests;
            }
        }, cancellationToken);
    }
}