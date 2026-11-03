using Microsoft.AspNetCore.Components.Forms;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.App.Components.Shared;

public static class UploadBoundary
{
    public static async Task<byte[]> ReadAsync(IBrowserFile file, int maximum)
    {
        if (file.Size == 0 || file.Size > maximum)
        {
            throw new DomainValidationException("file", $"文件应为1字节至{maximum / 1024 / 1024}MiB");
        }

        await using var stream = file.OpenReadStream(maxAllowedSize: maximum);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        if (memory.Length > maximum)
        {
            throw new DomainValidationException("file", "读取结果超过允许大小");
        }

        return memory.ToArray();
    }
}
