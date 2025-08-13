using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FileScanner;

/// <summary>
/// Hybrid depth-limited scanner optimized for extremely deep directory structures.
/// Uses BFS with circuit breakers to handle 300k+ files efficiently.
/// </summary>
public class NetworkOptimizedScanner
{
    private readonly string _rootPath;
    private readonly string? _nameContains;
    private readonly HashSet<string> _extensionFilter;
    private readonly int _networkConcurrency;
    private readonly int _bufferSize;
    private readonly int _maxDepth;
    private readonly int _maxQueueSize;
    
    // Network monitoring
    private long _networkRequestsPerSecond;
    private long _totalNetworkRequests;
    private long _totalFilesFound;
    private long _totalDirectoriesProcessed;
    private long _deepDirectoriesProcessed;
    private volatile bool _useSequentialMode = false;
    
    public NetworkOptimizedScanner(
        string rootPath, 
        string? nameContains = null, 
        IEnumerable<string>? extensionFilter = null,
        int networkConcurrency = 100,
        int bufferSize = 200000,
        int maxDepth = 50,
        int maxQueueSize = 10000)
    {
        _rootPath = rootPath;
        _nameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains;
        _extensionFilter = extensionFilter != null
            ? new HashSet<string>(extensionFilter.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()))
            : new HashSet<string>();
        _networkConcurrency = networkConcurrency;
        _bufferSize = bufferSize;
        _maxDepth = maxDepth;
        _maxQueueSize = maxQueueSize;
    }

    public async IAsyncEnumerable<FileEntry> ScanAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException($"Root path '{_rootPath}' does not exist.");
        }

        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine($"Network-optimized BFS scanning started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Config: Concurrency={_networkConcurrency}, Buffer={_bufferSize:N0}, MaxDepth={_maxDepth}, MaxQueue={_maxQueueSize:N0}");

        // Large network buffer for continuous streaming
        var networkBuffer = Channel.CreateBounded<FileEntry>(new BoundedChannelOptions(_bufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        // BFS directory queue with depth tracking
        var directoryQueue = new ConcurrentQueue<(string path, int depth)>();
        var discoveredDirectories = Channel.CreateUnbounded<(string path, int depth)>();
        
        // Start with root directory at depth 0
        directoryQueue.Enqueue((_rootPath, 0));
        
        var activeNetworkTasks = 0;
        var scanningComplete = false;

        // Network statistics monitoring
        var statsTask = StartNetworkMonitoring(stopwatch, cancellationToken);

        // Directory discovery task with queue size monitoring
        var discoveryTask = Task.Run(async () =>
        {
            await foreach (var newDir in discoveredDirectories.Reader.ReadAllAsync(cancellationToken))
            {
                // Circuit breaker: Monitor queue size and memory pressure
                if (directoryQueue.Count > _maxQueueSize)
                {
                    Console.WriteLine($"\nQueue size exceeded {_maxQueueSize:N0}, switching to sequential mode");
                    _useSequentialMode = true;
                    
                    // Process overflow directories sequentially to prevent memory explosion
                    await ProcessDeepDirectorySequential(newDir.path, newDir.depth, networkBuffer.Writer, cancellationToken);
                }
                else if (GC.GetTotalMemory(false) > 1_000_000_000) // 1GB memory pressure
                {
                    Console.WriteLine($"\nMemory pressure detected, switching to sequential mode");
                    _useSequentialMode = true;
                    await ProcessDeepDirectorySequential(newDir.path, newDir.depth, networkBuffer.Writer, cancellationToken);
                }
                else
                {
                    directoryQueue.Enqueue(newDir);
                }
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
                    if (directoryQueue.TryDequeue(out var directoryItem))
                    {
                        Interlocked.Increment(ref activeNetworkTasks);
                        try
                        {
                            processedDirs++;
                            await ProcessDirectoryBFS(directoryItem.path, directoryItem.depth, networkBuffer.Writer, discoveredDirectories.Writer, cancellationToken);
                            Interlocked.Increment(ref _totalDirectoriesProcessed);
                        }
                        catch (Exception ex)
                        {
                            // Log but don't stop - network resilience
                            Console.WriteLine($"\n{worker} error in {directoryItem.path}: {ex.Message}");
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
                var mode = _useSequentialMode ? "SEQUENTIAL" : "PARALLEL";
                Console.Write($"\r[{stopwatch.Elapsed:hh\\:mm\\:ss}] Files: {_totalFilesFound:N0} | Dirs: {_totalDirectoriesProcessed:N0} | Deep: {_deepDirectoriesProcessed:N0} | Mode: {mode} | Queue: {directoryQueue.Count:N0} | Active: {activeNetworkTasks}     ");
                lastProgressUpdate = now;
            }
        }

        await completionTask;
        
        stopwatch.Stop();
        Console.WriteLine($"\nBFS Network scanning completed in {stopwatch.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"Files found: {_totalFilesFound:N0} | Directories processed: {_totalDirectoriesProcessed:N0} | Deep directories: {_deepDirectoriesProcessed:N0}");
        Console.WriteLine($"Network requests: {_totalNetworkRequests:N0} | Avg req/sec: {_totalNetworkRequests / stopwatch.Elapsed.TotalSeconds:F1}");
        if (_useSequentialMode) Console.WriteLine("Sequential mode was activated for deep directory structures");
    }

    private async Task ProcessDirectoryBFS(
        string directory, 
        int depth,
        ChannelWriter<FileEntry> fileWriter,
        ChannelWriter<(string path, int depth)> directoryWriter,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalNetworkRequests);
        
        try
        {
            // Depth circuit breaker: Stop BFS if too deep
            if (depth >= _maxDepth)
            {
                await ProcessDeepDirectorySequential(directory, depth, fileWriter, cancellationToken);
                return;
            }

            // BFS: Discover subdirectories first (breadth-first expansion)
            var subdirTask = Task.Run(async () =>
            {
                try
                {
                    var subdirectories = Directory.EnumerateDirectories(directory);
                    foreach (var subdir in subdirectories)
                    {
                        // Add to QUEUE with depth tracking - this is BFS!
                        await directoryWriter.WriteAsync((subdir, depth + 1), cancellationToken);
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
            var entry = FileEntry.FromFileInfo(fileInfo);
            
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

    /// <summary>
    /// Sequential processing for extremely deep directory structures to prevent memory explosion
    /// </summary>
    private async Task ProcessDeepDirectorySequential(
        string directory,
        int depth,
        ChannelWriter<FileEntry> fileWriter,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deepDirectoriesProcessed);
        
        try
        {
            // Process files in current directory first
            try
            {
                var files = Directory.EnumerateFiles(directory);
                foreach (var file in files)
                {
                    if (MatchesFilters(file))
                    {
                        await ProcessSingleFileAsync(file, fileWriter, cancellationToken);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }

            // Process subdirectories sequentially (no queueing)
            try
            {
                var subdirectories = Directory.EnumerateDirectories(directory);
                foreach (var subdir in subdirectories)
                {
                    // Recursive call for deep processing (not BFS)
                    await ProcessDeepDirectorySequential(subdir, depth + 1, fileWriter, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }
        }
        catch (Exception)
        {
            // Continue processing other directories
        }
    }
}