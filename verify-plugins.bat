@echo off
REM Verification Script - Check if plugin system is ready
echo ==========================================
echo Plugin System Verification
echo ==========================================
echo.

set PASS=0
set FAIL=0

REM Check 1: Main application DLL
echo [1/7] Checking main application...
if exist "singalUI\bin\Debug\net8.0\singalUI.dll" (
    echo [32m  ✓ singalUI.dll found[0m
    set /a PASS+=1
) else (
    echo [31m  ✗ singalUI.dll NOT found - Need to build[0m
    set /a FAIL+=1
)

REM Check 2: Plugins directory
echo [2/7] Checking Plugins directory...
if exist "singalUI\bin\Debug\net8.0\Plugins" (
    echo [32m  ✓ Plugins directory exists[0m
    set /a PASS+=1
) else (
    echo [31m  ✗ Plugins directory NOT found[0m
    set /a FAIL+=1
)

REM Check 3: MockStageController plugin
echo [3/7] Checking MockStageController.Plugin...
if exist "singalUI\bin\Debug\net8.0\Plugins\MockStageController.Plugin.dll" (
    echo [32m  ✓ MockStageController.Plugin.dll found[0m
    set /a PASS+=1
) else (
    echo [31m  ✗ MockStageController.Plugin.dll NOT found[0m
    set /a FAIL+=1
)

REM Check 4: PIController plugin
echo [4/7] Checking PIController.Plugin...
if exist "singalUI\bin\Debug\net8.0\Plugins\PIController.Plugin.dll" (
    echo [32m  ✓ PIController.Plugin.dll found[0m
    set /a PASS+=1
) else (
    echo [31m  ✗ PIController.Plugin.dll NOT found[0m
    set /a FAIL+=1
)

REM Check 5: SigmakokiController plugin
echo [5/7] Checking SigmakokiController.Plugin...
if exist "singalUI\bin\Debug\net8.0\Plugins\SigmakokiController.Plugin.dll" (
    echo [32m  ✓ SigmakokiController.Plugin.dll found[0m
    set /a PASS+=1
) else (
    echo [31m  ✗ SigmakokiController.Plugin.dll NOT found[0m
    set /a FAIL+=1
)

REM Check 6: Test project
echo [6/7] Checking test project...
if exist "singalUI.Tests\bin\Debug\net8.0\singalUI.Tests.dll" (
    echo [32m  ✓ Test project built[0m
    set /a PASS+=1
) else (
    echo [33m  ⚠ Test project not built (optional)[0m
)

REM Check 7: Count plugin DLLs
echo [7/7] Counting plugin DLLs...
set COUNT=0
for %%f in (singalUI\bin\Debug\net8.0\Plugins\*.Plugin.dll) do set /a COUNT+=1
if %COUNT% GEQ 3 (
    echo [32m  ✓ Found %COUNT% plugin DLL(s)[0m
    set /a PASS+=1
) else (
    echo [31m  ✗ Found only %COUNT% plugin DLL(s), expected 3[0m
    set /a FAIL+=1
)

echo.
echo ==========================================
echo Verification Results
echo ==========================================
echo Passed: %PASS%
echo Failed: %FAIL%
echo.

if %FAIL% GTR 0 (
    echo [31mSome checks failed. Run: build-and-test.bat[0m
    echo.
    echo Quick fix:
    echo   dotnet build singalUI.sln
    echo.
) else (
    echo [32mAll checks passed! Ready to run.[0m
    echo.
    echo Next steps:
    echo   1. Run application: run-app.bat
    echo   2. Run tests: dotnet test singalUI.Tests\singalUI.Tests.csproj
    echo.
)

pause
