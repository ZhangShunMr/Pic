// This shim exercises shared MVVM logic on Linux, not WPF bindings or native UI behavior.
namespace System.Windows.Input;

public static class CommandManager
{
    public static event EventHandler? RequerySuggested;
    public static void InvalidateRequerySuggested() => RequerySuggested?.Invoke(null, EventArgs.Empty);
}
