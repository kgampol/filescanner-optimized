# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common Commands

### Build and Run
```bash
# Build the project
dotnet build

# Run with arguments (replace -- with actual arguments)
dotnet run -- --root "C:\SomeDirectory" --filter ".txt,.log" --contains "error"

# Build and run optimized version
.\FileScanner\run-optimized.bat

# Build release version
dotnet build -c Release

# Publish self-contained executable
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

### Project Structure Navigation
```bash
# Navigate to main project
cd FileScanner

# Check project structure
ls FileScanner/
```

## Architecture Overview

### Core Components
- **Program.cs**: Entry point with state machine (`Idle → Scanning → WritingCsv → Done/Error`) and CLI argument parsing
- **FileScannerTool.cs**: Async parallel directory scanner using channels and semaphores for concurrent processing
- **FileEntry.cs**: Immutable record representing file metadata (`FullPath`, `SizeBytes`, `LastModified`)
- **CsvExporter.cs**: Utility for exporting results to CSV with proper escaping
- **StreamingCsvExporter.cs**: Streaming version for memory-optimized processing

### Key Features
- **Async Streaming**: Uses `IAsyncEnumerable<FileEntry>` for memory-efficient processing
- **Parallel Processing**: Configurable concurrency with semaphore throttling (default: 50 concurrent tasks)
- **BFS Traversal**: Breadth-first search prevents stack overflow on deep directory trees
- **Error Resilience**: Gracefully handles access denied, path too long, and I/O exceptions
- **Real-time Progress**: Shows scanning progress with directory count and active tasks

### Performance Optimizations
The project includes several optimization variants:
- **Memory Management**: Bounded channels prevent memory explosion during large scans
- **Concurrent Directory Processing**: Multiple workers process directories in parallel
- **File Processing Batching**: Limits concurrent file operations per directory (max 10)
- **Progress Throttling**: Updates display every 100ms to avoid console spam

## CLI Arguments
- `--root <path>`: Root directory to scan (defaults to current directory)
- `--contains <substring>`: Filter files by filename substring (case-insensitive)
- `--filter <extensions>`: Comma/semicolon/space-separated list of extensions (`.txt,.log` or `txt log`)
- `--concurrency <number>`: Set parallel task limit (default: 50)

## Important Implementation Notes

### Entry Point Configuration
The project uses `OptimizedProgram` as the startup object (configured in FileScanner.csproj:9), not the standard `Program` class.

### State Machine Pattern
The application follows a strict state machine:
1. `Idle`: Parse arguments and prepare
2. `Scanning`: Recursively scan directories with parallel processing
3. `WritingCsv`: Export results to timestamped CSV file
4. `Done/Error`: Cleanup and exit

### Memory Safety
- Uses bounded channels (1000 directory capacity) to prevent memory issues
- Streaming processing avoids loading all results into memory at once
- Semaphore controls concurrent operations to prevent resource exhaustion

### File Filtering Logic
- Extension matching is case-insensitive with automatic dot prefixing
- Contains filter applies to filename only (not full path)
- Both filters are optional and can be combined

## Development Tips

### Adding New Features
- Extend the `FileEntry` record for additional file metadata
- Modify `FileScannerTool.MatchesFilters()` for new filtering logic
- Use the existing channel-based architecture for new async operations

### Performance Tuning
- Adjust `_concurrency` parameter for different environments
- Modify channel bounds based on available memory
- Consider network vs. local storage when setting concurrency levels

### Error Handling
- Directory-level exceptions are caught and skipped (access denied, path too long)
- File-level exceptions are handled gracefully without stopping the scan
- Use structured error handling when adding new I/O operations