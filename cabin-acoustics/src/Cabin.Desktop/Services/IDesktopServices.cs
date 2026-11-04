using Cabin.Core.Domain;
using Cabin.Core.IO;

namespace Cabin.Desktop.Services;

public interface IDesktopServices
{
    string? OpenProjectPath();
    string? SaveProjectPath(string currentPath, string suggested);
    string? OpenCsvPath();
    string? ExportPath(string label, string extension);
    bool Confirm(string title, string message);
    int SaveChanges(string name);
    int SelectExample();
    bool ConfirmImport(CsvPreview preview);
    void Error(string message);
    void ExportFieldImage();
}
