using System.Numerics;
using Cabin.Core.Domain;

namespace Cabin.Core.Analysis;

public sealed record Mode(int Nx, int Ny, int Nz, double Frequency, int Degeneracy = 1)
{
    public string Key => $"{Nx},{Ny},{Nz}";
    public int Dimension => (Nx > 0 ? 1 : 0) + (Ny > 0 ? 1 : 0) + (Nz > 0 ? 1 : 0);
    public string Kind => Dimension switch { 1 => "轴向", 2 => "切向", _ => "斜向" };
    public double NormFactor => Math.Pow(2, Dimension);
}

public static class Modes
{
    public static List<Mode> Enumerate(Cavity model)
    {
        var issues = new List<Issue>();
        Validation.Model(model, issues);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("；", issues.Select(x => x.Path + "：" + x.Message)));
        var result = new List<Mode>();
        var maxX = (int)Math.Floor(2 * model.Cutoff * model.Length / model.SoundSpeed);
        var maxY = (int)Math.Floor(2 * model.Cutoff * model.Width / model.SoundSpeed);
        var maxZ = (int)Math.Floor(2 * model.Cutoff * model.Height / model.SoundSpeed);
        for (var nx = 0; nx <= maxX; nx++)
        {
            for (var ny = 0; ny <= maxY; ny++)
            {
                for (var nz = 0; nz <= maxZ; nz++)
                {
                    if (nx + ny + nz == 0)
                        continue;
                    var f = Frequency(model, nx, ny, nz);
                    if (f <= model.Cutoff + 1e-9)
                        result.Add(new(nx, ny, nz, f));
                    if (result.Count > 10000)
                        throw new ArgumentException("模态数量超过10000，请降低截断频率");
                }
            }
        }
        result = result.OrderBy(x => x.Frequency).ThenBy(x => x.Nx).ThenBy(x => x.Ny).ThenBy(x => x.Nz).ToList();
        var start = 0;
        while (start < result.Count)
        {
            var end = start + 1;
            var tolerance = Math.Max(1e-8, result[start].Frequency * 1e-8);
            while (end < result.Count && result[end].Frequency - result[start].Frequency <= tolerance)
                end++;
            for (var i = start; i < end; i++)
                result[i] = result[i] with { Degeneracy = end - start };
            start = end;
        }
        return result;
    }

    public static double Frequency(Cavity model, int nx, int ny, int nz)
    {
        return model.SoundSpeed / 2 * Math.Sqrt(
            Math.Pow(nx / model.Length, 2) +
            Math.Pow(ny / model.Width, 2) +
            Math.Pow(nz / model.Height, 2));
    }

    public static double Shape(Mode mode, Cavity model, Position point)
    {
        return Math.Cos(mode.Nx * Math.PI * point.X / model.Length) *
            Math.Cos(mode.Ny * Math.PI * point.Y / model.Width) *
            Math.Cos(mode.Nz * Math.PI * point.Z / model.Height);
    }

    public static Complex SourceCoupling(Mode mode, Cavity model, IEnumerable<Emitter> sources)
    {
        var sum = Complex.Zero;
        foreach (var source in sources.Where(x => x.Enabled))
        {
            var phase = source.PhaseDegrees * Math.PI / 180;
            sum += Complex.FromPolarCoordinates(source.Amplitude, phase) * Shape(mode, model, source.Point);
        }
        return sum;
    }

    public static Complex Term(Mode mode, Cavity model, Complex coupling, Position point, double frequency)
    {
        var fn = mode.Frequency;
        var denominator = new Complex(fn * fn - frequency * frequency, 2 * model.Damping * fn * frequency);
        return mode.NormFactor * Software.ReferenceVolume / model.Volume * Shape(mode, model, point) * coupling *
            (Software.ReferenceFrequency * Software.ReferenceFrequency) / denominator;
    }
}
