using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Quy tắc liên kết một tiến trình game với một preset crosshair.
/// Ví dụ: <c>cs2.exe</c> → preset "CS2 T-Shape".
/// </summary>
/// <remarks>
/// Việc nhận diện chỉ dùng thông tin cửa sổ/tiến trình công khai
/// (<c>GetForegroundWindow</c>, <c>GetWindowThreadProcessId</c>, <c>QueryFullProcessImageName</c>).
/// KHÔNG mở handle với quyền đọc memory, KHÔNG enumerate module của game.
/// </remarks>
public sealed partial class GameProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tên hiển thị, vd "Counter-Strike 2".</summary>
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private ProcessMatchMode _matchMode = ProcessMatchMode.ProcessName;

    /// <summary>Giá trị đem so khớp, ý nghĩa phụ thuộc <see cref="MatchMode"/>. Vd "cs2.exe".</summary>
    [ObservableProperty] private string _pattern = string.Empty;

    /// <summary>Preset sẽ áp dụng khi rule khớp. Bỏ qua nếu <see cref="Behavior"/> là HideOverlay.</summary>
    [ObservableProperty] private Guid _presetId;

    // Optional weapon slots. Empty means that key keeps the current preset.
    [ObservableProperty] private Guid _weapon1PresetId;
    [ObservableProperty] private Guid _weapon2PresetId;
    [ObservableProperty] private Guid _weapon3PresetId;
    [ObservableProperty] private Guid _weapon4PresetId;
    [ObservableProperty] private Guid _weaponQPresetId;
    [ObservableProperty] private Guid _weaponEPresetId;

    public bool HasWeaponPresets => Weapon1PresetId != Guid.Empty || Weapon2PresetId != Guid.Empty
        || Weapon3PresetId != Guid.Empty || Weapon4PresetId != Guid.Empty
        || WeaponQPresetId != Guid.Empty || WeaponEPresetId != Guid.Empty;

    public Guid WeaponPresetForKey(uint virtualKey) => virtualKey switch
    {
        0x31 => Weapon1PresetId, 0x32 => Weapon2PresetId,
        0x33 => Weapon3PresetId, 0x34 => Weapon4PresetId,
        0x51 => WeaponQPresetId, 0x45 => WeaponEPresetId,
        _ => Guid.Empty,
    };

    [ObservableProperty] private GameProfileBehavior _behavior = GameProfileBehavior.ShowPreset;

    [ObservableProperty] private bool _enabled = true;

    /// <summary>
    /// Thứ tự ưu tiên khi nhiều rule cùng khớp — số nhỏ được xét trước.
    /// </summary>
    [ObservableProperty] private int _priority;

    /// <summary>
    /// Tên hiển thị. Danh sách dùng <c>DisplayMemberPath</c> chỉ đổi phần CHỮ vẽ ra; tên mà trình
    /// đọc màn hình (UI Automation) đọc lên vẫn lấy từ <c>ToString()</c> — không ghi đè thì nó đọc
    /// "CrosshairOverlay.Core.Models.GameProfile" cho mọi mục.
    /// </summary>
    public override string ToString() => Name;

    public GameProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        MatchMode = MatchMode,
        Pattern = Pattern,
        PresetId = PresetId,
        Weapon1PresetId = Weapon1PresetId,
        Weapon2PresetId = Weapon2PresetId,
        Weapon3PresetId = Weapon3PresetId,
        Weapon4PresetId = Weapon4PresetId,
        WeaponQPresetId = WeaponQPresetId,
        WeaponEPresetId = WeaponEPresetId,
        Behavior = Behavior,
        Enabled = Enabled,
        Priority = Priority,
    };
}
