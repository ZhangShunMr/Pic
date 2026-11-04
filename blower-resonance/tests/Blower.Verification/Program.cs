using System.Text.Json;
using System.Text.Json.Nodes;
using Blower.Core.Analysis;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Core.Operations;

var checks = new List<object>();
var failures = new List<string>();
void Test(string name, Action action)
{
    try { action(); checks.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures.Add(name + ": " + e.Message); checks.Add(new { name, status = "failed", error = e.Message }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}");
}
void Close(double expected, double actual, double tolerance = 1e-7)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}, actual {actual}");
}
void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { return; }
    throw new Exception("Invalid operation was accepted");
}
var p = Demo.Create();
var a = Engine.Run(p);
Test("independent formula: 40 blades at 900rpm = 600Hz", () => Close(600, Engine.Frequency(900, 40)));
Test("zero RPM is zero frequency", () => Close(0, Engine.Frequency(0, 40)));
Test("fractional shaft order", () => Close(25, Engine.Frequency(3000, 0.5)));
Test("blade harmonic order", () => Close(80, p.Orders[3].EffectiveOrder(40)));
Test("band maximum: 600Hz -> max(5,18,12,0) =18Hz", () => Close(18, a.Bands[0].HalfWidthHz));
Test("crossing: 600Hz / 40X ->900rpm, sensitive [853,947]", () => {
    var c=a.Crossings.Single(x=>x.ModeId==p.Modes[0].Id && x.OrderId==p.Orders[2].Id);
    Close(900,c.ExactRpm); Close(853,c.LowerRpm); Close(947,c.UpperRpm);
});
Test("frequency interval at900 +/-20RPM ->[586.6667,613.3333]Hz", () => {
    var c=a.Contacts.Single(x=>x.PlanId==p.Plans[0].Id && x.GearId==p.Plans[0].Gears[0].Id && x.ModeId==p.Modes[0].Id && x.OrderId==p.Orders[2].Id);
    Close(586.6666666667,c.LowerHz); Close(613.3333333333,c.UpperHz); Equal(true,c.Near); Close(-31.3333333333,c.ClearanceHz);
});
Test("exact boundary counts as contact", () => {
    var q=ProjectCodec.Clone(p); q.Plans[0].Gears[0].Rpm=947;
    var c=Engine.Evaluate(q,q.Plans[0],q.Plans[0].Gears[0],q.Orders[2],q.Modes[0]);
    Close(0,c.ClearanceHz); Equal(true,c.Near);
});
Test("beyond boundary is outside", () => {
    var q=ProjectCodec.Clone(p); q.Plans[0].Gears[0].Rpm=947.01;
    Equal(false,Engine.Evaluate(q,q.Plans[0],q.Plans[0].Gears[0],q.Orders[2],q.Modes[0]).Near);
});
Test("damping unknown stays null and adds no band", () => { var m=new Mode {FrequencyHz=100,DampingRatio=null}; Close(5,Engine.Band(m,new()).HalfWidthHz); Equal<double?>(null,m.DampingRatio); });
Test("damping switches and extra band", () => {
    var m=new Mode{FrequencyHz=1000,DampingRatio=.08}; var s=new DesignSettings();
    Close(80,Engine.Band(m,s).HalfWidthHz); s.UseDamping=false; Close(30,Engine.Band(m,s).HalfWidthHz); m.ExtraBandHz=50; Close(50,Engine.Band(m,s).HalfWidthHz);
});
Test("disabled modes and orders excluded", () => { var q=ProjectCodec.Clone(p);q.Modes[0].Enabled=false;q.Orders[0].Enabled=false;var r=Engine.Run(q);Equal(6,r.Crossings.Count);Equal(false,r.Contacts.Any(x=>x.ModeId==q.Modes[0].Id)); });
Test("empty project can save but cannot compute", () => { ProjectCodec.Encode(new());Reject(()=>Engine.Run(new())); });
Test("negative speed rejected", () => {var q=ProjectCodec.Clone(p);q.Plans[0].Gears[0].Rpm=-1;Reject(()=>Engine.Run(q));});
Test("invalid spacing rejected transactionally", () => {var s=new ProjectSession();s.Load(p);var old=ProjectCodec.InputHash(s.Current);Reject(()=>s.Change("bad",q=>q.Plans[0].Gears[1].Rpm=901));Equal(old,ProjectCodec.InputHash(s.Current));});
Test("NaN rejected", () => {var q=ProjectCodec.Clone(p);q.Modes[0].FrequencyHz=double.NaN;Reject(()=>Engine.Run(q));});
Test("out of range damping rejected", () => {var q=ProjectCodec.Clone(p);q.Modes[0].DampingRatio=.2;Reject(()=>Engine.Run(q));});
Test("zero blade count rejected", () => {var q=ProjectCodec.Clone(p);q.Settings.Blades=0;Reject(()=>Engine.Run(q));});
Test("missing active order rejected", () => {var q=ProjectCodec.Clone(p);q.Orders.ForEach(x=>x.Enabled=false);Reject(()=>Engine.Run(q));});
Test("comparison direction and shared conditions", () => {var c=Comparisons.Compare(p,a,p.Plans[0].Id,p.Plans[1].Id);Close(100,c.Rows[0].DeltaRpm!.Value);Equal(true,c.EqualGearCount);});
Test("different gear count preserves missing value", () => {var q=ProjectCodec.Clone(p);q.Plans[1].Gears.RemoveAt(5);var c=Comparisons.Compare(q,Engine.Run(q),q.Plans[0].Id,q.Plans[1].Id);Equal(false,c.EqualGearCount);Equal<double?>(null,c.Rows[^1].DeltaRpm);});
Test("stale result cannot compare/export", () => {var q=ProjectCodec.Clone(p);q.Settings.Blades=41;Reject(()=>Comparisons.Compare(q,a,q.Plans[0].Id,q.Plans[1].Id));Reject(()=>Exports.Contacts(q,a));});
Test("scan independent brute force optimum", () => {
    var q=ProjectCodec.Clone(p);q.Plans=[q.Plans[0]];q.Plans[0].Gears=q.Plans[0].Gears.Take(2).ToList();
    q.Settings.ScanWindowRpm=100;q.Settings.ScanStepRpm=100;
    var scan=SpeedScanner.Scan(q,q.Plans[0].Id);Equal(true,scan.Feasible);
    var best=double.PositiveInfinity;
    foreach(var r1 in new[]{800d,900,1000}) foreach(var r2 in new[]{1300d,1400,1500}) {
        if(r2-r1<q.Settings.MinimumGapRpm)continue;
        var cost=0d; var speeds=new[]{r1,r2};
        for(var i=0;i<2;i++) {
            var probe=new Gear{Rpm=speeds[i]};var count=0;
            foreach(var o in q.Orders)foreach(var m in q.Modes)if(Engine.Evaluate(q,q.Plans[0],probe,o,m).Near)count++;
            cost+=count+.15*Math.Abs(speeds[i]-q.Plans[0].Gears[i].Rpm)/100;
        }
        best=Math.Min(best,cost);
    }
    Close(best,scan.TotalCost);
});
Test("scan fixed gear remains unchanged", () => {var q=ProjectCodec.Clone(p);q.Plans[0].Gears[0].Fixed=true;var scan=SpeedScanner.Scan(q,q.Plans[0].Id);Close(900,scan.Choices[0].CandidateRpm);});
Test("scan step limit controlled", () => {var q=ProjectCodec.Clone(p);q.Settings.ScanWindowRpm=3000;q.Settings.ScanStepRpm=1;Equal(false,SpeedScanner.Scan(q,q.Plans[0].Id).Feasible);});
Test("scan accept adds new plan and leaves original", () => {var q=ProjectCodec.Clone(p);var scan=SpeedScanner.Scan(q,q.Plans[0].Id);var before=q.Plans[0].Gears[0].Rpm;ProjectCommands.ApplyScan(q,scan);Equal(3,q.Plans.Count);Close(before,q.Plans[0].Gears[0].Rpm);Validation.RequireValid(q);});
Test("stale scan rejected", () => {var q=ProjectCodec.Clone(p);var scan=SpeedScanner.Scan(q,q.Plans[0].Id);q.Settings.SpeedToleranceRpm++;Reject(()=>ProjectCommands.ApplyScan(q,scan));});
Test("rule study nonmutating with nine scenarios", () => {var hash=ProjectCodec.InputHash(p);var s=RuleStudy.Run(p,p.Plans[0].Id,"5,20,40","0,20,50");Equal(9,s.Cases.Count);Equal(hash,ProjectCodec.InputHash(p));});
Test("rule study invalid list rejected", () => {Reject(()=>RuleStudy.ParseValues("NaN",1000,"rpm"));Reject(()=>RuleStudy.ParseValues("1,2,3,4,5,6",1000,"rpm"));});
Test("check generation deduplicates", () => {var q=ProjectCodec.Clone(p);var count=ProjectCommands.GenerateChecks(q,a,q.Plans[0].Id);Equal(a.Summaries[0].ContactCount,count);Equal(0,ProjectCommands.GenerateChecks(q,a,q.Plans[0].Id));Validation.RequireValid(q);});
Test("check dependencies protect object deletion", () => {var q=ProjectCodec.Clone(p);ProjectCommands.GenerateChecks(q,a,q.Plans[0].Id);Reject(()=>ProjectCommands.RemoveObject(q,"mode",q.Checks[0].ModeId));});
Test("completed check needs evidence", () => {var q=ProjectCodec.Clone(p);ProjectCommands.GenerateChecks(q,a,q.Plans[0].Id);q.Checks[0].State=CheckState.Completed;Reject(()=>Validation.RequireValid(q));});
Test("input change marks completed check for recheck", () => {var q=ProjectCodec.Clone(p);ProjectCommands.GenerateChecks(q,a,q.Plans[0].Id);q.Checks[0].State=CheckState.Completed;q.Checks[0].Evidence="mock record path";var s=new ProjectSession();s.Load(q);s.Change("rpm",x=>x.Plans[0].Gears[0].Rpm=910);Equal(CheckState.Recheck,s.Current.Checks[0].State);});
Test("undo redo retains audit", () => {var s=new ProjectSession();s.Load(p);s.Change("edit",q=>q.Settings.Blades=41);s.Undo();Equal(40,s.Current.Settings.Blades);s.Redo();Equal(41,s.Current.Settings.Blades);Equal(4,s.Current.Audit.Count);});
Test("archived project rejects changes", () => {var s=new ProjectSession();var q=ProjectCodec.Clone(p);q.Archived=true;s.Load(q);Reject(()=>s.Change("edit",x=>x.Name="bad"));});
Test("project exact round trip", () => {Equal(ProjectCodec.InputHash(p),ProjectCodec.InputHash(ProjectCodec.Decode(ProjectCodec.Encode(p))));});
Test("corrupt JSON and schema rejected", () => {Reject(()=>ProjectCodec.Decode("{"));var json=JsonNode.Parse(ProjectCodec.Encode(p))!;json["schema"]=2;Reject(()=>ProjectCodec.Decode(json.ToJsonString()));});
Test("missing nested field rejected", () => {var json=JsonNode.Parse(ProjectCodec.Encode(p))!;json["settings"]!.AsObject().Remove("blades");Reject(()=>ProjectCodec.Decode(json.ToJsonString()));});
Test("null object and unknown field rejected", () => {var json=JsonNode.Parse(ProjectCodec.Encode(p))!;json["orders"]![0]=null;Reject(()=>ProjectCodec.Decode(json.ToJsonString()));json=JsonNode.Parse(ProjectCodec.Encode(p))!;json["bogus"]=2;Reject(()=>ProjectCodec.Decode(json.ToJsonString()));});
Test("duplicate property rejected", () => {var json=ProjectCodec.Encode(p).Replace("\"schema\": 1","\"schema\": 1, \"schema\": 1");Reject(()=>ProjectCodec.Decode(json));});
Test("duplicate identities rejected", () => {var q=ProjectCodec.Clone(p);q.Modes[1].Id=q.Modes[0].Id;Reject(()=>ProjectCodec.Encode(q));});
Test("CSV quoted commas parsed", () => {var r=CsvExchange.Preview("name,rpm,fixed\n\"低,挡\",900,false\n高挡,1800,true","gears",p);Equal(true,r.Valid);Equal("低,挡",r.Gears[0].Name);});
Test("CSV error lists row, no mutation", () => {var hash=ProjectCodec.InputHash(p);var r=CsvExchange.Preview("name,rpm,fixed\n低挡,abc,false","gears",p);Equal(false,r.Valid);Equal(2,r.Problems[0].Line);Equal(hash,ProjectCodec.InputHash(p));});
Test("CSV sorted spacing invalid", () => {Equal(false,CsvExchange.Preview("name,rpm,fixed\na,900,false\nb,905,false","gears",p).Valid);});
Test("CSV unknown damping retained", () => {var r=CsvExchange.Preview("name,frequency_hz,damping_ratio,source\nM,600,,模拟输入","modes",p);Equal(true,r.Valid);Equal<double?>(null,r.Modes[0].DampingRatio);});
Test("CSV formula escape and negative numeric", () => {Equal("\"'=cmd\"",CsvExchange.Cell("=cmd"));Equal("\"-3.5\"",CsvExchange.Cell("-3.5"));});
Test("HTML report escapes content and embeds SVG", () => {var q=ProjectCodec.Clone(p);q.Name="<script>alert(1)</script>";var r=Engine.Run(q);var h=Exports.Report(q,r,q.Plans[0].Id);Equal(false,h.Contains("<script>"));Equal(true,h.Contains("<svg"));Equal(true,h.Contains("模拟设计数据"));Equal(true,h.Contains(r.InputHash));});
Test("SVG axis source and self containment", () => {var svg=ChartModel.Create(p,a,p.Plans[0].Id).ToSvg("test","模拟");Equal(true,svg.Contains("频率 Hz"));Equal(false,svg.Contains("http://"+"cdn"));});
Test("duplicate enabled effective orders rejected", () => {var q=ProjectCodec.Clone(p);q.Orders.Add(new(){Name="duplicate40",Kind=OrderKind.Shaft,Multiplier=40});Reject(()=>Engine.Run(q));});
Test("disabled duplicate order preserved safely", () => {var q=ProjectCodec.Clone(p);q.Orders.Add(new(){Name="disabled40",Kind=OrderKind.Shaft,Multiplier=40,Enabled=false});Equal(a.Contacts.Count,Engine.Run(q).Contacts.Count);});
Test("check key mismatch rejected", () => {var q=ProjectCodec.Clone(p);ProjectCommands.GenerateChecks(q,a,q.Plans[0].Id);q.Checks[0].Key="bad";Reject(()=>ProjectCodec.Encode(q));});
Test("source-specific CSV column preserved", () => {var csv=Exports.Contacts(p,a);Equal(true,csv.Contains("mode_source"));Equal(true,csv.Contains("模拟模态频率"));});
var root=Path.Combine(Path.GetTempPath(),"blower-verification-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Test("save reopen backup conflict and migration", () => {
    var store=new ProjectStore(root);var s=new ProjectSession();s.Load(p);s.Save(store);Equal(false,s.Dirty);
    var opened=store.Open(p.Id);Equal(ProjectCodec.InputHash(p),ProjectCodec.InputHash(opened.Project));
    s.Change("edit",q=>q.Name="saved revision");s.Save(store);Equal(p.Name,store.Open(p.Id,true).Project.Name);
    Reject(()=>store.Save(p,opened.Token));
    var migrated=Path.Combine(root,"moved");Directory.CreateDirectory(migrated);
    File.Copy(Path.Combine(root,p.Id+".blowerproj"),Path.Combine(migrated,p.Id+".blowerproj"));
    Equal("saved revision",new ProjectStore(migrated).Open(p.Id).Project.Name);
});
Test("store path traversal rejected", () => {Reject(()=>new ProjectStore(root).Open("../../secret"));});
Test("damaged library entry visible", () => {File.WriteAllText(Path.Combine(root,"bad.blowerproj"),"not-json");Equal(true,new ProjectStore(root).List().Any(x=>x.Name=="损坏文件"));});
Directory.Delete(root,true);
var outFile=args.FirstOrDefault()??"verification-evidence.json";
File.WriteAllText(outFile,JsonSerializer.Serialize(new { date=DateTimeOffset.UtcNow,environment=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,passed=checks.Count-failures.Count,failed=failures.Count,checks },new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count-failures.Count}/{checks.Count} passed");
if(failures.Count>0)Environment.Exit(1);
