@echo off
setlocal

set "ROOT=%~dp0"
set "APP_PROJECT=%ROOT%ClippyPlus.App\ClippyPlus.App.csproj"
set "APP_EXE=%ROOT%ClippyPlus.App\bin\Debug\net10.0\ClippyPlus.App.exe"

echo Building ClippyPlus...
dotnet build "%APP_PROJECT%" -c Debug
if errorlevel 1 (
    echo.
    echo Build failed.
    echo If ClippyPlus is already running, close it from the tray and run this script again.
    pause
    exit /b 1
)

echo.
echo Starting ClippyPlus...
start "" "%APP_EXE%"

endlocal
