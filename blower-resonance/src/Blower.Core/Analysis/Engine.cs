using Blower.Core.Domain;

namespace Blower.Core.Analysis;

public sealed record SensitiveBand(
    string ModeId,
    string Name,
    double CenterHz,
    double HalfWidthHz,
    double LowerHz,
    double UpperHz,
    string Basis);

public sealed record Crossing(
    string ModeId,
    string OrderId,
    string Mode,
    string Order,
    double EffectiveOrder,
    double ExactRpm,
    double LowerRpm,
    double UpperRpm,
    bool InRange);

public sealed record Contact(
    string PlanId,
    string GearId,
    string OrderId,
    string ModeId,
    string Plan,
    string Gear,
    string Order,
    string Mode,
    double Rpm,
    double OrderNumber,
    double FrequencyHz,
    double LowerHz,
    double UpperHz,
    double ModeHz,
    double HalfWidthHz,
    double ClearanceHz,
    bool Near)
{
    public string Key => string.Join("/", PlanId, GearId, OrderId, ModeId);
    public string Status => Near ? "潜在接近" : "范围外";
}

public sealed record GearSummary(
    string GearId,
    string Name,
    double Rpm,
    int Contacts,
    double? MinimumClearanceHz);

public sealed record PlanSummary(
    string PlanId,
    string Name,
    int GearCount,
    int NearGearCount,
    int ContactCount,
    double? MinimumClearanceHz,
    IReadOnlyList<GearSummary> Gears);

public sealed class AnalysisResult
{
    public string InputHash { get; init; } = "";
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
    public List<SensitiveBand> Bands { get; init; } = [];
    public List<Crossing> Crossings { get; init; } = [];
    public List<Contact> Contacts { get; init; } = [];
    public List<PlanSummary> Summaries { get; init; } = [];
}

public static class Engine
{
    public static double Frequency(double rpm, double order)
    {
        return rpm * order / 60.0;
    }

    public static SensitiveBand Band(Mode mode, DesignSettings settings)
    {
        var absolute = settings.AbsoluteBandHz;
        var relative = mode.FrequencyHz * settings.RelativeBandPercent / 100.0;
        var damping = settings.UseDamping ? (mode.DampingRatio ?? 0) * mode.FrequencyHz : 0;
        var width = Math.Max(Math.Max(absolute, relative), Math.Max(damping, mode.ExtraBandHz));
        var basis = $"max(绝对{absolute:0.###},百分比{relative:0.###},阻尼近似{damping:0.###},附加{mode.ExtraBandHz:0.###}) Hz";
        return new(mode.Id, mode.Name, mode.FrequencyHz, width,
            Math.Max(0, mode.FrequencyHz - width), mode.FrequencyHz + width, basis);
    }

    public static Contact Evaluate(Project project, SpeedPlan plan, Gear gear, Excitation order, Mode mode)
    {
        var q = order.EffectiveOrder(project.Settings.Blades);
        var band = Band(mode, project.Settings);
        var center = Frequency(gear.Rpm, q);
        var tolerance = Frequency(project.Settings.SpeedToleranceRpm, q);
        var lower = Math.Max(0, center - tolerance);
        var upper = center + tolerance;
        var clearance = Math.Max(band.LowerHz - upper, lower - band.UpperHz);
        return new(plan.Id, gear.Id, order.Id, mode.Id,
            plan.Name, gear.Name, order.Name, mode.Name,
            gear.Rpm, q, center, lower, upper, mode.FrequencyHz,
            band.HalfWidthHz, clearance, clearance <= 1e-8);
    }

    public static AnalysisResult Run(Project project)
    {
        Validation.RequireValid(project, true);
        var modes = project.Modes.Where(x => x.Enabled).ToList();
        var orders = project.Orders.Where(x => x.Enabled).ToList();
        var bands = modes.Select(x => Band(x, project.Settings)).ToList();
        var crossings = new List<Crossing>();
        foreach (var order in orders)
        {
            var q = order.EffectiveOrder(project.Settings.Blades);
            foreach (var band in bands)
            {
                var exact = 60 * band.CenterHz / q;
                var lower = 60 * band.LowerHz / q - project.Settings.SpeedToleranceRpm;
                var upper = 60 * band.UpperHz / q + project.Settings.SpeedToleranceRpm;
                crossings.Add(new(band.ModeId, order.Id, band.Name, order.Name, q,
                    exact, Math.Max(0, lower), upper,
                    upper >= project.Settings.MinRpm && lower <= project.Settings.MaxRpm));
            }
        }
        var contacts = new List<Contact>();
        var summaries = new List<PlanSummary>();
        foreach (var plan in project.Plans)
        {
            var gearSummaries = new List<GearSummary>();
            foreach (var gear in plan.Gears)
            {
                var current = new List<Contact>();
                foreach (var order in orders)
                {
                    foreach (var mode in modes)
                    {
                        current.Add(Evaluate(project, plan, gear, order, mode));
                    }
                }
                contacts.AddRange(current);
                gearSummaries.Add(new(gear.Id, gear.Name, gear.Rpm,
                    current.Count(x => x.Near), current.Count == 0 ? null : current.Min(x => x.ClearanceHz)));
            }
            var planContacts = contacts.Where(x => x.PlanId == plan.Id).ToList();
            summaries.Add(new(plan.Id, plan.Name, plan.Gears.Count,
                gearSummaries.Count(x => x.Contacts > 0), planContacts.Count(x => x.Near),
                planContacts.Count == 0 ? null : planContacts.Min(x => x.ClearanceHz), gearSummaries));
        }
        return new()
        {
            InputHash = IO.ProjectCodec.InputHash(project),
            Bands = bands,
            Crossings = crossings,
            Contacts = contacts,
            Summaries = summaries
        };
    }
}
