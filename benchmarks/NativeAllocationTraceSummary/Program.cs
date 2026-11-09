using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: NativeAllocationTraceSummary <trace.etl> [pid]");
var selectedPid = args.Length == 2 ? int.Parse(args[1]) : (int?)null;
Console.WriteLine($"Opening {args[0]}");
using var log = TraceLog.OpenOrConvert(args[0]);
var targetEvents = new Dictionary<string, long>(StringComparer.Ordinal);
var virtualEvents = new Dictionary<string, long>(StringComparer.Ordinal);
var large = new List<(DateTime Time, int Pid, long Base, long Length, string Flags, string Stack)>();
var frees = new List<(DateTime Time, long Base, string Payload)>();
long allCount = 0, targetCount = 0;
foreach (var data in log.Events)
{
    allCount++;
    var name = data.EventName ?? "<unknown>";
    var process = data.ProcessName ?? "<unknown>";
    var isTarget = process.Contains("CrossGOverlay", StringComparison.OrdinalIgnoreCase)
        && (selectedPid is null || data.ProcessID == selectedPid);
    var isVirtual = name.Contains("VirtualAlloc", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VirtualFree", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("VirtualMem/", StringComparison.OrdinalIgnoreCase)
        || data.ProviderName.Contains("VirtualAlloc", StringComparison.OrdinalIgnoreCase);
    if (isVirtual)
    {
        virtualEvents.TryGetValue(name, out var count);
        virtualEvents[name] = count + 1;
    }
    if (!isTarget) continue;
    targetCount++;
    targetEvents.TryGetValue(name, out var targetEventCount);
    targetEvents[name] = targetEventCount + 1;
    if (name == "VirtualMem/Free")
    {
        frees.Add((data.TimeStamp, Convert.ToInt64(data.PayloadByName("BaseAddr")),
            string.Join(", ", data.PayloadNames.Select(key => $"{key}={data.PayloadByName(key)}"))));
        continue;
    }
    if (!isVirtual || name != "VirtualMem/Alloc") continue;
    var length = Convert.ToInt64(data.PayloadByName("Length"));
    if (length < 1024 * 1024) continue;
    var baseAddress = Convert.ToInt64(data.PayloadByName("BaseAddr"));
    var flags = Convert.ToString(data.PayloadByName("Flags")) ?? "<unknown>";
    var stack = log.GetCallStackForEvent(data);
    var frames = new List<string>();
    for (var frame = stack; frame is not null && frames.Count < 80; frame = frame.Caller)
        frames.Add(frame.CodeAddress.ModuleName);
    frames.Reverse();
    var modules = new List<string>(frames.Count);
    foreach (var module in frames)
        if (modules.Count == 0 || modules[^1] != module) modules.Add(module);
    large.Add((data.TimeStamp, data.ProcessID, baseAddress, length, flags, string.Join(" -> ", modules)));
}
Console.WriteLine($"All events: {allCount}; CrossGOverlay events: {targetCount}");
Console.WriteLine("Virtual event names:");
foreach (var pair in virtualEvents.OrderByDescending(x => x.Value).Take(30))
    Console.WriteLine($"{pair.Value,10} {pair.Key}");
Console.WriteLine("Target event names:");
foreach (var pair in targetEvents.OrderByDescending(x => x.Value).Take(30))
    Console.WriteLine($"{pair.Value,10} {pair.Key}");
Console.WriteLine("Largest target committed VirtualAlloc events (at least 1 MiB):");
foreach (var entry in large.Where(x => x.Flags == "MEM_COMMIT").OrderByDescending(x => x.Length).Take(10))
    Console.WriteLine($"{entry.Time:O} PID={entry.Pid} Base=0x{entry.Base:x} Bytes={entry.Length} ({entry.Length / 1048576d:0.###} MiB)");
Console.WriteLine("Committed target VirtualAlloc regions in the 50-70 MiB range:");
foreach (var entry in large.Where(x => x.Flags == "MEM_COMMIT" && x.Length is >= 50 * 1024 * 1024 and <= 70 * 1024 * 1024))
{
    Console.WriteLine($"{entry.Time:O} PID={entry.Pid} Base=0x{entry.Base:x} Bytes={entry.Length} ({entry.Length / 1048576d:0.###} MiB)");
    Console.WriteLine($"  MODULES {entry.Stack}");
    foreach (var free in frees.Where(x => x.Base == entry.Base))
        Console.WriteLine($"  FREE {free.Time:O} {free.Payload}");
}
Console.WriteLine("Committed target VirtualAlloc regions from 1 MiB to below 10 MiB:");
foreach (var entry in large.Where(x => x.Flags == "MEM_COMMIT" && x.Length is >= 1024 * 1024 and < 10 * 1024 * 1024)
                           .OrderBy(x => x.Time))
{
    Console.WriteLine($"{entry.Time:O} PID={entry.Pid} Base=0x{entry.Base:x} Bytes={entry.Length} ({entry.Length / 1048576d:0.###} MiB)");
    Console.WriteLine($"  MODULES {entry.Stack}");
    foreach (var free in frees.Where(x => x.Base == entry.Base))
        Console.WriteLine($"  FREE {free.Time:O} {free.Payload}");
}
