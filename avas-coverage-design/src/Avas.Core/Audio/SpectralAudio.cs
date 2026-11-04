using System.Numerics;
using Avas.Core.Domain;

namespace Avas.Core.Audio;

public static class Fourier
{
    public static void Transform(Complex[] data, bool inverse = false)
    {
        var count = data.Length;
        if (count == 0 || (count & (count - 1)) != 0)
            throw new ArgumentException("FFT长度必须是2的幂");
        for (int i = 1, j = 0; i < count; i++)
        {
            var bit = count >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }
        for (var length = 2; length <= count; length <<= 1)
        {
            var angle = (inverse ? 2 : -2) * Math.PI / length;
            var root = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var offset = 0; offset < count; offset += length)
            {
                var factor = Complex.One;
                for (var i = 0; i < length / 2; i++)
                {
                    var a = data[offset + i];
                    var b = data[offset + i + length / 2] * factor;
                    data[offset + i] = a + b;
                    data[offset + i + length / 2] = a - b;
                    factor *= root;
                }
            }
        }
        if (inverse)
            for (var i = 0; i < count; i++)
                data[i] /= count;
    }

    public static int BandIndex(double frequency)
    {
        for (var i = 0; i < Software.Frequencies.Length; i++)
            if (frequency >= Software.Frequencies[i] / Math.Sqrt(2) && frequency < Software.Frequencies[i] * Math.Sqrt(2))
                return i;
        return -1;
    }
}

public sealed record DigitalSpectrum(double[] Levels, double CapturedRms, double FullRms, int Frames, string Note);

public static class SpectralAudio
{
    public static DigitalSpectrum Analyze(WaveData audio)
    {
        const int blockSize = 4096;
        var energy = new double[7];
        var frames = 0;
        var fullEnergy = audio.Samples.Sum(x => x * x) / audio.Samples.Length;
        for (var start = 0; start < audio.Samples.Length; start += blockSize)
        {
            var count = Math.Min(blockSize, audio.Samples.Length - start);
            var block = new Complex[blockSize];
            for (var i = 0; i < count; i++)
                block[i] = audio.Samples[start + i];
            Fourier.Transform(block);
            for (var bin = 1; bin <= blockSize / 2; bin++)
            {
                var index = Fourier.BandIndex((double)bin * audio.Rate / blockSize);
                if (index < 0)
                    continue;
                var factor = bin == blockSize / 2 ? 1 : 2;
                energy[index] += factor * block[bin].Magnitude * block[bin].Magnitude / blockSize;
            }
            frames += count;
        }
        for (var i = 0; i < 7; i++)
            energy[i] /= frames;
        var levels = energy.Select(x => x > 1e-16 ? 10 * Math.Log10(x) : -160).ToArray();
        return new DigitalSpectrum(levels, Math.Sqrt(energy.Sum()), Math.Sqrt(fullEnergy), frames,
            "矩形窗4096点FFT能量分带；双声道先取平均；dBFS(RMS)参考幅值1，非声压级；有限记录会有频谱泄漏。");
    }

    public static List<BandInput> RelativeBands(DigitalSpectrum spectrum, double declaredTotal)
    {
        if (!double.IsFinite(declaredTotal) || declaredTotal is < 0 or > 120)
            throw new ArgumentException("用户声明的七带合成参考声级必须为0至120dB");
        if (spectrum.CapturedRms < 1e-7)
            throw new ArgumentException("七频带内音频能量过低，不能推导相对频谱");
        var total = Analysis.Acoustics.Sum(spectrum.Levels);
        var levels = spectrum.Levels.Select(x => declaredTotal + x - total).ToArray();
        if (levels.Any(x => x < -60 || x > 140))
            throw new ArgumentException("按相对谱分配后存在超出-60至140dB的频带；请手工填写有依据的七频带，不自动截断或补底噪");
        return levels.Select((x, i) => new BandInput
        {
            Hz = Software.Frequencies[i],
            Level = x
        }).ToList();
    }

    public static double[] Resample(WaveData wave, int rate, int count)
    {
        var output = new double[count];
        for (var i = 0; i < count; i++)
        {
            var position = (double)i * wave.Rate / rate;
            if (position >= wave.Samples.Length - 1)
                break;
            var left = (int)position;
            var fraction = position - left;
            output[i] = wave.Samples[left] * (1 - fraction) + wave.Samples[left + 1] * fraction;
        }
        return output;
    }

    public static double[] Shape(double[] input, int rate, double[] targetRms)
    {
        var size = 1;
        while (size < input.Length)
            size <<= 1;
        var spectrum = new Complex[size];
        for (var i = 0; i < input.Length; i++)
            spectrum[i] = input[i];
        Fourier.Transform(spectrum);
        var energy = new double[7];
        for (var bin = 0; bin < size; bin++)
        {
            var hz = (double)Math.Min(bin, size - bin) * rate / size;
            var index = Fourier.BandIndex(hz);
            if (index >= 0)
                energy[index] += spectrum[bin].Magnitude * spectrum[bin].Magnitude / size / input.Length;
        }
        for (var bin = 0; bin < size; bin++)
        {
            var hz = (double)Math.Min(bin, size - bin) * rate / size;
            var index = Fourier.BandIndex(hz);
            var gain = index >= 0 && energy[index] > 1e-14 ? targetRms[index] / Math.Sqrt(energy[index]) : 0;
            spectrum[bin] *= gain;
        }
        Fourier.Transform(spectrum, true);
        return spectrum.Take(input.Length).Select(x => x.Real).ToArray();
    }
}
