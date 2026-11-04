using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Avas.App.ViewModels;

namespace Avas.App.Components.Shared;

public class VmComponent : ComponentBase, IDisposable
{
    [Inject] public WorkspaceViewModel Vm { get; set; } = default!;
    [Inject] public IJSRuntime Js { get; set; } = default!;

    protected override void OnInitialized()
    {
        Vm.PropertyChanged += ViewModelChanged;
    }

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Vm.PropertyChanged -= ViewModelChanged;
        GC.SuppressFinalize(this);
    }

    protected async Task Confirm(string message, Action action)
    {
        if (await Js.InvokeAsync<bool>("confirm", message))
            action();
    }

    protected async Task Replace(string message, Action action)
    {
        if ((Vm.Dirty || Vm.HasDraft) && !await Js.InvokeAsync<bool>("confirm", message + " 当前未保存或未应用内容将丢失。"))
            return;
        action();
    }

    protected async Task Upload(InputFileChangeEventArgs args, long max, Action<string, byte[]> apply)
    {
        try
        {
            var file = args.File;
            using var memory = new MemoryStream();
            await file.OpenReadStream(max).CopyToAsync(memory);
            apply(file.Name, memory.ToArray());
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            Vm.Failure("上传失败：" + error.Message + "；请检查文件大小与格式。");
        }
    }
}
