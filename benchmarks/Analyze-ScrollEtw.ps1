param([Parameter(Mandatory)][string]$EtlPath,
      [Parameter(Mandatory)][string]$StartUtc,
      [Parameter(Mandatory)][string]$EndUtc)
$ErrorActionPreference = 'Stop'
$perfRoot = Get-ChildItem "$env:APPDATA\PerfView" -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $perfRoot) { throw 'PerfView support DLLs not found.' }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class PerfAssemblyResolver {
    public static void Register(string root) {
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
            string n = new AssemblyName(e.Name).Name;
            if(n != "System.Memory" && n != "System.Text.Json" &&
               n != "Microsoft.Diagnostics.Tracing.TraceEvent" &&
               n != "Microsoft.Diagnostics.FastSerialization") return null;
            string path = Path.Combine(root,n+".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }
}
'@
[PerfAssemblyResolver]::Register($perfRoot)
$refs = @(
    (Join-Path $perfRoot 'Microsoft.Diagnostics.Tracing.TraceEvent.dll'),
    (Join-Path $perfRoot 'Microsoft.Diagnostics.FastSerialization.dll'),
    'C:\Program Files\dotnet\sdk\8.0.425\Microsoft\Microsoft.NET.Build.Extensions\net461\lib\netstandard.dll'
)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Tracing.Etlx;
public static class ScrollEtwSummary {
    static string Percentiles(List<double> values) {
        if(values.Count==0) return "N/A";
        values.Sort();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F3},{1:F3},{2:F3}",
            values[(int)Math.Ceiling(values.Count*.50)-1],
            values[(int)Math.Ceiling(values.Count*.95)-1],
            values[(int)Math.Ceiling(values.Count*.99)-1]);
    }
    public static string Analyze(string path, DateTime startUtc, DateTime endUtc) {
        var appPresent = new List<double>();
        var longPresentGaps = new List<Tuple<double,double>>();
        var longGapUtc = new List<string>();
        var layoutSampleTimes = new List<double>();
        var dwmPresent = new List<double>();
        var names = new Dictionary<string,int>();
        var systemPresentNames = new Dictionary<string,int>();
        var cpuThreads = new Dictionary<int,int>();
        var presentWindows = new Dictionary<string,int>();
        var messageThreads = new Dictionary<int,int>();
        var cpuBins = new Dictionary<int,int>();
        var measureThreads = new Dictionary<int,int>();
        var arrangeThreads = new Dictionary<int,int>();
        var layoutMethods = new Dictionary<string,int>();
        var lastSwitchMsByCpu = new Dictionary<int,double>();
        var onCpuMsByThread = new Dictionary<int,double>();
        var appPids = new HashSet<int>();
        var samples = 0;
        var samplesWithStack = 0;
        var measureSamples = 0;
        var arrangeSamples = 0;
        var scrollMethodSamples = 0;
        var appPresentCount = 0;
        var dwmCount = 0;
        var glitchCount = 0;
        double appPrevious = -1, dwmPrevious = -1;
        string presentPayload = null;
        string cswitchPayload = null;
        string firstEvent = null, firstAppEvent = null;
        using(var log=TraceLog.OpenOrConvert(path,null)) {
            var src=log.Events.GetSource();
            src.EveryEvent += e => {
                if(firstEvent==null) firstEvent=e.TimeStamp.ToString("o")+"/"+e.TimeStamp.ToUniversalTime().ToString("o")+"/"+e.ProviderName+"/"+e.EventName;
                bool app = e.ProcessName != null && e.ProcessName.IndexOf("CrossGOverlay",StringComparison.OrdinalIgnoreCase)>=0;
                if(app && firstAppEvent==null) firstAppEvent=e.TimeStamp.ToString("o")+"/"+e.ProcessID+"/"+e.EventName;
                DateTime utc = e.TimeStamp.ToUniversalTime();
                if(utc < startUtc || utc > endUtc) return;
                if(e.ProviderName=="Windows Kernel" && e.EventName=="Thread/CSwitch") {
                    int cpu=e.ProcessorNumber;
                    int oldTid=Convert.ToInt32(e.PayloadByName("OldThreadID"));
                    double previousMs;
                    if(lastSwitchMsByCpu.TryGetValue(cpu,out previousMs)) {
                        double interval=e.TimeStampRelativeMSec-previousMs;
                        if(interval>=0 && interval<1000) {
                            double oldCpuMs;onCpuMsByThread.TryGetValue(oldTid,out oldCpuMs);
                            onCpuMsByThread[oldTid]=oldCpuMs+interval;
                        }
                    }
                    lastSwitchMsByCpu[cpu]=e.TimeStampRelativeMSec;
                }
                if(e.EventName.IndexOf("Present",StringComparison.OrdinalIgnoreCase)>=0 ||
                   e.ProviderName.IndexOf("Dwm",StringComparison.OrdinalIgnoreCase)>=0) {
                    string systemKey=e.ProviderName+"/"+e.EventName;
                    int systemOld;systemPresentNames.TryGetValue(systemKey,out systemOld);systemPresentNames[systemKey]=systemOld+1;
                }
                if(app) {
                    appPids.Add(e.ProcessID);
                    string key=e.ProviderName+"/"+e.EventName;
                    int old;
                    names.TryGetValue(key,out old); names[key]=old+1;
                }
                if(app && e.ProviderName=="Windows Kernel" && e.EventName=="Thread/CSwitch" && cswitchPayload==null) {
                    var parts=new List<string>();
                    foreach(string name in e.PayloadNames) { object value=null; try {value=e.PayloadByName(name);}catch{} parts.Add(name+"="+(value==null?"null":value.ToString())); }
                    cswitchPayload="cpu="+e.ProcessorNumber+";"+string.Join(";",parts);
                }
                if(app && e.EventName=="RetrievePostMessage") {
                    int old;messageThreads.TryGetValue(e.ThreadID,out old);messageThreads[e.ThreadID]=old+1;
                }
                if(e.ProviderName=="Microsoft-Windows-DxgKrnl" && e.EventName=="Present" && app) {
                    appPresentCount++;
                    string hwnd="unknown";
                    try { hwnd=Convert.ToString(e.PayloadByName("hWindow")); } catch {}
                    int hwndCount;presentWindows.TryGetValue(hwnd,out hwndCount);presentWindows[hwnd]=hwndCount+1;
                    if(appPrevious>=0) {
                        double gap=e.TimeStampRelativeMSec-appPrevious;
                        appPresent.Add(gap);
                        if(gap>=75) {
                            longPresentGaps.Add(Tuple.Create(appPrevious,e.TimeStampRelativeMSec));
                            longGapUtc.Add(e.TimeStamp.ToUniversalTime().AddMilliseconds(-gap).ToString("o")+".."+
                                e.TimeStamp.ToUniversalTime().ToString("o")+"="+gap.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"ms");
                        }
                    }
                    appPrevious=e.TimeStampRelativeMSec;
                    if(presentPayload==null) {
                        var parts = new List<string>();
                        foreach(string name in e.PayloadNames) {
                            object value = null;
                            try { value=e.PayloadByName(name); } catch {}
                            parts.Add(name+"="+(value==null ? "null":value.ToString()));
                        }
                        presentPayload=string.Join(";",parts);
                    }
                }
                if(e.ProviderName=="Microsoft-Windows-DxgKrnl" && e.EventName=="PresentMultiPlaneOverlay") {
                    dwmCount++;
                    if(dwmPrevious>=0) dwmPresent.Add(e.TimeStampRelativeMSec-dwmPrevious);
                    dwmPrevious=e.TimeStampRelativeMSec;
                }
                if(e.EventName=="CompositionTargetGlitch") glitchCount++;
                if(app && e.ProviderName=="Windows Kernel" && e.EventName=="PerfInfo/Sample") {
                    samples++;
                    int old;cpuThreads.TryGetValue(e.ThreadID,out old);cpuThreads[e.ThreadID]=old+1;
                    int bin=(int)(utc-startUtc).TotalSeconds;
                    cpuBins.TryGetValue(bin,out old);cpuBins[bin]=old+1;
                    var stack=e.CallStack();
                    if(stack!=null) {
                        samplesWithStack++;
                        bool measure=false, arrange=false, scroll=false;
                        while(stack!=null) {
                            string method=stack.CodeAddress.FullMethodName ?? "";
                            if(method.IndexOf("Measure",StringComparison.OrdinalIgnoreCase)>=0 || method.IndexOf("Arrange",StringComparison.OrdinalIgnoreCase)>=0) {
                                layoutMethods.TryGetValue(method,out old);layoutMethods[method]=old+1;
                            }
                            if(method.IndexOf("Measure",StringComparison.OrdinalIgnoreCase)>=0) measure=true;
                            if(method.IndexOf("Arrange",StringComparison.OrdinalIgnoreCase)>=0) arrange=true;
                            if(method.IndexOf("ScrollToVerticalOffset",StringComparison.OrdinalIgnoreCase)>=0) scroll=true;
                            stack=stack.Caller;
                        }
                        if(measure){measureSamples++;measureThreads.TryGetValue(e.ThreadID,out old);measureThreads[e.ThreadID]=old+1;}
                        if(arrange){arrangeSamples++;arrangeThreads.TryGetValue(e.ThreadID,out old);arrangeThreads[e.ThreadID]=old+1;}
                        if(measure || arrange)layoutSampleTimes.Add(e.TimeStampRelativeMSec);
                        if(scroll)scrollMethodSamples++;
                    }
                }
            };
            src.Process();
            var sb=new StringBuilder();
            sb.AppendLine("FirstEvent="+firstEvent+"; FirstAppEvent="+firstAppEvent+"; Range="+startUtc.ToString("o")+".."+endUtc.ToString("o"));
            sb.AppendLine("Events="+log.EventCount+"; EventsLost="+log.EventsLost+"; AppPids="+string.Join(",",appPids));
            sb.AppendLine("AppPresent="+appPresentCount+"; AppPresentIntervalP50,P95,P99Ms="+Percentiles(appPresent));
            sb.AppendLine("PresentByHwnd="+string.Join(",",presentWindows.Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("DwmMpo="+dwmCount+"; DwmMpoIntervalP50,P95,P99Ms="+Percentiles(dwmPresent)+"; DwmGlitchSystem="+glitchCount);
            sb.AppendLine("AppSampledProfile="+samples+"; TopThreads="+string.Join(",",cpuThreads.OrderByDescending(x=>x.Value).Take(8).Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("MessageThreads="+string.Join(",",messageThreads.OrderByDescending(x=>x.Value).Take(8).Select(x=>x.Key+":"+x.Value))+
                "; SampleStacks="+samplesWithStack+"; MeasureSamples="+measureSamples+"; ArrangeSamples="+arrangeSamples+"; ScrollMethodSamples="+scrollMethodSamples);
            sb.AppendLine("AppThreadOnCpuMs="+string.Join(",",cpuThreads.OrderByDescending(x=>x.Value).Take(8).Select(x=>x.Key+":"+
                (onCpuMsByThread.ContainsKey(x.Key) ? onCpuMsByThread[x.Key].ToString("F2",System.Globalization.CultureInfo.InvariantCulture):"0")))+
                "; UiMessageThreadOnCpuMs="+string.Join(",",messageThreads.OrderByDescending(x=>x.Value).Take(2).Select(x=>x.Key+":"+
                (onCpuMsByThread.ContainsKey(x.Key) ? onCpuMsByThread[x.Key].ToString("F2",System.Globalization.CultureInfo.InvariantCulture):"0"))));
            sb.AppendLine("CpuSamplesPerSecond="+string.Join(",",cpuBins.OrderBy(x=>x.Key).Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("MeasureThreads="+string.Join(",",measureThreads.Select(x=>x.Key+":"+x.Value))+"; ArrangeThreads="+string.Join(",",arrangeThreads.Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("LayoutSymbols="+string.Join(";",layoutMethods.OrderByDescending(x=>x.Value).Take(10).Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("PresentGapsAtLeast75Ms="+longPresentGaps.Count+"; WithLayoutStackSample="+
                longPresentGaps.Count(g=>layoutSampleTimes.Any(t=>t>=g.Item1 && t<=g.Item2)));
            sb.AppendLine("LongGapUtc="+string.Join(";",longGapUtc));
            sb.AppendLine("LongGapLayoutSampleCounts="+string.Join(",",
                longPresentGaps.Select(g=>layoutSampleTimes.Count(t=>t>=g.Item1 && t<=g.Item2))));
            sb.AppendLine("AppPresentPayload="+presentPayload);
            sb.AppendLine("CSwitchPayload="+cswitchPayload);
            sb.AppendLine("TopAppEvents="+string.Join(";",names.OrderByDescending(x=>x.Value).Take(18).Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("SystemPresentEvents="+string.Join(";",systemPresentNames.OrderByDescending(x=>x.Value).Take(16).Select(x=>x.Key+":"+x.Value)));
            sb.AppendLine("SystemMpoEvents="+string.Join(";",systemPresentNames.Where(x=>x.Key.IndexOf("MultiPlane",StringComparison.OrdinalIgnoreCase)>=0).Select(x=>x.Key+":"+x.Value)));
            return sb.ToString();
        }
    }
}
'@
[ScrollEtwSummary]::Analyze((Resolve-Path -LiteralPath $EtlPath).Path,
    ([DateTimeOffset]::Parse($StartUtc)).UtcDateTime,
    ([DateTimeOffset]::Parse($EndUtc)).UtcDateTime)
