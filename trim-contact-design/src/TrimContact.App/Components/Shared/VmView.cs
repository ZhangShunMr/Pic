using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using TrimContact.App.ViewModels;

namespace TrimContact.App.Components.Shared;

public abstract class VmView : ComponentBase, IDisposable
{
    [Inject] public WorkspaceViewModel Vm { get; set; } = null!;
    protected override void OnInitialized() => Vm.PropertyChanged += OnChanged;
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);
    public void Dispose()
    {
        Vm.PropertyChanged -= OnChanged;
        GC.SuppressFinalize(this);
    }
    protected static string F(double v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
}
