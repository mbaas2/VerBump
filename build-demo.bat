@echo off
setlocal
set PROJ=src\VerBump.DemoRunner\VerBump.DemoRunner.csproj
set OUT=src\VerBump.DemoRunner\bin\Release\net8.0-windows

dotnet build "%PROJ%" -c Release

if %errorlevel%==0 (
    echo.
    echo OK: %OUT%\VerBump.DemoRunner.exe
) else (
    echo.
    echo FEHLER beim Build.
    pause
)
