using Cabin.Core.IO;
using Cabin.Desktop.Services;

namespace Cabin.Tests;

internal sealed class TestServices : IDesktopServices
{
    public string? Path { get; set; }
    public int Example { get; set; } = 1;
    public int SaveDecision { get; set; } = 2;
    public List<string> Errors { get; } = new();
    public bool Confirm(string title, string message) => true;
    public bool ConfirmImport(CsvPreview preview) => preview.CanApply;
    public void Error(string message) => Errors.Add(message);
    public void ExportFieldImage() { }
    public string? ExportPath(string label, string extension) => Path;
    public string? OpenCsvPath() => Path;
    public string? OpenProjectPath() => Path;
    public int SaveChanges(string name) => SaveDecision;
    public string? SaveProjectPath(string currentPath, string suggested) => Path;
    public int SelectExample() => Example;
}
