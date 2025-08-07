@echo off
echo Building Optimized FileScanner...
dotnet build -c Release

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Running Optimized FileScanner...
echo Use --help for all options
echo.

dotnet run --project . -c Release -- %*

pause