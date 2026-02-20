@echo off
setlocal EnableExtensions

:: ============================================================
::  RqSimulator Launcher  (NET 10 / SDK-style)
:: ============================================================

:: Repository root (directory of this script, no trailing \)
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo === RqSimulator start.bat ===
echo Root: "%ROOT%"

:: --------------- configuration ---------------
set "CONFIG=Release"
set "SLN=%ROOT%\RqSimPlatform.sln"

:: Deterministic output paths derived from actual TFMs
set "CONSOLE=%ROOT%\RqSimConsole\bin\%CONFIG%\net10.0-windows\RqSimConsole.exe"
set "UI=%ROOT%\RqSimUI\bin\%CONFIG%\net10.0-windows10.0.22000.0\RqSimUI.exe"

:: --------------- build if needed ---------------
if exist "%CONSOLE%" if exist "%UI%" (
    echo Both executables already present. Skipping build.
    goto :launch
)

echo One or more executables missing. Building solution in %CONFIG%...
dotnet build "%SLN%" -c %CONFIG%
if errorlevel 1 (
    echo.
    echo *** BUILD FAILED ***
    pause
    endlocal
    exit /b 1
)
echo Build succeeded.

:: Verify executables appeared
if not exist "%CONSOLE%" (
    echo ERROR: RqSimConsole.exe not found after build at:
    echo   %CONSOLE%
    pause
    endlocal
    exit /b 1
)
if not exist "%UI%" (
    echo ERROR: RqSimUI.exe not found after build at:
    echo   %UI%
    pause
    endlocal
    exit /b 1
)

:: --------------- launch ---------------

:: Start UI and wait for it to exit
echo Starting RqSimUI (waiting for exit)...
start "RqSimUI" /D "%ROOT%\RqSimUI\bin\%CONFIG%\net10.0-windows10.0.22000.0" /WAIT "%UI%"

echo.
echo All done.
pause
endlocal
