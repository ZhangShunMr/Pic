using TrimContact.Core.Domain;
using TrimContact.Core.IO;

namespace TrimContact.Core.Analysis;

public sealed record BalanceRow(string Variant, string Load, double PhaseDegrees,
    double AppliedNormalN, double ReactionNormalN, double AppliedShearN, double ReactionShearN,
    double NormalErrorN, double ShearErrorN, double MomentXErrorNmm, double MomentYErrorNmm,
    double ElasticEnergyNmm);

public static class BalanceAudit
{
    public static IReadOnlyList<BalanceRow> Evaluate(DesignProject project, AnalysisReport report)
    {
        ResultExport.RequireCurrent(project, report);
        var rows = new List<BalanceRow>();
        var scale = Geometry.BoundsOf(project.Panel.Outline).Scale;
        foreach (var result in report.Variants)
        {
            var variant = project.Variants.Single(v => v.Id == result.VariantId);
            var clips = variant.Clips.ToDictionary(c => c.Id);
            var contacts = variant.Contacts.ToDictionary(c => c.Id);
            foreach (var trace in result.NominalTraces)
            {
                foreach (var state in trace.Samples)
                {
                    double normal = 0;
                    double shear = 0;
                    double momentX = 0;
                    double momentY = 0;
                    double energy = 0;
                    foreach (var point in state.Clips)
                    {
                        var clip = clips[point.Id];
                        var dx = clip.Xmm - project.Panel.CenterXmm;
                        var dy = clip.Ymm - project.Panel.CenterYmm;
                        var displacement = state.Displacement.Z + dx / scale * state.Displacement.X
                            + dy / scale * state.Displacement.Y;
                        normal += point.NormalN;
                        shear += point.ShearN;
                        momentX += point.NormalN * dy;
                        momentY -= point.NormalN * dx;
                        energy += 0.5 * clip.NormalStiffness * displacement * displacement;
                        energy += 0.5 * clip.TangentialStiffness * state.TangentialMm * state.TangentialMm;
                    }
                    foreach (var point in state.Contacts)
                    {
                        var contact = contacts[point.Id];
                        normal += point.NormalN;
                        shear += point.ShearN;
                        momentX += point.NormalN * (contact.Ymm - project.Panel.CenterYmm);
                        momentY -= point.NormalN * (contact.Xmm - project.Panel.CenterXmm);
                        energy += 0.5 * contact.NormalStiffness * point.CompressionMm * point.CompressionMm;
                        energy += 0.5 * point.ShearN * point.ShearN / contact.TangentialStiffness;
                    }
                    rows.Add(new(result.Name, trace.LoadName, state.PhaseDegrees, state.NormalForceN,
                        normal, state.TangentialForceN, shear, normal - state.NormalForceN,
                        shear - state.TangentialForceN, momentX, momentY, energy));
                }
            }
        }
        return rows;
    }
}
