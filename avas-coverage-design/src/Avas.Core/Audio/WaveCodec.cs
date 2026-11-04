using System.Buffers.Binary;
using System.Text;
using Avas.Core.Domain;
using Avas.Core.IO;

namespace Avas.Core.Audio;

public sealed record WaveData(int Rate, int Channels, double[] Samples)
{
    public double Duration => (double)Samples.Length / Rate;
}

public static class WaveCodec
{
    public static WaveData Read(byte[] bytes)
    {
        if (bytes.Length is < 44 or > 10485760)
            throw new ArgumentException("WAV文件必须在44字节至10MiB之间");
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new ArgumentException("只支持RIFF WAVE；请先转换为16位PCM WAV");
        var declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        if (declared + 8L != bytes.Length)
            throw new ArgumentException("WAV RIFF声明长度与文件长度不一致");
        var format = 0;
        var channels = 0;
        var rate = 0;
        var bits = 0;
        var align = 0;
        var byteRate = 0;
        byte[]? data = null;
        var position = 12;
        var seenFormat = false;
        while (position + 8 <= bytes.Length)
        {
            var name = Encoding.ASCII.GetString(bytes, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4, 4));
            position += 8;
            if (size > int.MaxValue || position + (long)size > bytes.Length)
                throw new ArgumentException("WAV数据块截断");
            if (name == "fmt ")
            {
                if (seenFormat || size < 16)
                    throw new ArgumentException("WAV格式块重复或不完整");
                seenFormat = true;
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 4, 4));
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 8, 4));
                align = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 12, 2));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 14, 2));
            }
            if (name == "data")
            {
                if (data is not null)
                    throw new ArgumentException("WAV存在多个data块，请转换后重试");
                data = bytes.AsSpan(position, (int)size).ToArray();
            }
            position += checked((int)size + (int)(size % 2));
        }
        if (format != 1 || bits != 16 || channels is < 1 or > 2 || rate is < 16000 or > 96000)
            throw new ArgumentException("仅支持16位PCM、单/双声道、16000至96000Hz WAV");
        if (align != channels * 2 || byteRate != rate * align || data is null || data.Length == 0 || data.Length % align != 0)
            throw new ArgumentException("WAV块对齐、字节率或采样长度无效");
        var frames = data.Length / align;
        if ((double)frames / rate > 30)
            throw new ArgumentException("音频最多30秒，请先裁剪");
        var samples = new double[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(frame * align + channel * 2, 2));
                sum += sample / 32768.0;
            }
            samples[frame] = sum / channels;
        }
        return new WaveData(rate, channels, samples);
    }

    public static AudioAsset CreateAsset(string fileName, byte[] bytes)
    {
        var data = Read(bytes);
        return new AudioAsset
        {
            Name = Path.GetFileName(fileName),
            Sha256 = ProjectCodec.Hash(bytes),
            Data = Convert.ToBase64String(bytes),
            Rate = data.Rate,
            Channels = data.Channels,
            Duration = data.Duration
        };
    }

    public static byte[] Write(double[] samples, int rate = 24000, string? metadata = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1, 0.999969) * 32768));
        if (metadata is not null)
        {
            var comment = Encoding.UTF8.GetBytes(metadata + "\0");
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            writer.Write(12 + comment.Length + comment.Length % 2);
            writer.Write(Encoding.ASCII.GetBytes("INFOICMT"));
            writer.Write(comment.Length);
            writer.Write(comment);
            if (comment.Length % 2 != 0)
                writer.Write((byte)0);
            var end = stream.Position;
            stream.Position = 4;
            writer.Write(checked((int)end - 8));
            stream.Position = end;
        }
        return stream.ToArray();
    }
}
