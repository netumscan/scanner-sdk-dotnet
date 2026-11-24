# .NET Integration Guide

## Installation

For the public release, integrate the SDK through NuGet:

```bash
dotnet add package ScannerSdk --version 2.2.0
```

The public .NET distribution repository is:

- https://github.com/netumscan/scanner-sdk-dotnet

The repository includes three buildable Windows x64 customer samples.
`samples/ScannerSDK.WpfQuickStart/` shows the minimal USB CDC flow,
`samples/ScannerSDK.MauiDemo/` is a dedicated NT-91 USB HID MAUI implementation, and
`samples/ScannerSDK.WPFDemo/` provides the broader product UI. All consume the
SDK through `PackageReference`. The console demo, USB HID smoke tool, and device
testbench remain internal validation tools.

Package targets:

- target frameworks: `net10.0`, `net10.0-windows10.0.19041.0`, and `netstandard2.0`
- official native RIDs: `win-x64`, `win-x86`, `win-arm64`, `linux-x64`,
  `linux-arm64`, `osx-arm64`, and `osx-x64`

If a release overrides package metadata URLs or support boundaries, use the
package page and release note recorded for that version.

## Recommended Flow

1. Install the `ScannerSdk` package from NuGet.
2. Initialize `ScannerSdk`.
3. On Windows and macOS, enumerate USB CDC scanners with `StartDiscoveryAsync([TransportType.UsbSerial], modelKey)`.
   On Linux, use `TransportType.UsbHid`.
4. Connect to the selected target.
5. After the session reaches `READY`, read resolved model and capability data.
6. Observe scan events, state changes, and failures.
7. Only then read or update settings.

## Runtime Requirements

For .NET 10 applications:

- .NET 10 SDK for building or .NET 10 runtime for running an existing build. WPF apps use the .NET 10 Desktop Runtime.
- `maui-windows` workload for building the MAUI Demo
- A [.NET 10-supported OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) on Windows x64, x86, or Arm64, Linux x64 or Arm64, or macOS Arm64 or x64
- Correct device enumeration in the operating system
- Device driver and user permissions for the selected USB transport

Check the release notes for the available transports and real-device verification
for the installed package version.

## Windows .NET Framework 4.8

Set the application target framework to `net48`. NuGet selects the ScannerSDK
`netstandard2.0` assembly and the `net462` Windows assemblies for both InTheHand
dependencies. The Windows .NET Framework host can use USB CDC, USB HID Report,
BLE GATT through InTheHand WinRT, native SPP through InTheHand Win32 RFCOMM, and
Bluetooth serial ports exposed as COM ports. Windows .NET Framework 4.8 Bluetooth
testing with a physical scanner is still pending.

Install the package with `PackageReference`. Set the application's platform
target to `x86` or `x64` to match the process architecture. The package copies
the corresponding `ScannerSDK.Native.dll` beside the application. When
distributing the application, include that DLL and the managed dependencies
from the build output.

After initialization, `StartDiscoveryAsync()` discovers USB CDC and USB HID Report.
For Bluetooth discovery, pass `new[] { TransportType.BleGatt }` or
`new[] { TransportType.SppClassic }` explicitly. Use the discovered device ID
and transport with `ConnectReadyAsync`. For BLE, select `BleTransportProvider.Automatic`
or `BleTransportProvider.InTheHand`. The SDK's separate
`BleTransportProvider.WindowsWinRt` provider is available on the Windows .NET 10 target.
For a Bluetooth serial port already mapped by Windows, connect directly with
`ConnectReadyAsync("spp-com:COM7", TransportType.SppClassic)` using the
actual COM port.

## Native Compatibility

Target framework, runtime identifier, and Scanner SDK C ABI describe different
compatibility layers:

- The managed package targets `net10.0`, `net10.0-windows10.0.19041.0`, and
  `netstandard2.0`.
- The supported native RIDs are `win-x64`, `win-x86`, `win-arm64`, `linux-x64`,
  `linux-arm64`, `osx-arm64`, and `osx-x64`.
- For .NET 10, NuGet restores `ScannerSDK.Native.dll` on Windows (for example,
  `runtimes/win-x64/native/ScannerSDK.Native.dll` for Windows x64),
  `libScannerSDKNative.so` on Linux, or `libScannerSDKNative.dylib` on macOS
  from the matching `runtimes/<rid>/native/` directory.
- P/Invoke uses `cdecl`, and the managed wrapper requires Scanner SDK C ABI
  `0x010000`.

`ScannerSdk.InitializeAsync()` resolves the native library, checks ABI
compatibility, and only then initializes callbacks and platform transports. The
host process architecture must match the restored RID. Applications must not
copy or replace the native library independently, or combine managed and native
files from different package releases.

Initialization errors have distinct causes:

- `DllNotFoundException`: the native asset or one of its load dependencies
  cannot be resolved.
- `BadImageFormatException`: the process and native library architectures do
  not match.
- `EntryPointNotFoundException`: the loaded native library has an incompatible export
  surface or came from the wrong release.
- `Scanner SDK native ABI mismatch: required=... actual=...`: the managed and
  native C ABI versions are incompatible.

The wrapper reports native load failures as `InvalidOperationException` with
the supported RID and asset path in the message; inspect the inner exception to
distinguish the first three cases. Restore one complete NuGet version instead
of bypassing ABI validation.

## WPF Quick Start

The recommended Quick Start is an unpackaged
`net10.0-windows10.0.19041.0` WPF application. It needs no additional workload.
It loads models that support USB CDC, connects to Ready, gates three common actions, and
keeps the latest 20 scan results in memory.

```powershell
dotnet restore samples/ScannerSDK.WpfQuickStart/ScannerSDK.WpfQuickStart.csproj -r win-x64
dotnet run --project samples/ScannerSDK.WpfQuickStart/ScannerSDK.WpfQuickStart.csproj -c Release -r win-x64
```

## NT-91 MAUI Demo

The MAUI Demo is an unpackaged `net10.0-windows10.0.19041.0` application
dedicated to the `NT-91 / USB HID / ModulePassthrough / NT212X` path. It
refreshes attached NT-91 devices, connects to Ready, dynamically loads supported
settings grouped by first-level capability domain, and keeps the latest 20 scan
results in memory. Settings can be located through search, click-to-scroll domain
navigation, a compact domain picker, or `Ctrl+F`. It does not display complete device
identifiers or serial numbers and does not persist or export scan content.

```powershell
dotnet workload install maui-windows
dotnet restore samples/ScannerSDK.MauiDemo/ScannerSDK.MauiDemo.csproj -r win-x64
dotnet run --project samples/ScannerSDK.MauiDemo/ScannerSDK.MauiDemo.csproj -c Release
```

## Key Behavior

- USB CDC (`TransportType.UsbSerial`) is the primary transport on Windows and macOS.
- USB HID Report is available as a compatibility transport and is the current Linux desktop path.
- USB validation is tracked by platform and device. BLE GATT and Classic SPP remain `CodeOnly`.
- For Windows BLE on .NET 10, use `net10.0-windows10.0.19041.0`. The default
  provider is InTheHand, and the SDK's WinRT provider is also available.
- Linux BLE uses InTheHand with BlueZ, D-Bus, and system permissions. macOS
  BLE uses CoreBluetooth and requires the host app's Bluetooth privacy description and permission.
- Windows and Linux use the InTheHand RFCOMM provider for Classic SPP. A system
  mapped serial port can also be passed explicitly as `spp-com:<port>`, including on macOS.
- `ScannerSdk` / `ScannerSession` are the public entry points.
- C API remains the only stable ABI boundary.

## API Starting Points

`ScannerSdk`:

- `InitializeAsync()`
- `StartDiscoveryAsync(...)`
- `ConnectReadyAsync(...)`
- `Discoveries`
- `DiscoveryFailures`
- `GetDeviceModelProfile(...)`
- `IsDeviceModelCompatible(templateModelKey, resolvedDeviceModelKey)`
- `GetCapabilityDomains(...)`
- `GetCapabilityEntries(...)`

USB discoveries expose `DiscoveredDevice.DeviceKind` as `WirelessScanner`,
`WiredScanner`, `ScanModule`, or `Unknown`. It is based on the enumerated USB
fingerprint and does not confirm the exact device model. Model compatibility
helps preflight a template; check the connected session's setting capabilities
and target values before writing.

`ScannerSession`:

- `LatestState`
- `StateChanges`
- `ScanEvents`
- `FailureEvents`
- `GetResolvedModelKey()`
- `GetDeviceCapabilitySummary()`
- `GetCapabilityDomains()`
- `GetCapabilityEntries()`
- `ReadCapabilityValueAsync(...)`
- `WriteCapabilityValueAsync(...)`
- `ExecuteCapabilityActionAsync(...)`
- `RefreshInfoAsync()`
- `GetBatteryInfoAsync()`

## Validation Boundary

The package release verifies package structure, managed assembly inclusion,
all seven native runtime assets, ABI exports, and native interop on each target
architecture. Windows x86 and x64 also test package initialization in a .NET
Framework 4.8 process. Device-side validation still requires real-hardware
regression before a transport path should be called fully verified for customer
production use.
