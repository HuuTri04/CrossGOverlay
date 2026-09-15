using System.Globalization;

namespace CrosshairOverlay.Core;

/// <summary>Tham số dòng lệnh ứng dụng tự truyền cho chính nó khi khởi động lại.</summary>
/// <param name="FactoryReset">Xoá cài đặt, preset và ảnh trước khi nạp gì cả.</param>
/// <param name="WaitForProcessId">PID của instance cũ phải thoát hẳn trước khi instance này làm gì.</param>
public sealed record StartupArguments(bool FactoryReset, int? WaitForProcessId)
{
    public const string FactoryResetSwitch = "--factory-reset";
    public const string WaitPidSwitch = "--wait-pid";

    public static StartupArguments None { get; } = new(false, null);

    public static StartupArguments Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return None;

        var reset = false;
        int? waitPid = null;

        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], FactoryResetSwitch, StringComparison.OrdinalIgnoreCase))
            {
                reset = true;
            }
            else if (string.Equals(args[i], WaitPidSwitch, StringComparison.OrdinalIgnoreCase)
                     && i + 1 < args.Count
                     && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                     && pid > 0)
            {
                waitPid = pid;
                i++;
            }
        }

        return new StartupArguments(reset, waitPid);
    }
}
