@echo off
REM Lilia Editor API - Quick Start
REM Simply double-click this file to start the API

echo ========================================
echo   Lilia Editor API
echo ========================================
echo.

cd /d "%~dp0"

echo Starting API on http://localhost:5001
echo Swagger UI: http://localhost:5001/
echo Press Ctrl+C to stop
echo.

dotnet run --project src\Lilia.Api

pause
