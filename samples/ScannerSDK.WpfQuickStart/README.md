# Scanner SDK .NET WPF Quick Start

English-only Windows x64 Quick Start for Scanner SDK `2.2.0`. The
application uses WPF and .NET 10, so it does not require the .NET MAUI workload.

## SDK dependency

```xml
<PackageReference Include="ScannerSdk" Version="2.2.0" />
```

## What the sample covers

- Loads USB CDC models supported by the SDK and selects CS7501 when available.
- Refreshes and deduplicates attached USB CDC scanners without displaying device IDs.
- Connects to Ready and handles explicit or unexpected disconnects.
- Enables `Trigger Scan`, `Refresh Device Info`, and `Read Battery` from session support.
- Keeps the latest 20 scan results in memory only.

The sample does not expose capability catalogs, setting writes, data rules,
serial numbers, raw bytes, complete device identifiers, or log export.

## Requirements

- Windows x64 build 19041 or newer on a [release supported by .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#windows)
- .NET 10 SDK

## Build and run

```powershell
dotnet restore ScannerSDK.WpfQuickStart.csproj -r win-x64
dotnet build ScannerSDK.WpfQuickStart.csproj -c Release -r win-x64
dotnet run --project ScannerSDK.WpfQuickStart.csproj -c Release -r win-x64
```

Device identifiers are used internally for connection and deduplication but
are never rendered. Scan text is not logged, persisted, or exported.
