using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

if (args.Length != 1) throw new ArgumentException("Usage: AllocationTraceSummary <trace.nettrace>");
var types = new Dictionary<string, (long Count, long SampledBytes)>(StringComparer.Ordinal);
var stacks = new Dictionary<string, long>(StringComparer.Ordinal);
long ticks = 0;
var etlx = TraceLog.CreateFromEventPipeDataFile(args[0], args[0] + ".etlx", null);
using var log = new TraceLog(etlx);
foreach (var traceEvent in log.Events)
{
    if (traceEvent is not GCAllocationTickTraceData data) continue;
    ticks++;
    var type = string.IsNullOrEmpty(data.TypeName) ? "<unknown>" : data.TypeName;
    types.TryGetValue(type, out var value);
    types[type] = (value.Count + 1, value.SampledBytes + data.AllocationAmount64);
    var stack = log.GetCallStackForEvent(data);
    if (stack is not null)
    {
        var frames = new List<string>(6);
        for (var frame = stack; frame is not null && frames.Count < 6; frame = frame.Caller)
            frames.Add(frame.CodeAddress.FullMethodName);
        var key = type + " <= " + string.Join(" <= ", frames);
        stacks.TryGetValue(key, out var count);
        stacks[key] = count + 1;
    }
}
Console.WriteLine($"Sampled allocation ticks: {ticks}");
Console.WriteLine("The type names are samples, not exact per-type allocation totals.");
foreach (var pair in types.OrderByDescending(pair => pair.Value.Count).Take(30))
    Console.WriteLine($"{pair.Value.Count,8} {pair.Value.SampledBytes,14} {pair.Key}");
Console.WriteLine($"Resolved allocation stacks: {stacks.Values.Sum()}");
foreach (var pair in stacks.OrderByDescending(pair => pair.Value).Take(15))
    Console.WriteLine($"{pair.Value,8} {pair.Key}");
