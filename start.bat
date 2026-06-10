@echo off
cd /d "%~dp0"
taskkill /im AudioHQ.App.exe /f >nul 2>&1
echo Building AudioHQ...
dotnet build src\AudioHQ.App --nologo -v minimal
if errorlevel 1 (
    echo.
    echo Build FAILED - details above.
    pause
    exit /b 1
)
start "" "src\AudioHQ.App\bin\Debug\net7.0-windows\AudioHQ.App.exe"
