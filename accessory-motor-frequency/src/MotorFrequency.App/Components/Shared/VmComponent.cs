using MotorFrequency.Core.Services;
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using MotorFrequency.App.ViewModels;

namespace MotorFrequency.App.Components.Shared;

public abstract class VmComponent : ComponentBase, IDisposable
{
    [Inject] protected WorkspaceViewModel Vm { get; set; } = default!;

    protected override void OnInitialized()
    {
        Vm.PropertyChanged += Changed;
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    protected static string N(double value) => ChartGeometry.N(value);
    protected static string Display(double? value) => value is double number ? Labels.Number(number) : "—";

    public void Dispose()
    {
        Vm.PropertyChanged -= Changed;
        GC.SuppressFinalize(this);
    }
}
