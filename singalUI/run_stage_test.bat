@echo off
echo ========================================
echo PI Stage Connection Test Runner
echo ========================================
echo.

cd /d "%~dp0"

echo Checking for PI DLLs...
if exist "PI_GCS2_DLL_x64.dll" (
    echo [OK] PI_GCS2_DLL_x64.dll found
) else (
    echo [ERROR] PI_GCS2_DLL_x64.dll NOT FOUND!
    echo Please ensure PI_GCS2_DLL_x64.dll is in this folder.
    pause
    exit /b 1
)

echo.
echo Compiling test program...
csc /target:exe /out:StageConnectionTest.exe /reference:PI_GCS2_DLL_x64.dll StageConnectionTest.cs 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [INFO] CSC not available, trying with dotnet...

    REM Try to run as a console app by modifying the project temporarily
    echo Running via dotnet with test flag...
    dotnet run -- --test-stage
) else (
    echo [OK] Compilation successful
    echo.
    echo Running test...
    echo ========================================
    StageConnectionTest.exe
)

echo.
echo ========================================
echo Test complete. Check output above for results.
echo ========================================
pause
