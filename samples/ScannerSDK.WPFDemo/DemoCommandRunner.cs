namespace ScannerSDK.WPFDemo;

internal sealed class DemoCommandRunner : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private CancellationTokenSource generation = new();
    private int pendingCount;
    private bool disposed;

    internal event Action<bool>? BusyChanged;

    internal bool IsBusy => Volatile.Read(ref pendingCount) > 0;

    internal async Task RunAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        CancellationTokenSource linked;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                generation.Token,
                cancellationToken);
        }

        using (linked)
        {
            var entered = false;
            if (Interlocked.Increment(ref pendingCount) == 1)
            {
                BusyChanged?.Invoke(true);
            }

            try
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(true);
                entered = true;
                await action(linked.Token).ConfigureAwait(true);
            }
            finally
            {
                if (entered)
                {
                    gate.Release();
                }
                if (Interlocked.Decrement(ref pendingCount) == 0)
                {
                    BusyChanged?.Invoke(false);
                }
            }
        }
    }

    internal void CancelPending()
    {
        CancellationTokenSource previous;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            previous = generation;
            generation = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource previous;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            previous = generation;
        }

        previous.Cancel();
        previous.Dispose();
    }
}
