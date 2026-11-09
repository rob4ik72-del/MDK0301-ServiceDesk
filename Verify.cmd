@echo off
cd /d "%~dp0"
dotnet build ServiceDesk.sln -c Release
if errorlevel 1 exit /b %errorlevel%
dotnet run --project Code/Verification -c Release --no-build -- . RunResults
exit /b %errorlevel%
