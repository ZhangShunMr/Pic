using Avas.Core.Analysis;
using Avas.Core.Domain;

namespace Avas.Core.Audio;

public sealed record Audition(byte[] Wave, string Description, double Peak, bool Overloaded, string Hash);

public static class AuditionEngine
{
    public static Audition Render(Project project, Listener point, double seconds, double playbackReference, bool includeBackground)
    {
        var errors = Validation.Check(project, true);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("\n", errors));
        if (!double.IsFinite(seconds) || seconds is < 1 or > 8)
            throw new ArgumentException("试听长度范围1至8秒");
        if (!double.IsFinite(playbackReference) || playbackReference is < 60 or > 140)
            throw new ArgumentException("数字播放参考范围60至140dB；不是设备声压标定");
        const int rate = 24000;
        var count = (int)(seconds * rate);
        var mix = new double[count];
        var notes = new List<string>();
        var sourceIndex = 0;
        foreach (var speaker in project.Speakers.Where(x => x.Enabled))
        {
            double[] input;
            if (speaker.Audio is not null)
            {
                var wave = WaveCodec.Read(Convert.FromBase64String(speaker.Audio.Data));
                input = SpectralAudio.Resample(wave, rate, count);
                notes.Add($"{speaker.Name}：导入音频{speaker.Audio.Name}，短记录尾部补静音");
            }
            else
            {
                input = Noise(count, 420 + sourceIndex);
                notes.Add($"{speaker.Name}：无音频，使用固定种子模拟频带噪声");
            }
            var propagation = Acoustics.Propagation(speaker, project.Settings, point.X, point.Y);
            var target = Software.Frequencies.Select(hz =>
            {
                var band = speaker.Bands.Single(x => x.Hz == hz);
                return Math.Pow(10, (band.Level + band.Correction + speaker.Gain + propagation - playbackReference) / 20);
            }).ToArray();
            var shaped = SpectralAudio.Shape(input, rate, target);
            for (var i = 0; i < count; i++)
                mix[i] += shaped[i];
            sourceIndex++;
        }
        if (includeBackground)
        {
            var asset = project.Settings.BackgroundAudio;
            var input = asset is null ? Noise(count, 802) : SpectralAudio.Resample(WaveCodec.Read(Convert.FromBase64String(asset.Data)), rate, count);
            var target = Software.Frequencies.Select(hz =>
                Math.Pow(10, (project.Settings.Background.Single(x => x.Hz == hz).Level - playbackReference) / 20)).ToArray();
            var background = SpectralAudio.Shape(input, rate, target);
            for (var i = 0; i < count; i++)
                mix[i] += background[i];
            notes.Add(asset is null ? "背景为固定种子频带噪声模拟" : "背景采用导入音频的频谱整形");
        }
        var peak = mix.Max(x => Math.Abs(x));
        if (peak > 0.98)
            throw new ArgumentException($"预计峰值{peak:F3}超过0.98，请将数字播放参考提高至少{Math.Ceiling(20 * Math.Log10(peak / 0.98))}dB后重试；不自动归一化");
        var fade = Math.Min(1200, count / 2);
        for (var i = 0; i < fade; i++)
        {
            var factor = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            mix[i] *= factor;
            mix[count - 1 - i] *= factor;
        }
        notes.Add($"24kHz单声道、七频带整形，50ms淡入淡出；数字幅值1对应参数{playbackReference}dB，仅定义相对音量。");
        notes.Add("未模拟到达时延、双耳线索和车身；混合波形相位与非相干能量模型不等价；不得据此判定真实可听性。");
        notes.Add("设备音量、声卡及耳机未校准，改变系统音量会改变实际听感。");
        var metadata = IO.ProjectCodec.Json(new
        {
            Software.Name,
            Software.Version,
            Software.Model,
            Boundary = Software.Boundary,
            Project = project.Name,
            Source = IO.ProjectCodec.SourceSummary(project),
            Conditions = IO.ProjectCodec.Conditions(project.Settings),
            Point = new { point.Id, point.Name, point.X, point.Y },
            Seconds = seconds,
            DigitalReferenceDb = playbackReference,
            IncludeBackground = includeBackground,
            Description = notes,
            InputHash = IO.ProjectCodec.InputHash(project)
        });
        var waveBytes = WaveCodec.Write(mix, rate, metadata);
        return new Audition(waveBytes, string.Join("\n", notes), peak, false, IO.ProjectCodec.InputHash(project));
    }

    private static double[] Noise(int count, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => random.NextDouble() * 2 - 1).ToArray();
    }
}
