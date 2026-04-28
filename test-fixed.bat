@echo off
echo ==========================================
echo FIXED - Testing Plugin System
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

echo Step 2: Running application...
echo.
echo Watch for this message:
echo   [PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
echo.
echo If you see it, the plugin system is working!
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
