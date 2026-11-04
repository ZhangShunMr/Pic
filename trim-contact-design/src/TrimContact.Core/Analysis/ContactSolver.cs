using TrimContact.Core.Domain;

namespace TrimContact.Core.Analysis;

public sealed record PointState(Guid Id, string Name, double CompressionMm, double NormalN,
    double ShearN, double SlipMm, double Utilization);

public sealed record PhaseState(double PhaseDegrees, double NormalForceN, double TangentialForceN,
    Vector3 Displacement, double TangentialMm, double ResidualN,
    IReadOnlyList<PointState> Contacts, IReadOnlyList<PointState> Clips);

public sealed class ContactSolver
{
    private readonly DesignVariant variant;
    private readonly Panel panel;
    private readonly Matrix3 clipMatrix = new();
    private readonly Vector3[] clipBasis;
    private readonly Vector3[] contactBasis;

    public ContactSolver(Panel panel, DesignVariant variant)
    {
        this.panel = panel;
        this.variant = variant;
        var scale = Geometry.BoundsOf(panel.Outline).Scale;
        Vector3 Basis(double x, double y) => new(1, (x - panel.CenterXmm) / scale, (y - panel.CenterYmm) / scale);
        clipBasis = variant.Clips.Select(c => Basis(c.Xmm, c.Ymm)).ToArray();
        contactBasis = variant.Contacts.Select(c => Basis(c.Xmm, c.Ymm)).ToArray();
        for (int i = 0; i < variant.Clips.Count; i++)
            clipMatrix.AddSpring(variant.Clips[i].NormalStiffness, clipBasis[i]);
        clipMatrix.Solve(new Vector3(1, 0, 0));
    }

    private double Energy(Vector3 q, Vector3 force, IReadOnlyList<double> offsets)
    {
        var value = 0.5 * q.Dot(clipMatrix.Multiply(q)) - force.Dot(q);
        for (int i = 0; i < variant.Contacts.Count; i++)
        {
            var compression = Math.Max(0, offsets[i] + contactBasis[i].Dot(q));
            value += 0.5 * variant.Contacts[i].NormalStiffness * compression * compression;
        }
        return value;
    }

    private (Vector3 Q, double Residual) SolveNormal(double forceN, IReadOnlyList<double> tolerance)
    {
        var force = new Vector3(forceN, 0, 0);
        var offsets = variant.Contacts.Select((c, i) => c.PreloadMm - c.GapMm - tolerance[i]).ToArray();
        var q = clipMatrix.Solve(force);
        var threshold = 1e-8 * Math.Max(1, Math.Abs(forceN));
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var gradient = clipMatrix.Multiply(q) - force;
            var hessian = clipMatrix.Copy();
            for (int i = 0; i < offsets.Length; i++)
            {
                var compression = offsets[i] + contactBasis[i].Dot(q);
                if (compression <= 0)
                    continue;
                var stiffness = variant.Contacts[i].NormalStiffness;
                gradient += stiffness * compression * contactBasis[i];
                hessian.AddSpring(stiffness, contactBasis[i]);
            }
            if (gradient.Norm <= threshold)
                return (q, gradient.Norm);
            var step = hessian.Solve(-1 * gradient);
            var oldEnergy = Energy(q, force, offsets);
            var slope = gradient.Dot(step);
            var alpha = 1.0;
            while (alpha > 1e-10)
            {
                var candidate = q + alpha * step;
                if (Energy(candidate, force, offsets) <= oldEnergy + 1e-4 * alpha * slope + 1e-12)
                {
                    q = candidate;
                    break;
                }
                alpha /= 2;
            }
            if (alpha <= 1e-10)
                throw new InvalidDataException("法向求解线搜索失败，请检查过大的刚度差和预紧量");
        }
        throw new InvalidDataException("法向求解100次未收敛，结果取消");
    }

    public PhaseState Solve(double phaseDegrees, double normalG, double tangentialG, IReadOnlyList<double> tolerance)
    {
        if (tolerance.Count != variant.Contacts.Count)
            throw new ArgumentException("公差维度与接触点不一致");
        var normalForce = panel.MassKg * Product.Gravity * normalG;
        var shearForce = panel.MassKg * Product.Gravity * tangentialG;
        var (q, normalResidual) = SolveNormal(normalForce, tolerance);
        var compression = variant.Contacts.Select((c, i) => Math.Max(0,
            c.PreloadMm - c.GapMm - tolerance[i] + contactBasis[i].Dot(q))).ToArray();
        var normal = variant.Contacts.Select((c, i) => c.NormalStiffness * compression[i]).ToArray();
        var clipKt = variant.Clips.Sum(c => c.TangentialStiffness);
        double ShearAt(double u)
        {
            double force = clipKt * u;
            for (int i = 0; i < variant.Contacts.Count; i++)
                force += Math.Min(variant.Contacts[i].TangentialStiffness * u, variant.Contacts[i].Friction * normal[i]);
            return force;
        }
        double low = 0;
        var high = Math.Abs(shearForce) / clipKt;
        for (int iteration = 0; iteration < 70; iteration++)
        {
            var middle = (low + high) / 2;
            if (ShearAt(middle) < Math.Abs(shearForce))
                low = middle;
            else
                high = middle;
        }
        var magnitude = (low + high) / 2;
        var sign = Math.Sign(shearForce);
        var contacts = new List<PointState>();
        for (int i = 0; i < variant.Contacts.Count; i++)
        {
            var c = variant.Contacts[i];
            var cap = c.Friction * normal[i];
            var shear = Math.Min(c.TangentialStiffness * magnitude, cap);
            var slip = compression[i] > Product.ContactEpsilon
                ? Math.Max(0, magnitude - cap / c.TangentialStiffness) : 0;
            contacts.Add(new(c.Id, c.Name, compression[i], normal[i], sign * shear,
                slip, compression[i] / c.CompressionLimitMm));
        }
        var clips = variant.Clips.Select((c, i) =>
        {
            var n = c.NormalStiffness * clipBasis[i].Dot(q);
            var shear = c.TangentialStiffness * magnitude;
            return new PointState(c.Id, c.Name, 0, n, sign * shear, 0,
                Math.Sqrt(n * n + shear * shear) / c.ForceLimitN);
        }).ToArray();
        var residual = Math.Max(normalResidual, Math.Abs(ShearAt(magnitude) - Math.Abs(shearForce)));
        return new PhaseState(phaseDegrees, normalForce, shearForce, q, sign * magnitude,
            residual, contacts, clips);
    }
}
