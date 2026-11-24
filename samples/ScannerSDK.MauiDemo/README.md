# Scanner SDK .NET NT-91 MAUI Demo

English-only Windows MAUI Demo dedicated to the Scanner SDK `2.2.0`
`NT-91 / USB HID / ModulePassthrough / NT212X` path. The application is an
unpackaged Windows x64 app; it is not a cross-platform MAUI shell or the
general .NET Quick Start.

## SDK dependency

The application consumes the released NuGet package:

```xml
<PackageReference Include="ScannerSdk" Version="2.2.0" />
```

## What the sample covers

- Verifies that the SDK supports `NT-91` over USB HID and keeps it as the fixed target.
- Refreshes and deduplicates attached NT-91 scanners without displaying device IDs.
- Connects to Ready and handles explicit or unexpected disconnects.
- Dynamically loads available settings and supplies value-kind-specific editors.
- Groups settings by the SDK's first-level capability domain display name and order.
- Adds search, click-to-scroll domain navigation, active-domain tracking, a compact
  domain picker, and `Ctrl+F` search focus.
- Reads individual or all readable settings; writes default to non-persistent,
  with opt-in persistence and confirmation for non-normal risk levels.
- Retries SDK initialization and provides actionable no-device guidance.
- Keeps the latest 20 scan results in memory only and can clear them explicitly.
- Adapts to narrow windows and supports light, dark, keyboard, and screen-reader use.

The sample does not expose fixed Quick Actions, capability actions, data rules,
serial numbers, raw command bytes, complete device identifiers, or log export.

## Requirements

- Windows x64 build 19041 or newer on a [release supported by .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#windows)
- .NET 10 SDK
- .NET MAUI Windows workload
- A physical NT-91 scanner configured for its supported USB HID communication path

Install the workload if needed:

```powershell
dotnet workload install maui-windows
```

## Build and run

```powershell
dotnet restore ScannerSDK.MauiDemo.csproj -r win-x64
dotnet build ScannerSDK.MauiDemo.csproj -c Release -r win-x64
dotnet run --project ScannerSDK.MauiDemo.csproj -c Release
```

## Privacy

Device identifiers are used internally for connection and deduplication but
are never rendered. Scan text is not logged, persisted, or exported. Current
setting values remain in application memory only; an explicit `Persist on
device` write may still change the scanner configuration.
