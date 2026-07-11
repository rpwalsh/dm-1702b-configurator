using System.ComponentModel;
using System.Windows.Input;

namespace Bao1702.Desktop.Commands;

/// <summary>
/// Base class for commands that support <see cref="INotifyPropertyChanged"/> for observable state such as <c>IsRunning</c>.
/// </summary>
public abstract class ObservableCommandBase : ICommand, INotifyPropertyChanged
{
    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public abstract bool CanExecute(object? parameter);
    public abstract void Execute(object? parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
