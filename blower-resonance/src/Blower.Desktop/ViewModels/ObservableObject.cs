using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Blower.Desktop.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }
        storage = value;
        Raise(property);
        return true;
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? available = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return available?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        execute();
    }

    public void Refresh()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
