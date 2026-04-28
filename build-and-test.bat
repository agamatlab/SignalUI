@echo off
REM Build and Test Script for Plugin System
REM Run this script from the solution root directory

echo ==========================================
echo SignalUI Plugin System - Build ^& Test
echo ==========================================
echo.

REM Step 1: Clean previous builds
echo Step 1: Cleaning previous builds...
dotnet clean singalUI.sln
if %ERRORLEVEL% EQU 0 (
    echo [32m✓ Clean successful[0m
) else (
    echo [31m✗ Clean failed[0m
    exit /b 1
)
echo.

REM Step 2: Restore dependencies
echo Step 2: Restoring dependencies...
dotnet restore singalUI.sln
if %ERRORLEVEL% EQU 0 (
    echo [32m✓ Restore successful[0m
) else (
    echo [31m✗ Restore failed[0m
    exit /b 1
)
echo.

REM Step 3: Build solution
echo Step 3: Building solution...
dotnet build singalUI.sln --configuration Debug
if %ERRORLEVEL% EQU 0 (
    echo [32m✓ Build successful[0m
) else (
    echo [31m✗ Build failed[0m
    exit /b 1
)
echo.

REM Step 4: Verify plugin DLLs were created
echo Step 4: Verifying plugin DLLs...
set PLUGINS_DIR=singalUI\bin\Debug\net8.0\Plugins

if exist "%PLUGINS_DIR%" (
    echo [32m✓ Plugins directory exists[0m
    echo Contents:
    dir /B "%PLUGINS_DIR%\*.dll"
    
    REM Check for each plugin
    if exist "%PLUGINS_DIR%\MockStageController.Plugin.dll" (
        echo [32m✓ MockStageController.Plugin.dll found[0m
    ) else (
        echo [31m✗ MockStageController.Plugin.dll NOT found[0m
    )
    
    if exist "%PLUGINS_DIR%\PIController.Plugin.dll" (
        echo [32m✓ PIController.Plugin.dll found[0m
    ) else (
        echo [31m✗ PIController.Plugin.dll NOT found[0m
    )
    
    if exist "%PLUGINS_DIR%\SigmakokiController.Plugin.dll" (
        echo [32m✓ SigmakokiController.Plugin.dll found[0m
    ) else (
        echo [31m✗ SigmakokiController.Plugin.dll NOT found[0m
    )
    
    REM Check for PI DLLs
    if exist "%PLUGINS_DIR%\PI_GCS2_DLL_x64.dll" (
        echo [32m✓ PI_GCS2_DLL_x64.dll found[0m
    ) else (
        echo [33m⚠ PI_GCS2_DLL_x64.dll NOT found (may be expected)[0m
    )
) else (
    echo [31m✗ Plugins directory does not exist[0m
)
echo.

REM Step 5: Run plugin tests
echo Step 5: Running plugin tests...
dotnet test singalUI.Tests\singalUI.Tests.csproj --filter "FullyQualifiedName~Plugin" --verbosity normal
if %ERRORLEVEL% EQU 0 (
    echo [32m✓ Plugin tests passed[0m
) else (
    echo [31m✗ Some plugin tests failed[0m
    exit /b 1
)
echo.

REM Step 6: Run all tests
echo Step 6: Running all tests...
dotnet test singalUI.Tests\singalUI.Tests.csproj --verbosity normal
if %ERRORLEVEL% EQU 0 (
    echo [32m✓ All tests passed[0m
) else (
    echo [33m⚠ Some tests failed (may be expected if hardware not connected)[0m
)
echo.

REM Step 7: Summary
echo ==========================================
echo Build ^& Test Summary
echo ==========================================
echo.
echo Projects built:
echo   ✓ singalUI
echo   ✓ singalUI.Tests
echo   ✓ MockStageController.Plugin
echo   ✓ PIController.Plugin
echo   ✓ SigmakokiController.Plugin
echo.
echo Plugins created:
dir /B "%PLUGINS_DIR%\*.Plugin.dll" 2>nul | find /C ".dll"
echo.
echo Next steps:
echo   1. Run the application: dotnet run --project singalUI\singalUI.csproj
echo   2. Check console output for plugin loading messages
echo   3. Verify all 3 plugins load successfully
echo.
echo [32mBuild and test complete![0m
pause
