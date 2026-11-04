namespace Blower.Desktop.Services;

public enum SaveDecision
{
    Save,
    Discard,
    Cancel
}

public interface IDesktopDialogs
{
    string? OpenProject();
    string? SaveProject(string name);
    string? OpenCsv();
    string? ExportPath(string name, string extension);
    bool Confirm(string message);
    SaveDecision Unsaved(string message);
}

