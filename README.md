# PC Care

A safe, modern Windows optimization tool built with WPF on .NET 8.

## Features

- **Cleaner** — removes temporary files, Windows Update cache and other junk safely.
- **Startup** — review and manage programs that start with Windows.
- **Performance** — quick system health overview and tuning.
- **Drivers** — checks for driver updates through Windows Update and installs them.

## Requirements

- Windows 10/11, 64-bit (x64)
- Administrator rights (the app requests elevation on start)

The published build is **self-contained**, so the .NET 8 runtime does not need to be installed separately.

## Download

Grab the latest installer from the [Releases](https://github.com/USERNAME/pc-care/releases/latest) page:

```
PC_Care_Setup.exe
```

Run it, confirm the UAC prompt and follow the wizard. The app is installed to
`C:\Program Files\PC Care` with Start Menu and optional desktop shortcuts.

> The executable is not code-signed yet, so Windows SmartScreen may show
> "Windows protected your PC". Choose **More info → Run anyway**.

## Build from source

```powershell
dotnet publish PCCare.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

That produces `publish\PC Care.exe`.

## Build the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer.iss
```

Output: `publish\setup\PC_Care_Setup.exe`.

## Project layout

| Path | Purpose |
| --- | --- |
| `Views/` | XAML windows and controls |
| `ViewModels/` | MVVM view models and commands |
| `Services/` | Cleaning, startup, driver and performance logic |
| `Models/` | Data models |
| `Helpers/`, `Converters/` | Shared utilities |
| `installer.iss` | Inno Setup packaging script |
| `app.manifest` | Elevation and DPI settings |
