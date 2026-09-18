using System.Reflection;

namespace CrosshairOverlay.Core;

/// <summary>
/// Tên và phiên bản hiển thị của ứng dụng.
/// </summary>
/// <remarks>
/// Gom về một chỗ để tên thương hiệu không bị rải rác thành chuỗi literal khắp nơi — đổi tên
/// lần sau chỉ phải sửa ở đây.
///
/// <para>
/// Tên hiển thị KHÔNG nằm trong file resource ngôn ngữ: tên riêng thì không dịch.
/// </para>
/// </remarks>
public static class AppInfo
{
    public const string DisplayName = "CrossGOverlay";

    /// <summary>Slogan dưới tên ứng dụng. Là một phần thương hiệu nên cũng không dịch.</summary>
    public const string Tagline = "Overlay for every gamer";

    /// <summary>Phiên bản dạng "v1.2.3", bỏ phần revision vì nó luôn là 0.</summary>
    public static string DisplayVersion { get; } = BuildVersion();

    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static string BuildVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "v0.0.0" : "v" + Short(version);
    }

    /// <summary>
    /// Phiên bản dạng người đọc: "0.1.0", không phải "0.1.0.0".
    /// </summary>
    /// <remarks>
    /// Phiên bản assembly luôn có 4 số, còn thẻ phát hành trên GitHub ("v0.2.0") chỉ có 3. Hiện cả hai cạnh nhau mà
    /// một bên thừa số 0 thì trông như hai cách đánh số khác nhau.
    /// </remarks>
    public static string Short(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        // Revision > 0 mới đáng hiện; Build âm nghĩa là phiên bản chỉ có hai số.
        if (version.Revision > 0) return version.ToString(4);
        return version.Build > 0 ? version.ToString(3) : version.ToString(Math.Max(2, Math.Min(3, version.Build + 3)));
    }
}
