using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MotorFrequency.Application;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        Raise(property);
        return true;
    }

    protected internal void Raise([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> action;
    private readonly Predicate<object?>? enabled;
    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action action, Func<bool>? enabled = null)
        : this(_ => action(), enabled is null ? null : _ => enabled())
    {
    }

    public RelayCommand(Action<object?> action, Predicate<object?>? enabled = null)
    {
        this.action = action;
        this.enabled = enabled;
    }

    public bool CanExecute(object? parameter)
    {
        return enabled?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            action(parameter);
        }
    }

    public void Refresh()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public enum LeaveChoice
{
    Save,
    Discard,
    Cancel
}

public interface IDesktopInteraction
{
    string? OpenProjectFile();
    string? OpenSpectrumFile();
    string? SaveFile(string suggestedName, string extension, string description);
    LeaveChoice ConfirmLeave(string reason);
    bool Confirm(string message);
    void RevealFile(string path);
}
