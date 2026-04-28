@echo off
echo ==========================================
echo Building Plugin System
echo ==========================================
echo.

echo Step 1: Building solution...
dotnet build singalUI.sln --configuration Debug

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed!
    pause
    exit /b 1
)

echo.
echo ==========================================
echo Build Complete!
echo ==========================================
echo.

echo Checking for plugin DLLs...
echo.

REM Find the actual output directory
for /d %%d in (singalUI\bin\Debug\*) do (
    if exist "%%d\Plugins" (
        echo Found Plugins directory: %%d\Plugins
        echo.
        echo Plugin DLLs:
        dir /B "%%d\Plugins\*.Plugin.dll" 2>nul
        echo.
        set PLUGINS_DIR=%%d\Plugins
    )
)

if not defined PLUGINS_DIR (
    echo [WARNING] Could not find Plugins directory
    echo Checking common locations...
    if exist "singalUI\bin\Debug\net8.0\Plugins" (
        echo Found: singalUI\bin\Debug\net8.0\Plugins
        dir /B "singalUI\bin\Debug\net8.0\Plugins\*.Plugin.dll"
    )
    if exist "singalUI\bin\Debug\net8.0-windows10.0.19041.0\Plugins" (
        echo Found: singalUI\bin\Debug\net8.0-windows10.0.19041.0\Plugins
        dir /B "singalUI\bin\Debug\net8.0-windows10.0.19041.0\Plugins\*.Plugin.dll"
    )
)

echo.
echo ==========================================
echo Next Steps:
echo ==========================================
echo.
echo 1. Run the application:
echo    dotnet run --project singalUI\singalUI.csproj
echo.
echo 2. Run tests:
echo    dotnet test singalUI.Tests\singalUI.Tests.csproj --filter "Plugin"
echo.
pause
