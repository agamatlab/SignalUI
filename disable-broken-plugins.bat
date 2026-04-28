@echo off
echo ==========================================
echo Quick Fix - Disable Problematic Plugins
echo ==========================================
echo.

echo This will temporarily disable PI and Sigmakoki plugins
echo so you can test the plugin system with just the Mock plugin.
echo.
echo The Mock plugin works without any hardware and demonstrates
echo the plugin system functionality.
echo.
pause

REM Find the Plugins directory
for /d %%d in (singalUI\bin\Debug\net8.0*) do (
    if exist "%%d\Plugins" (
        echo Found Plugins directory: %%d\Plugins
        
        REM Rename problematic plugins to .disabled
        if exist "%%d\Plugins\PIController.Plugin.dll" (
            echo Disabling PIController.Plugin.dll
            ren "%%d\Plugins\PIController.Plugin.dll" "PIController.Plugin.dll.disabled"
        )
        
        if exist "%%d\Plugins\SigmakokiController.Plugin.dll" (
            echo Disabling SigmakokiController.Plugin.dll
            ren "%%d\Plugins\SigmakokiController.Plugin.dll" "SigmakokiController.Plugin.dll.disabled"
        )
        
        echo.
        echo Remaining plugins:
        dir /B "%%d\Plugins\*.Plugin.dll"
    )
)

echo.
echo ==========================================
echo Now run the application:
echo ==========================================
echo.
echo dotnet run --project singalUI\singalUI.csproj --framework net8.0
echo.
echo You should see:
echo [PluginLoader] Found 1 plugin DLL(s)
echo [PluginLoader] ✓ Loaded plugin: Mock Stage Controller (Test) v1.0.0
echo.
pause
