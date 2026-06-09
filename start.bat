@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ============================================================
rem  RqSimulator Launcher (.NET 10 / SDK-style)
rem ============================================================

rem Repository root (directory of this script, no trailing \)
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo === RqSimulator start.bat ===
echo Root: "%ROOT%"

rem --------------- configuration ---------------
set "CONFIG=Release"
set "SLN=%ROOT%\RqSimPlatform.sln"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found in PATH.
    call :MaybePause
    endlocal
    exit /b 1
)

if not exist "%SLN%" (
    echo ERROR: Solution file not found:
    echo   %SLN%
    call :MaybePause
    endlocal
    exit /b 1
)

rem --------------- locate UI output ---------------
call :ResolveUiTarget
if defined UI_TARGET (
    echo RqSimUI found. Build is not required.
    goto :launch
)

rem --------------- build if needed ---------------
echo RqSimUI was not found in output folders. Building solution (%CONFIG%)...
dotnet build "%SLN%" -c %CONFIG%
if errorlevel 1 (
    echo.
    echo *** BUILD FAILED ***
    call :MaybePause
    endlocal
    exit /b 1
)

call :ResolveUiTarget
if not defined UI_TARGET (
    echo RqSimUI was not found after build. Trying Platform=x64...
    dotnet build "%SLN%" -c %CONFIG% -p:Platform=x64
    if errorlevel 1 (
        echo.
        echo *** BUILD FAILED ^(x64^) ***
        call :MaybePause
        endlocal
        exit /b 1
    )

    call :ResolveUiTarget
)

if not defined UI_TARGET (
    echo ERROR: RqSimUI was not found as exe or dll after build.
    echo Check the build output and bin folder structure.
    call :MaybePause
    endlocal
    exit /b 1
)

:launch
rem --------------- launch ---------------

echo Starting RqSimUI (waiting for exit)...
echo [DEBUG] UI_TARGET="%UI_TARGET%"
echo [DEBUG] CONFIG=%CONFIG%
for %%I in ("%UI_TARGET%") do set "UI_DIR=%%~dpI"

if not exist "%UI_TARGET%" (
    echo ERROR: UI_TARGET does not exist:
    echo   %UI_TARGET%
    call :MaybePause
    endlocal
    exit /b 1
)

echo UI target: "%UI_TARGET%"
echo UI kind: %UI_KIND%
echo UI dir: "%UI_DIR%"

if /I "%DRY_RUN%"=="1" (
    echo DRY_RUN=1, launch skipped.
    call :MaybePause
    endlocal
    exit /b 0
)

pushd "%UI_DIR%"
if /I "%UI_KIND%"=="exe" (
    "%UI_TARGET%"
) else (
    dotnet "%UI_TARGET%"
)
set "UI_EXITCODE=%errorlevel%"
popd

if not "%UI_EXITCODE%"=="0" (
    echo.
    echo WARNING: RqSimUI exited with code %UI_EXITCODE%
)

echo.
echo All done.
call :MaybePause
endlocal
exit /b %UI_EXITCODE%

rem ---------------- helpers ----------------

rem Sets:
rem   UI_TARGET = full path to RqSimUI.exe or RqSimUI.dll
rem   UI_KIND   = exe|dll
:ResolveUiTarget
set "UI_TARGET="
set "UI_KIND="

call :FindUiTargetByExt exe
if defined UI_TARGET (
    goto :eof
)

call :FindUiTargetByExt dll
if defined UI_TARGET (
    goto :eof
)

goto :eof

rem %1 = extension: exe or dll
:FindUiTargetByExt
if not exist "%ROOT%\RqSimUI\bin\%CONFIG%" goto :eof

rem SDK-style projects: check for TFM subdirectories like net10.0-windows*
for /r "%ROOT%\RqSimUI\bin\%CONFIG%" %%F in (RqSimUI.%~1) do (
    if exist "%%F" (
        set "UI_TARGET=%%F"
        set "UI_KIND=%~1"
        goto :eof
    )
)

goto :eof

:MaybePause
if /I "%NO_PAUSE%"=="1" goto :eof
pause
goto :eof
