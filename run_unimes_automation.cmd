@echo off
setlocal
chcp 65001 > nul
cd /d "%~dp0"

echo UNIMES Automation PoC
echo.
echo Part No input dialog will open first.
echo Close this window only after the run finishes.
echo.

dotnet run --project ".\src\UnimesAutomation\UnimesAutomation.csproj"

echo.
echo Exit code: %ERRORLEVEL%
pause
