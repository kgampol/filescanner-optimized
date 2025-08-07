@echo off
echo ========================================
echo Testing Network-Optimized FileScanner
echo ========================================
echo.
echo This test will validate:
echo 1. BFS traversal (no stack overflow)
echo 2. Constant network utilization
echo 3. Memory management
echo.

echo Building optimized version...
cd FileScanner
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Starting network monitoring test...
echo Watch Task Manager - Network tab during this test
echo Your ethernet should maintain steady activity (not bursty)
echo.
pause

echo.
echo Test 1: Small directory (should show constant activity)
echo =====================================================
dotnet run -c Release -- --root "." --concurrency 10 --buffer 5000 --memory 256
echo.

echo Test 2: Larger concurrency (should max out network)
echo ==================================================
echo Note: Watch network utilization in Task Manager
dotnet run -c Release -- --root "C:\Windows\System32" --concurrency 50 --buffer 20000 --memory 512
echo.

echo Test 3: Low memory limit (should trigger memory management)
echo ========================================================
dotnet run -c Release -- --root "C:\Program Files" --concurrency 25 --buffer 50000 --memory 128
echo.

echo ========================================
echo Network optimization tests complete!
echo ========================================
echo.
echo Expected behavior:
echo - Network activity should be CONSTANT (not bursty)
echo - Memory usage should stay within limits
echo - No stack overflow errors on deep directories
echo - Progress should show steady req/sec rates
echo.
pause