@echo off
setlocal
chcp 65001 > nul
cd /d "%~dp0"

echo UNIMES 자동화 (GUI)
echo.
echo 메인 창이 열립니다. 창에서 파트 입력 후 실행하세요.
echo Keep this console open while the app is running.
echo.

dotnet run --project ".\src\UnimesAutomation\UnimesAutomation.csproj"

echo.
echo Exit code: %ERRORLEVEL%
pause
