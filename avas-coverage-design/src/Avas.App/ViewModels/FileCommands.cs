using System.Text;
using Avas.Core.Analysis;
using Avas.Core.Audio;
using Avas.Core.Domain;
using Avas.Core.IO;

namespace Avas.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public void Save()
    {
        Execute(() =>
        {
            RequireApplied();
            var receipt = store.Save(Project, diskHash);
            diskHash = receipt.Hash;
            savedFingerprint = ProjectCodec.Json(Project);
            RefreshLibrary();
            Status = $"已保存：{receipt.Path}。{(receipt.BackupCreated ? "上次版本保存在同路径.bak。" : "首次保存。")}";
        });
    }

    public void RefreshLibrary()
    {
        Execute(() => Library = store.List());
    }

    public void Open(Guid id, bool backup)
    {
        Execute(() =>
        {
            var loaded = store.Open(id, backup);
            LoadProject(loaded.Project);
            diskHash = backup ? null : loaded.Hash;
            savedFingerprint = backup ? "" : ProjectCodec.Json(Project);
            Status = backup ? "已打开备份。请复制为独立项目保存，避免覆盖当前磁盘版。" : "已从本机项目库打开项目，输入和内嵌音频已复核。";
        });
    }

    public void ImportProject(byte[] bytes)
    {
        Execute(() =>
        {
            var loaded = ProjectCodec.Decode(bytes);
            LoadProject(loaded);
            diskHash = null;
            savedFingerprint = "";
            Status = "已载入上传项目；音频与快照均内嵌。若本机已有同标识文件，请打开磁盘版续改或复制项目后保存。";
        });
    }

    private void LoadProject(Project loaded)
    {
        Project = loaded;
        undo.Clear();
        redo.Clear();
        Result = null;
        SelectedSpeakerId = loaded.Speakers.FirstOrDefault()?.Id;
        SelectedListenerId = loaded.Listeners.FirstOrDefault()?.Id;
        ResetAfterMutation();
        BaselineId = "";
        TargetId = "";
        Tab = "项目";
    }

    public void PreviewCsv()
    {
        Execute(() =>
        {
            RequireApplied();
            if (!ImportBackground && SelectedSpeakerId is null)
                throw new ArgumentException("请先建立并选择扬声器，或勾选背景导入");
            Preview = SpectrumCsv.Parse(CsvText, ImportBackground);
            previewTarget = ImportBackground ? "background" : SelectedSpeakerId.ToString();
            previewText = CsvText;
            Status = Preview.Valid ? "预检通过，尚未修改项目；确认对象后点击“应用频谱导入”。" : "预检未通过，原频带保持不变。";
        });
    }

    public void ApplyCsv()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var target = ImportBackground ? "background" : SelectedSpeakerId.ToString();
            if (Preview is null || !Preview.Valid || previewTarget != target || previewText != CsvText)
                throw new ArgumentException("预检不存在、失败或对象/文本已改变，请重新预检");
            var candidate = ProjectCodec.Clone(Project);
            var bands = Preview.Bands.Select(x => x.Copy()).ToList();
            if (ImportBackground)
                candidate.Settings.Background = bands;
            else
                candidate.Speakers.Single(x => x.Id == SelectedSpeakerId).Bands = bands;
            Commit(candidate, "完整替换" + (ImportBackground ? "背景" : "当前扬声器") + "七频带CSV");
            Status = "七频带已整体导入，请核对来源说明并重新计算。";
        });
    }

    public void ImportAudio(string name, byte[] bytes)
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var asset = WaveCodec.CreateAsset(name, bytes);
            var candidate = ProjectCodec.Clone(Project);
            if (AudioTarget == "background")
                candidate.Settings.BackgroundAudio = asset;
            else
            {
                var source = candidate.Speakers.SingleOrDefault(x => x.Id == SelectedSpeakerId)
                    ?? throw new ArgumentException("请先选择扬声器");
                source.Audio = asset;
            }
            Commit(candidate, "内嵌导入数字音频 " + asset.Name);
            AudioSpectrum = SpectralAudio.Analyze(WaveCodec.Read(bytes));
            AudioAnalysisLabel = AudioTarget == "background" ? "背景 / " + asset.Name : "当前扬声器 / " + asset.Name;
            Status = $"已导入{asset.Name}，{asset.Duration:F2}秒、{asset.Rate}Hz。未自动改变任何物理声压级。";
        });
    }

    public void AnalyzeAudio()
    {
        Execute(() =>
        {
            RequireApplied();
            var asset = CurrentAudio() ?? throw new ArgumentException("当前对象尚未导入音频");
            AudioSpectrum = SpectralAudio.Analyze(WaveCodec.Read(Convert.FromBase64String(asset.Data)));
            AudioAnalysisLabel = AudioTarget == "background" ? "背景 / " + asset.Name : "当前扬声器 / " + asset.Name;
            Status = "数字频谱分析完成；dBFS值不能当作声压级。";
        });
    }

    public AudioAsset? CurrentAudio()
    {
        return AudioTarget == "background" ? Project.Settings.BackgroundAudio
            : Project.Speakers.SingleOrDefault(x => x.Id == SelectedSpeakerId)?.Audio;
    }

    public void ApplyAudioSpectrum()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var asset = CurrentAudio() ?? throw new ArgumentException("当前对象尚无音频");
            var spectrum = SpectralAudio.Analyze(WaveCodec.Read(Convert.FromBase64String(asset.Data)));
            var bands = SpectralAudio.RelativeBands(spectrum, AudioReference);
            var candidate = ProjectCodec.Clone(Project);
            var sourceText = $"数字WAV {asset.Name} 相对频谱 + 人工声明七带参考{AudioReference}dB；无声压标定链路；{asset.Sha256}";
            if (AudioTarget == "background")
            {
                candidate.Settings.Background = bands;
                candidate.Settings.BackgroundOrigin = Origin.Estimate;
                candidate.Settings.BackgroundSource = sourceText;
            }
            else
            {
                var speaker = candidate.Speakers.Single(x => x.Id == SelectedSpeakerId);
                speaker.Bands = bands;
                speaker.Origin = Origin.Estimate;
                speaker.Source = sourceText;
            }
            Commit(candidate, "以音频相对频谱和人工参考构建估算七频带");
            Status = "已生成估算频谱并标记来源；不是音频自动标定结果。";
        });
    }

    public void RemoveAudio()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            if (AudioTarget == "background")
                candidate.Settings.BackgroundAudio = null;
            else
                candidate.Speakers.Single(x => x.Id == SelectedSpeakerId).Audio = null;
            Commit(candidate, "移除当前对象内嵌音频");
            Status = "已移除音频，频带声级仍保留。";
        });
    }

    public async Task RenderAuditionAsync()
    {
        if (Busy)
            return;
        Busy = true;
        Notify();
        await Task.Yield();
        Execute(() =>
        {
            RequireApplied();
            if (Stale)
                throw new ArgumentException("请先重新计算，再制作当前观察点试听");
            var point = Project.Listeners.SingleOrDefault(x => x.Id == SelectedListenerId)
                ?? throw new ArgumentException("请先建立并选择观察点");
            Audition = AuditionEngine.Render(Project, point, AuditionSeconds, PlaybackReference, IncludeBackground);
            AudioUrl = vault.Add(Audition.Wave, "audio/wav", "相对试听-" + SafeName(point.Name) + ".wav");
            Status = "相对试听已生成；固定数字参考，不自动归一化；设备未标定。";
        });
        Busy = false;
        Notify();
    }

    public void Export(string kind)
    {
        Execute(() =>
        {
            RequireApplied();
            byte[] bytes;
            string mime;
            string suffix;
            switch (kind)
            {
                case "project":
                    bytes = ProjectCodec.Encode(Project);
                    mime = "application/json";
                    suffix = ".avasproj";
                    break;
                case "template":
                    bytes = Encoding.UTF8.GetBytes("\uFEFF" + SpectrumCsv.Template(ImportBackground));
                    mime = "text/csv;charset=utf-8";
                    suffix = "-频谱模板.csv";
                    break;
                case "input":
                    var bands = ImportBackground ? Project.Settings.Background
                        : Project.Speakers.Single(x => x.Id == SelectedSpeakerId).Bands;
                    bytes = SpectrumCsv.Write(bands);
                    mime = "text/csv;charset=utf-8";
                    suffix = "-当前频带.csv";
                    break;
                case "comparison":
                    bytes = ResultExport.ComparisonCsv(Comparison ?? throw new ArgumentException("请先完成方案比较"));
                    mime = "text/csv;charset=utf-8";
                    suffix = "-方案比较.csv";
                    break;
                case "scan":
                    bytes = ResultExport.ScanCsv(Scan ?? throw new ArgumentException("请先完成参数扫描"));
                    mime = "text/csv;charset=utf-8";
                    suffix = "-参数扫描.csv";
                    break;
                default:
                    if (Result is null || Stale)
                        throw new ArgumentException("结果不存在或已过期，请先重新计算");
                    ResultExport.RequireCurrent(Project, Result);
                    (bytes, mime, suffix) = kind switch
                    {
                        "report" => (ResultExport.Report(Project, Result), "text/html;charset=utf-8", "-工程报告.html"),
                        "grid" => (ResultExport.Csv(Project, Result, true), "text/csv;charset=utf-8", "-网格.csv"),
                        "points" => (ResultExport.Csv(Project, Result, false), "text/csv;charset=utf-8", "-观察点.csv"),
                        "svg" => (Encoding.UTF8.GetBytes(ChartRenderer.Map(Project, Result, CoverageOnly)), "image/svg+xml", "-声场.svg"),
                        "json" => (Encoding.UTF8.GetBytes(ProjectCodec.Json(new { Software.Model, Software.Version, Boundary = Software.Boundary, Input = Project, Result })), "application/json", "-计算快照.json"),
                        _ => throw new ArgumentException("不支持的输出类型")
                    };
                    break;
            }
            LastFileName = SafeName(Project.Name) + suffix;
            LastDownload = vault.Add(bytes, mime, LastFileName);
            Status = $"已生成{LastFileName}，点击下载链接保存至浏览器下载目录；下载不等于本机项目库保存。链接10分钟内有效。";
        });
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("/\\:").ToHashSet();
        return string.Concat(name.Take(60).Select(c => invalid.Contains(c) ? '_' : c));
    }
}
