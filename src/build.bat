@echo off
setlocal
set OUT=bin\Release\net8.0-windows\win-x64\publish

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

if %errorlevel%==0 (
    echo.
    echo OK: %OUT%\VerBump.exe
    explorer "%~dp0%OUT%"
) else (
    echo.
    echo FEHLER beim Build.
    pause
)
