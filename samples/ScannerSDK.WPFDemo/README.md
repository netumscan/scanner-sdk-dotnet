# ScannerSDK WPF Demo

Customer-facing Windows reference application for `ScannerSDK 2.2.0`.

Requirements:

- Windows x64 build 19041 or newer on a [release supported by .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#windows)
- .NET 10 SDK
- a supported scanner connected through USB HID, USB Serial, or the preview Windows BLE path

Run:

```powershell
dotnet restore
dotnet run
```

The project consumes the released `ScannerSdk` NuGet package. USB CDC is the
default desktop transport. USB HID Report is selectable. Windows BLE remains preview until a dated
real-device compatibility record says otherwise.

Exports redact device identifiers, serial numbers, scan content, and raw
payloads by default. Enable the sensitive diagnostic export option only when
the resulting file can be handled as customer data.
