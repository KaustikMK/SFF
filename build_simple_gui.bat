@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo Building SteaMidra GUI for Wine/Winlator
echo ========================================
echo.
echo Publishing the native Avalonia application as 32-bit Windows.
echo This avoids the retired PyInstaller/PySide/QWebEngine release, whose
echo embedded Chromium process is not reliable in Wine-based containers.

if exist "dist\SteaMidra_GUI" rmdir /s /q "dist\SteaMidra_GUI"

dotnet publish "src\SteaMidra.Desktop\SteaMidra.Desktop.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o "dist\SteaMidra_GUI"
if errorlevel 1 (
    echo.
    echo ========================================
    echo BUILD FAILED!
    echo ========================================
    echo Install the .NET 8 SDK and try again.
    pause
    exit /b 1
)

echo.
echo ========================================
echo BUILD SUCCESSFUL!
echo ========================================
echo.
echo Folder:     dist\SteaMidra_GUI\
echo Executable: dist\SteaMidra_GUI\SteaMidra_GUI.exe
echo.
echo Use the entire folder. Do not copy only the EXE: the self-contained
echo .NET runtime and Avalonia native libraries beside it are required.
echo The win-x86 build is intentional: it runs in normal 64-bit Windows and
echo in both 32-bit and 64-bit Wine/Winlator containers.
if not defined CI pause
