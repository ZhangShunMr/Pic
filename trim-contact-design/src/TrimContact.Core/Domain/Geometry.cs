namespace TrimContact.Core.Domain;

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double Scale => Math.Max(Width, Height);
}

public static class Geometry
{
    public static Bounds BoundsOf(IReadOnlyList<Vertex> points)
    {
        if (points.Count == 0)
            return new Bounds(0, 0, 400, 300);
        return new Bounds(points.Min(p => p.Xmm), points.Min(p => p.Ymm),
            points.Max(p => p.Xmm), points.Max(p => p.Ymm));
    }

    public static double SignedArea(IReadOnlyList<Vertex> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += a.Xmm * b.Ymm - b.Xmm * a.Ymm;
        }
        return area / 2;
    }

    private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;

    private static bool OnSegment(Vertex a, Vertex b, double x, double y)
    {
        const double eps = 1e-8;
        return Math.Abs(Cross(b.Xmm - a.Xmm, b.Ymm - a.Ymm, x - a.Xmm, y - a.Ymm)) < eps
            && x >= Math.Min(a.Xmm, b.Xmm) - eps && x <= Math.Max(a.Xmm, b.Xmm) + eps
            && y >= Math.Min(a.Ymm, b.Ymm) - eps && y <= Math.Max(a.Ymm, b.Ymm) + eps;
    }

    public static bool Contains(IReadOnlyList<Vertex> points, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var a = points[j];
            var b = points[i];
            if (OnSegment(a, b, x, y))
                return true;
            if ((a.Ymm > y) != (b.Ymm > y)
                && x < (b.Xmm - a.Xmm) * (y - a.Ymm) / (b.Ymm - a.Ymm) + a.Xmm)
                inside = !inside;
        }
        return inside;
    }

    private static bool Intersects(Vertex a, Vertex b, Vertex c, Vertex d)
    {
        var abC = Cross(b.Xmm - a.Xmm, b.Ymm - a.Ymm, c.Xmm - a.Xmm, c.Ymm - a.Ymm);
        var abD = Cross(b.Xmm - a.Xmm, b.Ymm - a.Ymm, d.Xmm - a.Xmm, d.Ymm - a.Ymm);
        var cdA = Cross(d.Xmm - c.Xmm, d.Ymm - c.Ymm, a.Xmm - c.Xmm, a.Ymm - c.Ymm);
        var cdB = Cross(d.Xmm - c.Xmm, d.Ymm - c.Ymm, b.Xmm - c.Xmm, b.Ymm - c.Ymm);
        if (abC * abD < 0 && cdA * cdB < 0)
            return true;
        return OnSegment(a, b, c.Xmm, c.Ymm) || OnSegment(a, b, d.Xmm, d.Ymm)
            || OnSegment(c, d, a.Xmm, a.Ymm) || OnSegment(c, d, b.Xmm, b.Ymm);
    }

    public static bool IsSimple(IReadOnlyList<Vertex> points)
    {
        if (points.Count < 3 || Math.Abs(SignedArea(points)) < 1)
            return false;
        for (int i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            if (Math.Abs(points[i].Xmm - points[next].Xmm) + Math.Abs(points[i].Ymm - points[next].Ymm) < 1e-6)
                return false;
            for (int j = i + 1; j < points.Count; j++)
            {
                if (j == next || (j + 1) % points.Count == i)
                    continue;
                if (Intersects(points[i], points[next], points[j], points[(j + 1) % points.Count]))
                    return false;
            }
        }
        return true;
    }

    public static double TriangleSpread(IReadOnlyList<Clip> clips, double scale)
    {
        double max = 0;
        for (int i = 0; i < clips.Count; i++)
            for (int j = i + 1; j < clips.Count; j++)
                for (int k = j + 1; k < clips.Count; k++)
                {
                    var a = clips[i];
                    var b = clips[j];
                    var c = clips[k];
                    max = Math.Max(max, Math.Abs(Cross(b.Xmm - a.Xmm, b.Ymm - a.Ymm,
                        c.Xmm - a.Xmm, c.Ymm - a.Ymm)) / (scale * scale));
                }
        return max;
    }
}
