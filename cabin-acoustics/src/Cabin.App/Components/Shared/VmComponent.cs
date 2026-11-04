using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Cabin.App.ViewModels;

namespace Cabin.App.Components.Shared;

public abstract class VmComponent : ComponentBase, IDisposable
{
    [Inject] public WorkspaceViewModel Vm { get; set; } = null!;

    protected override void OnInitialized()
    {
        Vm.PropertyChanged += Changed;
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Vm.PropertyChanged -= Changed;
        GC.SuppressFinalize(this);
    }

    protected static string N(double value, string format = "F2") => value.ToString(format, CultureInfo.InvariantCulture);
}
