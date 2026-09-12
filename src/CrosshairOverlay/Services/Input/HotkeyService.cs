using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Interop;
using CrosshairOverlay.Localization;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Input;

/// <inheritdoc cref="IHotkeyService"/>
/// <remarks>
/// Hai đường nhận phím tắt, vì Windows không cho đăng ký nút chuột qua <c>RegisterHotKey</c>:
/// <list type="bullet">
///   <item>Phím bàn phím → <c>RegisterHotKey</c>. Hệ điều hành chỉ báo đúng tổ hợp đã đăng ký.</item>
///   <item>Nút chuột phụ → Raw Input (<c>WM_INPUT</c>), kênh CHỈ ĐỌC, không chặn và không giả
///         lập được sự kiện nào.</item>
/// </list>
/// Cả hai đều không phải hook cấp thấp.
/// </remarks>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<int, HotkeyAction> _byId = [];
    private readonly List<HotkeyBinding> _mouseBindings = [];

    private nint _hwnd;
    private HwndSourceHook? _hook;
    private bool _rawInputRegistered;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService(ILogger<HotkeyService> logger) => _logger = logger;

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public void Attach(nint hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hwnd != 0) throw new InvalidOperationException("HotkeyService đã được gắn vào một cửa sổ.");
        if (hwnd == 0) throw new ArgumentException("HWND không hợp lệ.", nameof(hwnd));

        _hwnd = hwnd;

        var source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("Không lấy được HwndSource từ HWND đã cho.");

        _hook = WndProc;
        source.AddHook(_hook);

        RegisterRawMouse();
    }

    /// <summary>
    /// Đăng ký nhận sự kiện chuột thô. Cờ INPUTSINK là thứ khiến ta nhận được cả khi cửa sổ
    /// không ở foreground — điều kiện bắt buộc để phím tắt hoạt động lúc đang trong game.
    /// </summary>
    private void RegisterRawMouse()
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = Win32Constants.HID_USAGE_PAGE_GENERIC,
                usUsage = Win32Constants.HID_USAGE_GENERIC_MOUSE,
                dwFlags = Win32Constants.RIDEV_INPUTSINK,
                hwndTarget = _hwnd,
            },
        };

        _rawInputRegistered = NativeMethods.RegisterRawInputDevices(
            devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        if (!_rawInputRegistered)
        {
            _logger.LogWarning(
                "Không đăng ký được Raw Input (lỗi {Error}) — phím tắt bằng nút chuột sẽ không hoạt động.",
                Marshal.GetLastWin32Error());
        }
    }

    public HotkeyRegistrationResult Apply(IEnumerable<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bindings);

        if (_hwnd == 0) throw new InvalidOperationException("Phải gọi Attach trước khi đăng ký hotkey.");

        UnregisterAll();

        var registered = new List<HotkeyBinding>();
        var failures = new List<HotkeyRegistrationFailure>();

        foreach (var binding in bindings)
        {
            binding.IsRegistered = false;
            binding.RegistrationError = null;

            if (!binding.Enabled || !binding.IsAssigned) continue;

            if (binding.IsMouseBinding)
            {
                RegisterMouse(binding, registered, failures);
                continue;
            }

            RegisterKeyboard(binding, registered, failures);
        }

        if (failures.Count > 0)
        {
            _logger.LogWarning(
                "{Failed}/{Total} hotkey không đăng ký được.",
                failures.Count, failures.Count + registered.Count);
        }

        return new HotkeyRegistrationResult(registered, failures);
    }

    private void RegisterMouse(
        HotkeyBinding binding, List<HotkeyBinding> registered, List<HotkeyRegistrationFailure> failures)
    {
        if (!_rawInputRegistered)
        {
            Fail(binding, failures, Tr.Get("Hotkeys_ErrRawInput"));
            return;
        }

        _mouseBindings.Add(binding);
        binding.IsRegistered = true;
        registered.Add(binding);
    }

    private void RegisterKeyboard(
        HotkeyBinding binding, List<HotkeyBinding> registered, List<HotkeyRegistrationFailure> failures)
    {
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
        if (virtualKey == 0)
        {
            Fail(binding, failures, Tr.Get("Hotkeys_ErrUnmappable"));
            return;
        }

        var modifiers = ToWin32Modifiers(binding.Modifiers) | Win32Constants.MOD_NOREPEAT;
        var id = _nextId++;

        if (NativeMethods.RegisterHotKey(_hwnd, id, modifiers, virtualKey))
        {
            _byId[id] = binding.Action;
            binding.IsRegistered = true;
            registered.Add(binding);
            return;
        }

        var error = Marshal.GetLastWin32Error();
        var reason = error == Win32Constants.ERROR_HOTKEY_ALREADY_REGISTERED
            ? Tr.Get("Hotkeys_ErrTaken")
            : Tr.Format("Hotkeys_ErrRejected", error);

        Fail(binding, failures, reason);
    }

    public void UnregisterAll()
    {
        _mouseBindings.Clear();

        if (_hwnd == 0) return;

        foreach (var id in _byId.Keys)
        {
            if (!NativeMethods.UnregisterHotKey(_hwnd, id))
                _logger.LogDebug("UnregisterHotKey thất bại cho id {Id}.", id);
        }

        _byId.Clear();
    }

    private void Fail(HotkeyBinding binding, List<HotkeyRegistrationFailure> failures, string reason)
    {
        binding.RegistrationError = reason;
        failures.Add(new HotkeyRegistrationFailure(binding, reason));
        _logger.LogWarning("Hotkey {Binding} ({Action}) thất bại: {Reason}", binding, binding.Action, reason);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32Constants.WM_HOTKEY:
                HandleHotkeyMessage((int)wParam, ref handled);
                break;

            case Win32Constants.WM_INPUT:
                HandleRawInput(lParam);
                break;
        }

        return 0;
    }

    private void HandleHotkeyMessage(int id, ref bool handled)
    {
        if (!_byId.TryGetValue(id, out var action)) return;

        handled = true;
        Raise(action);
    }

    private void HandleRawInput(nint lParam)
    {
        if (_mouseBindings.Count == 0) return;

        var size = (uint)Marshal.SizeOf<RAWINPUTMOUSE>();
        var read = NativeMethods.GetRawInputData(
            lParam,
            Win32Constants.RID_INPUT,
            out var raw,
            ref size,
            (uint)Marshal.SizeOf<RAWINPUTHEADER>());

        // GetRawInputData trả về (uint)-1 khi lỗi.
        if (read == uint.MaxValue || raw.header.dwType != Win32Constants.RIM_TYPEMOUSE) return;

        var button = ToButton(raw.mouse.usButtonFlags);
        if (button == HotkeyMouseButton.None) return;

        var modifiers = CurrentModifiers();

        foreach (var binding in _mouseBindings)
        {
            if (binding.MouseButton != button || binding.Modifiers != modifiers) continue;

            Raise(binding.Action);
            return;
        }
    }

    private static HotkeyMouseButton ToButton(ushort buttonFlags)
    {
        if ((buttonFlags & Win32Constants.RI_MOUSE_BUTTON_4_DOWN) != 0) return HotkeyMouseButton.XButton1;
        if ((buttonFlags & Win32Constants.RI_MOUSE_BUTTON_5_DOWN) != 0) return HotkeyMouseButton.XButton2;
        if ((buttonFlags & Win32Constants.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) return HotkeyMouseButton.Middle;

        return HotkeyMouseButton.None;
    }

    /// <summary>Trạng thái phím bổ trợ ngay lúc này, đọc thẳng từ hệ điều hành.</summary>
    private static ModifierKeys CurrentModifiers()
    {
        var modifiers = ModifierKeys.None;

        if (IsDown(Win32Constants.VK_CONTROL)) modifiers |= ModifierKeys.Control;
        if (IsDown(Win32Constants.VK_MENU)) modifiers |= ModifierKeys.Alt;
        if (IsDown(Win32Constants.VK_SHIFT)) modifiers |= ModifierKeys.Shift;
        if (IsDown(Win32Constants.VK_LWIN) || IsDown(Win32Constants.VK_RWIN)) modifiers |= ModifierKeys.Windows;

        return modifiers;

        // Bit cao của giá trị trả về cho biết phím đang được giữ.
        static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private void Raise(HotkeyAction action)
    {
        _logger.LogDebug("Hotkey kích hoạt: {Action}", action);
        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(action));
    }

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= Win32Constants.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= Win32Constants.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= Win32Constants.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= Win32Constants.MOD_WIN;
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAll();

        if (_rawInputRegistered)
        {
            // Gỡ đăng ký Raw Input: hwndTarget phải là 0 khi dùng cờ REMOVE.
            var devices = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = Win32Constants.HID_USAGE_PAGE_GENERIC,
                    usUsage = Win32Constants.HID_USAGE_GENERIC_MOUSE,
                    dwFlags = Win32Constants.RIDEV_REMOVE,
                    hwndTarget = 0,
                },
            };

            NativeMethods.RegisterRawInputDevices(
                devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            _rawInputRegistered = false;
        }

        if (_hwnd != 0 && _hook is not null)
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(_hook);

        _hook = null;
        _hwnd = 0;
    }
}
