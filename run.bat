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

rem Do not wait here. When a batch file is the foreground job, Ctrl+C always makes cmd.exe ask
rem "Terminate batch job (Y/N)?". Starting dotnet separately lets Ctrl+C stop the app cleanly.
start "Puppeteer" /b dotnet run --project "src\Puppeteer.App\Puppeteer.App.csproj"

endlocal
exit /b
