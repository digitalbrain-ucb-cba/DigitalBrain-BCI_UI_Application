@echo off
rem Abre la interfaz de DigitalBrain (compila si hace falta).
cd /d "%~dp0FRONT"
dotnet run --project DigitalBrain.UI.csproj
if errorlevel 1 pause
