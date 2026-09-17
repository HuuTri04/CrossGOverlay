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
///         lập được sự kiện nào. Nhận trên luồng nền riêng (<see cref="RawMouseInputThread"/>) —
///         luồng giao diện chỉ bị đánh thức khi có nút được bấm/nhả, không phải mỗi lần di chuột.</item>
/// </list>
/// Cả hai đều không phải hook cấp thấp.
/// </remarks>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<int, HotkeyAction> _byId = [];
    private readonly List<MouseHotkey> _mouseBindings = [];
    private readonly Dispatcher _dispatcher;
    private readonly RawMouseInputThread _rawInput;

    /// <summary>
    /// Bản chụp phím tắt chuột mà luồng input đọc. Mảng bất biến, thay cả mảng khi đổi — luồng input
    /// không bao giờ phải khoá hay thấy danh sách đang sửa dở.
    /// </summary>
    private volatile MouseHotkey[] _mouseSnapshot = [];

    private nint _hwnd;
    private HwndSourceHook? _hook;

    /// <summary>Có phím tắt nào dùng nút chuột không — một trong hai lý do cần Raw Input.</summary>
    private bool _wantsMouseBindings;

    /// <summary>Đọc từ cả luồng input lẫn luồng giao diện.</summary>
    private volatile bool _trackRightButton;

    /// <summary>Nút phải đang được giữ (1) hay không (0). Đổi qua Interlocked.</summary>
    private int _rightHeld;

    private volatile bool _trackLeftButton;

    /// <summary>Nút trái đang được giữ (1) hay không (0). Đổi qua Interlocked.</summary>
    private int _leftHeld;

    /// <summary>
    /// Mốc (Environment.TickCount64) được phép đối chiếu lại trạng thái nút thật. Chỉ luồng input đọc/ghi.
    /// </summary>
    private long _nextRightCheck;

    private long _nextLeftCheck;

    /// <summary>
    /// Khoảng cách tối thiểu giữa hai lần đối chiếu "gói nhả bị lỡ". Giữ chuột trái mà lia (bắn liên thanh)
    /// là 1000 gói mỗi giây; đối chiếu mỗi gói là 2000 lời gọi hệ thống mỗi giây chỉ cho một lưới an toàn.
    /// </summary>
    private const long HeldRecheckIntervalMs = 250;
    private int _nextId = 1;
    private volatile bool _disposed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _rawInput = new RawMouseInputThread(OnRawMousePacket);
    }

    /// <summary>Một phím tắt chuột dạng gọn cho luồng input: không chạm tới đối tượng binding của giao diện.</summary>
    private readonly record struct MouseHotkey(HotkeyMouseButton Button, ModifierKeys Modifiers, HotkeyAction Action);

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public event EventHandler<bool>? RightButtonChanged;

    public event EventHandler<bool>? LeftButtonChanged;

    public bool TrackRightButton
    {
        get => _trackRightButton;
        set
        {
            if (_trackRightButton == value) return;
            _trackRightButton = value;

            // Tắt giữa lúc đang giữ nút: báo nhả để không ai kẹt ở trạng thái "đang ngắm".
            if (!value) SetRightHeld(false);

            SetRawMouse(NeedsRawMouse());
        }
    }

    public bool TrackLeftButton
    {
        get => _trackLeftButton;
        set
        {
            if (_trackLeftButton == value) return;
            _trackLeftButton = value;

            // Tắt giữa lúc đang bắn: báo nhả để tâm ngắm không kẹt ở màu khi bắn.
            if (!value) SetLeftHeld(false);

            SetRawMouse(NeedsRawMouse());
        }
    }

    /// <summary>Raw Input chỉ đăng ký khi còn ít nhất một thứ cần tới nút chuột.</summary>
    private bool NeedsRawMouse() => _wantsMouseBindings || _trackRightButton || _trackLeftButton;

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

        // Cửa sổ này chỉ nhận WM_HOTKEY. KHÔNG đăng ký Raw Input ở đây — xem SetRawMouse.
    }

    /// <summary>
    /// Bật hoặc tắt nhận sự kiện chuột thô, theo đúng nhu cầu.
    /// </summary>
    /// <remarks>
    /// Raw Input của chuột không cho lọc riêng sự kiện NÚT: đã đăng ký là nhận MỌI sự kiện, kể cả
    /// từng lần di chuột. Với cờ INPUTSINK (bắt buộc để phím tắt chạy khi đang trong game), chuột
    /// gaming 1000 Hz đánh thức luồng nhận tới 1000 lần mỗi giây trong suốt trận đấu. Nên chỉ đăng
    /// ký khi có phím tắt dùng nút chuột hoặc bật "ẩn khi giữ chuột phải", và gỡ ngay khi không còn.
    /// </remarks>
    private void SetRawMouse(bool wanted)
    {
        if (wanted == _rawInput.IsRunning) return;

        if (!wanted)
        {
            _rawInput.Stop();
            _logger.LogInformation("Đã gỡ Raw Input chuột — không còn tính năng nào cần nút chuột.");
            return;
        }

        if (_rawInput.Start())
        {
            _logger.LogInformation("Đã đăng ký Raw Input chuột trên luồng nền (phím tắt nút chuột hoặc ẩn khi giữ chuột phải).");
            return;
        }

        _logger.LogWarning("Không đăng ký được Raw Input — phím tắt bằng nút chuột sẽ không hoạt động.");
    }

    /// <summary>Có đang nhận Raw Input chuột không. Dùng cho chẩn đoán và kiểm thử.</summary>
    public bool IsReceivingRawMouse => _rawInput.IsRunning;

    public HotkeyRegistrationResult Apply(IEnumerable<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bindings);

        if (_hwnd == 0) throw new InvalidOperationException("Phải gọi Attach trước khi đăng ký hotkey.");

        UnregisterAll();

        var list = bindings as IReadOnlyCollection<HotkeyBinding> ?? bindings.ToList();
        _wantsMouseBindings = list.Any(b => b.Enabled && b.IsAssigned && b.IsMouseBinding);
        SetRawMouse(NeedsRawMouse());

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

        _mouseSnapshot = [.. _mouseBindings];

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
        if (!_rawInput.IsRunning)
        {
            Fail(binding, failures, Tr.Get("Hotkeys_ErrRawInput"));
            return;
        }

        _mouseBindings.Add(new MouseHotkey(binding.MouseButton, binding.Modifiers, binding.Action));
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
        _mouseSnapshot = [];

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
        if (msg == Win32Constants.WM_HOTKEY) HandleHotkeyMessage((int)wParam, ref handled);

        return 0;
    }

    private void HandleHotkeyMessage(int id, ref bool handled)
    {
        if (!_byId.TryGetValue(id, out var action)) return;

        handled = true;
        Raise(action);
    }

    /// <summary>
    /// Một gói chuột thô. Chạy TRÊN LUỒNG INPUT, ở tần số polling của chuột.
    /// </summary>
    /// <remarks>
    /// Gói chỉ di chuyển (phần áp đảo) thoát ra sau vài phép so sánh: không cấp phát, không đánh thức
    /// luồng giao diện. Chỉ bấm/nhả nút mới xếp việc sang dispatcher.
    /// </remarks>
    internal void OnRawMousePacket(ushort buttonFlags)
    {
        if (_disposed) return;

        if (_trackRightButton) TrackRight(buttonFlags);
        if (_trackLeftButton) TrackLeft(buttonFlags);

        if (buttonFlags == 0) return;

        var snapshot = _mouseSnapshot;
        if (snapshot.Length == 0) return;

        var button = ToButton(buttonFlags);
        if (button == HotkeyMouseButton.None) return;

        var modifiers = CurrentModifiers();

        foreach (var binding in snapshot)
        {
            if (binding.Button != button || binding.Modifiers != modifiers) continue;

            Raise(binding.Action);
            return;
        }
    }

    /// <summary>
    /// Cập nhật trạng thái nút phải từ một gói Raw Input (luồng input).
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

        if (Volatile.Read(ref _rightHeld) == 1 && DueForRecheck(ref _nextRightCheck) && !IsPhysicalButtonDown(left: false))
            SetRightHeld(false);
    }

    /// <summary>
    /// Nút TRÁI, cùng cách làm với <see cref="TrackRight"/>: nút vật lý, lưới an toàn khi gói "nhả" bị lỡ.
    /// </summary>
    private void TrackLeft(ushort buttonFlags)
    {
        if ((buttonFlags & Win32Constants.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
        {
            SetLeftHeld(true);
            return;
        }

        if ((buttonFlags & Win32Constants.RI_MOUSE_LEFT_BUTTON_UP) != 0)
        {
            SetLeftHeld(false);
            return;
        }

        if (Volatile.Read(ref _leftHeld) == 1 && DueForRecheck(ref _nextLeftCheck) && !IsPhysicalButtonDown(left: true))
            SetLeftHeld(false);
    }

    private static bool DueForRecheck(ref long nextCheck)
    {
        var now = Environment.TickCount64;
        if (now < nextCheck) return false;

        nextCheck = now + HeldRecheckIntervalMs;
        return true;
    }

    private static bool IsPhysicalButtonDown(bool left)
    {
        // GetAsyncKeyState làm việc với nút LOGIC; đổi vai trò nút trong Windows thì nút vật lý bên phải
        // là VK_LBUTTON và ngược lại.
        var swapped = NativeMethods.GetSystemMetrics(Win32Constants.SM_SWAPBUTTON) != 0;
        var key = left != swapped ? Win32Constants.VK_LBUTTON : Win32Constants.VK_RBUTTON;
        return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private void SetRightHeld(bool held)
    {
        var value = held ? 1 : 0;
        if (Interlocked.Exchange(ref _rightHeld, value) == value) return;

        // Người nghe (overlay) sống trên luồng giao diện. Ưu tiên Input để crosshair ẩn/hiện kịp cùng
        // khung hình với cú bấm. "Đang giữ" tới nơi khi đã tắt theo dõi thì bỏ: gói tin đó bay đi trước
        // lúc tắt, báo lên sẽ để overlay kẹt ở trạng thái ẩn.
        _dispatcher.InvokeAsync(() =>
        {
            if (_disposed || (held && !_trackRightButton)) return;
            RightButtonChanged?.Invoke(this, held);
        }, DispatcherPriority.Input);
    }

    private void SetLeftHeld(bool held)
    {
        var value = held ? 1 : 0;
        if (Interlocked.Exchange(ref _leftHeld, value) == value) return;

        // Cùng lý do với SetRightHeld: báo sang luồng giao diện, ưu tiên Input để màu đổi kịp cùng khung
        // hình với cú bấm; gói "đang giữ" tới sau khi đã tắt theo dõi thì bỏ.
        _dispatcher.InvokeAsync(() =>
        {
            if (_disposed || (held && !_trackLeftButton)) return;
            LeftButtonChanged?.Invoke(this, held);
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
    /// Gọi từ WndProc (WM_HOTKEY) hoặc từ luồng Raw Input. Việc mà một phím tắt kích hoạt — đổi
    /// preset, dựng lại overlay, ghi settings.json — thuộc về luồng giao diện và nặng hơn nhiều lần
    /// so với thứ được phép làm trong một lượt WndProc; ở luồng input, làm tại chỗ còn khiến mọi gói
    /// chuột kế tiếp phải xếp hàng chờ. Đẩy sang <see cref="Dispatcher"/> nên bên gọi kết thúc chỉ
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
        _rawInput.Dispose();

        if (_hwnd != 0 && _hook is not null)
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(_hook);

        _hook = null;
        _hwnd = 0;
    }
}
