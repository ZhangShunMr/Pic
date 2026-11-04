namespace TrimContact.Desktop.ViewModels;

public enum UnsavedChoice { Save, Discard, Cancel }

public interface IDesktopInteraction
{
    string? OpenPath(string filter);
    string? SavePath(string filter, string suggested);
    bool Confirm(string message);
    UnsavedChoice Unsaved(string message);
    void Error(string message);
    void Information(string message);
}
