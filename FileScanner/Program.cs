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
        private enum AppState//enum is a keyword that defines a set of named constants
        {
            Idle,
            Scanning,
            WritingCsv,
            Done,
            Error
        }

        private static int Main(string[] args)//Main is a method that is the entry point of the application
        {
            try
            {
                // Parse command-line arguments
                var (rootPath, containsFilter, extFilters) = ParseArguments(args);//ParseArguments is a method that parses the command-line arguments

                AppState state = AppState.Idle;//AppState is a enum that represents the state of the application
                List<FileEntry> results = new();//List is a collection that stores a list of objects
                string csvPath = $"FileScanResults_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";//DateTime.UtcNow is a property that gets the current date and time in UTC

                while (state != AppState.Done)
                {
                    switch (state)//switch is a keyword that is used to switch on a value
                    {
                        case AppState.Idle:
                            Console.WriteLine($"Starting scan...\n  Root      : {rootPath}\n  Contains  : {containsFilter ?? "<none>"}\n  Extensions: {(extFilters.Any() ? string.Join(", ", extFilters) : "<any>")}\n");
                            state = AppState.Scanning;
                            break;

                        case AppState.Scanning:
                            try
                            {
                                var scanner = new FileScannerTool(rootPath, containsFilter, extFilters);//FileScannerTool is a class that scans the directory and returns a list of files
                                results = scanner.Scan().ToList();//Scan is a method that scans the directory and returns a list of files
                                Console.WriteLine($"Scanning complete. Found {results.Count} matching files.");//WriteLine is a method that writes a line to the console
                                state = AppState.WritingCsv;
                            }
                            catch (Exception ex)//Exception is a class that represents errors that occur during application execution
                            {
                                Console.WriteLine($"Error during scanning: {ex.Message}");//WriteLine is a method that writes a line to the console
                                state = AppState.Error;
                            }
                            break;

                        case AppState.WritingCsv:
                            try
                            {
                                FileScanner.CsvExporter.Export(results, csvPath);//CsvExporter is a class that exports the results to a CSV file
                                Console.WriteLine($"Results written to {csvPath}");//WriteLine is a method that writes a line to the console
                                state = AppState.Done;
                            }
                            catch (Exception ex)//Exception is a class that represents errors that occur during application execution
                            {
                                Console.WriteLine($"Error during CSV export: {ex.Message}");//WriteLine is a method that writes a line to the console
                                state = AppState.Error;
                            }
                            break;

                        case AppState.Error:
                            Console.WriteLine("Application terminated due to an error.");//WriteLine is a method that writes a line to the console
                            state = AppState.Done;
                            break;
                    }
                }

                return 0;
            }
            catch (Exception ex)//Exception is a class that represents errors that occur during application execution
            {
                Console.WriteLine($"Fatal error: {ex.Message}");//WriteLine is a method that writes a line to the console
                return -1;
            }
        }

        private static (string Root, string? Contains, List<string> Extensions) ParseArguments(string[] args)//ParseArguments is a method that parses the command-line arguments
        {
            string rootPath = Directory.GetCurrentDirectory();//Directory.GetCurrentDirectory is a method that gets the current directory
            string? containsFilter = null;//? is a nullable type
            var extList = new List<string>();//List is a collection that stores a list of objects

            for (int i = 0; i < args.Length; i++)//for is a loop that iterates over a collection
            {
                string arg = args[i];//arg is a variable that stores the argument
                if (arg.StartsWith("--root="))//StartsWith is a method that checks if a string starts with a specified substring
                {
                    rootPath = arg[7..];//7.. is a range operator that gets the substring from the 7th character to the end of the string
                }
                else if (arg == "--root" && i + 1 < args.Length)//i + 1 < args.Length is a condition that checks if the index is less than the length of the array
                {
                    rootPath = args[++i];//++i is a pre-increment operator that increments the index and then returns the value
                }
                else if (arg.StartsWith("--contains="))//StartsWith is a method that checks if a string starts with a specified substring
                {
                    containsFilter = arg[11..];//11.. is a range operator that gets the substring from the 11th character to the end of the string
                }
                else if (arg == "--contains" && i + 1 < args.Length)//i + 1 < args.Length is a condition that checks if the index is less than the length of the array
                {
                    containsFilter = args[++i];//++i is a pre-increment operator that increments the index and then returns the value
                }
                else if (arg.StartsWith("--filter="))//StartsWith is a method that checks if a string starts with a specified substring
                {
                    string exts = arg[9..];//9.. is a range operator that gets the substring from the 9th character to the end of the string
                    extList.AddRange(exts.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));//Split is a method that splits a string into a substrings
                }
                else if (arg == "--filter" && i + 1 < args.Length)//i + 1 < args.Length is a condition that checks if the index is less than the length of the array
                {
                    extList.AddRange(args[++i].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));//Split is a method that splits a string into a substrings
                }
            }

            return (rootPath, containsFilter, extList);//return is a keyword that returns a value from a method
        }
    }
}
