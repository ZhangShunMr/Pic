namespace TrimContact.Core.Analysis;

public readonly record struct Vector3(double Z, double X, double Y)
{
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.Z + b.Z, a.X + b.X, a.Y + b.Y);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.Z - b.Z, a.X - b.X, a.Y - b.Y);
    public static Vector3 operator *(double s, Vector3 v) => new(s * v.Z, s * v.X, s * v.Y);
    public double Dot(Vector3 v) => Z * v.Z + X * v.X + Y * v.Y;
    public double Norm => Math.Sqrt(Dot(this));
}

public sealed class Matrix3
{
    private readonly double[,] values = new double[3, 3];

    public void AddSpring(double stiffness, Vector3 b)
    {
        double[] v = [b.Z, b.X, b.Y];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                values[i, j] += stiffness * v[i] * v[j];
    }

    public Matrix3 Copy()
    {
        var copy = new Matrix3();
        Array.Copy(values, copy.values, values.Length);
        return copy;
    }

    public Vector3 Multiply(Vector3 q) => new(
        values[0, 0] * q.Z + values[0, 1] * q.X + values[0, 2] * q.Y,
        values[1, 0] * q.Z + values[1, 1] * q.X + values[1, 2] * q.Y,
        values[2, 0] * q.Z + values[2, 1] * q.X + values[2, 2] * q.Y);

    public Vector3 Solve(Vector3 rhs)
    {
        var a = new double[3, 4];
        double[] b = [rhs.Z, rhs.X, rhs.Y];
        var scale = 0.0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                a[i, j] = values[i, j];
                scale = Math.Max(scale, Math.Abs(a[i, j]));
            }
            a[i, 3] = b[i];
        }
        for (int col = 0; col < 3; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < 3; row++)
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                    pivot = row;
            if (Math.Abs(a[pivot, col]) < Math.Max(1e-12, scale * 1e-10))
                throw new InvalidDataException("卡扣布置或刚度导致矩阵近奇异，请分散卡扣并检查刚度比");
            for (int j = col; j < 4; j++)
                (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);
            var diagonal = a[col, col];
            for (int j = col; j < 4; j++)
                a[col, j] /= diagonal;
            for (int row = 0; row < 3; row++)
            {
                if (row == col)
                    continue;
                var factor = a[row, col];
                for (int j = col; j < 4; j++)
                    a[row, j] -= factor * a[col, j];
            }
        }
        var result = new Vector3(a[0, 3], a[1, 3], a[2, 3]);
        if (!double.IsFinite(result.Norm))
            throw new InvalidDataException("求解结果非有限数值");
        return result;
    }
}
