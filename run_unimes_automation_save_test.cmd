@echo off
setlocal
chcp 65001 > nul
cd /d "%~dp0"

echo UNIMES Automation SAVE TEST
echo.
echo WARNING: dryRun=false and saveEnabled=true.
echo Use this only for a small, intentional save test.
echo.
echo Part No input dialog will open first.
echo Enter one known test Part No first.
echo.

dotnet run --project ".\src\UnimesAutomation\UnimesAutomation.csproj" -- --config ".\appsettings.save-test.json"

echo.
echo Exit code: %ERRORLEVEL%
pause
