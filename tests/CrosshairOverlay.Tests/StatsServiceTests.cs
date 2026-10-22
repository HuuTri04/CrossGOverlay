using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class StatsServiceTests
{
    [Fact]
    public async Task Startup_PrunesHistoryOlderThanThirtyDays_ThenClearResetsFileAndSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrossGOverlay-stats-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var presetId = Guid.NewGuid();
            var path = Path.Combine(root, "stats_history.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new StatsHistoryData
            {
                Days =
                [
                    new StatsDayData { Day = today.AddDays(-31), SwitchCount = 2,
                        SecondsByPreset = new Dictionary<Guid, double> { [presetId] = 120 } },
                    new StatsDayData { Day = today, SwitchCount = 3,
                        SecondsByPreset = new Dictionary<Guid, double> { [presetId] = 60 } },
                ],
            }));

            using var service = CreateService(root);
            await service.StartAsync();
            var snapshot = service.Snapshot();
            Assert.Single(snapshot.Days);
            Assert.Equal(today, snapshot.Days[0].Day);
            Assert.Equal(3, snapshot.Days[0].SwitchCount);

            service.ClearAllStats();
            Assert.Empty(service.Snapshot().Days);
            var saved = JsonSerializer.Deserialize<StatsHistoryData>(await File.ReadAllTextAsync(path));
            Assert.NotNull(saved);
            Assert.Empty(saved.Days);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DisablingCleanup_RetainsOldHistory_AndSavesPreference()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrossGOverlay-stats-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var oldDay = DateOnly.FromDateTime(DateTime.Now).AddDays(-45);
            var path = Path.Combine(root, "stats_history.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new StatsHistoryData
            {
                AutoCleanupOldData = false,
                Days = [new StatsDayData { Day = oldDay, SwitchCount = 1 }],
            }));

            using var service = CreateService(root);
            await service.StartAsync();
            Assert.Single(service.Snapshot().Days);
            service.AutoCleanupOldData = true;
            Assert.Empty(service.Snapshot().Days);
            service.SaveNow();
            var saved = JsonSerializer.Deserialize<StatsHistoryData>(await File.ReadAllTextAsync(path));
            Assert.NotNull(saved);
            Assert.True(saved.AutoCleanupOldData);
            Assert.Empty(saved.Days);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task VisibilityAndPresetEvents_CountUsageWithoutImmediateDiskWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrossGOverlay-stats-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var first = CrosshairProfile.CreateDefault();
            var second = CrosshairProfile.CreateDefault();
            var overlay = DispatchProxy.Create<IOverlayController, StatsProxy>();
            var library = DispatchProxy.Create<IPresetLibrary, StatsProxy>();
            var libraryProbe = (StatsProxy)(object)library;
            libraryProbe.ActiveProfile = first;
            libraryProbe.Items.Add(first);
            libraryProbe.Items.Add(second);
            var path = Path.Combine(root, "stats_history.json");

            using var service = new StatsService(overlay, library, new AppPathProvider(root),
                NullLogger<StatsService>.Instance);
            await service.StartAsync();
            overlay.SetVisible(true);
            await Task.Delay(30);
            library.SetActive(second);
            overlay.SetVisible(false);

            var today = Assert.Single(service.Snapshot().Days);
            Assert.Equal(1, today.SwitchCount);
            Assert.True(today.TotalSeconds > 0);
            Assert.False(File.Exists(path));

            service.SaveNow();
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static StatsService CreateService(string root)
    {
        var overlay = DispatchProxy.Create<IOverlayController, StatsProxy>();
        var library = DispatchProxy.Create<IPresetLibrary, StatsProxy>();
        return new StatsService(overlay, library, new AppPathProvider(root),
            NullLogger<StatsService>.Instance);
    }

    public class StatsProxy : DispatchProxy
    {
        public CrosshairProfile? ActiveProfile { get; set; }
        public ObservableCollection<CrosshairProfile> Items { get; } = [];
        private readonly ReadOnlyObservableCollection<CrosshairProfile> _presets;
        private EventHandler? _activeChanged;
        private EventHandler<OverlayVisibilityChangedEventArgs>? _visibilityChanged;
        private bool _visible;

        public StatsProxy() => _presets = new ReadOnlyObservableCollection<CrosshairProfile>(Items);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsVisible": return _visible;
                case "get_Presets": return _presets;
                case "get_Active": return ActiveProfile;
                case "add_ActiveChanged": _activeChanged += (EventHandler)args![0]!; break;
                case "remove_ActiveChanged": _activeChanged -= (EventHandler)args![0]!; break;
                case "add_VisibilityChanged":
                    _visibilityChanged += (EventHandler<OverlayVisibilityChangedEventArgs>)args![0]!; break;
                case "remove_VisibilityChanged":
                    _visibilityChanged -= (EventHandler<OverlayVisibilityChangedEventArgs>)args![0]!; break;
                case "SetVisible":
                    _visible = (bool)args![0]!;
                    _visibilityChanged?.Invoke(this, new OverlayVisibilityChangedEventArgs(_visible));
                    break;
                case "SetActive":
                    ActiveProfile = (CrosshairProfile)args![0]!;
                    _activeChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }
            return null;
        }
    }
}
