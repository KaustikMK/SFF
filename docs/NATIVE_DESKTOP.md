# Native desktop application

The released SteaMidra UI is the cross-platform C# project in
`src/SteaMidra.Desktop`. It uses Avalonia and is published as a self-contained
.NET application. Launching the released application does not require Python,
PySide, PyInstaller, Qt, or QWebEngine.

## Build from source

Install the .NET 8 SDK and run:

```bash
dotnet restore src/SteaMidra.Desktop/SteaMidra.Desktop.csproj
dotnet publish src/SteaMidra.Desktop/SteaMidra.Desktop.csproj -c Release -r win-x64 --self-contained true -o dist/SteaMidra_GUI
```

Use `linux-x64` instead of `win-x64` for Linux. On Windows,
`build_installer.bat` publishes the same native application and passes its
output to NSIS.

The old Python modules remain in the source tree as implementation reference
while their services are moved into the native application; they are not part
of the desktop release pipeline.
