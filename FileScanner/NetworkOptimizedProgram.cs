using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileScanner;

namespace FileScannerApp
{
    /// <summary>
    /// Network-optimized entry point that maintains constant network utilization
    /// while using BFS to prevent stack overflow on deep directory trees.
    /// </summary>
    internal static class OptimizedProgram
    {
        private enum AppState
        {
            Idle,
            Scanning,
            WritingCsv,
            Done,
            Error
        }

        private static async Task<int> Main(string[] args)
        {
            try
            {
                var (rootPath, containsFilter, extFilters, concurrency, bufferSize, memoryLimit) = ParseArguments(args);

                AppState state = AppState.Idle;
                var results = new List<FileEntry>();
                string csvPath = $"FileScanResults_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

                // Memory monitoring for large scans
                var initialMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB

                while (state != AppState.Done)
                {
                    switch (state)
                    {
                        case AppState.Idle:
                            Console.WriteLine($"Starting network-optimized BFS scan...");
                            Console.WriteLine($"  Root         : {rootPath}");
                            Console.WriteLine($"  Contains     : {containsFilter ?? "<none>"}");
                            Console.WriteLine($"  Extensions   : {(extFilters.Any() ? string.Join(", ", extFilters) : "<any>")}");
                            Console.WriteLine($"  Concurrency  : {concurrency}");
                            Console.WriteLine($"  Buffer Size  : {bufferSize:N0}");
                            Console.WriteLine($"  Memory Limit : {memoryLimit:N0} MB");
                            Console.WriteLine($"  Initial RAM  : {initialMemory:N0} MB");
                            Console.WriteLine();
                            state = AppState.Scanning;
                            break;

                        case AppState.Scanning:
                            try
                            {
                                var scanner = new NetworkOptimizedScanner(
                                    rootPath, 
                                    containsFilter, 
                                    extFilters, 
                                    concurrency, 
                                    bufferSize);

                                results = new List<FileEntry>();
                                var fileCount = 0;
                                var lastMemoryCheck = DateTime.Now;

                                await foreach (var entry in scanner.ScanAsync())
                                {
                                    results.Add(entry);
                                    fileCount++;

                                    // Memory management - check every 10,000 files
                                    if (fileCount % 10000 == 0)
                                    {
                                        var currentMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                                        
                                        if (currentMemory > memoryLimit)
                                        {
                                            Console.WriteLine($"\nMemory limit reached: {currentMemory:N0} MB > {memoryLimit:N0} MB");
                                            Console.WriteLine("Forcing garbage collection...");
                                            
                                            GC.Collect();
                                            GC.WaitForPendingFinalizers();
                                            GC.Collect();
                                            
                                            var afterGC = GC.GetTotalMemory(false) / 1024 / 1024;
                                            Console.WriteLine($"Memory after GC: {afterGC:N0} MB");
                                            
                                            if (afterGC > memoryLimit * 0.9) // Still too high
                                            {
                                                Console.WriteLine("Writing partial results to prevent memory overflow...");
                                                var partialPath = $"Partial_{csvPath}";
                                                CsvExporter.Export(results, partialPath);
                                                Console.WriteLine($"Partial results saved to {partialPath}");
                                                results.Clear();
                                            }
                                        }
                                    }
                                }

                                var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024;
                                Console.WriteLine($"\nScanning complete. Found {results.Count:N0} matching files.");
                                Console.WriteLine($"Final memory usage: {finalMemory:N0} MB (Peak estimate: {finalMemory:N0} MB)");
                                state = AppState.WritingCsv;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error during scanning: {ex.Message}");
                                state = AppState.Error;
                            }
                            break;

                        case AppState.WritingCsv:
                            try
                            {
                                Console.WriteLine("Writing results to CSV...");
                                CsvExporter.Export(results, csvPath);
                                Console.WriteLine($"Results written to {csvPath}");
                                
                                var csvSize = new FileInfo(csvPath).Length / 1024 / 1024; // MB
                                Console.WriteLine($"CSV file size: {csvSize:N1} MB");
                                
                                state = AppState.Done;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error during CSV export: {ex.Message}");
                                state = AppState.Error;
                            }
                            break;

                        case AppState.Error:
                            Console.WriteLine("Application terminated due to an error.");
                            state = AppState.Done;
                            break;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                return -1;
            }
        }

        private static (string Root, string? Contains, List<string> Extensions, int Concurrency, int BufferSize, long MemoryLimit) ParseArguments(string[] args)
        {
            string rootPath = Directory.GetCurrentDirectory();
            string? containsFilter = null;
            var extList = new List<string>();
            int concurrency = 50; // Conservative default for network
            int bufferSize = 100000; // Large buffer for network optimization
            long memoryLimit = 1024; // 1GB default memory limit

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                
                if (arg.StartsWith("--root="))
                {
                    rootPath = arg[7..];
                }
                else if (arg == "--root" && i + 1 < args.Length)
                {
                    rootPath = args[++i];
                }
                else if (arg.StartsWith("--contains="))
                {
                    containsFilter = arg[11..];
                }
                else if (arg == "--contains" && i + 1 < args.Length)
                {
                    containsFilter = args[++i];
                }
                else if (arg.StartsWith("--filter="))
                {
                    string exts = arg[9..];
                    extList.AddRange(exts.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (arg == "--filter" && i + 1 < args.Length)
                {
                    extList.AddRange(args[++i].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (arg.StartsWith("--concurrency="))
                {
                    if (int.TryParse(arg[14..], out int parsedConcurrency) && parsedConcurrency > 0)
                    {
                        concurrency = Math.Min(parsedConcurrency, 200); // Cap at 200 for network safety
                    }
                }
                else if (arg == "--concurrency" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int parsedConcurrency) && parsedConcurrency > 0)
                    {
                        concurrency = Math.Min(parsedConcurrency, 200);
                    }
                }
                else if (arg.StartsWith("--buffer="))
                {
                    if (int.TryParse(arg[9..], out int parsedBuffer) && parsedBuffer > 0)
                    {
                        bufferSize = Math.Max(10000, parsedBuffer); // Minimum 10K buffer
                    }
                }
                else if (arg == "--buffer" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int parsedBuffer) && parsedBuffer > 0)
                    {
                        bufferSize = Math.Max(10000, parsedBuffer);
                    }
                }
                else if (arg.StartsWith("--memory="))
                {
                    if (long.TryParse(arg[9..], out long parsedMemory) && parsedMemory > 0)
                    {
                        memoryLimit = Math.Max(256, parsedMemory); // Minimum 256MB
                    }
                }
                else if (arg == "--memory" && i + 1 < args.Length)
                {
                    if (long.TryParse(args[++i], out long parsedMemory) && parsedMemory > 0)
                    {
                        memoryLimit = Math.Max(256, parsedMemory);
                    }
                }
                else if (arg == "--help")
                {
                    ShowHelp();
                    Environment.Exit(0);
                }
            }

            return (rootPath, containsFilter, extList, concurrency, bufferSize, memoryLimit);
        }

        private static void ShowHelp()
        {
            Console.WriteLine("FileScanner Network-Optimized Mode");
            Console.WriteLine("Maintains constant network utilization while preventing stack overflow");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --root <path>         Root directory to scan (default: current directory)");
            Console.WriteLine("  --contains <text>     Filter files containing text in filename");
            Console.WriteLine("  --filter <exts>       File extensions to include (.txt,.log,etc)");
            Console.WriteLine("  --concurrency <num>   Network concurrency level (default: 50, max: 200)");
            Console.WriteLine("  --buffer <num>        Network buffer size (default: 100,000, min: 10,000)");
            Console.WriteLine("  --memory <mb>         Memory limit in MB (default: 1024, min: 256)");
            Console.WriteLine("  --help               Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- --root \"\\\\server\\share\" --concurrency 25 --buffer 50000");
            Console.WriteLine("  dotnet run -- --root \"C:\\Data\" --filter \".pdf,.docx\" --memory 512");
        }
    }
}