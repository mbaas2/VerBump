@echo off
:: VerBump screenshot generator
:: Requires a built VerBump.DemoRunner.exe and a demo settings file.
:: Run from the repo root.

set EXE=src\VerBump.DemoRunner\bin\Release\net8.0-windows\VerBump.DemoRunner.exe
set DEMO=src\screenshot-demo.json
set OUT=docs\screenshots
:: Which project entry to show in settings screenshot (0-based index)
set ENTRY=3
:: Which main-window row to show the list dropdown for (-1 = no dropdown)
set ROW=3

if not exist "%EXE%" (
    echo ERROR: %EXE% not found. Run build-demo.bat first.
    exit /b 1
)
if not exist "%DEMO%" (
    echo ERROR: %DEMO% not found. Create a demo settings file with nice-looking test projects.
    exit /b 1
)

echo Taking EN screenshots...
"%EXE%" C:\devt\VerBump\doc\settings.json --lang=en --settings="%DEMO%" --screenshot="%OUT%" --screenshot-entry=%ENTRY% --screenshot-row=%ROW% --screenshot-help

echo Taking DE screenshots...
"%EXE%" C:\devt\VerBump\doc\settings.json --lang=de --settings="%DEMO%" --screenshot="%OUT%" --screenshot-entry=%ENTRY% --screenshot-row=%ROW% --screenshot-help

echo Done. Files written to %OUT%\
dir "%OUT%\*.png" /b
