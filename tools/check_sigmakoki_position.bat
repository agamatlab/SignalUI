@echo off
echo === Sigmakoki Position Checker ===
echo.
echo Checking position from log file...
echo.

set LOGFILE=%TEMP%\singalui_sigmakoki.log

if not exist "%LOGFILE%" (
    echo ERROR: Log file not found at %LOGFILE%
    echo Make sure the application is running and connected to Sigmakoki.
    pause
    exit /b
)

echo Latest position queries:
echo.
findstr /C:"CMD: Q:" "%LOGFILE%" | findstr /C:"RESP:" | more +0

echo.
echo Latest move commands:
echo.
findstr /C:"CMD: M:" "%LOGFILE%" | findstr /C:"RESP:" | more +0

echo.
echo Motor enable status:
echo.
findstr /C:"CMD: R:" "%LOGFILE%" | findstr /C:"RESP:" | more +0

echo.
echo Press any key to refresh...
pause >nul
goto :eof
