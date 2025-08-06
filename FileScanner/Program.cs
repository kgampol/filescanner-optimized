using System;
using System.Collections.Generic;
using System.Linq;
using FileScanner;

namespace FileScannerApp
{
    /// <summary>
    /// Application entry point for FileScanner.
    /// </summary>
    internal static class Program
    {
        private enum AppState
        {
            Idle,
            Scanning,
            WritingCsv,
            Done,
            Error
        }

        private static int Main(string[] args)
        {
            try
            {
                // Parse command-line arguments
                var (rootPath, containsFilter, extFilters) = ParseArguments(args);

                AppState state = AppState.Idle;
                List<FileEntry> results = new();
                string csvPath = $"FileScanResults_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

                while (state != AppState.Done)
                {
                    switch (state)
                    {
                        case AppState.Idle:
                            Console.WriteLine($"Starting scan...\n  Root      : {rootPath}\n  Contains  : {containsFilter ?? "<none>"}\n  Extensions: {(extFilters.Any() ? string.Join(", ", extFilters) : "<any>")}\n");
                            state = AppState.Scanning;
                            break;

                        case AppState.Scanning:
                            try
                            {
                                var scanner = new FileScannerTool(rootPath, containsFilter, extFilters);
                                results = scanner.Scan().ToList();
                                Console.WriteLine($"Scanning complete. Found {results.Count} matching files.");
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
                                FileScanner.CsvExporter.Export(results, csvPath);
                                Console.WriteLine($"Results written to {csvPath}");
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

        private static (string Root, string? Contains, List<string> Extensions) ParseArguments(string[] args)
        {
            string rootPath = Directory.GetCurrentDirectory();
            string? containsFilter = null;
            var extList = new List<string>();

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
            }

            return (rootPath, containsFilter, extList);
        }
    }
}
