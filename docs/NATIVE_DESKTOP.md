# Native desktop application

The released SteaMidra UI is the cross-platform C# project in
`src/SteaMidra.Desktop`. It uses Avalonia and is published as a self-contained
.NET application. Launching the released application does not require Python,
PySide, PyInstaller, Qt, or QWebEngine.

## Build from source

Install the .NET 8 SDK and run:

```bash
dotnet restore src/SteaMidra.Desktop/SteaMidra.Desktop.csproj
dotnet publish src/SteaMidra.Desktop/SteaMidra.Desktop.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o dist/SteaMidra_GUI
```

Use `linux-x64` instead of `win-x86` for Linux. On Windows,
`build_installer.bat` publishes the same native application and passes its
output to NSIS.

The old Python modules remain in the source tree as implementation reference
while their services are moved into the native application; they are not part
of the desktop release pipeline.

## Wine and Winlator

The Windows release is intentionally published as **self-contained `win-x86`**.
It works on regular 64-bit Windows and can also run in either 32-bit or 64-bit
Wine/Winlator containers without an installed .NET runtime. Keep every file in
the published `SteaMidra_GUI` folder together; launching only the EXE will fail.

When Wine is detected, the application uses Avalonia's software renderer rather
than ANGLE/D3D composition, which avoids a common early-exit failure on Android
graphics drivers. If it still cannot create a window, inspect
`%LOCALAPPDATA%\\SteaMidra\\startup.log` (normally under the Winlator container's
`drive_c/users/<user>/AppData/Local/SteaMidra/`) for the startup exception.
