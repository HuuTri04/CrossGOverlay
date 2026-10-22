using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Input;

/// <summary>Opt-in, non-blocking keyboard observer for mapped weapon keys in the active game.</summary>
public sealed class WeaponAutoSwitchService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x100;
    private const int WmSysKeyDown = 0x104;
    private readonly IAppSettingsService _settings;
    private readonly IForegroundWindowWatcher _watcher;
    private readonly IGameProfileMatcher _matcher;
    private readonly IPresetLibrary _library;
    private readonly ILogger<WeaponAutoSwitchService> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly HookProc _callback;
    private readonly HashSet<GameProfile> _subscribedProfiles = [];
    private nint _hook;
    private GameProfile? _active;
    private nint _activeWindow;
    private bool _started;

    public WeaponAutoSwitchService(IAppSettingsService settings, IForegroundWindowWatcher watcher,
        IGameProfileMatcher matcher, IPresetLibrary library, ILogger<WeaponAutoSwitchService> logger)
    {
        _settings = settings;
        _watcher = watcher;
        _matcher = matcher;
        _library = library;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = OnKey;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _watcher.ForegroundChanged += OnForegroundChanged;
        _settings.Current.PropertyChanged += OnSettingsChanged;
        _settings.Current.GameProfiles.CollectionChanged += OnProfilesChanged;
        SyncProfileSubscriptions();
        Refresh();
    }

    private void OnForegroundChanged(object? sender, ForegroundWindowChangedEventArgs e) => Refresh();
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.AutoSwitchByGameProfile)) Refresh();
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncProfileSubscriptions();
        Refresh();
    }

    private void SyncProfileSubscriptions()
    {
        var current = _settings.Current.GameProfiles.ToHashSet();
        foreach (var profile in _subscribedProfiles.Except(current).ToArray())
        {
            profile.PropertyChanged -= OnProfileChanged;
            _subscribedProfiles.Remove(profile);
        }
        foreach (var profile in current.Except(_subscribedProfiles))
        {
            profile.PropertyChanged += OnProfileChanged;
            _subscribedProfiles.Add(profile);
        }
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        _settings.RequestSave();
        Refresh();
    }

    private void Refresh()
    {
        if (!_started) return;
        var window = _watcher.Current;
        _active = window.IsValid && !window.IsOwnProcess && _settings.Current.AutoSwitchByGameProfile
            ? _matcher.Match(window, _settings.Current.GameProfiles) : null;
        if (_active is not { Enabled: true, Behavior: GameProfileBehavior.ShowPreset, HasWeaponPresets: true })
            _active = null;
        _activeWindow = _active is null ? 0 : window.Handle;

        if (_active is not null && _hook == 0)
        {
            // LL callbacks execute in this process, on the installing UI thread; no DLL is
            // injected into the game. Supply the host module for the global hook registration.
            _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
            if (_hook == 0) _logger.LogWarning("Cannot install optional weapon keyboard hook: {Error}.", Marshal.GetLastWin32Error());
        }
        else if (_active is null && _hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code >= 0 && (message == WmKeyDown || message == WmSysKeyDown) && _active is { } profile
            && GetForegroundWindow() == _activeWindow)
        {
            var key = (uint)Marshal.ReadInt32(data);
            var injected = (Marshal.ReadInt32(data, 8) & 0x10) != 0;
            var presetId = injected ? Guid.Empty : profile.WeaponPresetForKey(key);
            if (presetId != Guid.Empty)
            {
                // Never block input or suppress the game's key. The UI dispatcher owns the library.
                _dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (!ReferenceEquals(_active, profile) || GetForegroundWindow() != _activeWindow) return;
                    var preset = _library.Presets.FirstOrDefault(p => p.Id == presetId);
                    if (preset is not null) _library.SetActive(preset);
                });
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        _watcher.ForegroundChanged -= OnForegroundChanged;
        _settings.Current.PropertyChanged -= OnSettingsChanged;
        _settings.Current.GameProfiles.CollectionChanged -= OnProfilesChanged;
        foreach (var profile in _subscribedProfiles) profile.PropertyChanged -= OnProfileChanged;
        _subscribedProfiles.Clear();
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
        _active = null;
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint module, uint threadId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
