using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;

namespace ScannerSDK.MauiDemo;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Func<object?, bool>? canExecute;
    private readonly Action<Exception>? handleError;
    private int executing;

    internal AsyncCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? handleError = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute(), handleError)
    {
    }

    internal AsyncCommand(
        Func<object?, Task> execute,
        Func<object?, bool>? canExecute = null,
        Action<Exception>? handleError = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
        this.handleError = handleError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        Volatile.Read(ref executing) == 0 && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter) || Interlocked.Exchange(ref executing, 1) != 0)
        {
            return;
        }
        RaiseCanExecuteChanged();
        try
        {
            await execute(parameter).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            handleError?.Invoke(error);
        }
        finally
        {
            Interlocked.Exchange(ref executing, 0);
            RaiseCanExecuteChanged();
        }
    }

    internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal interface IMauiDemoDispatcher
{
    void Dispatch(Action action);
}

internal sealed class MauiDemoDispatcher : IMauiDemoDispatcher
{
    public void Dispatch(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }
        MainThread.BeginInvokeOnMainThread(action);
    }
}
