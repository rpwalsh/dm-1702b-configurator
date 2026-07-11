using System.Windows.Input;

namespace Bao1702.Desktop.Commands;

/// <summary>
/// An <see cref="ICommand"/> implementation that wraps an async delegate and reports execution state.
/// Unhandled exceptions are routed to <see cref="OnError"/> rather than crashing the application.
/// </summary>
public sealed class AsyncRelayCommand : ObservableCommandBase
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this((_) => execute(), canExecute)
    {
    }

    /// <summary>
    /// Raised when the async delegate throws an unhandled exception.
    /// Subscribe to this event to surface errors in the UI rather than silently losing them.
    /// </summary>
    public event Action<Exception>? OnError;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                RaiseCanExecuteChanged();
            }
        }
    }

    public override bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke() ?? true);

    public override async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            await _execute(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is intentional — do not treat as an error.
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }
}
