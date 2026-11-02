#if BENCHMARK
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace CrosshairOverlay.Core.Diagnostics;

/// <summary>In-memory, fixture-only timestamps. This type does not exist in normal builds.</summary>
internal static class BenchmarkPresetProbe
{
    private readonly record struct Stamp(long SwitchId, Guid PresetId, string Stage, long Ticks, string Source);
    private static readonly ConcurrentQueue<Stamp> Events = new();
    private static readonly long OriginTicks = Stopwatch.GetTimestamp();
    private static readonly DateTimeOffset OriginUtc = DateTimeOffset.UtcNow;
    private static readonly string? Root = Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_ROOT");
    private static readonly bool Active = !string.IsNullOrWhiteSpace(Root) &&
        Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_SCENARIO") == "preset-ab";
    private static readonly string RequestedVariant =
        Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_VARIANT") ?? "baseline";
    internal static readonly string Variant = RequestedVariant is "no-stats" or "no-preview" or "no-bring"
        ? RequestedVariant : "baseline";
    private static readonly ConcurrentDictionary<long, byte> Rendered = new();
    private static readonly ConcurrentDictionary<long, Guid> PresetIds = new();

    [ThreadStatic] private static long _pendingRequest;
    [ThreadStatic] private static string? _pendingSource;
    private static long _nextId;
    private static long _currentId;
    private static int _running;

    internal static bool Enabled => Active && Volatile.Read(ref _running) != 0;
    internal static bool SkipStats => Enabled && Variant == "no-stats";
    internal static bool SkipPreview => Enabled && Variant == "no-preview";
    internal static bool SkipBringIntoView => Enabled && Variant == "no-bring";
    internal static long CurrentSwitchId => Enabled ? Interlocked.Read(ref _currentId) : 0;

    internal static void BeginRun() => Volatile.Write(ref _running, Active ? 1 : 0);
    internal static void EndRun() => Volatile.Write(ref _running, 0);

    internal static void RequestReceived(string source, Guid presetId = default)
    {
        if (!Enabled) return;
        _pendingRequest = Interlocked.Increment(ref _nextId);
        _pendingSource = source;
        if (presetId != Guid.Empty) PresetIds[_pendingRequest] = presetId;
        Events.Enqueue(new Stamp(_pendingRequest, presetId, "T1.RequestReceived", Stopwatch.GetTimestamp(), source));
    }

    internal static long SetActiveStarted(Guid presetId)
    {
        if (!Enabled) return 0;
        if (_pendingRequest == 0) RequestReceived("DirectSetActive", presetId);
        var id = _pendingRequest;
        var source = _pendingSource ?? "DirectSetActive";
        _pendingRequest = 0;
        _pendingSource = null;
        PresetIds[id] = presetId;
        Interlocked.Exchange(ref _currentId, id);
        Events.Enqueue(new Stamp(id, presetId, "T2.SetActiveStart", Stopwatch.GetTimestamp(), source));
        return id;
    }

    internal static void Mark(string stage, long id = 0)
    {
        if (!Enabled) return;
        if (id == 0) id = CurrentSwitchId;
        if (id == 0) return;
        PresetIds.TryGetValue(id, out var presetId);
        Events.Enqueue(new Stamp(id, presetId, stage, Stopwatch.GetTimestamp(), string.Empty));
    }

    internal static SubscriberScope Subscriber(string name)
    {
        if (!Enabled) return default;
        var id = CurrentSwitchId;
        Mark(name + ".Start", id);
        return new SubscriberScope(id, name);
    }

    internal readonly struct SubscriberScope(long id, string name) : IDisposable
    {
        public void Dispose()
        {
            if (id != 0) Mark(name + ".End", id);
        }
    }

    internal static void OverlayRendered(long drawingSwitchId)
    {
        if (!Enabled || drawingSwitchId == 0 || !Rendered.TryAdd(drawingSwitchId, 0)) return;
        Mark("T5.OverlayOnRender", drawingSwitchId);
    }

    internal static async Task FlushAsync()
    {
        if (!Active || Root is null) return;
        var path = Path.Combine(Root, "diagnostics", "preset-stage-events.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new StringBuilder("SwitchId,PresetId,Stage,TimestampTicks,ElapsedMs,TimestampUtc,Variant,Source\n");
        foreach (var stamp in Events)
        {
            var ms = Stopwatch.GetElapsedTime(OriginTicks, stamp.Ticks).TotalMilliseconds;
            var utc = OriginUtc.AddMilliseconds(ms);
            builder.Append(stamp.SwitchId).Append(',').Append(stamp.PresetId).Append(',')
                .Append(stamp.Stage).Append(',').Append(stamp.Ticks).Append(',')
                .Append(ms.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                .Append(utc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(Variant).Append(',').Append(stamp.Source).Append('\n');
        }
        await File.WriteAllTextAsync(path, builder.ToString()).ConfigureAwait(false);
    }
}
#endif
