using Microsoft.AspNetCore.Components;
using AcousticSeal.App.ViewModels;

namespace AcousticSeal.App.Components.Shared;

public abstract class VmComponent : ComponentBase, IDisposable
{
    [Inject]
    protected WorkspaceViewModel Vm { get; set; } = null!;

    protected override void OnInitialized()
    {
        Vm.Changed += Changed;
    }

    private void Changed()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Vm.Changed -= Changed;
        GC.SuppressFinalize(this);
    }
}
