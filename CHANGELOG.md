# Scanner SDK .NET Release 2.2.0

## Changes

- `ScannerSdk.IsDeviceModelCompatible` checks whether a selected template model matches the model resolved by a device session. Matching models do not guarantee that every setting or target value is supported; check the active session's capabilities before writing.
- `DiscoveredDevice.DeviceKind` classifies a USB discovery result as `WirelessScanner`, `WiredScanner`, `ScanModule`, or `Unknown` from its transport and USB fingerprint. Classification does not connect to the device or confirm its exact model.
- The package targets `net10.0`, `net10.0-windows10.0.19041.0`, and `netstandard2.0`. Windows .NET Framework 4.8 applications consume the `netstandard2.0` assembly.
- The shared native Core adds 29 SSI symbology mappings for the 212M USB Serial packet path without changing existing values.

## Compatibility and validation

- The C ABI remains `1.0.0` (`0x010000`). Use the managed assembly and native library from the same SDK release and source revision.
- The release package requires native libraries for `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, and `osx-x64`. Publication requires verification of all seven assets and consumption on their target runtimes.
- USB CDC is the primary transport on Windows and macOS. USB HID Report is included for all seven desktop RIDs and is the current Linux USB path.
- USB validation is tracked by platform and device. BLE GATT and Classic SPP remain `CodeOnly`. The integration guide describes the platform requirements.
