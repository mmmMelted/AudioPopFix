@echo off
:: Safe Build + Package script (handles paths with parentheses)
setlocal ENABLEDELAYEDEXPANSION

REM ----- Configure tool paths (quoted SET syntax to handle parentheses) -----
set "INNO=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set "INNO_ALT=C:\Program Files\Inno Setup 6\ISCC.exe"
set "SIGNTOOL=C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe"

REM ----- Resolve Inno Setup path -----
if not exist "%INNO%" (
    if exist "%INNO_ALT%" (
        set "INNO=%INNO_ALT%"
    )
)

if not exist "%INNO%" (
    echo ERROR: Inno Setup ISCC.exe not found.
    echo Install Inno Setup 6 and update the INNO path in this script if needed.
    echo Tried:
    echo   %INNO%
    echo   %INNO_ALT%
    goto :error
)

REM ----- Project/Output paths (quoted) -----
set "PROJECT=%~dp0..\src\AudioPopFixTray\AudioPopFixTray.csproj"
set "OUTDIR=%~dp0..\src\AudioPopFixTray\bin\Release\net6.0-windows"
set "ISS=%~dp0AudioPopFix_Inno.iss"
set "INSTALLER_OUT=%~dp0AudioPopFixTray_Setup.exe"

echo.
echo === Building AudioPopFixTray (Release) ===
dotnet build -c Release "%PROJECT%"
if errorlevel 1 goto :error

REM ----- Optional signing of EXE -----
REM To enable: set CERT_PATH, CERT_PASS, TIMESTAMP_URL in this shell before running.
if defined CERT_PATH if exist "%SIGNTOOL%" (
    echo.
    echo === Signing EXE ===
    "%SIGNTOOL%" sign /f "%CERT_PATH%" /p "%CERT_PASS%" /tr "%TIMESTAMP_URL%" /td sha256 /fd sha256 "%OUTDIR%\AudioPopFixTray.exe"
    if errorlevel 1 echo WARNING: Signing EXE failed.
) else (
    if defined CERT_PATH (
        echo WARNING: signtool not found at "%SIGNTOOL%". Skipping code signing.
    ) else (
        echo Skipping code signing (CERT_PATH not set).
    )
)

echo.
echo === Compiling installer with Inno Setup ===
"%INNO%" "%ISS%"
if errorlevel 1 goto :error

REM ----- Optional signing of installer -----
if defined CERT_PATH if exist "%SIGNTOOL%" (
    if exist "%INSTALLER_OUT%" (
        echo.
        echo === Signing installer ===
        "%SIGNTOOL%" sign /f "%CERT_PATH%" /p "%CERT_PASS%" /tr "%TIMESTAMP_URL%" /td sha256 /fd sha256 "%INSTALLER_OUT%"
        if errorlevel 1 echo WARNING: Signing installer failed.
    )
)

echo.
echo SUCCESS. Output installer:
echo   %INSTALLER_OUT%
pause
exit /b 0

:error
echo.
echo BUILD FAILED.
pause
exit /b 1
