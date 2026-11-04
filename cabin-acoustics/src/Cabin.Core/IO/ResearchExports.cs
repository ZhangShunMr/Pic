using System.Globalization;
using System.Text;
using Cabin.Core.Analysis;
using Cabin.Core.Domain;

namespace Cabin.Core.IO;

public static class ResearchExports
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Responses(Project project, AnalysisResult result)
    {
        var output = Start(project, "seat_id,seat,frequency_hz,real_relative,imag_relative,magnitude_relative,phase_deg,relative_db,reference_relative");
        foreach (var curve in result.Curves)
        {
            foreach (var point in curve.Points)
            {
                output.AppendLine(PointCsv.Join(curve.Id.ToString(), curve.Name,
                    N(point.Frequency), N(point.Real), N(point.Imaginary), N(point.Magnitude),
                    N(point.Phase), N(point.RelativeDb(result.Reference)), N(result.Reference)));
            }
        }
        return output.ToString();
    }

    public static string ModeLedger(Project project, AnalysisResult result)
    {
        var output = Start(project, "nx,ny,nz,frequency_hz,kind,degeneracy");
        foreach (var mode in result.Modes)
        {
            output.AppendLine(PointCsv.Join(mode.Nx.ToString(Invariant), mode.Ny.ToString(Invariant),
                mode.Nz.ToString(Invariant), N(mode.Frequency), mode.Kind, mode.Degeneracy.ToString(Invariant)));
        }
        return output.ToString();
    }

    public static string Field(Project project, FieldResult field, double frequency, Mode? mode)
    {
        var output = Start(project, "u_fraction,v_fraction,x_m,y_m,z_m,value,display_normalized");
        output.Insert(0, $"# plane={field.Plane}; slice_fraction={N(field.Slice)}; frequency_hz={N(frequency)}; mode={mode?.Key}; quantity={field.Quantity}; scale={N(field.Maximum)}\n");
        foreach (var cell in field.Cells)
        {
            var point = Engine.PlanePoint(project.Model, field.Plane, cell.U, cell.V, field.Slice);
            output.AppendLine(PointCsv.Join(N(cell.U), N(cell.V), N(point.X), N(point.Y), N(point.Z),
                N(cell.Value), N(field.Maximum > 1e-15 ? cell.Value / field.Maximum : 0)));
        }
        return output.ToString();
    }

    public static string Scan(Project project, ScanResult result)
    {
        var output = Start(project, "parameter_value,peak_relative,peak_frequency_hz,rms_relative,first_mode_hz,error");
        output.Insert(0, $"# parameter={result.Settings.Parameter}; unit={result.Unit}; seat_id={result.Settings.SeatId}; points={result.Settings.Count}\n");
        foreach (var row in result.Rows)
        {
            output.AppendLine(PointCsv.Join(N(row.Parameter), row.Error.Length == 0 ? N(row.PeakMagnitude) : "",
                row.Error.Length == 0 ? N(row.PeakFrequency) : "", row.Error.Length == 0 ? N(row.Rms) : "",
                row.Error.Length == 0 ? N(row.FirstMode) : "", row.Error));
        }
        return output.ToString();
    }

    public static string Differences(Project project, ComparisonResult result)
    {
        var output = Start(project, "seat,baseline_peak_relative,current_peak_relative,peak_ratio_db,baseline_peak_hz,current_peak_hz,rms_ratio_db");
        foreach (var warning in result.Warnings)
            output.AppendLine("# " + OneLine(warning));
        foreach (var row in result.Seats)
        {
            output.AppendLine(PointCsv.Join(row.Name, N(row.BaselinePeak), N(row.CurrentPeak),
                N(row.PeakRatioDb), N(row.BaselineFrequency), N(row.CurrentFrequency), N(row.RmsRatioDb)));
        }
        return output.ToString();
    }

    public static string Report(Project project, AnalysisResult result, ComparisonResult? comparison,
        ScanResult? scan, TruncationStudy? truncation)
    {
        var output = new StringBuilder();
        output.AppendLine(Software.Name + " V" + Software.Version);
        output.AppendLine("离线研究报告 / " + result.Computed.ToString("O", Invariant));
        output.AppendLine(Software.Scope);
        output.AppendLine("以下内容为理想模型计算，不能据此认定实车降噪效果。");
        output.AppendLine();
        Section(output, "项目与来源");
        Pair(output, "名称", project.Name);
        Pair(output, "标识", project.Id.ToString());
        Pair(output, "修订", project.Revision);
        Pair(output, "负责人", project.Owner);
        Pair(output, "来源", project.DataSource);
        Pair(output, "模拟项目", project.IsDemo ? "是 / 模拟数据，非实测" : "否 / 人工理想化输入，未校准");
        Pair(output, "备注", project.Notes);
        Section(output, "模型与计算口径");
        Pair(output, "尺寸 Lx Ly Lz / m", $"{N(project.Model.Length)}, {N(project.Model.Width)}, {N(project.Model.Height)}");
        Pair(output, "体积 / m³", N(project.Model.Volume));
        Pair(output, "声速 / m/s", N(project.Model.SoundSpeed));
        Pair(output, "阻尼比", N(project.Model.Damping));
        Pair(output, "分析频带 / Hz", $"{N(project.Model.MinimumFrequency)} – {N(project.Model.MaximumFrequency)}");
        Pair(output, "频率步长 / Hz", N(project.Model.FrequencyStep));
        Pair(output, "截断频率 / Hz", N(project.Model.Cutoff));
        output.AppendLine("fn = c/2 sqrt((nx/Lx)^2+(ny/Ly)^2+(nz/Lz)^2)，排除000。");
        output.AppendLine("phi = cos(nx*pi*x/Lx)cos(ny*pi*y/Ly)cos(nz*pi*z/Lz)。");
        output.AppendLine("响应 = Σ 2^d/V * phi(r) * Σ A exp(i phase) phi(source) * 100^2 / (fn^2-f^2+i2ζfnf)。");
        output.AppendLine("V以1m³为参考；结果无量纲，非Pa，非dB SPL。所有曲线共用全频带最大值为0dB参考。");
        output.AppendLine("剖面图响应按自身最大值归一化，各幅图颜色不能直接比较绝对大小。");
        output.AppendLine("RMS为离散频率样点幅值均方根，不是时间域声压有效值；峰值为离散样点峰值。");
        Section(output, "声源与耳点");
        output.Append(PointCsv.Export(project));
        Section(output, "座位摘要");
        foreach (var curve in result.Curves)
        {
            Pair(output, curve.Name,
                $"峰值 {N(curve.Peak.Magnitude)} @ {N(curve.Peak.Frequency)} Hz；采样RMS {N(curve.Rms)}；峰值相对 {N(curve.Peak.RelativeDb(result.Reference))} dB");
        }
        Pair(output, "公共参考幅值", N(result.Reference));
        Pair(output, "频率样点", result.Frequencies.Count.ToString(Invariant));
        Pair(output, "非零阶模态数", result.Modes.Count.ToString(Invariant));
        Section(output, "数值检查提示");
        foreach (var advice in Diagnostics.Advice(project, result))
            output.AppendLine("• " + advice);
        if (comparison is not null)
        {
            Section(output, "基准比较");
            output.Append(Differences(project, comparison));
        }
        if (scan is not null)
        {
            Section(output, "参数扫描");
            output.Append(Scan(project, scan));
        }
        if (truncation is not null)
        {
            Section(output, "截断敏感性");
            output.AppendLine($"{N(truncation.OriginalCutoff)} → {N(truncation.ExtendedCutoff)} Hz，模态 {truncation.OriginalModes} → {truncation.ExtendedModes}");
            foreach (var row in truncation.Rows)
                Pair(output, row.Seat, $"采样RMS {N(row.OriginalRms)} → {N(row.ExtendedRms)}；比值 {N(row.RatioDb)} dB");
        }
        Section(output, "工程复核");
        foreach (var review in project.Reviews)
        {
            Pair(output, review.Target, review.Action);
            Pair(output, "负责人 / 状态", review.Owner + " / " + review.Result);
        }
        Section(output, "已知限制与追溯");
        output.AppendLine("矩形刚壁、常量声速、所有非零模态共用阻尼；未包含柔性板、开口、内饰吸声、流动及真实几何。");
        output.AppendLine("截断及步长会影响数值结果；近零比值采用-120dB显示下限，不能用作物理噪声底。");
        output.AppendLine("扫描以绝对坐标保持点位，几何变小后越界候选不会参与排序。");
        foreach (var entry in project.Journal)
            output.AppendLine($"{entry.Time:O} | {entry.Action} | {entry.Detail}");
        return output.ToString();
    }

    private static StringBuilder Start(Project project, string header)
    {
        var output = new StringBuilder();
        output.AppendLine("# " + Software.Name + " V" + Software.Version);
        output.AppendLine("# " + Software.Scope);
        output.AppendLine("# project=" + OneLine(project.Name) + "; id=" + project.Id + "; revision=" + OneLine(project.Revision));
        output.AppendLine("# source=" + OneLine(project.DataSource) + "; demo=" + project.IsDemo);
        output.AppendLine("# c_m_s=" + N(project.Model.SoundSpeed) + "; damping=" + N(project.Model.Damping) + "; cutoff_hz=" + N(project.Model.Cutoff));
        output.AppendLine(header);
        return output;
    }

    private static void Pair(StringBuilder output, string key, string value)
    {
        output.AppendLine(key + "：" + value);
    }

    private static void Section(StringBuilder output, string title)
    {
        output.AppendLine();
        output.AppendLine("=== " + title + " ===");
    }

    private static string OneLine(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ");
    }

    private static string N(double value)
    {
        return double.IsFinite(value) ? value.ToString("G12", Invariant) : "不可计算（近零基准）";
    }
}
