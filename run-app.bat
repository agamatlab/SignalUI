@echo off
REM Quick Run Script - Runs the application and shows plugin loading
echo ==========================================
echo Running SignalUI with Plugin System
echo ==========================================
echo.

REM Check if already built
if not exist "singalUI\bin\Debug\net8.0\singalUI.dll" (
    echo Application not built yet. Building...
    dotnet build singalUI.sln --configuration Debug
    if %ERRORLEVEL% NEQ 0 (
        echo [31mBuild failed![0m
        pause
        exit /b 1
    )
    echo.
)

REM Show plugins directory
echo Checking Plugins directory...
if exist "singalUI\bin\Debug\net8.0\Plugins" (
    echo [32mPlugins directory found[0m
    echo.
    echo Plugin DLLs:
    dir /B "singalUI\bin\Debug\net8.0\Plugins\*.Plugin.dll" 2>nul
    echo.
) else (
    echo [33mWarning: Plugins directory not found[0m
    echo.
)

echo ==========================================
echo Starting Application...
echo Watch for plugin loading messages below:
echo ==========================================
echo.

REM Run the application
dotnet run --project singalUI\singalUI.csproj

echo.
echo ==========================================
echo Application closed
echo ==========================================
pause
