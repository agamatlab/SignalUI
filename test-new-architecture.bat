@echo off
echo ==========================================
echo Testing New StageControllers Architecture
echo ==========================================
echo.

echo Step 1: Rebuilding solution...
dotnet build singalUI.sln --configuration Debug

if %ERRORLEVEL% NEQ 0 (
    echo [31mBuild failed![0m
    pause
    exit /b 1
)

echo.
echo [32mBuild succeeded![0m
echo.

echo Step 2: Checking StageControllers directory...
echo.

REM Find the actual output directory
for /d %%d in (singalUI\bin\Debug\net8.0*) do (
    if exist "%%d\StageControllers" (
        echo [32mFound StageControllers directory: %%d\StageControllers[0m
        echo.
        echo Subdirectories:
        dir /B /AD "%%d\StageControllers" 2>nul
        echo.
        echo Controller DLLs:
        dir /B /S "%%d\StageControllers\*.dll" 2>nul
        echo.
    ) else (
        echo [33mStageControllers directory not found yet (will be created on first run)[0m
    )
)

echo.
echo Step 3: Running application...
echo.
echo Watch for this message:
echo   [PluginLoader] Loading stage controllers from: .../StageControllers
echo   [PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
echo.
pause

echo Starting application...
echo.
dotnet run --project singalUI\singalUI.csproj --framework net8.0

echo.
echo ==========================================
echo Application closed
echo ==========================================
pause
