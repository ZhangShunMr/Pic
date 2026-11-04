using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AcousticSeal.DesktopLogic;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
}

public sealed class ActionCommand : ICommand
{
    private readonly Func<Task> action;
    private readonly Action<Exception> failed;
    private bool running;
    public ActionCommand(Func<Task> action, Action<Exception> failed)
    {
        this.action = action;
        this.failed = failed;
    }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        catch (Exception error) { failed(error); }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public interface IUserDialogs
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string filename);
    bool Confirm(string title, string text);
    // true=save, false=discard, null=cancel
    bool? SaveChanges(string text);
    void ShowError(string text);
}

public sealed record Choice(string Key, string Label);
public sealed record NavigationItem(string Key, string Title, string Hint);
public sealed record ObjectItem(Guid Id, string Title, string Subtitle);
