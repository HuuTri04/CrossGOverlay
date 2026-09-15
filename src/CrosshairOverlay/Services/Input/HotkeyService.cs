using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
    /// <summary>
    /// Kích thước struct tính sẵn một lần.
    /// </summary>
    /// <remarks>
    /// <c>Marshal.SizeOf&lt;T&gt;()</c> không phải hằng số biên dịch — mỗi lần gọi là một lượt
    /// tra cứu layout qua reflection. Đường WM_INPUT chạy ở tần số polling của chuột (chuột
    /// gaming là 1000 lần/giây), nên hai lời gọi mỗi message hoá ra 2000 lượt tra cứu mỗi giây
    /// cho một con số không bao giờ đổi.
    /// </remarks>
    private static readonly uint RawMouseSize = (uint)Marshal.SizeOf<RAWINPUTMOUSE>();

    private static readonly uint RawHeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<int, HotkeyAction> _byId = [];
    private readonly List<HotkeyBinding> _mouseBindings = [];
    private readonly Dispatcher _dispatcher;

    private nint _hwnd;
    private HwndSourceHook? _hook;
    private bool _rawInputRegistered;

    /// <summary>Có phím tắt nào dùng nút chuột không — một trong hai lý do cần Raw Input.</summary>
    private bool _wantsMouseBindings;

    private bool _trackRightButton;

    /// <summary>Nút phải đang được giữ (theo Raw Input). Chỉ đụng trên UI thread.</summary>
    private bool _rightHeld;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public event EventHandler<bool>? RightButtonChanged;

    public bool TrackRightButton
    {
        get => _trackRightButton;
        set
        {
            if (_trackRightButton == value) return;
            _trackRightButton = value;

            // Tắt giữa lúc đang giữ nút: báo nhả để không ai kẹt ở trạng thái "đang ngắm".
            if (!value) SetRightHeld(false);

            if (_hwnd != 0) SetRawMouse(_wantsMouseBindings || value);
        }
    }

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

        // KHÔNG đăng ký Raw Input ở đây — xem SetRawMouse. Chỉ đăng ký khi Apply thấy có phím tắt
        // thật sự dùng nút chuột.
    }

    /// <summary>
    /// Bật hoặc tắt nhận sự kiện chuột thô, theo đúng nhu cầu.
    /// </summary>
    /// <remarks>
    /// Raw Input của chuột không cho lọc riêng sự kiện NÚT: đã đăng ký là nhận MỌI sự kiện, kể cả
    /// từng lần di chuột. Với cờ INPUTSINK (bắt buộc để phím tắt chạy khi đang trong game), chuột
    /// gaming 1000 Hz đánh thức tiến trình này tới 1000 lần mỗi giây trong suốt trận đấu — chỉ để
    /// đọc gói tin rồi bỏ đi. Nên chỉ đăng ký khi có ít nhất một phím tắt dùng nút chuột, và gỡ
    /// ngay khi không còn.
    /// </remarks>
    private void SetRawMouse(bool wanted)
    {
        if (wanted == _rawInputRegistered) return;

        if (!wanted)
        {
            RemoveRawMouse();
            _logger.LogInformation("Đã gỡ Raw Input chuột — không còn tính năng nào cần nút chuột.");
            return;
        }

        RegisterRawMouse();
        if (_rawInputRegistered)
            _logger.LogInformation("Đã đăng ký Raw Input chuột (phím tắt nút chuột hoặc ẩn khi giữ chuột phải).");
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

    private void RemoveRawMouse()
    {
        if (!_rawInputRegistered) return;

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

    /// <summary>Có đang nhận Raw Input chuột không. Dùng cho chẩn đoán và kiểm thử.</summary>
    public bool IsReceivingRawMouse => _rawInputRegistered;

    public HotkeyRegistrationResult Apply(IEnumerable<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bindings);

        if (_hwnd == 0) throw new InvalidOperationException("Phải gọi Attach trước khi đăng ký hotkey.");

        UnregisterAll();

        var list = bindings as IReadOnlyCollection<HotkeyBinding> ?? bindings.ToList();
        _wantsMouseBindings = list.Any(b => b.Enabled && b.IsAssigned && b.IsMouseBinding);
        SetRawMouse(_wantsMouseBindings || _trackRightButton);

        var registered = new List<HotkeyBinding>();
        var failures = new List<HotkeyRegistrationFailure>();

        foreach (var binding in list)
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
        if (_mouseBindings.Count == 0 && !_trackRightButton) return;

        var size = RawMouseSize;
        var read = NativeMethods.GetRawInputData(
            lParam,
            Win32Constants.RID_INPUT,
            out var raw,
            ref size,
            RawHeaderSize);

        // GetRawInputData trả về (uint)-1 khi lỗi.
        if (read == uint.MaxValue || raw.header.dwType != Win32Constants.RIM_TYPEMOUSE) return;

        if (_trackRightButton) TrackRight(raw.mouse.usButtonFlags);

        if (_mouseBindings.Count == 0) return;

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

    /// <summary>
    /// Cập nhật trạng thái nút phải từ một gói Raw Input.
    /// </summary>
    /// <remarks>
    /// Raw Input báo nút VẬT LÝ, nên đúng là nút phải kể cả khi người dùng đổi vai trò nút trong
    /// Windows. Lưới an toàn: gói "nhả" có thể bị lỡ (vd hộp thoại UAC chiếm màn hình đúng lúc
    /// đó) — trong khi đang coi là giữ, mỗi gói chuột kế tiếp đối chiếu với trạng thái nút thật
    /// để crosshair không bị ẩn mãi. Việc đối chiếu chỉ xảy ra lúc đang giữ nút.
    /// </remarks>
    private void TrackRight(ushort buttonFlags)
    {
        if ((buttonFlags & Win32Constants.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
        {
            SetRightHeld(true);
            return;
        }

        if ((buttonFlags & Win32Constants.RI_MOUSE_RIGHT_BUTTON_UP) != 0)
        {
            SetRightHeld(false);
            return;
        }

        if (_rightHeld && !IsPhysicalRightButtonDown()) SetRightHeld(false);
    }

    private static bool IsPhysicalRightButtonDown()
    {
        // GetAsyncKeyState làm việc với nút LOGIC; đổi vai trò nút thì nút phải vật lý là VK_LBUTTON.
        var swapped = NativeMethods.GetSystemMetrics(Win32Constants.SM_SWAPBUTTON) != 0;
        var key = swapped ? Win32Constants.VK_LBUTTON : Win32Constants.VK_RBUTTON;
        return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private void SetRightHeld(bool held)
    {
        if (_rightHeld == held) return;
        _rightHeld = held;

        // Cùng lý do với Raise: không làm việc của người nghe ngay trong WndProc. Ưu tiên Input để
        // crosshair ẩn/hiện kịp cùng khung hình với cú bấm.
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed) RightButtonChanged?.Invoke(this, held);
        }, DispatcherPriority.Input);
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

    /// <summary>
    /// Ghi nhận phím tắt rồi TRẢ LUỒNG VỀ NGAY.
    /// </summary>
    /// <remarks>
    /// Hàm này được gọi từ trong WndProc, tức là đang ở giữa một lượt bơm message. Việc mà một
    /// phím tắt kích hoạt — đổi preset, dựng lại overlay, ghi settings.json — nặng hơn nhiều
    /// lần so với thứ được phép làm trong một lượt WndProc, và suốt thời gian đó mọi WM_INPUT
    /// kế tiếp phải xếp hàng chờ. Đẩy sang <see cref="Dispatcher"/> khiến WndProc kết thúc chỉ
    /// sau một lần xếp hàng, còn phần việc thật chạy ở lượt dispatcher ngay sau đó.
    ///
    /// <para>
    /// Kể cả ghi log cũng dời sang bên kia: tạo chuỗi và định dạng tham số đều là cấp phát bộ
    /// nhớ, mà cấp phát trong đường callback chính là thứ sinh ra GC churn cần tránh.
    /// </para>
    /// </remarks>
    private void Raise(HotkeyAction action) =>
        _dispatcher.InvokeAsync(() => Dispatch(action), DispatcherPriority.Input);

    private void Dispatch(HotkeyAction action)
    {
        // Người dùng có thể đã thoát app trong khoảng giữa lúc xếp hàng và lúc chạy.
        if (_disposed) return;

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
        RemoveRawMouse();

        if (_hwnd != 0 && _hook is not null)
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(_hook);

        _hook = null;
        _hwnd = 0;
    }
}
