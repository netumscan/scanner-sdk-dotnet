# scanner-sdk-dotnet

Public .NET distribution repository for Netum Scanner SDK.

Current version: 2.2.0

NuGet package:

- `ScannerSdk`
- https://www.nuget.org/packages/ScannerSdk

Current official package scope:

- managed package: `ScannerSdk`
- native RIDs: `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`
- target frameworks: `net10.0`, `net10.0-windows10.0.19041.0`, and `netstandard2.0`

Install:

```bash
dotnet add package ScannerSdk --version 2.2.0
```

Repository:

- https://github.com/netumscan/scanner-sdk-dotnet

Documentation:

- [Integration guide](docs/dotnet.md)
- [Security and privacy](docs/security-and-privacy.md)
- [Release notes](releases/v2.2.0/release-notes.md)
- [License](LICENSE)

Samples:

- [WPF Quick Start](samples/ScannerSDK.WpfQuickStart)
- [MAUI Demo](samples/ScannerSDK.MauiDemo)
- [WPF Full Demo](samples/ScannerSDK.WPFDemo)

Transport integration:

- USB CDC is the primary transport on Windows and macOS.
- USB HID Report is included for all seven desktop RIDs and is the current Linux USB path.
- USB and Bluetooth validation status is tracked by platform and device. BLE GATT and Classic SPP remain CodeOnly.

License:

- Free for commercial application integration and redistribution as part of
  your applications.
- Closed-source. Do not redistribute the SDK as a standalone SDK, source
  package, component library, or competing product.

This repository is the public customer entry for the .NET package. The private
development repository is not required for customer integration.
