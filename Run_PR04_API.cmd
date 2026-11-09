@echo off
cd /d "%~dp0"
dotnet run --project Code/ServiceDeskApi -- --urls http://127.0.0.1:5000
