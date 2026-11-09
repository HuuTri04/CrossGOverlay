using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.System;

namespace CrosshairOverlay.ViewModels;

public enum StatsRange { Last7Days, Last30Days, Monthly }

public sealed partial class StatsViewModel(StatsService? service, IDialogService dialogs) : ObservableObject
{
    private bool? _lastAutoCleanupOldData;
    public IReadOnlyList<LocalizedOption<StatsRange>> RangeOptions { get; } =
    [
        new(StatsRange.Last7Days, "Stats_Last7"),
        new(StatsRange.Last30Days, "Stats_Last30"),
        new(StatsRange.Monthly, "Stats_Monthly"),
    ];

    [ObservableProperty] private StatsRange _selectedRange = StatsRange.Last7Days;
    [ObservableProperty] private string _visibleTime = "00h 00m";
    [ObservableProperty] private int _switchCount;
    [ObservableProperty] private string _activePreset = "—";
    [ObservableProperty] private string _fileSizeText = "0 KB";
    [ObservableProperty] private IReadOnlyList<DailyUsageRow> _dailyRows = [];
    [ObservableProperty] private IReadOnlyList<PresetUsageRow> _rows = [];

    public bool AutoCleanupOldData
    {
        get => service?.AutoCleanupOldData ?? true;
        set
        {
            if (service is null || service.AutoCleanupOldData == value) return;
            service.AutoCleanupOldData = value;
            Refresh();
        }
    }

    partial void OnSelectedRangeChanged(StatsRange value) => Refresh();

    public void Refresh()
    {
        if (service is null) return;
        var snapshot = service.Snapshot();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayUsage = snapshot.Days.FirstOrDefault(d => d.Day == today);
        VisibleTime = FormatDuration(todayUsage?.TotalSeconds ?? 0);
        SwitchCount = todayUsage?.SwitchCount ?? 0;
        ActivePreset = snapshot.ActiveName;
        FileSizeText = $"{snapshot.FileSizeBytes / 1024d:0.#} KB";
        if (_lastAutoCleanupOldData != snapshot.AutoCleanupOldData)
        {
            _lastAutoCleanupOldData = snapshot.AutoCleanupOldData;
            OnPropertyChanged(nameof(AutoCleanupOldData));
        }

        var firstDay = SelectedRange switch
        {
            StatsRange.Last7Days => today.AddDays(-6),
            StatsRange.Last30Days => today.AddDays(-29),
            _ => new DateOnly(today.Year, today.Month, 1).AddMonths(-11),
        };
        var filtered = snapshot.Days.Where(d => d.Day >= firstDay && d.Day <= today).ToArray();
        var daysByDate = filtered.ToDictionary(d => d.Day);

        var chart = new List<(string Label, double Seconds)>();
        if (SelectedRange == StatsRange.Monthly)
        {
            var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
            for (var month = firstMonth; month <= today; month = month.AddMonths(1))
                chart.Add((month.ToString("MM/yyyy"), filtered.Where(d => d.Day.Year == month.Year
                    && d.Day.Month == month.Month).Sum(d => d.TotalSeconds)));
        }
        else
        {
            for (var day = firstDay; day <= today; day = day.AddDays(1))
                chart.Add((day.ToString("dd/MM"), daysByDate.GetValueOrDefault(day)?.TotalSeconds ?? 0));
        }
        var max = Math.Max(1d, chart.Max(c => c.Seconds));
        IReadOnlyList<DailyUsageRow> dailyRows = [.. chart.Select(c => new DailyUsageRow(c.Label, c.Seconds / max * 100d,
            FormatDuration(c.Seconds)))];
        if (!DailyRows.SequenceEqual(dailyRows)) DailyRows = dailyRows;

        var distribution = filtered.SelectMany(d => d.Presets)
            .GroupBy(p => p.PresetId)
            .Select(g => new { Name = g.Last().Name, Seconds = g.Sum(p => p.Seconds) })
            .OrderByDescending(p => p.Seconds).ToArray();
        var total = distribution.Sum(p => p.Seconds);
        IReadOnlyList<PresetUsageRow> rows = [.. distribution.Select(p => new PresetUsageRow(p.Name,
            total > 0 ? p.Seconds / total * 100d : 0d, FormatDuration(p.Seconds)))];
        if (!Rows.SequenceEqual(rows)) Rows = rows;
    }

    [RelayCommand]
    private async Task ClearStatsAsync()
    {
        if (service is null) return;
        if (!dialogs.Confirm(Tr.Get("Stats_Clear"), Tr.Get("Stats_ClearConfirm"))) return;
        try
        {
            await service.ClearAllStatsAsync();
            Refresh();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            dialogs.ShowMessage(Tr.Get("Stats_Clear"), ex.Message, isError: true);
        }
    }

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return $"{(int)value.TotalHours:00}h {value.Minutes:00}m";
    }
}

public sealed record DailyUsageRow(string Label, double PercentOfMax, string Duration);
public sealed record PresetUsageRow(string Name, double Percent, string Duration)
{
    public string PercentText => $"{Percent:0.0}%";
}
