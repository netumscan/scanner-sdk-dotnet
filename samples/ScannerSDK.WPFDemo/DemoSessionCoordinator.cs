using ScannerSdkLib = global::ScannerSDK;

namespace ScannerSDK.WPFDemo;

internal sealed class DemoSessionCoordinator
{
    private readonly IDemoSdkBackend backend;

    internal DemoSessionCoordinator(IDemoSdkBackend backend)
    {
        this.backend = backend;
    }

    internal bool IsInitialized { get; private set; }

    internal IDemoSessionHandle? ActiveSession { get; private set; }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            return;
        }

        await backend.InitializeAsync(cancellationToken).ConfigureAwait(true);
        IsInitialized = true;
    }

    internal Task StartDiscoveryAsync(
        ScannerSdkLib.TransportType transport,
        string selectedModelKey,
        CancellationToken cancellationToken = default) =>
        backend.StartDiscoveryAsync(transport, selectedModelKey, cancellationToken);

    internal Task StopDiscoveryAsync(CancellationToken cancellationToken = default) =>
        backend.StopDiscoveryAsync(cancellationToken);

    internal async Task<IDemoSessionHandle> ConnectReadyAsync(
        DemoDeviceItem device,
        string fallbackModelKey,
        CancellationToken cancellationToken = default)
    {
        await StopDiscoveryAsync(cancellationToken).ConfigureAwait(true);
        var connected = await backend.ConnectReadyAsync(
            new ScannerSdkLib.DiscoveredDevice(
                device.DeviceId,
                device.Name,
                device.TransportType,
                device.ModelKey,
                device.Detail,
                device.Rssi),
            fallbackModelKey,
            cancellationToken).ConfigureAwait(true);
        ActiveSession = connected;
        return connected;
    }

    internal async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var active = ActiveSession;
        ActiveSession = null;
        if (active is not null)
        {
            await active.DisconnectAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    internal void ReleaseActiveSession(IDemoSessionHandle session)
    {
        if (ReferenceEquals(ActiveSession, session))
        {
            ActiveSession = null;
        }
    }

    internal async Task ShutdownAsync()
    {
        Exception? disconnectError = null;
        try
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            disconnectError = error;
        }

        Exception? shutdownError = null;
        if (IsInitialized)
        {
            try
            {
                await backend.ShutdownAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                shutdownError = error;
            }
            finally
            {
                IsInitialized = false;
            }
        }

        if (disconnectError is not null && shutdownError is not null)
        {
            throw new AggregateException(disconnectError, shutdownError);
        }
        if (shutdownError is not null)
        {
            throw shutdownError;
        }
        if (disconnectError is not null)
        {
            throw disconnectError;
        }
    }
}
