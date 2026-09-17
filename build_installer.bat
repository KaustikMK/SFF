@echo off
setlocal

set NSIS=C:\Program Files (x86)\NSIS\makensis.exe

:: Extract version from the .NET project metadata. No Python runtime is used.
for /f "tokens=2 delims=<>" %%v in ('findstr "<Version>" Directory.Build.props') do set APP_VERSION=%%v
set APP_VERSION=%APP_VERSION:"=%
if "%APP_VERSION%"=="" (
    echo Could not read Version from Directory.Build.props
    pause
    exit /b 1
)
echo Version: %APP_VERSION%

:: Allow NSI-only mode: pass "nsi" as first argument to skip dotnet publish
if /i "%~1"=="nsi" goto compile_nsi

echo [1/2] Publishing Wine/Winlator-compatible native .NET distribution...
REM win-x86 works on 64-bit Windows and avoids requiring a WoW64/box64-only
REM Wine container on Android. Keep the complete publish folder together.
dotnet publish src\SteaMidra.Desktop\SteaMidra.Desktop.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o dist\SteaMidra_GUI
if %errorlevel% neq 0 (
    echo .NET publish failed.
    pause
    exit /b 1
)

:compile_nsi
echo [2/2] Compiling NSIS installer...
if not exist "%NSIS%" goto nsis_missing
goto nsis_found
:nsis_missing
    echo NSIS not found at "%NSIS%"
    echo Install NSIS from https://nsis.sourceforge.io/Download
    pause
    exit /b 1
:nsis_found
"%NSIS%" /DVERSION=%APP_VERSION% installer.nsi
if %errorlevel% neq 0 (
    echo NSIS compile failed.
    pause
    exit /b 1
)

echo Done. Installer written to SteaMidra-%APP_VERSION%-Setup.exe
if not defined CI pause
