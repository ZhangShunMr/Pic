using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.IO;

public sealed record ExportDocument(string Filename, string ContentType, byte[] Bytes);

public sealed class ResultExport
{
    public ExportDocument Project(AcousticProject project)
    {
        return new ExportDocument(SafeName(project.Name) + Software.Extension,
            "application/json; charset=utf-8", new ProjectCodec().Encode(project));
    }

    public ExportDocument Measurements(AcousticProject project)
    {
        var rows = new List<IEnumerable<object?>>
        {
            new object?[] { "trial_id", "frequency_hz", "level_db" }
        };
        foreach (var trial in project.Trials)
        {
            foreach (var band in trial.Bands.OrderBy(band => band.Frequency))
            {
                rows.Add(new object?[] { trial.Id, band.Frequency, band.Level });
            }
        }

        return Csv(project, ".measurements.csv", rows);
    }

    public ExportDocument Results(AcousticProject project, AnalysisReport report)
    {
        var rows = new List<IEnumerable<object?>>
        {
            new object?[]
            {
                "project_id", "project", "revision", "software_version", "schema_version",
                "pair_id", "purpose", "baseline_id", "target_id", "baseline", "target",
                "source_kind", "baseline_source", "target_source", "baseline_condition_id",
                "target_condition_id", "frequency_hz", "baseline_db", "target_db",
                "measured_baseline_minus_target_db", "status", "blocking_reasons", "warnings",
                "level_reference", "settings_json", "input_hash"
            }
        };
        var trials = project.Trials.ToDictionary(trial => trial.Id);
        foreach (var pair in report.Pairs)
        {
            var bands = pair.Bands.Count > 0 ? pair.Bands : [new BandDifference { Status = pair.Status }];
            foreach (var band in bands)
            {
                rows.Add(new object?[]
                {
                    project.Id, project.Name, project.Revision, Software.Version, project.SchemaVersion,
                    pair.PairId, pair.Purpose, pair.BaselineId, pair.TargetId, pair.BaselineName, pair.TargetName,
                    pair.Source, pair.BaselineSource, pair.TargetSource,
                    trials[pair.BaselineId].ConditionId, trials[pair.TargetId].ConditionId,
                    band.Frequency == 0 ? null : band.Frequency, band.Baseline, band.Target, band.Difference,
                    band.Status, string.Join("；", pair.BlockingReasons), string.Join("；", pair.Warnings),
                    Software.LevelUnit, JsonSerializer.Serialize(project.Settings, ProjectCodec.Options),
                    ProjectCodec.Fingerprint(project)
                });
            }
        }

        return Csv(project, ".results.csv", rows);
    }

    public ExportDocument AnalysisJson(AcousticProject project, AnalysisReport report)
    {
        var content = new
        {
            softwareName = Software.Name,
            softwareVersion = Software.Version,
            unit = Software.LevelUnit,
            differenceDefinition = "baseline-target",
            inputHash = ProjectCodec.Fingerprint(project),
            limitation = Software.Limitation,
            project,
            report
        };
        return new ExportDocument(SafeName(project.Name) + ".analysis.json", "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(content, ProjectCodec.Options));
    }

    public ExportDocument Report(AcousticProject project, AnalysisReport report)
    {
        return new ExportDocument(SafeName(project.Name) + ".report.html", "text/html; charset=utf-8",
            Encoding.UTF8.GetBytes(new HtmlReport().Build(project, report)));
    }

    public static string SafeName(string value)
    {
        var invalid = "<>:\"/\\|?*";
        var name = new string(value.Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character).ToArray());
        name = name.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(name) ? "声学项目" : name[..Math.Min(name.Length, 100)];
    }

    private static ExportDocument Csv(AcousticProject project, string extension, IEnumerable<IEnumerable<object?>> rows)
    {
        var builder = new StringBuilder("\uFEFF");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(Cell)));
        }

        return new ExportDocument(SafeName(project.Name) + extension, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string Cell(object? value)
    {
        if (value is null)
        {
            return "";
        }

        var text = value is IFormattable formatted
            ? formatted.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? "";
        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

public sealed class HtmlReport
{
    private readonly StringBuilder output = new();

    public string Build(AcousticProject project, AnalysisReport report)
    {
        output.Clear();
        output.AppendLine("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        output.Append("<title>").Append(E(project.Name)).AppendLine(" · 声学分析报告</title>");
        output.AppendLine("<style>body{font:14px/1.7 'Noto Sans CJK SC','Microsoft YaHei',sans-serif;color:#17344b;max-width:1160px;margin:32px auto;padding:0 22px}h1{font-size:28px}h2{border-bottom:2px solid #168d88;margin-top:34px;padding-bottom:6px}h3{font-size:16px}table{border-collapse:collapse;width:100%;font-size:12px;margin:12px 0}th,td{border:1px solid #cad8e0;padding:7px;text-align:left;overflow-wrap:anywhere}th{background:#edf3f7}.notice{padding:14px;border-left:4px solid #c39235;background:#fff4db}.error{color:#a53737}svg{max-width:100%;background:#f6fafb}small{color:#627d8e}@media print{body{margin:0;font-size:11px}h2,h3{break-after:avoid}tr{break-inside:avoid}svg{max-height:240px}}</style></head><body>");
        output.Append("<h1>").Append(E(project.Name)).AppendLine("</h1>");
        Paragraph(Software.Name + " " + Software.DisplayVersion + " · " + Software.Time(report.CalculatedAt));
        Notice(Software.Limitation);
        Heading("1 项目信息与输入身份");
        Table(["字段", "内容"],
        [
            ["项目标识", project.Id],
            ["修订号", project.Revision],
            ["负责人（用户填写）", project.Owner],
            ["车辆项目", project.VehicleProgramme],
            ["试验目标", project.Objective],
            ["项目说明", project.Description],
            ["状态", project.Status],
            ["软件/格式版本", Software.Version + " / " + project.SchemaVersion],
            ["输入SHA-256", ProjectCodec.Fingerprint(project)],
            ["来源分类", project.SourceSummary()],
            ["有效/阻断配对", report.ValidPairCount + " / " + report.BlockedPairCount]
        ]);
        Heading("2 封堵位置与工艺");
        Table(["位置", "区域", "材料", "覆盖范围", "封堵步骤", "恢复步骤", "参考编号", "备注"],
            project.Locations.Select(location => new object?[]
            {
                location.Name, location.Zone, location.Material, location.Extent,
                location.Procedure, location.RecoveryProcedure, location.Reference, location.Notes
            }));
        Heading("3 工况和测量口径");
        foreach (var condition in project.Conditions)
        {
            Subheading(condition.Name);
            var rows = condition.TextFields().Select(field => new object?[] { field.Label, field.Value }).ToList();
            rows.AddRange(new object?[][]
            {
                ["标识", condition.Id],
                ["车速km/h", condition.Speed],
                ["转速r/min", condition.Rpm],
                ["温度°C", condition.Temperature],
                ["积分时长s", condition.Duration],
                ["采样率Hz", condition.SampleRate],
                ["时钟/同步", condition.Synchronization],
                ["用户确认频带互不重叠", condition.NonOverlappingBands],
                ["备注", condition.Notes]
            });
            Table(["项目", "值"], rows);
        }

        Heading("4 原始试验与频带");
        Paragraph("频带声级已采用工况所声明的计权，本软件不再次加权。空值显示—，不补零。");
        foreach (var trial in project.Trials)
        {
            Subheading(trial.Name + " · " + trial.Kind.Text() + " · " + trial.Source.Text());
            Table(["项目", "内容"],
            [
                ["标识", trial.Id],
                ["工况", project.ConditionName(trial.ConditionId)],
                ["时间", Software.Time(trial.Timestamp)],
                ["封堵位置", project.LocationNames(trial.LocationIds)],
                ["重复组", trial.RepeatGroup],
                ["来源说明", trial.SourceNote],
                ["原始文件/记录编号（仅文字）", trial.OriginalFile],
                ["操作员", trial.Operator],
                ["试验准备", trial.Preparation],
                ["备注", trial.Notes]
            ]);
            Table(["频率Hz", "声级 " + Software.LevelUnit, "频带备注"], trial.Bands.OrderBy(band => band.Frequency)
                .Select(band => new object?[] { band.Frequency, N(band.Level), band.Note }));
        }

        Heading("5 显式配对和测得声级差");
        foreach (var pair in report.Pairs)
        {
            Subheading(pair.Title + " · " + pair.Purpose.Text());
            Paragraph("来源：" + pair.Source.Text() + "；状态：" + pair.Status.Text() + "；配对标识：" + pair.PairId);
            if (pair.BlockingReasons.Count > 0)
            {
                output.Append("<p class=\"error\">阻断：").Append(E(string.Join("；", pair.BlockingReasons))).AppendLine("</p>");
            }

            if (pair.Warnings.Count > 0)
            {
                Notice(string.Join("；", pair.Warnings));
            }

            output.AppendLine(Chart(pair));
            output.AppendLine(SpectrumChart.Build(pair));
            Table(["Hz", "基线dB", "目标dB", "基线−目标dB", "状态", "说明"], pair.Bands.Select(band => new object?[]
            {
                band.Frequency, N(band.Baseline), N(band.Target), N(band.Difference), band.Status.Text(), band.Interpretation
            }));
            Paragraph("录入频带合成差：" + N(pair.TotalDifference) + " dB；" + pair.TotalNote);
            Paragraph("基线来源：" + pair.BaselineSource + "；目标来源：" + pair.TargetSource + "；目标减基线时间：" + pair.ElapsedSeconds + " s");
            if (pair.Screening is { } screening)
            {
                Paragraph("最大频带绝对差：" + N(screening.MaximumAbsoluteDifference) + " dB；最差频率：" +
                    N(screening.WorstFrequency) + " Hz；阈值：" + N(screening.Threshold) + " dB；" + screening.Status.Text() + "。" + Software.ScreeningNote);
            }
        }

        Heading("6 重复配对统计");
        Paragraph("配对差算术均值和组能量均值之差分别计算。95%区间要求独立配对差近似正态，未包含仪器系统不确定性；n=1或试验复用时不生成。");
        foreach (var effect in report.Effects)
        {
            Subheading(project.LocationNames(effect.LocationIds) + " · " + effect.Source.Text());
            Paragraph(string.Join("；", effect.BlockingReasons));
            Table(["Hz", "n", "配对差均值dB", "差值SD dB", "组能量均值差dB", "95%下限", "95%上限", "区间说明"],
                effect.Bands.Select(band => new object?[]
                {
                    band.Frequency, band.Statistics.Count, N(band.Statistics.MeanDifference), N(band.Statistics.StandardDeviation),
                    N(band.Statistics.GroupEnergyMeanDifference), N(band.Statistics.Lower95), N(band.Statistics.Upper95), band.Statistics.IntervalNote
                }));
        }

        Heading("7 重复性");
        foreach (var group in report.Repeatability)
        {
            Subheading(group.Name + " · " + group.Kind.Text() + " · " + group.Source.Text());
            Paragraph("样本SD筛查阈值：" + N(group.Threshold) + " dB。" + string.Join("；", group.BlockingReasons));
            Table(["Hz", "n", "缺失", "算术均值dB", "能量均值dB", "样本SD dB", "极差dB", "状态"], group.Bands.Select(band => new object?[]
            {
                band.Frequency, band.Statistics.Count, band.MissingCount, N(band.Statistics.ArithmeticMean),
                N(band.Statistics.EnergyMean), N(band.Statistics.StandardDeviation), N(band.Statistics.Range), band.Status.Text()
            }));
        }

        Coverage(report);
        Matrix(project, report);
        Heading("10 参数、问题与修复建议");
        Table(["参数", "值", "单位", "工程初值", "允许范围"], project.Settings.Describe().Select(setting => new object?[]
        {
            setting.Label, setting.Value, setting.Unit, setting.Default, setting.Minimum + "—" + setting.Maximum
        }));
        Paragraph("采样率必须一致：" + project.Settings.RequireSampleRateMatch + "；同步定义必须一致：" + project.Settings.RequireSynchronizationMatch);
        Table(["级别", "对象", "问题", "修复建议"], report.Issues.Select(issue => new object?[]
        {
            issue.Severity.Text(), issue.Title, issue.Reason, issue.Repair
        }));
        Heading("11 复核快照与操作审计");
        Table(["时间", "输入修订", "复核人", "有效/阻断", "备注", "输入SHA-256"], project.Reviews.Select(review => new object?[]
        {
            Software.Time(review.Time), review.InputRevision, review.Reviewer,
            review.ValidPairs + " / " + review.BlockedPairs, review.Comment, review.InputHash
        }));
        Table(["时间", "修订", "操作", "对象", "详情", "输入SHA-256"], project.Audit.Select(entry => new object?[]
        {
            Software.Time(entry.Time), entry.Revision, entry.Action, entry.Entity, entry.Detail, entry.InputHash
        }));
        Heading("12 方法与边界");
        Paragraph("ΔL=L基线−L目标。等权能量均值=10log10(Σ10^(Li/10)/n)；不重叠频带合成去除分母n。只对共同有效配对比较；频带合成须集合一致、无缺失且用户确认互不重叠。");
        Paragraph("软件不认证原始数据、标定或权属，不提供标准符合性结论。审计和哈希便于核对，但本地项目可被编辑，不是数字签名或防篡改证书。");
        Notice(Software.Limitation);
        output.AppendLine("</body></html>");
        return output.ToString();
    }

    private void Coverage(AnalysisReport report)
    {
        Heading("8 试验覆盖和时间线");
        Paragraph(report.Coverage.Note);
        Table(["试验类型", "记录数"], report.Coverage.TrialTypes.Select(item => new object?[] { item.Key.Text(), item.Value }));
        Table(["工况", "试验数", "缺失口径"], report.Coverage.Conditions.Select(item => new object?[] { item.Name, item.TrialCount, string.Join("；", item.Missing) }));
        Table(["封堵位置", "单项数", "组合数", "有效配对数"], report.Coverage.Locations.Select(item => new object?[]
        {
            item.Name, item.SingleCount, item.CombinationCount, item.ValidPairCount
        }));
        Table(["Hz", "有效", "明确缺失", "未声明"], report.Coverage.Frequencies.Select(item => new object?[]
        {
            item.Frequency, item.Valid, item.Missing, item.Undeclared
        }));
        Table(["时间", "试验", "类型", "来源", "有效频带", "目标配对数"], report.Coverage.Timeline.Select(item => new object?[]
        {
            Software.Time(item.Time), item.Name, item.Kind.Text(), item.Source.Text(), item.ValidBands, item.TargetPairs
        }));
    }

    private void Matrix(AcousticProject project, AnalysisReport report)
    {
        Heading("9 单项与组合测得差值矩阵");
        Paragraph("各列使用自己的显式基线、工况和来源；不能直接作独立贡献排名或单项相加预测。");
        var pairs = report.Pairs.Where(pair => pair.Purpose == PairPurpose.Effect).ToList();
        var columns = new List<string> { "Hz" };
        columns.AddRange(pairs.Select(pair => pair.TargetName + "；基线=" + pair.BaselineName + "；" + pair.Source.Text()));
        var rows = new List<object?[]>();
        foreach (var frequency in pairs.SelectMany(pair => pair.Bands.Select(band => band.Frequency)).Distinct().Order())
        {
            var cells = new List<object?> { frequency };
            foreach (var pair in pairs)
            {
                var band = pair.Bands.FirstOrDefault(band => band.Frequency == frequency);
                cells.Add(band?.Difference is { } value ? N(value) : band?.Status.Text() ?? "未声明");
            }

            rows.Add(cells.ToArray());
        }

        Table(columns, rows);
    }

    public static string Chart(PairResult pair)
    {
        var bands = pair.Bands.Where(band => band.Difference.HasValue).ToList();
        if (bands.Count == 0)
        {
            return "<p>无可绘制差值，未生成图表。</p>";
        }

        if (bands.Count > 30)
        {
            return "<p>频带超过30个，为保证文字可读，报告使用完整数值表；界面可扫描较窄频率范围查看图表。</p>";
        }

        var maximum = Math.Max(1, bands.Max(band => Math.Abs(band.Difference!.Value)));
        var svg = new StringBuilder("<svg viewBox=\"0 0 820 270\" role=\"img\" aria-label=\"测得声级差图，横轴Hz，纵轴dB\"><text x=\"8\" y=\"20\" font-size=\"12\">ΔL / dB</text><line x1=\"60\" y1=\"130\" x2=\"800\" y2=\"130\" stroke=\"#a5bac8\"/>");
        svg.Append("<text x=\"8\" y=\"45\" font-size=\"11\">").Append(N(maximum)).Append("</text><text x=\"20\" y=\"135\" font-size=\"11\">0</text>");
        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var x = 60 + (index + 0.5) * 740 / bands.Count;
            var width = Math.Min(45, 500.0 / bands.Count);
            var height = Math.Abs(band.Difference!.Value) / maximum * 85;
            var y = band.Difference >= 0 ? 130 - height : 130;
            svg.Append("<rect x=\"").Append(C(x - width / 2)).Append("\" y=\"").Append(C(y)).Append("\" width=\"").Append(C(width))
                .Append("\" height=\"").Append(C(height)).Append("\" fill=\"").Append(band.Difference >= 0 ? "#148c86" : "#be792e").Append("\"/>");
            svg.Append("<text x=\"").Append(C(x)).Append("\" y=\"245\" text-anchor=\"middle\" font-size=\"10\">").Append(C(band.Frequency)).Append("</text>");
            svg.Append("<text x=\"").Append(C(x)).Append("\" y=\"").Append(C(band.Difference >= 0 ? y - 8 : y + height + 15))
                .Append("\" text-anchor=\"middle\" font-size=\"11\">").Append(N(band.Difference)).Append("</text>");
        }

        return svg.Append("<text x=\"775\" y=\"265\" font-size=\"12\">Hz</text></svg>").ToString();
    }

    private void Heading(string text)
    {
        output.Append("<h2>").Append(E(text)).AppendLine("</h2>");
    }

    private void Subheading(string text)
    {
        output.Append("<h3>").Append(E(text)).AppendLine("</h3>");
    }

    private void Paragraph(string text)
    {
        output.Append("<p>").Append(E(text)).AppendLine("</p>");
    }

    private void Notice(string text)
    {
        output.Append("<p class=\"notice\">").Append(E(text)).AppendLine("</p>");
    }

    private void Table(IEnumerable<string> headings, IEnumerable<object?[]> rows)
    {
        var columns = headings.ToList();
        output.Append("<table><thead><tr>");
        foreach (var heading in columns)
        {
            output.Append("<th>").Append(E(heading)).Append("</th>");
        }

        output.Append("</tr></thead><tbody>");
        var count = 0;
        foreach (var row in rows)
        {
            output.Append("<tr>");
            foreach (var cell in row)
            {
                output.Append("<td>").Append(E(cell?.ToString() ?? "—")).Append("</td>");
            }

            output.Append("</tr>");
            count++;
        }

        if (count == 0)
        {
            output.Append("<tr><td colspan=\"").Append(columns.Count).Append("\">暂无记录</td></tr>");
        }

        output.AppendLine("</tbody></table>");
    }

    private static string E(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string N(double? value)
    {
        return Software.Format(value, 6);
    }

    private static string C(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
