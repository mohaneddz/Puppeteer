@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo Puppeteer requires the .NET 10 SDK.
    echo Download it from https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

rem A still-running instance locks the build output; close it before rebuilding.
taskkill /IM Puppeteer.App.exe /F >nul 2>&1

dotnet run --project "src\Puppeteer.App\Puppeteer.App.csproj"
if errorlevel 1 (
    echo.
    echo Puppeteer exited with an error.
    pause
)

endlocal
